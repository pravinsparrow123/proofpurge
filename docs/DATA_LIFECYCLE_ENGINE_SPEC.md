# Enterprise Data Lifecycle & Safe Purge Engine (Specification & Blueprint)

> **Autonomous, zero-downtime database archiving, foreign-key cascade purging, and compliance audit engine for SQL Server and PostgreSQL.**

---

## 1. Executive Summary & Value Proposition

- **The Problem:** Enterprises accumulate massive historical data (audit logs, transactions, inactive customer records) across high-cost relational databases. Unmanaged data leads to ballooning cloud/hardware bills, slow query execution, multi-hour backup windows, and severe regulatory fines under laws like GDPR, India's DPDP Act, and HIPAA.
- **Why Naive Approaches Fail:** DBAs cannot safely run simple `DELETE` queries because bulk deletes cause table locks, exhaust transaction logs (`.ldf` / WAL), break replication, or fail due to deep foreign-key dependency trees.
- **The Solution:** A self-hosted, lightweight engine (single Go binary or Docker container) running inside the enterprise network that:
  1. Auto-discovers foreign-key dependency trees and calculates safe deletion order.
  2. Archives records to compressed, queryable Parquet files in AWS S3 or Azure Blob before deleting.
  3. Executes micro-batched, zero-lock deletes with real-time database vitals monitoring (pauses automatically if transaction log space or replication lag spikes).
  4. Issues cryptographically signed audit certificates proving compliance for legal teams.

---

## 2. System Architecture

```
                                 [ RETENTION POLICY ]
                                 (retention.yaml)
                                        |
                                        v
+-------------------------------------------------------------------------------+
|  SAFE DATA LIFECYCLE ENGINE (Runs inside Client VPC / On-Prem Network)       |
|                                                                               |
|  [ 1. Catalog Inspector ]  --->  [ 2. DAG Dependency Solver ]                 |
|     Reads sys.foreign_keys          Topological sort (Leaves -> Parents)     |
|                                                                               |
|  [ 3. Streaming Archiver ] --->  [ 4. Cold Storage Sink ]                     |
|     Keyset paging queries           Compressed Parquet to S3/Azure Blob      |
|                                                                               |
|  [ 5. Adaptive Purger ]    <---  [ 6. Database Health Sentinel ]              |
|     Micro-batch DELETEs             Monitors log space & locks in real-time   |
|                                                                               |
|  [ 7. Audit Cryptographer ]                                                   |
|     SHA-256 Checksums + Signed Compliance PDF/JSON Report                     |
+-------------------------------------------------------------------------------+
           |                                     |                     |
           v                                     v                     v
   [ Production Database ]               [ Object Storage ]    [ Compliance Audit ]
   (SQL Server / Postgres)               (AWS S3 / Azure Blob) (Signed PDF Report)
```

---

## 3. Core Technical Modules

### 3.1. Foreign Key Discovery & Topological Sort (The DAG Engine)
- Queries SQL Server catalog: `sys.foreign_keys`, `sys.foreign_key_columns`, `sys.tables`.
- Queries PostgreSQL catalog: `information_schema.table_constraints`, `information_schema.key_column_usage`.
- Builds a Directed Acyclic Graph (DAG) and executes **Kahn's Algorithm** (Topological Sort).
- Order of execution:
  $$\text{Leaf Tables (Deepest Children)} \longrightarrow \text{Intermediate Children} \longrightarrow \text{Root Parent}$$

### 3.2. Streaming Parquet Archiver (Safety Net)
- Uses keyset pagination (`WHERE id > @last_id AND created_at < @cutoff ORDER BY id ASC`) to avoid memory-heavy `OFFSET` scans.
- Encodes rows into columnar Apache Parquet with Snappy or ZSTD compression (achieving 85–90% storage savings).
- Streams multipart uploads directly to AWS S3, Azure Blob, or local NAS/SAN.
- Provides a one-click reverse restore CLI:
  ```bash
  engine restore --file s3://bucket/archive-2021.parquet --table Orders
  ```

### 3.3. Zero-Lock Adaptive Purge Sentinel
- Executes deletions in micro-batches:
  ```sql
  -- SQL Server Keyset Micro-Batch
  DELETE TOP (2000) FROM Orders 
  WHERE CreatedAt < @CutoffDate;
  ```
- **Real-time Vitals Monitoring:**
  - SQL Server: Monitors `sys.dm_db_log_space_usage`. If `used_log_space_in_percent > 65%`, pauses and triggers `WAITFOR DELAY '00:00:05'`.
  - PostgreSQL: Monitors replication lag via `pg_stat_replication`.
- **Graceful Resumption:** Handles `SIGINT` (Ctrl+C), completes the active 2,000-row batch, checkpoints progress in an embedded SQLite database, and resumes without re-processing.

### 3.4. Cryptographic Compliance Auditing
- Computes SHA-256 checksums of every generated Parquet archive.
- Generates a PDF certificate detailing:
  - Exact timestamp, retention rule ID, and operator identity.
  - Rows purged and archived per table.
  - Cryptographic hash proof that the cold archive matches the deleted rows.

---

## 4. Retention Policy Configuration Schema (`retention.yaml`)

```yaml
version: "1.0"
target_database:
  driver: "sqlserver"
  connection_string: "${DB_CONNECTION_STRING}"

storage_sink:
  type: "s3"
  bucket: "enterprise-cold-vault"
  prefix: "archives/sqlserver-prod"
  region: "us-east-1"
  compression: "snappy"

safety_thresholds:
  max_batch_size: 2000
  throttle_pause_ms: 1500
  max_transaction_log_percent: 65.0
  max_replication_lag_seconds: 3

policies:
  - name: "purge_inactive_accounts_and_orders"
    description: "Anonymize/Purge records inactive for > 3 years (DPDP / GDPR)"
    root_table: "Accounts"
    retention_cutoff: "3y"
    date_column: "LastActivityDate"
    where_clause: "Status = 'CLOSED'"
    action: "archive_and_purge" # Options: dry_run | archive_only | archive_and_purge | anonymize
    cascade_children:
      - table: "Orders"
        foreign_key: "account_id"
        children:
          - table: "OrderItems"
            foreign_key: "order_id"
          - table: "Shipments"
            foreign_key: "order_id"
      - table: "AuditLogs"
        foreign_key: "account_id"
```

---

## 5. Development Roadmap (8-Week Plan)

- **Weeks 1–2:** Read-Only Discovery Scanner CLI (Outputs table bloat, estimated storage cost savings, and foreign-key dependency tree).
- **Weeks 3–4:** Streaming Parquet Archiver + S3/Azure Blob multipart upload + 1-click restore command.
- **Weeks 5–6:** Zero-Lock Adaptive Purge Engine with transaction log space monitoring and state checkpointing.
- **Weeks 7–8:** Cryptographic compliance PDF generator + embedded browser UI cockpit + Docker packaging.
