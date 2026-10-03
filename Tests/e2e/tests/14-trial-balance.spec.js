// @ts-check
const { test, expect } = require('@playwright/test');
const crypto = require('crypto');
const { collectPageErrors, ignorableWithoutDb } = require('../helpers/app');

/**
 * تراز آزمایشی چهارستونی — بدون دیتابیس.
 *
 * عددها را رویه‌های دیتابیسِ مشتری می‌سازند (dbo.TARAZ_4 و …) که در دیتابیسِ تست
 * نیستند؛ پس اینجا فقط api/trial-balance را در مرورگر شبیه‌سازی می‌کنیم و خودِ
 * صفحه را واقعاً می‌رانیم: رندر، رفتن از کل به معین و تفصیلی و تفصیلی ۲، برگشت
 * با مسیر، جمع‌ها، جستجو، لینکِ صورت‌حساب و پیامِ نداشتنِ دسترسی.
 * درستیِ نام و پارامترِ رویه‌ها را TrialBalanceServiceTests ثابت می‌کند.
 */

// همان کلید/صادرکننده/مخاطبِ Server/appsettings.json (و TestJwt.cs)
const JWT_KEY = process.env.Jwt__Key ||
  'hsgmvbpZbXTbxfHk7x+03c6Zq/K5j0NpVgdJIMYYXanQAnstSOpMoFSHExygq1LKYG2+XYLCfAmmr50UKBTclg==';

function jwt(userCo = 9001, name = 'tbtester') {
  const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  const body = b64({ alg: 'HS256', typ: 'JWT' }) + '.' + b64({
    sub: String(userCo), unique_name: name,
    'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier': String(userCo),
    'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name': name,
    UUSER: name, IDD: String(userCo), GRSAL: '1',
    iss: 'SafirAppIssuer', aud: 'SafirAppAudience', nbf: now - 60, exp: now + 3600, iat: now,
  });
  return body + '.' + crypto.createHmac('sha256', Buffer.from(JWT_KEY, 'utf8')).update(body).digest('base64url');
}

const KOL = [
  { kol: 104, name: 'موجودی کالا', sumBed: 900000, sumBes: 300000, bed: 600000, bes: 0 },
  { kol: 201, name: 'حساب‌های پرداختنی', sumBed: 100000, sumBes: 700000, bed: 0, bes: 600000 },
];
const MOIN = [{ kol: 104, moin: 9, name: 'انبار مواد', sumBed: 900000, sumBes: 300000, bed: 600000, bes: 0 }];
const TAF = [
  { kol: 104, moin: 9, tafsili: 2044, name: 'خامه ۴۵٪', sumBed: 500000, sumBes: 100000, bed: 400000, bes: 0 },
  { kol: 104, moin: 9, tafsili: 328, name: 'خامه ۶۵٪', sumBed: 400000, sumBes: 200000, bed: 200000, bes: 0 },
];
const TAF2 = [{ kol: 104, moin: 9, tafsili: 2044, tafsili2: 3, name: 'بخش سرد', sumBed: 500000, sumBes: 100000, bed: 400000, bes: 0 }];
// تفصیلیِ «همه‌ی معین‌ها»: تفصیلی‌ها زیر معین‌های مختلف
const TAF_ALL = [
  { kol: 104, moin: 9, tafsili: 2044, name: 'خامه ۴۵٪', sumBed: 500000, sumBes: 100000, bed: 400000, bes: 0 },
  { kol: 104, moin: 3, tafsili: 15, name: 'شیر خام', sumBed: 90000, sumBes: 0, bed: 90000, bes: 0 },
];
const KOLS = [{ number: 104, name: 'موجودی کالا' }, { number: 201, name: 'حساب‌های پرداختنی' }];
const MOINS = [{ number: 9, name: 'انبار مواد' }, { number: 3, name: 'انبار شیر' }];
// ماهانه: (حساب، ماه) با مانده‌ی خالصِ همان ماه
const M_KOL = [
  { kol: 104, name: 'موجودی کالا', ym: 140505, bed: 700000, bes: 0 },
  { kol: 104, name: 'موجودی کالا', ym: 140506, bed: 0, bes: 100000 },
  { kol: 201, name: 'حساب‌های پرداختنی', ym: 140506, bed: 0, bes: 600000 },
];
const M_MOIN = [{ kol: 104, moin: 9, name: 'انبار مواد', ym: 140505, bed: 700000, bes: 0 }];

// روی context، نه page: تبِ چاپ (window.open) هم باید همین توکن و همین پاسخ‌ها را ببیند.
async function boot(page, meta, calls = []) {
  const token = jwt();
  await page.context().addInitScript(t => {
    sessionStorage.setItem('authToken', JSON.stringify(t));
    localStorage.setItem('authToken', JSON.stringify(t));
  }, token);
  await page.context().route('**/api/trial-balance/meta', r => r.fulfill({ json: { companyName: 'شرکت آزمایشی', ...meta } }));
  await page.context().route(/\/api\/trial-balance\/accounts/, r =>
    r.fulfill({ json: new URL(r.request().url()).searchParams.get('kol') ? MOINS : KOLS }));
  await page.context().route(/\/api\/trial-balance\/monthly\?/, r => {
    const u = new URL(r.request().url());
    calls.push({ monthly: true, ...Object.fromEntries(u.searchParams) });
    return r.fulfill({ json: { Kol: M_KOL, Moin: M_MOIN }[u.searchParams.get('level')] || [] });
  });
  await page.context().route(/\/api\/trial-balance\?/, r => {
    const u = new URL(r.request().url());
    const level = u.searchParams.get('level');
    calls.push(Object.fromEntries(u.searchParams));
    const data = level === 'Tafsili' && u.searchParams.get('allMoins') === 'true'
      ? TAF_ALL
      : { Kol: KOL, Moin: MOIN, Tafsili: TAF, Tafsili2: TAF2 }[level] || [];
    return r.fulfill({ json: data });
  });
  await page.goto('/trial-balance');
}

const META = { fiscalYear: 1405, canKol: true, canMoin: true, canTafsili: true };

test('تراز کل ← معین ← تفصیلی ← تفصیلی ۲ و برگشت با مسیر', async ({ page }) => {
  const errors = collectPageErrors(page);
  const calls = [];
  await boot(page, META, calls);

  await expect(page.getByRole('tab', { name: 'چهارستونی کل' })).toBeVisible({ timeout: 90_000 });
  await page.getByRole('button', { name: 'نمایش تراز کل' }).click();

  const rows = page.locator('.tb-table tbody tr');
  await expect(rows).toHaveCount(2);
  await expect(page.locator('.tb-table tfoot')).toContainText('1,000,000');
  await expect(page.locator('.trs-bar__stats')).toContainText('تراز است');
  expect(calls[0]).toMatchObject({ level: 'Kol', from: '14050101' });

  // کل ← معین
  await rows.filter({ hasText: 'موجودی کالا' }).locator('.tb-next').click();
  await expect(rows).toHaveCount(1);
  await expect(page.locator('.tb-crumb.is-on')).toContainText('کل');
  await expect(page.locator('.tb-crumb.is-on')).toContainText('موجودی کالا');
  await expect(page.locator('.tb-count')).toContainText('معین');
  await expect(page.locator('.trs-bar__stats')).not.toContainText('تراز است');
  expect(calls.at(-1)).toMatchObject({ level: 'Moin', kol: '104' });

  // معین ← تفصیلی (با دوبار کلیک روی سطر، مثل WPF)
  await rows.first().dblclick();
  await expect(rows).toHaveCount(2);
  expect(calls.at(-1)).toMatchObject({ level: 'Tafsili', kol: '104', moin: '9' });
  // تفصیلی لینکِ صورت‌حساب دارد
  await expect(rows.filter({ hasText: 'خامه ۴۵٪' }).locator('a[href="/customer-statement/104-9-2044"]')).toHaveCount(1);

  // جستجو
  await page.getByPlaceholder('شماره یا نام حساب…').fill('۶۵');
  await expect(rows).toHaveCount(1);
  await page.getByPlaceholder('شماره یا نام حساب…').fill('');

  // تفصیلی ← تفصیلی ۲
  await rows.filter({ hasText: 'خامه ۴۵٪' }).locator('.tb-next').click();
  await expect(rows).toHaveCount(1);
  expect(calls.at(-1)).toMatchObject({ level: 'Tafsili2', kol: '104', moin: '9', tafsili: '2044' });
  await expect(page.locator('a[href="/customer-statement/104-9-2044-3"]')).toHaveCount(1);
  await expect(page.locator('.tb-crumb')).toHaveCount(4);

  // برگشت به کل از مسیر — بدون درخواستِ دوباره
  const before = calls.length;
  await page.locator('.tb-crumb').first().click();
  await expect(rows).toHaveCount(2);
  await expect(page.locator('.tb-crumb')).toHaveCount(1);
  expect(calls.length).toBe(before);

  // بدون دیتابیس، سرویس‌های دیگرِ برنامه (مثلاً تنظیماتِ سازمان) در پس‌زمینه ۴۰۴/۵۰۰ می‌گیرند و
  // بسته به زمان‌بندی در کنسول خطا می‌نویسند؛ اینجا فقط خطای خودِ صفحه و تراز مهم است.
  expect(errors.filter(e => !ignorableWithoutDb(e) &&
    (e.startsWith('pageerror:') || /TrialBalance|trial-balance/i.test(e)))).toEqual([]);
});

test('بدون دسترسیِ معین، کل دیده می‌شود ولی ریز نمی‌شود', async ({ page }) => {
  await boot(page, { ...META, canMoin: false, canTafsili: false });
  await page.getByRole('button', { name: 'نمایش تراز کل' }).click({ timeout: 90_000 });
  await expect(page.locator('.tb-table tbody tr')).toHaveCount(2);
  await expect(page.locator('.tb-next')).toHaveCount(0);
});

test('بدون هیچ دسترسیِ تراز، پیامِ فارسی و بدون دکمه‌ی نمایش', async ({ page }) => {
  await boot(page, { ...META, canKol: false, canMoin: false, canTafsili: false });
  await expect(page.getByText('اجازه‌ی دیدن تراز آزمایشی را ندارید')).toBeVisible({ timeout: 90_000 });
  await expect(page.getByRole('button', { name: /نمایش/ })).toBeDisabled();
});

test('بدون دسترسیِ کل، تراز کل و ماهانه بسته‌اند ولی معین باز است', async ({ page }) => {
  await boot(page, { ...META, canKol: false });
  await expect(page.getByRole('tab', { name: 'چهارستونی کل' })).toBeDisabled({ timeout: 90_000 });
  await expect(page.getByRole('tab', { name: 'تراز ماهانه' })).toBeDisabled();
  await expect(page.getByRole('tab', { name: 'تراز معین' })).toBeEnabled();
});

test('کلیدهای «این ماه» و «ماه قبل» بازه را می‌گذارند و همان‌جا تراز را می‌سازند', async ({ page }) => {
  const calls = [];
  await boot(page, META, calls);
  await page.getByRole('button', { name: 'این ماه' }).click({ timeout: 90_000 });
  await expect(page.locator('.tb-table tbody tr')).toHaveCount(2);
  const first = calls.at(-1);
  expect(first.level).toBe('Kol');
  expect(first.from.endsWith('01')).toBe(true);
  expect(first.to.endsWith('31')).toBe(true);

  await page.getByRole('button', { name: 'ماه قبل' }).click();
  await expect.poll(() => calls.length).toBe(2);
  expect(calls[1].from).not.toBe(first.from);
  await page.getByRole('button', { name: 'از ابتدای سال' }).click();
  await expect.poll(() => calls.length).toBe(3);
  expect(calls[2]).toMatchObject({ from: '14050101', to: '14051230' });
});

test('تراز معین و تراز تفصیلیِ همه‌ی معین‌ها مستقیم از فیلتر', async ({ page }) => {
  const calls = [];
  await boot(page, META, calls);
  const rows = page.locator('.tb-table tbody tr');

  await page.getByRole('tab', { name: 'تراز معین' }).click({ timeout: 90_000 });
  await page.getByRole('button', { name: 'نمایش تراز معین' }).click();
  await expect(page.getByText('حساب کل را انتخاب کنید')).toBeVisible();   // بدون کل درخواستی نمی‌رود
  expect(calls.length).toBe(0);
  await page.locator('.tb-f--acc select').first().selectOption('104');
  await page.getByRole('button', { name: 'نمایش تراز معین' }).click();
  await expect(rows).toHaveCount(1);
  expect(calls.at(-1)).toMatchObject({ level: 'Moin', kol: '104' });
  await expect(page.locator('.tb-crumb.is-on')).toContainText('معین‌های 104');

  await page.getByRole('tab', { name: 'تراز تفصیلی' }).click();
  await page.locator('.tb-f--acc select').first().selectOption('104');
  await page.getByRole('button', { name: 'نمایش تراز تفصیلی' }).click();
  await expect(rows).toHaveCount(2);
  expect(calls.at(-1)).toMatchObject({ level: 'Tafsili', kol: '104', allMoins: 'true' });
  await expect(rows.first()).toContainText('3/15');   // معین/تفصیلی چون معین‌ها مختلف‌اند
  await expect(page.locator('a[href="/customer-statement/104-3-15"]')).toHaveCount(1);

  // یک معینِ مشخص
  await page.locator('.tb-f--acc select').nth(1).selectOption('9');
  await page.getByRole('button', { name: 'نمایش تراز تفصیلی' }).click();
  await expect.poll(() => calls.at(-1).moin).toBe('9');
  expect(calls.at(-1).allMoins).toBeUndefined();
});

test('تراز ماهانه: ماه‌ها ستون‌اند و از کل به معین ریز می‌شود', async ({ page }) => {
  const calls = [];
  await boot(page, META, calls);
  await page.getByRole('tab', { name: 'تراز ماهانه' }).click({ timeout: 90_000 });
  await page.getByRole('button', { name: 'نمایش تراز ماهانه' }).click();

  const table = page.locator('.tb-table--month');
  await expect(table.locator('thead')).toContainText('مرداد');
  await expect(table.locator('thead')).toContainText('شهریور');
  const kol104 = table.locator('tbody tr').filter({ hasText: 'موجودی کالا' });
  await expect(kol104.locator('td.tot')).toHaveText('600,000');      // ۷۰۰ هزار بد − ۱۰۰ هزار بس
  await expect(kol104.locator('td.tot')).toHaveClass(/bed/);
  await expect(table.locator('tfoot td.tot')).toHaveText('—');        // جمعِ کل‌ها صفر: تراز
  expect(calls.at(-1)).toMatchObject({ monthly: true, level: 'Kol' });

  await kol104.locator('.tb-next').click();
  await expect(table.locator('tbody tr')).toHaveCount(1);
  expect(calls.at(-1)).toMatchObject({ monthly: true, level: 'Moin', kol: '104' });
});

test('چاپ در هر سطح همان جدول را در صفحه‌ی A4 می‌آورد', async ({ page, context }) => {
  await boot(page, META);
  await page.getByRole('button', { name: 'نمایش تراز کل' }).click({ timeout: 90_000 });
  await page.locator('.tb-table tbody tr').filter({ hasText: 'موجودی کالا' }).locator('.tb-next').click();
  await page.locator('.tb-table tbody tr').first().dblclick();       // تفصیلی
  await page.getByPlaceholder('شماره یا نام حساب…').fill('۴۵');

  const [print] = await Promise.all([context.waitForEvent('page'), page.getByRole('button', { name: 'چاپ' }).click()]);
  await print.waitForLoadState();
  const sheet = print.locator('.tb-sheet');
  await expect(sheet).toBeVisible({ timeout: 90_000 });
  await expect(sheet.locator('h1')).toContainText('تفصیلی');
  await expect(sheet).toContainText('شرکت آزمایشی');
  await expect(sheet).toContainText('104 موجودی کالا');                // مسیر
  await expect(sheet.locator('tbody tr')).toHaveCount(1);              // همان جستجو
  await expect(sheet.locator('tbody')).toContainText('خامه ۴۵٪');
  expect(new URL(print.url()).searchParams.get('level')).toBe('Tafsili');

  // ماهانه افقی چاپ می‌شود
  await page.getByRole('tab', { name: 'تراز ماهانه' }).click();
  await page.getByRole('button', { name: 'نمایش تراز ماهانه' }).click();
  const [mprint] = await Promise.all([context.waitForEvent('page'), page.getByRole('button', { name: 'چاپ' }).click()]);
  await expect(mprint.locator('.tb-landscape .tb-sheet')).toBeVisible({ timeout: 90_000 });
  await expect(mprint.locator('.tb-sheet thead')).toContainText('مرداد');
});
