import { test, expect } from '@playwright/test';

test.describe('ProofPurge End-to-End Lifecycle Demo', () => {
  test('walks through Scan -> Archive -> Dry-Run -> Live Purge -> Certify -> Verify', async ({ page }) => {
    // 1. Open ProofPurge Cockpit
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'ProofPurge Cockpit' })).toBeVisible();
    await expect(page.getByTestId('driver-badge')).toHaveText('SQLSERVER');
    await expect(page.getByTestId('policy-name')).toHaveText('purge_inactive_accounts_and_orders');

    // 2. Phase 1: Scan Catalog & Foreign-Key DAG
    await page.getByTestId('btn-scan').click();
    const scanSection = page.getByTestId('scan-section');
    await expect(scanSection).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('dag-flow')).toContainText('OrderItems');
    await expect(page.getByTestId('dag-flow')).toContainText('Accounts (Root)');
    await expect(page.getByTestId('scan-warnings')).toContainText('AuditLogs.AccountId');
    await expect(page.getByTestId('scan-table-body')).toContainText('Accounts');

    // 3. Phase 2: Archive Candidate Rows to Apache Parquet
    await page.getByTestId('btn-archive').click();
    const archiveSection = page.getByTestId('archive-section');
    await expect(archiveSection).toBeVisible({ timeout: 30_000 });
    await expect(page.getByTestId('archive-table-body')).toContainText('.parquet');

    // 4. Phase 3A: Dry-Run Simulation
    await page.getByTestId('btn-dry-run').click();
    const purgeSection = page.getByTestId('purge-section');
    await expect(purgeSection).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('purge-summary')).toContainText('DRY-RUN completed');

    // 5. Phase 3B: Live Zero-Lock Micro-Batch Purge
    await page.getByTestId('btn-live-purge').click();
    await expect(page.getByTestId('purge-summary')).toContainText('LIVE PURGE completed', { timeout: 30_000 });
    await expect(page.getByTestId('run-status')).toHaveText('Live Purge Complete');

    // 6. Phase 4A: Issue Cryptographic Compliance Certificate (PDF + JSON)
    await page.getByTestId('btn-certify').click();
    const certSection = page.getByTestId('cert-section');
    await expect(certSection).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId('cert-id')).toContainText('CERT-');
    await expect(page.getByTestId('cert-pdf')).toContainText('.audit.pdf');

    // 7. Phase 4B: Verify HMAC-SHA256 Signature & Disk Parquet Integrity
    const verifyBtn = page.getByTestId('btn-verify');
    await expect(verifyBtn).toBeEnabled();
    await verifyBtn.click();
    await expect(page.getByTestId('verify-status-box')).toContainText('AUTHENTIC & INTACT', { timeout: 20_000 });
  });
});
