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

async function boot(page, meta, calls = []) {
  const token = jwt();
  await page.addInitScript(t => {
    sessionStorage.setItem('authToken', JSON.stringify(t));
    localStorage.setItem('authToken', JSON.stringify(t));
  }, token);
  await page.route('**/api/trial-balance/meta', r => r.fulfill({ json: meta }));
  await page.route(/\/api\/trial-balance\?/, r => {
    const u = new URL(r.request().url());
    const level = u.searchParams.get('level');
    calls.push(Object.fromEntries(u.searchParams));
    const data = { Kol: KOL, Moin: MOIN, Tafsili: TAF, Tafsili2: TAF2 }[level] || [];
    return r.fulfill({ json: data });
  });
  await page.goto('/trial-balance');
}

const META = { fiscalYear: 1405, canKol: true, canMoin: true, canTafsili: true };

test('تراز کل ← معین ← تفصیلی ← تفصیلی ۲ و برگشت با مسیر', async ({ page }) => {
  const errors = collectPageErrors(page);
  const calls = [];
  await boot(page, META, calls);

  await expect(page.getByText('تراز آزمایشی چهارستونی').first()).toBeVisible({ timeout: 90_000 });
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

  expect(errors.filter(e => !ignorableWithoutDb(e) && !/Failed to load resource/.test(e))).toEqual([]);
});

test('بدون دسترسیِ معین، کل دیده می‌شود ولی ریز نمی‌شود', async ({ page }) => {
  await boot(page, { ...META, canMoin: false, canTafsili: false });
  await page.getByRole('button', { name: 'نمایش تراز کل' }).click({ timeout: 90_000 });
  await expect(page.locator('.tb-table tbody tr')).toHaveCount(2);
  await expect(page.locator('.tb-next')).toHaveCount(0);
});

test('بدون دسترسیِ تراز کل، پیامِ فارسی و بدون دکمه‌ی نمایش', async ({ page }) => {
  await boot(page, { ...META, canKol: false });
  await expect(page.getByText('اجازه‌ی دیدن تراز آزمایشی را ندارید')).toBeVisible({ timeout: 90_000 });
  await expect(page.getByRole('button', { name: 'نمایش تراز کل' })).toBeDisabled();
});
