# ProofPurge Playwright E2E Demo

Automated browser walkthrough demonstrating all 4 ProofPurge phases through the **ProofPurge Web Cockpit** (`proofpurge ui`):
1. **Scan Catalog & DAG** (`sys.foreign_keys`, topological Leaf $\to$ Root order, missing index warnings)
2. **Archive to Parquet** (Snappy compression + SHA-256 checksums)
3. **Dry-Run Purge** (Zero-impact simulation)
4. **Live Zero-Lock Purge** (Micro-batched cascade deletes + transaction log vitals)
5. **Issue Certificate** (Signed `.audit.json` & QuestPDF `.audit.pdf`)
6. **Verify Authenticity** (HMAC-SHA256 signature + disk Parquet integrity)

## Run the Demo

```powershell
# 1. Seed fresh demo records (40,000 rows, 20,000 purge candidates)
sqlcmd -S ".\SQLEXPRESS" -i ..\test\fixtures\sqlserver-schema.sql

# 2. Install dependencies & Chromium browser (first time only)
npm install
npx playwright install chromium

# 3. Watch the live demo in a visible browser
npm run test:headed

# 4. Or run headless and open the HTML report + recorded video
npm test
npm run report
```
