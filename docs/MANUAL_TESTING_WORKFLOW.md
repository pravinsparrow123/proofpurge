# ProofPurge: Manual QA Testing Workflow & Test Plan

> **Complete Step-by-Step QA Verification Guide for Manual Testers**  
> Tests database schema discovery, topological deletion ordering, compressed cold archival, zero-lock micro-batched cascade purging, 1-click restore, and cryptographic compliance certification.

---

## 📋 Test Environment Prerequisites

1. **Operating System:** Windows 10/11 or Linux.
2. **Runtime:** .NET 8.0 SDK (`dotnet --version` $\ge$ `8.0.x`).
3. **Target Database:** Microsoft SQL Server (Local, Express, or Docker instance).
4. **CLI Executable:** `src/DataLifecycleEngine.Cli` (Aliased as `proofpurge`).

---

## 🛠️ Step 0: Test Database Initialization

Run the test schema and data seed script against SQL Server:

```powershell
sqlcmd -S ".\SQLEXPRESS" -i test/fixtures/sqlserver-schema.sql
```
*(Or execute [test/fixtures/sqlserver-schema.sql](file:///d:/AntiGravity/Project%20DB/test/fixtures/sqlserver-schema.sql) in SQL Server Management Studio / Azure Data Studio).*

### Seeded Data Baseline:
* **`Accounts` (Root):** 500 rows (250 active, 250 closed $> 3$ years ago).
* **`Orders` (Level 1 Child):** 1,000 rows (500 linked to closed accounts).
* **`AuditLogs` (Level 1 Child):** 500 rows (250 linked to closed accounts).
* **`OrderItems` (Level 2 Child):** 1,000 rows (500 linked to eligible orders).
* **`Shipments` (Level 2 Child):** 1,000 rows (500 linked to eligible orders).

---

## 🧪 Test Case 1: Schema Discovery & Safety Scanner (`scan`)

### Objective
Verify that the engine automatically introspects foreign keys, computes the topological deletion order, flags missing indexes, and calculates purge candidate row counts without modifying any data.

### Execution Command
```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- scan -c configs/retention-policy.example.yaml
```

### Expected Results (Pass Criteria):
* [ ] **Discovered Relations:** Detects all 5 user tables and 4 foreign-key relationships.
* [ ] **Missing Index Warning:** Flags `AuditLogs(AccountId)` with a yellow/red warning: *No supporting index found on child column. Risk of table scan during cascade operations.*
* [ ] **Topological Sort Order:** Strictly orders deletion from leaves to root:
  $$\text{OrderItems, Shipments} \longrightarrow \text{AuditLogs, Orders} \longrightarrow \text{Accounts}$$
* [ ] **Candidate Row Counts:** Accurately identifies 250 candidate Accounts and matching cascade counts.
* [ ] **Zero Impact:** Zero rows deleted from the database.

---

## 🧪 Test Case 2: Streaming Cold Parquet Archival (`archive`)

### Objective
Verify that historical candidate records across all cascade levels are serialized into Snappy-compressed columnar Parquet files with SHA-256 integrity checksums.

### Execution Command
```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- archive -c configs/retention-policy.example.yaml
```

### Expected Results (Pass Criteria):
* [ ] **Parquet Files Generated:** Parquet files are created under `./archives/purge_inactive_accounts_and_orders/` for each table:
  * `OrderItems_*.parquet`
  * `Shipments_*.parquet`
  * `Orders_*.parquet`
  * `AuditLogs_*.parquet`
  * `Accounts_*.parquet`
* [ ] **Storage Savings:** Reports 80% to 92% compression savings compared to uncompressed raw SQL rows.
* [ ] **SHA-256 Checksums:** A unique 64-character SHA-256 hash is printed for every archive file.
* [ ] **Non-Locking Query:** Queries use `WITH (NOLOCK)` without blocking transactional queries.

---

## 🧪 Test Case 3: Dry-Run Purge Simulation (`purge --dry-run`)

### Objective
Verify that the purger can simulate cascade deletions without deleting any database records.

### Execution Command
```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- purge --dry-run -c configs/retention-policy.example.yaml
```

### Expected Results (Pass Criteria):
* [ ] Displays `DRY-RUN` status badge.
* [ ] Reads initial log space percentage via `sys.dm_db_log_space_usage`.
* [ ] Traverses tables in strict topological order (`OrderItems` $\to$ `Shipments` $\to$ `AuditLogs` $\to$ `Orders` $\to$ `Accounts`).
* [ ] Reports simulated rows purged.
* [ ] **Database Verification:** Execute `SELECT COUNT(1) FROM Accounts;` in SSMS $\to$ count remains exactly **500**.

---

## 🧪 Test Case 4: Live Zero-Lock Cascade Purge (`purge --live`)

### Objective
Verify that the engine executes micro-batched deletions with real-time log vitals monitoring, checkpoints progress, and cleanly removes historical records without FK constraint errors.

### Execution Command
```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- purge --live --force -c configs/retention-policy.example.yaml
```

### Expected Results (Pass Criteria):
* [ ] **Execution Order:** Deletes child rows first, intermediate children second, and root parents last.
* [ ] **Zero Constraint Violations:** No foreign key conflict errors (`FK_Orders_Accounts`, `FK_OrderItems_Orders`).
* [ ] **Vitals Monitoring:** Displays live log utilization and active transaction count.
* [ ] **Database Row Counts After Purge:**
  * `SELECT COUNT(1) FROM Accounts;` $\longrightarrow$ **250** (All remaining are `Status = 'ACTIVE'`).
  * `SELECT COUNT(1) FROM Orders;` $\longrightarrow$ **500**.
  * `SELECT COUNT(1) FROM OrderItems;` $\longrightarrow$ **500**.
  * `SELECT COUNT(1) FROM Shipments;` $\longrightarrow$ **500**.
  * `SELECT COUNT(1) FROM AuditLogs;` $\longrightarrow$ **250**.
* [ ] **State Checkpoint:** `checkpoints/purge_state.db` updates with `COMPLETED` status.

---

## 🧪 Test Case 5: 1-Click Reverse Restore (`restore`)

### Objective
Verify disaster recovery: restore purged records from a Parquet archive back into SQL Server using `SqlBulkCopy` while preserving original primary key IDs.

### Execution Command
```powershell
# Restore Orders from the cold archive file
dotnet run --project src/DataLifecycleEngine.Cli -- restore -f ./archives/purge_inactive_accounts_and_orders/Orders_*.parquet -t Orders -c configs/retention-policy.example.yaml
```

### Expected Results (Pass Criteria):
* [ ] Reads columnar rows from the Parquet archive.
* [ ] Inserts records into `[dbo].[Orders]` with `KEEP_IDENTITY = true`.
* [ ] `SELECT COUNT(1) FROM Orders;` increases from 500 back to **1,000**.
* [ ] Restored primary key IDs match original IDs without collision.

---

## 🧪 Test Case 6: Interruption & Crash Resumption (`purge --resume`)

### Objective
Verify fault tolerance: simulate an unexpected shutdown (`Ctrl+C`), verify checkpoint persistence in SQLite, and resume execution without reprocessing completed batches.

### Test Steps:
1. Re-seed test database with `sqlserver-schema.sql`.
2. Start live purge with small micro-batch:
   ```powershell
   dotnet run --project src/DataLifecycleEngine.Cli -- purge --live --force --batch-size 100 -c configs/retention-policy.example.yaml
   ```
3. Press **`Ctrl+C`** mid-execution.
4. Verify engine catches `SIGINT`, commits active batch, and records checkpoint in `checkpoints/purge_state.db`.
5. Run resume command:
   ```powershell
   dotnet run --project src/DataLifecycleEngine.Cli -- purge --resume -c configs/retention-policy.example.yaml
   ```

### Expected Results (Pass Criteria):
* [ ] Engine reads the last completed table and batch ID from SQLite.
* [ ] Skips previously purged tables and resumes at the exact stopped record.
* [ ] Final purge completes with 100% data integrity and zero FK conflicts.

---

## 🧪 Test Case 7: Cryptographic Compliance & Audit Reporting (`certify`)

### Objective
Verify that the engine generates tamper-evident, digitally signed (HMAC-SHA256) JSON and QuestPDF compliance certificates.

### Execution Command
```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- certify -c configs/retention-policy.example.yaml
```

### Expected Results (Pass Criteria):
* [ ] Generates both `.audit.json` and `.audit.pdf` in `./audit-certificates/`.
* [ ] **PDF Quality Check:**
  * Open the PDF file.
  * Displays "VERIFIED AUDIT" header and Certificate ID.
  * Displays Operator name, database target, and timestamp in UTC.
  * Displays GDPR Art. 17, India DPDP Act 2023, HIPAA badges.
  * Summarizes row counts, Parquet file SHA-256 hashes, and storage savings.
  * Displays the Cryptographic Digital Seal (Canonical SHA-256 & HMAC signature).

---

## 🧪 Test Case 8: Tamper Verification & Non-Repudiation (`certify --verify`)

### Objective
Verify that any unauthorized alteration to archived files or certificate metrics is detected and rejected.

### Sub-case 8A: Valid Certificate Verification
```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- certify --verify audit-certificates/<CERT_NAME>.audit.json
```
* **Expected:** All 3 verification checks pass:
  * Payload Hash Integrity: **`PASS`**
  * HMAC-SHA256 Digital Signature: **`AUTHENTIC`**
  * Cold Archive Tamper Check: **`INTACT`** (All files match SHA-256 checksums).

### Sub-case 8B: Negative Testing - Tampered JSON Content
1. Open the `.audit.json` file in an editor.
2. Change `"totalRowsPurged"` from `1500` to `999999`. Save the file.
3. Run verification again:
   ```powershell
   dotnet run --project src/DataLifecycleEngine.Cli -- certify --verify audit-certificates/<CERT_NAME>.audit.json
   ```
* **Expected Result:**
  * Verification **`FAILS`** with exit code 1.
  * Flags: `[FAIL] Payload hash modified / Digital signature verification failed`.

### Sub-case 8C: Negative Testing - Tampered Parquet File on Disk
1. Revert the JSON file.
2. Open one of the `.parquet` archive files in a text editor and append a single character to corrupt its binary content.
3. Run verification again:
   ```powershell
   dotnet run --project src/DataLifecycleEngine.Cli -- certify --verify audit-certificates/<CERT_NAME>.audit.json
   ```
* **Expected Result:**
  * Verification **`FAILS`**.
  * Flags: `[COMPROMISED] Checksum corruption in <table.parquet>: Stored hash does not match file contents`.

---

## 📊 QA Test Execution Matrix

| Test ID | Test Scenario | CLI Command | Expected Result | Status |
| :--- | :--- | :--- | :--- | :--- |
| **TC-01** | Foreign Key & Schema Scan | `safe-purge scan` | Topo order: Leaves $\to$ Root; flags unindexed FKs | [ ] Pass / [ ] Fail |
| **TC-02** | Cold Parquet Archival | `safe-purge archive` | Columnar `.parquet` files with SHA-256 checksums | [ ] Pass / [ ] Fail |
| **TC-03** | Dry-Run Purge Simulation | `safe-purge purge --dry-run` | Zero rows deleted; candidate counts calculated | [ ] Pass / [ ] Fail |
| **TC-04** | Live Micro-Batch Purge | `safe-purge purge --live --force` | Cascade deletion; zero FK errors; log monitored | [ ] Pass / [ ] Fail |
| **TC-05** | 1-Click Reverse Restore | `safe-purge restore -f ...` | Restores rows into table; preserves identities | [ ] Pass / [ ] Fail |
| **TC-06** | Crash Resilience & Resume | `safe-purge purge --resume` | Resumes from SQLite checkpoint seamlessly | [ ] Pass / [ ] Fail |
| **TC-07** | Certificate Generation | `safe-purge certify` | Generates signed A4 PDF & JSON manifests | [ ] Pass / [ ] Fail |
| **TC-08** | Tamper Proof Verification | `safe-purge certify --verify ...` | Validates HMAC signature & on-disk SHA-256 | [ ] Pass / [ ] Fail |
| **TC-09** | Negative Tamper Detection | Modified JSON or Parquet | Detects unauthorized edits; rejects certificate | [ ] Pass / [ ] Fail |
