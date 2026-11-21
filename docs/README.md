# Enterprise Data Lifecycle & Safe Purge Engine

Autonomous, zero-downtime database archiving, foreign-key cascade purging, and compliance audit engine for SQL Server and PostgreSQL.

---

## 📌 Project Documentation & Specification

- **[TUTORIAL_AND_GETTING_STARTED.md](TUTORIAL_AND_GETTING_STARTED.md)**: Hands-on, step-by-step tutorial with prerequisites, verified terminal commands, and outputs.
- **[DEVELOPMENT_TASKS.md](DEVELOPMENT_TASKS.md)**: Master engineering task breakdown, acceptance criteria, deliverables, and progress tracking across all phases.
- **[SYSTEM_DOCUMENTATION.md](SYSTEM_DOCUMENTATION.md)**: Complete system manual documenting all four engineering phases, algorithms, CLI commands, and test suites.
- **[DATA_LIFECYCLE_ENGINE_SPEC.md](DATA_LIFECYCLE_ENGINE_SPEC.md)**: Full technical architecture, market positioning, and implementation specification.
- **[configs/retention-policy.example.yaml](../configs/retention-policy.example.yaml)**: Declarative YAML retention policy schema.

---

## 🚀 CLI Commands & Usage

The CLI tool `safe-purge` provides five commands:

### 1. Catalog Inspection & Safety Scanner (`scan`)
Scans relational database catalogs (`sys.foreign_keys`, `sys.tables`), analyzes table bloat, detects missing indexes on foreign key columns, and computes the safe topological deletion pipeline (Leaves $\to$ Parents $\to$ Root).
```bash
dotnet run --project src/DataLifecycleEngine.Cli -- scan -c configs/retention-policy.example.yaml
```

### 2. Streaming Cold Parquet Archiver (`archive`)
Extracts candidate historical records across cascade tables using keyset queries and serializes them into Snappy-compressed columnar Parquet files with SHA-256 integrity checksums.
```bash
dotnet run --project src/DataLifecycleEngine.Cli -- archive -c configs/retention-policy.example.yaml
```

### 3. One-Click Reverse Restore (`restore`)
Restores archived records from a Parquet file back into the relational database using high-throughput bulk copy with identity preservation.
```bash
dotnet run --project src/DataLifecycleEngine.Cli -- restore -f ./archives/policy/Orders.parquet -t Orders -c configs/retention-policy.example.yaml
```

### 4. Zero-Lock Sentinel Micro-Batch Purging (`purge`)
Executes micro-batched cascade deletions (e.g. 2,000 rows per batch) in strict reverse topological order. Monitors `sys.dm_db_log_space_usage` in real time, automatically throttling execution if log utilization exceeds safety thresholds. Checkpoints progress in embedded SQLite for crash resilience and resume support.
```bash
# Dry-run simulation (zero impact)
dotnet run --project src/DataLifecycleEngine.Cli -- purge --dry-run -c configs/retention-policy.example.yaml

# Live micro-batched deletion
dotnet run --project src/DataLifecycleEngine.Cli -- purge --live --force -c configs/retention-policy.example.yaml

# Resuming an interrupted job
dotnet run --project src/DataLifecycleEngine.Cli -- purge --resume -c configs/retention-policy.example.yaml
```

### 5. Cryptographic Compliance Certifier (`certify`)
Issues tamper-evident, digitally signed (HMAC-SHA256) JSON and PDF compliance certificates detailing purged records, archived Parquet file hashes, and compliance attestations under GDPR Article 17, India DPDP Act 2023, and HIPAA.
```bash
# Generate signed PDF and JSON certificates
dotnet run --project src/DataLifecycleEngine.Cli -- certify -c configs/retention-policy.example.yaml

# Verify certificate authenticity and storage file integrity
dotnet run --project src/DataLifecycleEngine.Cli -- certify --verify audit-certificates/purge_policy.audit.json
```

---

## 🐳 Docker Deployment

Build and run in containerized environments:
```bash
docker build -t safe-purge:latest .
docker run --rm -v $(pwd)/configs:/app/configs -v $(pwd)/archives:/app/archives safe-purge:latest scan
```
