# ProofPurge User Guide

> **Welcome to ProofPurge!**  
> This guide walks you through installing `proofpurge` on your local machine, connecting to your own SQL Server or PostgreSQL database, setting up retention rules, safely archiving old records to Parquet, purging without downtime, and generating signed compliance certificates.

---

## 1. Local Installation

### Option A: Run Directly as a Local CLI (`proofpurge`)
If you have the .NET 8 SDK installed, you can install ProofPurge once and run `proofpurge` from **any folder** on your machine:

```powershell
# 1. Package the CLI tool
dotnet pack src/DataLifecycleEngine.Cli/DataLifecycleEngine.Cli.csproj -c Release -o ./nupkg

# 2. Install globally on your machine
dotnet tool update --global --add-source ./nupkg ProofPurge.Cli
```

Verify it works from anywhere:
```powershell
proofpurge --help
```

### Option B: Build a Standalone Single-File Executable (No .NET Required on Target PC)
```powershell
# Windows (64-bit)
dotnet publish src/DataLifecycleEngine.Cli/DataLifecycleEngine.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./dist/win-x64

# Linux (64-bit)
dotnet publish src/DataLifecycleEngine.Cli/DataLifecycleEngine.Cli.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o ./dist/linux-x64
```
You get a single `proofpurge.exe` (or `proofpurge`) binary you can copy to any server.

---

## 2. Step-by-Step Usage Workflow

```
proofpurge init  ──▶  proofpurge scan  ──▶  proofpurge archive  ──▶  proofpurge purge  ──▶  proofpurge certify
 (Create Rules)       (Safe Preview)       (Cold Parquet Backup)    (Zero-Lock Delete)     (PDF + JSON Proof)
```

### Step 1: Set Up Your Rules (`proofpurge init`)
Run the interactive setup wizard — no manual YAML editing needed:

```powershell
proofpurge init -o my-policy.yaml
```

The wizard asks you step-by-step:
1. **Database Engine:** `sqlserver` or `postgres`.
2. **Connection String:** Your DB connection string (or `${DB_CONNECTION_STRING}` for environment variables).
3. **Policy Name:** A label for your cleanup job (e.g. `cleanup_old_orders`).
4. **Root Table:** The parent table to evaluate (e.g. `Accounts` or `Orders`).
5. **Date Column:** Which timestamp column measures record age (e.g. `LastActivityDate`, `CreatedAt`).
6. **Retention Cutoff:** How old records must be before archival/purge (`30d`, `90d`, `12m`, `3y`, `7y`).
7. **Filter Condition (Optional):** SQL `WHERE` filter (e.g. `Status = 'CLOSED'`, or leave blank for all old rows).
8. **Action & Storage Sink:** `archive_and_purge`, `dry_run`, or `archive_only`; `local`, `s3`, or `azure`.
9. **Live Foreign-Key Auto-Discovery:** Connects to your database and automatically maps all child/grandchild tables in `cascade_children:` for you!

> [!TIP]
> **Keeping Passwords Out of Files:** Set `connection_string: "${DB_CONNECTION_STRING}"` in your YAML and export it in your shell:
> ```powershell
> $env:DB_CONNECTION_STRING = "Server=localhost;Database=MyAppDB;User Id=purger;Password=StrongPassword!;"
> ```

---

### Step 2: Inspect & Preview Safely (`proofpurge scan`)
Before touching a single row, run a read-only scan:

```powershell
proofpurge scan -c my-policy.yaml
```

What you'll see:
* **Topological Deletion Pipeline:** Exact order from deepest leaf tables up to the root parent (so foreign-key constraints never fail).
* **Row & Size Estimates:** Current row counts, table sizes (MB), and exact candidate purge counts per table.
* **Performance Warnings:** Alerts you if any child foreign-key column is missing an index (which could slow down deletes).

---

### Step 3: Archive Old Records to Parquet (`proofpurge archive`)
Extract matching historical records into Snappy-compressed `.parquet` files before deleting them:

```powershell
proofpurge archive -c my-policy.yaml
```

* Saves `.parquet` files to `./archives/<policy_name>/`.
* Prints compression savings (typically 80–90%) and a 64-character **SHA-256 checksum** for each file.

---

### Step 4: Simulate, Then Run Live Purge (`proofpurge purge`)

1. **Always simulate first (0 rows deleted):**
   ```powershell
   proofpurge purge --dry-run -c my-policy.yaml
   ```

2. **Run the live zero-lock purge:**
   ```powershell
   proofpurge purge --live --force --certify -c my-policy.yaml
   ```
   * Deletes in micro-batches (default `2,000` rows per batch) using row locks so your live app stays responsive.
   * Monitors transaction log usage (`sys.dm_db_log_space_usage`) and automatically pauses if log usage crosses `65%`.
   * Saves progress in `./checkpoints/purge_state.db`.

3. **If interrupted (`Ctrl+C` or reboot), resume safely:**
   ```powershell
   proofpurge purge --resume -c my-policy.yaml
   ```

---

### Step 5: Generate & Verify Compliance Certificates (`proofpurge certify`)
If you didn't pass `--certify` during `purge`, generate your audit report anytime:

```powershell
proofpurge certify -c my-policy.yaml
```

This creates two files in `./audit-certificates/`:
* **`.audit.pdf`:** Auditor-ready PDF certificate (GDPR Art. 17, India DPDP Act 2023, HIPAA) with metrics, Parquet hashes, and HMAC-SHA256 seal.
* **`.audit.json`:** Machine-readable signed manifest.

**Verify a certificate & check that archive files haven't been modified:**
```powershell
proofpurge certify --verify ./audit-certificates/<certificate-file>.audit.json
```

---

### Step 6: Restore Data Anytime (`proofpurge restore`)
Need archived records back in the database? Restore any Parquet archive with original IDs preserved:

```powershell
proofpurge restore -f ./archives/<policy_name>/Orders_20261121_114205.parquet -t Orders -c my-policy.yaml
```
*(Add `--schema myschema` if your table is not in `dbo`).*

---

## 3. Quick Command Cheatsheet

| Task | Command |
| :--- | :--- |
| Create config interactively | `proofpurge init -o my-policy.yaml` |
| Preview tables & candidate rows | `proofpurge scan -c my-policy.yaml` |
| Archive cold data to Parquet | `proofpurge archive -c my-policy.yaml` |
| Dry-run purge simulation | `proofpurge purge --dry-run -c my-policy.yaml` |
| Live purge + auto-certify | `proofpurge purge --live --force --certify -c my-policy.yaml` |
| Resume interrupted purge | `proofpurge purge --resume -c my-policy.yaml` |
| Verify audit certificate | `proofpurge certify --verify <file.audit.json>` |
| Restore Parquet file to DB | `proofpurge restore -f <file.parquet> -t <Table> -c my-policy.yaml` |
