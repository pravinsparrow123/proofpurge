# ProofPurge

> **Autonomous, Zero-Downtime Relational Database Archiving, Foreign-Key Cascade Purging, and Cryptographic Compliance Engine for SQL Server and PostgreSQL.**

[![Build & Verify Engine](https://github.com/pravinsparrow123/proofpurge/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/pravinsparrow123/proofpurge/actions/workflows/build-and-test.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

---

## 📌 Master Documentation & Architecture Manuals

- 📖 **[Complete System Architecture & Operator Manual (docs/SYSTEM_DOCUMENTATION.md)](docs/SYSTEM_DOCUMENTATION.md)**: Exhaustive manual detailing all five engineering modules, Mermaid diagrams, algorithmic specifications, configuration schemas, CLI commands, enterprise automation, and pricing.
- 🧪 **[Manual QA Testing Workflow & Test Plan (docs/MANUAL_TESTING_WORKFLOW.md)](docs/MANUAL_TESTING_WORKFLOW.md)**: Step-by-step QA testing guide with 40,000-record seeded baseline and negative tamper-detection scenarios.
- 📊 **[Master Development Tasks (docs/DEVELOPMENT_TASKS.md)](docs/DEVELOPMENT_TASKS.md)**: Full project roadmap, phase breakdowns, and verification matrix (100% completed).
- 📐 **[System Specification & Blueprint (docs/DATA_LIFECYCLE_ENGINE_SPEC.md)](docs/DATA_LIFECYCLE_ENGINE_SPEC.md)**: Technical architecture, database catalog queries, and market positioning.

---

## 🚀 CLI Commands & Usage

The CLI tool `proofpurge` provides six subcommands:

### 1. Interactive Step-by-Step Setup Wizard (`init`)
Launches an interactive prompt wizard that asks you question-by-question to configure your database connection, tables, retention cutoff, and filters:
```bash
dotnet run --project src/DataLifecycleEngine.Cli -- init -o retention-policy.yaml
```

### 2. Catalog Inspection & Safety Scanner (`scan`)
Scans relational database catalogs (`sys.foreign_keys`, `sys.tables`), analyzes table bloat, detects missing indexes on foreign key columns, and computes the safe topological deletion pipeline (Leaves $\to$ Parents $\to$ Root).
```bash
dotnet run --project src/DataLifecycleEngine.Cli -- scan -c configs/retention-policy.example.yaml
```

### 3. Streaming Cold Parquet Archiver (`archive`)
Extracts candidate historical records across cascade tables using keyset queries and serializes them into Snappy-compressed columnar Parquet files with SHA-256 integrity checksums.
```bash
dotnet run --project src/DataLifecycleEngine.Cli -- archive -c configs/retention-policy.example.yaml
```

### 4. One-Click Reverse Restore (`restore`)
Restores archived records from a Parquet file back into the relational database using high-throughput bulk copy with identity preservation.
```bash
dotnet run --project src/DataLifecycleEngine.Cli -- restore -f ./archives/purge_inactive_accounts_and_orders/Orders_20261121_100036.parquet -t Orders -c configs/retention-policy.example.yaml
```

### 5. Zero-Lock Sentinel Micro-Batch Purging (`purge`)
Executes micro-batched cascade deletions (e.g. 2,000 rows per batch) in strict reverse topological order. Monitors `sys.dm_db_log_space_usage` in real time, automatically throttling execution if log utilization exceeds safety thresholds. Checkpoints progress in embedded SQLite for crash resilience and resume support.
```bash
# Dry-run simulation (zero impact)
dotnet run --project src/DataLifecycleEngine.Cli -- purge --dry-run -c configs/retention-policy.example.yaml

# Live micro-batched deletion
dotnet run --project src/DataLifecycleEngine.Cli -- purge --live --force -c configs/retention-policy.example.yaml

# Resuming an interrupted job
dotnet run --project src/DataLifecycleEngine.Cli -- purge --resume -c configs/retention-policy.example.yaml

# Live purge with automatic cryptographic compliance certification
dotnet run --project src/DataLifecycleEngine.Cli -- purge --live --force --certify -c configs/retention-policy.example.yaml
```

### 6. Cryptographic Compliance Certifier (`certify`)
Issues tamper-evident, digitally signed (HMAC-SHA256) JSON and PDF compliance certificates detailing purged records, archived Parquet file hashes, and compliance attestations under GDPR Article 17, India DPDP Act 2023, and HIPAA.
```bash
# Generate signed PDF and JSON certificates
dotnet run --project src/DataLifecycleEngine.Cli -- certify -c configs/retention-policy.example.yaml

# Verify certificate authenticity and storage file integrity
dotnet run --project src/DataLifecycleEngine.Cli -- certify --verify audit-certificates/purge_inactive_accounts_and_orders_CERT-20261121-C1A2A5C3.audit.json
```

---

## 🐳 Docker Deployment

Build and run in containerized environments:
```bash
docker build -t proofpurge:latest .
docker run --rm -v $(pwd)/configs:/app/configs -v $(pwd)/archives:/app/archives proofpurge:latest scan -c /app/configs/retention-policy.example.yaml
```

---

*ProofPurge &copy; 2026. Architected for zero downtime, data safety, and compliance.*
