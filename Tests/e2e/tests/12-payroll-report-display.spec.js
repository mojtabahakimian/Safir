// @ts-check
const { test, expect } = require('@playwright/test');
const fs = require('fs');

// Read-only regression for the restored YAZDSEPAR1405 copy, Shahrivar run 57.
// Supply an authorized user's token; never recalculate or modify payroll here.
const token = process.env.SAFIR_PAYROLL_REPORT_TOKEN;
const settings = process.env.SAFIR_PAYROLL_REPORT_DB_SETTINGS;

test.describe('نمایش حقوق و گزارش بیمه شهریور', () => {
  test.skip(!token, 'Supply SAFIR_PAYROLL_REPORT_TOKEN for the restored customer copy.');

  test.beforeEach(async ({ page }) => {
    await page.addInitScript(({ token, settings }) => {
      sessionStorage.setItem('safir.tabInit', '1');
      sessionStorage.setItem('authToken', JSON.stringify(token));
      if (settings) sessionStorage.setItem('dbConnectionSettings', settings);
    }, { token, settings });
    await page.goto('/salary/manage');
    await page.getByText('محاسبه حقوق', { exact: true }).click();
    await expect(page.getByText('1405 - 06 - شهریور آماده محاسبه', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'ورود به میز کار محاسباتی', exact: false }).click();
    await expect(page.getByRole('button', { name: 'چاپ لیست بیمه', exact: false })).toBeVisible();
  });

  test('جمع کسورات شامل بیمه است و خالص واقعی سه نفر حفظ می‌شود', async ({ page }) => {
    await expect(page.getByRole('columnheader', { name: 'جمع کسورات', exact: false })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'مساعده/وام/سایر کسورات', exact: false })).toHaveCount(0);
    for (const [code, name, totalDed, net] of [
      ['549', 'دبیلی نصرآبادی محمد حسین', '41,089,117', '233,969,000'],
      ['675', 'حجتی سعید', '18,032,252', '205,765,000'],
      ['676', 'حجتی وحید', '17,873,481', '205,924,000'],
    ]) {
      await page.getByRole('searchbox', { name: 'جستجو', exact: true }).fill(code);
      await page.getByRole('searchbox', { name: 'جستجو', exact: true }).press('Enter');
      const row = page.getByRole('row').filter({ hasText: name });
      await expect(row).toHaveCount(1);
      await expect(row).toContainText(totalDed);
      await expect(row).toContainText(net);
    }
  });

  test('گزارش بیمه بدون اتکا به ابزار PDF مرورگر قابل دانلود است', async ({ page }) => {
    await page.getByRole('button', { name: 'چاپ لیست بیمه', exact: false }).click();
    const link = page.getByRole('link', { name: 'دانلود PDF', exact: true });
    await expect(link).toBeVisible();
    await expect(link).toHaveAttribute('download', 'Report.pdf');
    const downloadPromise = page.waitForEvent('download');
    await link.click();
    const path = await (await downloadPromise).path();
    expect(path).toBeTruthy();
    const bytes = fs.readFileSync(path);
    expect(bytes.subarray(0, 4).toString()).toBe('%PDF');
    expect(bytes.length).toBeGreaterThan(1000);
  });
});
