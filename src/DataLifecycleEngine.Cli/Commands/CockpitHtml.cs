namespace DataLifecycleEngine.Cli.Commands;

internal static class CockpitHtml
{
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>ProofPurge Cockpit — Enterprise Data Lifecycle &amp; Safe Purge</title>
  <style>
    :root {
      --bg: #0b1120;
      --panel: #111827;
      --panel-border: #1f2937;
      --text: #f3f4f6;
      --muted: #9ca3af;
      --accent: #38bdf8;
      --green: #22c55e;
      --amber: #f59e0b;
      --red: #ef4444;
      --purple: #a855f7;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
      background: var(--bg);
      color: var(--text);
      line-height: 1.5;
    }
    header {
      background: #0f172a;
      border-bottom: 1px solid var(--panel-border);
      padding: 16px 28px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 12px;
    }
    .brand { display: flex; align-items: center; gap: 12px; }
    .brand h1 { margin: 0; font-size: 20px; letter-spacing: 0.3px; }
    .badge {
      font-size: 12px;
      padding: 3px 10px;
      border-radius: 999px;
      background: rgba(56, 189, 248, 0.15);
      color: var(--accent);
      border: 1px solid rgba(56, 189, 248, 0.35);
      font-weight: 600;
    }
    .container { max-width: 1200px; margin: 24px auto; padding: 0 20px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 14px; margin-bottom: 20px; }
    .card {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 10px;
      padding: 16px;
    }
    .card-label { font-size: 12px; color: var(--muted); text-transform: uppercase; letter-spacing: 0.6px; }
    .card-value { font-size: 22px; font-weight: 700; margin-top: 4px; }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
      margin-bottom: 20px;
    }
    button {
      border: none;
      border-radius: 8px;
      padding: 10px 16px;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      color: #0b1120;
      transition: opacity 0.15s ease, transform 0.05s ease;
    }
    button:hover { opacity: 0.92; }
    button:active { transform: translateY(1px); }
    button:disabled { opacity: 0.5; cursor: not-allowed; }
    .btn-scan { background: var(--accent); }
    .btn-archive { background: #34d399; }
    .btn-dry { background: var(--amber); }
    .btn-live { background: #fb7185; }
    .btn-certify { background: #c084fc; }
    .btn-verify { background: var(--green); }
    .section {
      background: var(--panel);
      border: 1px solid var(--panel-border);
      border-radius: 10px;
      padding: 18px;
      margin-bottom: 18px;
    }
    .section h2 { margin: 0 0 12px 0; font-size: 16px; color: var(--accent); }
    .dag-flow { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-bottom: 14px; }
    .dag-node {
      padding: 6px 12px;
      border-radius: 6px;
      background: #1e293b;
      border: 1px solid #334155;
      font-size: 13px;
      font-weight: 600;
    }
    .dag-node.root { border-color: #fb7185; color: #fda4af; }
    .dag-arrow { color: var(--muted); font-weight: 700; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid var(--panel-border); }
    th { color: var(--muted); font-weight: 600; font-size: 12px; text-transform: uppercase; }
    .warn-box {
      margin-top: 12px;
      padding: 10px 14px;
      border-radius: 8px;
      background: rgba(245, 158, 11, 0.12);
      border: 1px solid rgba(245, 158, 11, 0.35);
      color: #fcd34d;
      font-size: 13px;
    }
    .ok-box {
      margin-top: 12px;
      padding: 12px 14px;
      border-radius: 8px;
      background: rgba(34, 197, 94, 0.12);
      border: 1px solid rgba(34, 197, 94, 0.35);
      color: #86efac;
      font-size: 13px;
    }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12px; word-break: break-all; }
    .status-pill {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
      background: rgba(34, 197, 94, 0.18);
      color: #4ade80;
    }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <h1>ProofPurge Cockpit</h1>
      <span class="badge" data-testid="driver-badge">Loading...</span>
    </div>
    <div style="font-size: 13px; color: var(--muted);" data-testid="policy-subtitle">
      Loading policy configuration...
    </div>
  </header>

  <main class="container">
    <div class="grid">
      <div class="card">
        <div class="card-label">Active Policy</div>
        <div class="card-value" style="font-size: 16px;" data-testid="policy-name">—</div>
      </div>
      <div class="card">
        <div class="card-label">Root Table &amp; Cutoff</div>
        <div class="card-value" style="font-size: 16px;" data-testid="policy-rule">—</div>
      </div>
      <div class="card">
        <div class="card-label">Transaction Log Usage</div>
        <div class="card-value" data-testid="log-usage">—</div>
      </div>
      <div class="card">
        <div class="card-label">Last Run Status</div>
        <div class="card-value" style="font-size: 16px;" data-testid="run-status">Ready</div>
      </div>
    </div>

    <div class="actions">
      <button class="btn-scan" data-testid="btn-scan" onclick="runScan()">1. Scan Catalog &amp; DAG</button>
      <button class="btn-archive" data-testid="btn-archive" onclick="runArchive()">2. Archive to Parquet</button>
      <button class="btn-dry" data-testid="btn-dry-run" onclick="runPurge(true)">3. Dry-Run Purge</button>
      <button class="btn-live" data-testid="btn-live-purge" onclick="runPurge(false)">4. Live Zero-Lock Purge</button>
      <button class="btn-certify" data-testid="btn-certify" onclick="runCertify()">5. Issue Certificate</button>
      <button class="btn-verify" data-testid="btn-verify" onclick="runVerify()" disabled>6. Verify Authenticity</button>
    </div>

    <section class="section" data-testid="scan-section" style="display:none;">
      <h2>Phase 1 — Foreign-Key DAG &amp; Candidate Scan</h2>
      <div class="dag-flow" data-testid="dag-flow"></div>
      <table>
        <thead>
          <tr><th>Table</th><th>Est. Rows</th><th>Size (MB)</th><th>Candidate Purge Rows</th></tr>
        </thead>
        <tbody data-testid="scan-table-body"></tbody>
      </table>
      <div data-testid="scan-warnings"></div>
    </section>

    <section class="section" data-testid="archive-section" style="display:none;">
      <h2>Phase 2 — Cold Apache Parquet Archives</h2>
      <table>
        <thead>
          <tr><th>Table</th><th>Rows</th><th>Compression</th><th>File</th><th>SHA-256 Checksum</th></tr>
        </thead>
        <tbody data-testid="archive-table-body"></tbody>
      </table>
    </section>

    <section class="section" data-testid="purge-section" style="display:none;">
      <h2>Phase 3 — Adaptive Zero-Lock Purge Sentinel</h2>
      <div class="ok-box" data-testid="purge-summary"></div>
      <table style="margin-top: 12px;">
        <thead>
          <tr><th>Table</th><th>Status</th><th>Rows Purged</th><th>Duration</th></tr>
        </thead>
        <tbody data-testid="purge-table-body"></tbody>
      </table>
    </section>

    <section class="section" data-testid="cert-section" style="display:none;">
      <h2>Phase 4 — Cryptographic Compliance Certificate &amp; Verification</h2>
      <div class="card" style="background: #0f172a;">
        <div><strong>Certificate ID:</strong> <span data-testid="cert-id">—</span></div>
        <div><strong>Issued UTC:</strong> <span data-testid="cert-issued">—</span></div>
        <div><strong>Rows Archived / Purged:</strong> <span data-testid="cert-counts">—</span></div>
        <div><strong>Payload SHA-256:</strong> <span class="mono" data-testid="cert-hash">—</span></div>
        <div><strong>HMAC-SHA256 Signature:</strong> <span class="mono" data-testid="cert-sig">—</span></div>
        <div><strong>PDF Report:</strong> <span class="mono" data-testid="cert-pdf">—</span></div>
      </div>
      <div data-testid="verify-result"></div>
    </section>
  </main>

  <script>
    let latestCertJsonPath = null;

    async function loadStatus() {
      const res = await fetch('/api/status');
      const data = await res.json();
      document.querySelector('[data-testid="driver-badge"]').textContent = data.driver.toUpperCase();
      document.querySelector('[data-testid="policy-subtitle"]').textContent = `Batch: ${data.maxBatchSize} | Log Threshold: ${data.maxLogPercent}%`;
      document.querySelector('[data-testid="policy-name"]').textContent = data.policyName;
      document.querySelector('[data-testid="policy-rule"]').textContent = `${data.rootTable} (${data.dateColumn} < ${data.retentionCutoff})`;
      document.querySelector('[data-testid="log-usage"]').textContent = `${data.vitals.usedLogSpacePercent.toFixed(2)}%`;
    }

    function setBusy(label) {
      document.querySelector('[data-testid="run-status"]').textContent = label;
    }

    async function runScan() {
      setBusy('Scanning catalog...');
      const res = await fetch('/api/scan', { method: 'POST' });
      const data = await res.json();
      const sec = document.querySelector('[data-testid="scan-section"]');
      sec.style.display = 'block';

      const flow = document.querySelector('[data-testid="dag-flow"]');
      flow.innerHTML = data.deletionOrder.map((t, idx) => {
        const isLast = idx === data.deletionOrder.length - 1;
        const node = `<span class="dag-node ${isLast ? 'root' : ''}">${idx + 1}. ${t}${isLast ? ' (Root)' : ''}</span>`;
        return isLast ? node : node + '<span class="dag-arrow">→</span>';
      }).join('');

      document.querySelector('[data-testid="scan-table-body"]').innerHTML = data.tables.map(t =>
        `<tr><td><strong>${t.name}</strong></td><td>${t.estimatedRows.toLocaleString()}</td><td>${t.sizeMb.toFixed(2)}</td><td><strong>${t.candidateRows.toLocaleString()}</strong></td></tr>`
      ).join('');

      const warnEl = document.querySelector('[data-testid="scan-warnings"]');
      warnEl.innerHTML = (data.warnings || []).map(w => `<div class="warn-box">⚠ ${w}</div>`).join('');
      setBusy('Scan Complete');
    }

    async function runArchive() {
      setBusy('Archiving to Parquet...');
      const res = await fetch('/api/archive', { method: 'POST' });
      const data = await res.json();
      document.querySelector('[data-testid="archive-section"]').style.display = 'block';
      document.querySelector('[data-testid="archive-table-body"]').innerHTML = data.archives.map(a =>
        `<tr><td><strong>${a.tableName}</strong></td><td>${a.rowCount.toLocaleString()}</td><td>${a.compressionRatio.toFixed(1)}%</td><td class="mono">${a.fileName}</td><td class="mono">${a.sha256Checksum}</td></tr>`
      ).join('');
      setBusy('Archive Complete');
    }

    async function runPurge(dryRun) {
      setBusy(dryRun ? 'Simulating Dry-Run...' : 'Executing Live Purge...');
      const res = await fetch(dryRun ? '/api/purge/dry-run' : '/api/purge/live', { method: 'POST' });
      const data = await res.json();
      document.querySelector('[data-testid="purge-section"]').style.display = 'block';
      document.querySelector('[data-testid="purge-summary"]').textContent =
        `${data.isDryRun ? 'DRY-RUN' : 'LIVE PURGE'} completed: ${data.totalRowsPurged.toLocaleString()} rows in ${data.totalDurationSeconds.toFixed(2)}s`;
      document.querySelector('[data-testid="purge-table-body"]').innerHTML = data.tables.map(t =>
        `<tr><td><strong>${t.tableName}</strong></td><td><span class="status-pill">${t.status}</span></td><td>${t.rowsPurged.toLocaleString()}</td><td>${t.durationSeconds.toFixed(2)}s</td></tr>`
      ).join('');
      await loadStatus();
      setBusy(dryRun ? 'Dry-Run Complete' : 'Live Purge Complete');
    }

    async function runCertify() {
      setBusy('Issuing Certificate...');
      const res = await fetch('/api/certify', { method: 'POST' });
      const data = await res.json();
      latestCertJsonPath = data.jsonPath;
      document.querySelector('[data-testid="cert-section"]').style.display = 'block';
      document.querySelector('[data-testid="cert-id"]').textContent = data.certificateId;
      document.querySelector('[data-testid="cert-issued"]').textContent = data.issuedAtUtc;
      document.querySelector('[data-testid="cert-counts"]').textContent = `${data.totalRowsArchived.toLocaleString()} archived / ${data.totalRowsPurged.toLocaleString()} purged`;
      document.querySelector('[data-testid="cert-hash"]').textContent = data.payloadSha256Hash;
      document.querySelector('[data-testid="cert-sig"]').textContent = data.digitalSignature;
      document.querySelector('[data-testid="cert-pdf"]').textContent = data.pdfPath;
      document.querySelector('[data-testid="btn-verify"]').disabled = false;
      setBusy('Certificate Issued');
    }

    async function runVerify() {
      setBusy('Verifying Signature & Archives...');
      const res = await fetch('/api/verify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jsonPath: latestCertJsonPath })
      });
      const data = await res.json();
      document.querySelector('[data-testid="verify-result"]').innerHTML =
        `<div class="${data.isValid ? 'ok-box' : 'warn-box'}" data-testid="verify-status-box">` +
        `<strong>${data.isValid ? '✓ AUTHENTIC & INTACT' : '✗ VERIFICATION FAILED'}</strong> — ${data.message} ` +
        `(${data.verifiedFileCount}/${data.totalFiles} archive files verified)</div>`;
      setBusy(data.isValid ? 'Verified Authentic' : 'Verification Failed');
    }

    loadStatus();
  </script>
</body>
</html>
""";
}
