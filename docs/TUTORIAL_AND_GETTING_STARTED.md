# Enterprise Data Lifecycle & Safe Purge Engine
## Getting Started & Step-by-Step Hands-On Tutorial

> **Autonomous, zero-downtime database archiving, foreign-key cascade purging, and compliance audit engine for enterprise relational databases (SQL Server & PostgreSQL).**

---

## 📋 Table of Contents
1. [Prerequisites & System Requirements](#-prerequisites--system-requirements)
2. [Step-by-Step Hands-On Tutorial](#-step-by-step-hands-on-tutorial)
   - [Step 1: Verify Build & Automated Tests](#step-1-verify-build--automated-tests)
   - [Step 2: Seed the Demo Database (LifecycleDemo)](#step-2-seed-the-demo-database-lifecycledemo)
   - [Step 3: Run the Schema Discovery & Safety Scanner (scan)](#step-3-run-the-schema-discovery--safety-scanner-scan)
   - [Step 4: Stream Candidate Records to Cold Parquet (archive)](#step-4-stream-candidate-records-to-cold-parquet-archive)
   - [Step 5: Simulate Purge with Zero Risk (purge --dry-run)](#step-5-simulate-purge-with-zero-risk-purge---dry-run)
   - [Step 6: Execute Zero-Lock Micro-Batched Purging (purge --live --force)](#step-6-execute-zero-lock-micro-batched-purging-purge---live---force)
   - [Step 7: Issue Cryptographic Compliance Certificate (certify)](#step-7-issue-cryptographic-compliance-certificate-certify)
   - [Step 8: Verify Certificate Tamper-Resistance (certify --verify)](#step-8-verify-certificate-tamper-resistance-certify---verify)
   - [Step 9: Reverse Restore Data with 1-Click (restore)](#step-9-reverse-restore-data-with-1-click-restore)
3. [Adapting for Your Production Database](#-adapting-for-your-production-database)
4. [Command Reference Cheatsheet](#-command-reference-cheatsheet)

---

## 🔧 Prerequisites & System Requirements

Everything required to run and test this project runs natively on your machine:

| Requirement | Description | Check Command |
| :--- | :--- | :--- |
| **.NET 8 SDK** | Modern, cross-platform C# runtime and compiler | `dotnet --version` (returns `8.x`, `9.x`, or `10.x`) |
| **Microsoft SQL Server** | The target relational database | `.\SQLEXPRESS` or `localhost` running as a service |
| **`sqlcmd` Utility** | Command-line utility to run SQL setup scripts | `sqlcmd -?` |
| **PowerShell / Terminal** | Shell to run CLI commands | Windows Terminal / PowerShell 7+ |

> [!NOTE]
> No Docker or cloud subscriptions are required for local testing. All compressed Parquet archives, SQLite checkpoints, and PDF compliance certificates are generated in local project directories.

---

## 🚀 Step-by-Step Hands-On Tutorial

Run all commands from the root of the repository: `d:\AntiGravity\Project DB`.

```powershell
cd "d:\AntiGravity\Project DB"
```

---

### Step 1: Verify Build & Automated Tests

Verify that the solution compiles cleanly and all 19 automated tests pass:

```powershell
dotnet test DataLifecycleEngine.slnx
```

**Expected Output:**
```text
Passed!  - Failed: 0, Passed: 19, Skipped: 0, Total: 19, Duration: ~600 ms
```

---

### Step 2: Seed the Demo Database (`LifecycleDemo`)

We have a sample SQL fixture that creates a 3-tier cascade schema and seeds 3,500 records:

```powershell
sqlcmd -S ".\SQLEXPRESS" -E -i "test\fixtures\sqlserver-schema.sql"
```

**What this creates in `.\SQLEXPRESS`:**
- **Database:** `LifecycleDemo`
- **Root Table:** `Accounts` (500 records: 250 active, 250 closed and older than 3 years)
- **Level 1 Children:** `Orders` (1,000 records), `AuditLogs` (500 records)
- **Level 2 Children:** `OrderItems` (1,000 records), `Shipments` (1,000 records)
- **Unindexed Foreign Key:** Deliberately leaves `AuditLogs.AccountId` unindexed to test our performance scanner.

---

### Step 3: Run the Schema Discovery & Safety Scanner (`scan`)

Inspect database catalogs, analyze table bloat, check for missing indexes, and calculate safe deletion order:

```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- scan -c configs/retention-policy.example.yaml
```

**What you will see:**
1. Auto-discovers 5 tables and 4 foreign key relationships.
2. Computes the safe reverse topological sort order:
   ```text
   Purge Pipeline
   ├── Step 1: OrderItems (Child Leaf)
   ├── Step 2: Shipments (Child Leaf)
   ├── Step 3: AuditLogs (Child Leaf)
   ├── Step 4: Orders (Child Leaf)
   └── Step 5: Accounts (Root Parent)
   ```
3. Estimates candidate purge records ($250$ accounts older than 3 years).
4. Generates an index performance warning:
   ```text
   ⚠ Missing index on foreign key column: AuditLogs.AccountId referencing Accounts.Id.
     Cascading deletes may cause full table scans!
   ```

---

### Step 4: Stream Candidate Records to Cold Parquet (`archive`)

Before deleting anything, stream all candidate records across all 5 cascade tables into compressed Parquet:

```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- archive -c configs/retention-policy.example.yaml
```

**What happens:**
- Extracts all candidate records across the foreign-key hierarchy.
- Compresses records into **Apache Parquet with Snappy compression** (achieving **50%–80% storage savings**).
- Computes **SHA-256 cryptographic digests** on-the-fly while streaming.
- Files are saved to: `./archives/purge_inactive_accounts_and_orders/`.

---

### Step 5: Simulate Purge with Zero Risk (`purge --dry-run`)

Simulate the micro-batch purge without deleting any data:

```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- purge --dry-run -c configs/retention-policy.example.yaml
```

**What happens:**
- Queries `sys.dm_db_log_space_usage` to verify initial transaction log capacity.
- Simulates batches and reports exact rows that would be deleted per table ($2,000$ total records across all tables).
- Confirms zero database modifications were made.

---

### Step 6: Execute Zero-Lock Micro-Batched Purging (`purge --live --force`)

Perform the live, safe cascading purge:

```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- purge --live --force -c configs/retention-policy.example.yaml
```

**What happens:**
- Deletes records in micro-batches (e.g. 500–2,000 rows) with `WITH (ROWLOCK)` to prevent table lock escalation.
- Checks database vitals before and after every batch:
  ```text
  Purging OrderItems | Batch: 500 rows | DB Log: 13.3%
  Purging Shipments  | Batch: 500 rows | DB Log: 15.0%
  Purging AuditLogs  | Batch: 250 rows | DB Log: 16.6%
  Purging Orders     | Batch: 500 rows | DB Log: 19.2%
  Purging Accounts   | Batch: 250 rows | DB Log: 20.8%
  ```
- Checkpoints progress in local SQLite (`purge_checkpoints.db`). If interrupted (`Ctrl+C`), running with `--resume` seamlessly resumes from the exact last primary key.

---

### Step 7: Issue Cryptographic Compliance Certificate (`certify`)

Generate legally binding proof of compliance (GDPR Art. 17, India DPDP Act 2023, HIPAA):

```powershell
dotnet run --project src/DataLifecycleEngine.Cli -- certify -c configs/retention-policy.example.yaml --secret "EnterpriseComplianceKey2026!"
```

**Deliverables created in `./audit-certificates/`:**
1. **`.audit.json`**: Machine-readable manifest containing operator ID, timestamp, table metrics, and SHA-256 digests.
2. **`.audit.pdf`**: Auditor-ready certificate rendered using QuestPDF with compliance seals, tables, and HMAC digital signatures.

---

### Step 8: Verify Certificate Tamper-Resistance (`certify --verify`)

Verify that a generated certificate is authentic and Parquet files have not been modified:

```powershell
# Pick the JSON certificate generated in Step 7:
$cert = (Get-ChildItem -Path "./audit-certificates" -Filter "*.json" | Select-Object -First 1).FullName

dotnet run --project src/DataLifecycleEngine.Cli -- certify --verify "$cert" --secret "EnterpriseComplianceKey2026!"
```

**Output:**
```text
✓ Certificate Signature Valid: HMAC matches secret key
✓ All Parquet archive hashes verified on disk: ZERO tampering detected
```

---

### Step 9: Reverse Restore Data with 1-Click (`restore`)

Verify complete reversibility by restoring archived records back into SQL Server:

```powershell
# 1. Pick an archived Parquet file:
$file = (Get-ChildItem -Path "./archives/purge_inactive_accounts_and_orders" -Filter "Accounts_*.parquet" | Select-Object -First 1).FullName

# 2. Create a test destination table:
sqlcmd -S ".\SQLEXPRESS" -d "LifecycleDemo" -E -Q "IF OBJECT_ID('dbo.Accounts_Restored', 'U') IS NOT NULL DROP TABLE dbo.Accounts_Restored; SELECT * INTO dbo.Accounts_Restored FROM dbo.Accounts WHERE 1 = 0;"

# 3. Restore records:
dotnet run --project src/DataLifecycleEngine.Cli -- restore -f "$file" -t Accounts_Restored -c configs/retention-policy.example.yaml

# 4. Verify restored record count:
sqlcmd -S ".\SQLEXPRESS" -d "LifecycleDemo" -E -Q "SELECT COUNT(*) AS [ExactRestoredCount] FROM dbo.Accounts_Restored;"
```

**Result:**
The exact 250 records are restored with original IDs preserved!

---

## ⚙️ Adapting for Your Production Database

To run this against your real production or staging database, edit `configs/retention-policy.example.yaml`:

```yaml
version: "1.0"
target_database:
  driver: "sqlserver"
  connection_string: "Server=YourServer;Database=YourDB;Trusted_Connection=True;TrustServerCertificate=True;"

storage_sink:
  type: "local"
  local_path: "./archives"
  compression: "snappy"

safety_thresholds:
  max_batch_size: 2000
  throttle_pause_ms: 1000
  max_transaction_log_percent: 65.0
  max_replication_lag_seconds: 3

policies:
  - name: "purge_old_orders"
    description: "Archive & purge completed orders older than 2 years"
    root_table: "Orders"
    retention_cutoff: "2y" # Supported: 90d (days), 6m (months), 3y (years)
    date_column: "OrderDate"
    where_clause: "Status = 'COMPLETED'"
    cascade_children:
      - table: "OrderItems"
        foreign_key: "OrderId"
      - table: "Shipments"
        foreign_key: "OrderId"
```

---

## ⚡ Command Reference Cheatsheet

```bash
# 1. Inspect schema & missing indexes
dotnet run --project src/DataLifecycleEngine.Cli -- scan -c configs/retention-policy.example.yaml

# 2. Stream historical data to Parquet
dotnet run --project src/DataLifecycleEngine.Cli -- archive -c configs/retention-policy.example.yaml

# 3. Dry-run purge simulation (zero impact)
dotnet run --project src/DataLifecycleEngine.Cli -- purge --dry-run -c configs/retention-policy.example.yaml

# 4. Execute live micro-batched purge
dotnet run --project src/DataLifecycleEngine.Cli -- purge --live --force -c configs/retention-policy.example.yaml

# 5. Resume interrupted purge job
dotnet run --project src/DataLifecycleEngine.Cli -- purge --resume -c configs/retention-policy.example.yaml

# 6. Issue signed audit certificate (PDF & JSON)
dotnet run --project src/DataLifecycleEngine.Cli -- certify -c configs/retention-policy.example.yaml --secret "YourSecretKey"

# 7. Verify certificate integrity
dotnet run --project src/DataLifecycleEngine.Cli -- certify --verify ./audit-certificates/cert.json --secret "YourSecretKey"

# 8. Restore from Parquet archive
dotnet run --project src/DataLifecycleEngine.Cli -- restore -f ./archives/archive.parquet -t TargetTable -c configs/retention-policy.example.yaml
```
