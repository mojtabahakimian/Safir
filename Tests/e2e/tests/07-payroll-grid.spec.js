// @ts-check
/**
 * جدول «محاسبه حقوق» در مرورگر: ستون‌های ریل اسمی و دکمه‌ی یکی‌شده‌ی «فیش پرداخت».
 *
 * داده را خودش نمی‌سازد؛ روی آخرین کارگاهی کار می‌کند که 03-payroll-chain ساخته است:
 *   ۱۴۰۵/۰۶ — محاسبه‌ی عادی، تأیید نهایی و سند (STATUS ≥ 2)
 *   ۱۴۰۵/۰۷ — پرسنل فقط بیمه‌ای (DAYS=30, DAYSB=0)، محاسبه‌شده ولی تأییدنشده (STATUS = 1)
 * اگر آن کارگاه نباشد skip می‌شود، نه fail.
 *
 * مقدار ستون‌ها روی API در 03 (گام ۷-الف) بررسی شده؛ اینجا فقط چیزی است که کاربر می‌بیند.
 */
const { test, expect } = require('@playwright/test');
const { databaseAvailable, apiLogin, uiLogin } = require('../helpers/app');

test.describe.configure({ mode: 'serial' });

let workshopName = '';

test.beforeAll(async ({ request }) => {
  if (!await databaseAvailable(request)) return;
  const auth = { Authorization: `Bearer ${await apiLogin(request, 'admin')}` };
  const list = await (await request.get('/api/pay2/workshops', { headers: auth })).json();
  const chain = list
    .map(w => ({ id: w.wS_ID ?? w.WS_ID, name: w.wS_NAME ?? w.WS_NAME }))
    .filter(w => String(w.name).startsWith('کارگاه آزمون سرتاسری'))
    .sort((a, b) => b.id - a.id)[0];
  if (chain) workshopName = chain.name;
});

test.beforeEach(async ({ request }) => {
  test.skip(!await databaseAvailable(request),
    'دیتابیس تست در دسترس نیست — ابتدا scripts/setup-test-env.sh را اجرا کنید.');
  test.skip(!workshopName, 'کارگاه آزمون زنجیره نیست — اول 03-payroll-chain.spec.js را اجرا کنید.');
});

async function openPeriod(page, periodLabel) {
  await uiLogin(page, 'admin');
  await page.goto('/salary/manage', { waitUntil: 'networkidle' });
  await page.locator('li.pay2-nav-item').filter({ hasText: 'محاسبه حقوق' }).first().click();

  const ws = page.locator('.form-field').filter({ has: page.locator('label', { hasText: 'انتخاب کارگاه' }) })
    .locator('input.pay2-select-input');
  await ws.locator('xpath=..').locator('.pay2-select-arrow-button').click();
  await page.locator('.pay2-select-menu .pay2-select-item').filter({ hasText: workshopName }).first().click();

  await page.locator('.period-card-item').filter({ hasText: periodLabel })
    .getByRole('button', { name: /ورود به میز کار محاسباتی/ }).click();
  await expect(page.locator('.e-row').first()).toBeVisible();
}

/** مقدار یک ستون در ردیف اول، از روی عنوان ستون — ترتیب ستون‌های پویا ثابت نیست. */
async function cell(page, header) {
  const headers = (await page.locator('.e-headercell').allInnerTexts()).map(h => h.split('\n')[0].trim());
  const i = headers.indexOf(header);
  expect(i, `ستون «${header}»`).toBeGreaterThanOrEqual(0);
  const text = await page.locator('.e-row').first().locator('td').nth(i).innerText();
  return Number(text.replace(/[۰-۹]/g, d => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(d))).replace(/[^\d.-]/g, ''));
}

test('فقط بیمه‌ای، تأییدنشده: کارکرد رسمی ۰، اسمی ۳۰، حقوق روزانه اسمی > ۰، فیش غیرفعال', async ({ page }) => {
  await openPeriod(page, '1405 - 07 - مهر');

  expect(await cell(page, 'کارکرد رسمی (پرداخت)')).toBe(0);
  expect(await cell(page, 'کارکرد اسمی (بیمه)')).toBe(30);
  expect(await cell(page, 'حقوق روزانه اسمی')).toBeGreaterThan(0);
  expect(await cell(page, 'ناخالص اسمی')).toBeGreaterThan(0);
  await expect(page.getByText('«حقوق روزانه اسمی» مبلغ اسمی است')).toBeVisible();

  const slip = page.getByRole('button', { name: /فیش پرداخت/ });
  await expect(slip).toHaveCount(1);
  await expect(slip).toBeDisabled();
  await expect(slip).toHaveAttribute('title', 'پس از تأیید نهایی، فیش قابل چاپ است');
});

test('تأییدشده: یک دکمه‌ی «فیش پرداخت» فعال که فیش رسمی را باز می‌کند', async ({ page }) => {
  await openPeriod(page, '1405 - 06 - شهریور');

  expect(await cell(page, 'کارکرد اسمی (بیمه)')).toBeGreaterThan(0);
  const slip = page.getByRole('button', { name: /فیش پرداخت/ });
  await expect(slip).toHaveCount(1);
  await expect(page.getByRole('button', { name: /^\W*(اسمی|رسمی)$/ }), 'دو دکمه‌ی قدیمی اسمی/رسمی نباید باشند').toHaveCount(0);
  await expect(slip).toBeEnabled();

  await slip.click();
  await expect(page.locator('.mud-dialog').getByText(/فیش پرداخت رسمی -/)).toBeVisible();
});
