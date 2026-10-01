// @ts-check
const { test, expect } = require('@playwright/test');
const { field } = require('../helpers/app');

// Read-only regression against the restored customer copy. Supply a test-server
// token for mohsen e (IDD=181) and, optionally, its connection settings.
const token = process.env.SAFIR_ACCOUNT_ACCESS_TOKEN;
const dbSettings = process.env.SAFIR_ACCOUNT_ACCESS_DB_SETTINGS;
const username = process.env.SAFIR_ACCOUNT_ACCESS_USERNAME;
const password = process.env.SAFIR_ACCOUNT_ACCESS_PASSWORD;
const headers = () => ({
  Authorization: `Bearer ${token}`,
  ...(dbSettings ? { 'X-DB-Connection': Buffer.from(dbSettings).toString('base64') } : {}),
});

test.describe('محدودیت حساب‌های ویزیتور', () => {
  test.skip(!token, 'Supply SAFIR_ACCOUNT_ACCESS_TOKEN for a read-only test on the customer copy.');

  test('فهرست عمومی فقط استثناهای مجاز محسن را برمی‌گرداند', async ({ request }) => {
    const response = await request.get('/api/customers/list-for-user?pageSize=500', { headers: headers() });
    expect(response.ok()).toBeTruthy();
    const result = await response.json();
    expect(result.items.length).toBeGreaterThan(0);
    for (const row of result.items) {
      const code = row.hes;
      expect(code === '115-33' || code.startsWith('115-33-') ||
        code === '115-5-26-3545' || code.startsWith('115-5-26-3545-')).toBeTruthy();
    }
    expect(result.items.some(row => row.hes === '115-1-1037')).toBeFalsy();
  });

  test('صورت حساب و هر دو مسیر PDF حساب ممنوع را رد می‌کنند', async ({ request }) => {
    for (const suffix of ['statement', 'statement/pdf']) {
      const response = await request.get(`/api/customers/115-1-1037/${suffix}`, { headers: headers() });
      expect(response.status()).toBe(403);
      expect(await response.text()).toContain('اجازه دسترسی');
    }
    const report = await request.post('/api/reports/generate', {
      headers: headers(),
      data: { reportName: './R_DAFTAR_TAFZILY_2_2.mrt', parameters: { HESAB: '115-1-1037' } },
    });
    expect(report.status()).toBe(403);
  });

  test('حساب مجاز بدون گردش، پاسخ موفق می‌گیرد', async ({ request }) => {
    const response = await request.get('/api/customers/115-33/statement', { headers: headers() });
    expect(response.status()).toBe(200);
    expect(await response.json()).toEqual([]);
  });

  test('ورود و فهرست مشتریان در مرورگر حساب ممنوع را نشان نمی‌دهد', async ({ page }) => {
    await page.addInitScript(({ token, settings, login }) => {
      sessionStorage.setItem('safir.tabInit', '1');
      if (!login) sessionStorage.setItem('authToken', JSON.stringify(token));
      if (settings) sessionStorage.setItem('dbConnectionSettings', settings);
    }, { token, settings: dbSettings, login: Boolean(username && password) });
    if (username && password) {
      await page.goto('/login', { waitUntil: 'networkidle' });
      await field(page, 'نام کاربری').fill(username);
      await field(page, 'رمز عبور').fill(password);
      await page.getByRole('button', { name: 'ورود', exact: true }).click();
      await expect(page).not.toHaveURL(/\/login/);
    }
    await page.goto('/visitor-customers', { waitUntil: 'networkidle' });
    await expect(page.locator('.customer-card').first()).toBeVisible();
    await expect(page.getByText('115-1-1037', { exact: true })).toHaveCount(0);
    await field(page, 'جستجوی مشتری').fill('115-1-1037');
    await expect(page.getByText('مشتری با عبارت جستجو شده یافت نشد.', { exact: true })).toBeVisible();
    await expect(page.locator('.customer-card')).toHaveCount(0);
  });

  test('مرورگر عدم دسترسی را با نبود گردش اشتباه نمی‌گیرد', async ({ page }) => {
    await page.addInitScript(({ token, settings }) => {
      sessionStorage.setItem('safir.tabInit', '1');
      sessionStorage.setItem('authToken', JSON.stringify(token));
      if (settings) sessionStorage.setItem('dbConnectionSettings', settings);
    }, { token, settings: dbSettings });
    await page.goto('/customer-statement/115-1-1037', { waitUntil: 'networkidle' });
    await expect(page.getByText('شما اجازه دسترسی به این حساب را ندارید.', { exact: true })).toBeVisible();
    await expect(page.getByText(/هیچ موردی برای نمایش/)).toHaveCount(0);
    await page.goto('/customer-statement/115-33', { waitUntil: 'networkidle' });
    await expect(page.getByText(/هیچ موردی برای نمایش/)).toBeVisible();
    await expect(page.getByText('شما اجازه دسترسی به این حساب را ندارید.', { exact: true })).toHaveCount(0);
  });
});
