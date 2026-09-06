// @ts-check
const { test, expect } = require('@playwright/test');
const { BASE, field, collectPageErrors } = require('../helpers/app');

/**
 * تابلوی سود و زیان کالا و دیالوگ پیشنهاد جابه‌جایی مواد.
 *
 * ── چرا این فایل وجود دارد ──
 * دو باگ پشت‌سرهم در این صفحه رخ داد که هیچ‌کدام را `dotnet build` نگرفت،
 * چون هر دو فقط در زمان اجرا و فقط در مرورگر بروز می‌کنند:
 *
 *   ۱. یک کامنت Razor (`@* … *@`) داخل فهرست ویژگی‌های یک تگ گذاشته شد.
 *      کامپایل شد، ولی در DOM به‌عنوان *نام یک attribute* رندر شد و
 *      setAttribute استثنا داد. کل زیردرختِ رندر شکست و صفحه خالی بالا آمد.
 *
 *   ۲. یک پارامتر ناموجود (`Checked` روی MudRadio در MudBlazor 6.21) روی
 *      یک کامپوننت گذاشته شد. کامپوننت آن را به‌عنوان attribute ناشناخته
 *      می‌گیرد و بی‌سروصدا به DOM می‌دهد — همان الگو، همان نتیجه.
 *
 * پس سنجه‌ی اصلی این تست‌ها «هیچ استثنای رندری رخ ندهد» است، نه فقط
 * «صفحه بالا بیاید». نوار #blazor-error-ui هم مستقیم بررسی می‌شود چون
 * دقیقاً همان چیزی است که کاربر می‌بیند.
 *
 * ── اجرا ──
 * این تست‌ها به دیتابیس واقعی و یک کاربر واقعی نیاز دارند، پس اعتبارنامه
 * از متغیر محیطی خوانده می‌شود و بدون آن skip می‌شوند (نه fail):
 *
 *   SAFIR_USER='...' SAFIR_PASS='...' APP_URL='http://localhost:7026' \
 *     npx playwright test 05-cost-margin.spec.js
 *
 * رمز عمداً در فایل نیست: این تست روی دیتابیس عملیاتی مشتری اجرا می‌شود،
 * نه روی کاربران آزمایشیِ helpers/app.js.
 */

const USER = process.env.SAFIR_USER;
const PASS = process.env.SAFIR_PASS;
const RUN_ID = process.env.SAFIR_RUN_ID || '6';

/** خطاهایی که به این صفحه ربطی ندارند و در محیط واقعی هم دیده می‌شوند. */
function irrelevant(text) {
  return /favicon|ERR_CONNECTION_REFUSED|healthcheck|user state|UserStateApiService/i.test(text);
}

/** استثناهای رندر Blazor — همان چیزی که صفحه را سفید می‌کند. */
function isRenderException(text) {
  return /Unhandled exception rendering component|InvalidCharacterError|setAttribute|No element is currently associated/i
    .test(text);
}

test.describe('تابلوی سود و زیان کالا', () => {
  test.skip(!USER || !PASS,
    'برای اجرا SAFIR_USER و SAFIR_PASS را ست کنید (رمز در مخزن ذخیره نمی‌شود).');

  test.beforeEach(async ({ page }) => {
    await page.goto('/login', { waitUntil: 'networkidle' });
    await field(page, 'نام کاربری').fill(USER);
    await field(page, 'رمز عبور').fill(PASS);
    await page.getByRole('button', { name: 'ورود', exact: true }).click();
    await expect(page).not.toHaveURL(/\/login/, { timeout: 60_000 });
  });

  test('جدول رندر می‌شود و هیچ استثنای رندری رخ نمی‌دهد', async ({ page }) => {
    const errors = collectPageErrors(page);

    await page.goto(`/cost-close/runs/${RUN_ID}/margin`, { waitUntil: 'networkidle' });

    // کارت سرصفحه یعنی داده آمده
    await expect(page.getByText('سود کل')).toBeVisible({ timeout: 60_000 });

    // ⚠ سنجه‌ی اصلی: باگ کامنت Razor دقیقاً همین‌جا می‌افتاد — کارت‌ها
    // رندر می‌شدند (بالای دکمه بودند) ولی جدول و هرچه پایین‌ترش خالی می‌ماند.
    const rows = page.locator('table tbody tr');
    await expect.poll(() => rows.count(), { timeout: 30_000 }).toBeGreaterThan(0);

    // نوار خطای Blazor نباید دیده شود
    const errorBar = page.locator('#blazor-error-ui');
    await expect(errorBar).toBeHidden();

    const render = errors.filter(isRenderException);
    expect(render, `استثنای رندر:\n${render.join('\n')}`).toHaveLength(0);
  });

  test('فیلترها جدول را می‌شکنند یا نه', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto(`/cost-close/runs/${RUN_ID}/margin`, { waitUntil: 'networkidle' });
    await expect(page.getByText('سود کل')).toBeVisible({ timeout: 60_000 });

    for (const chip of ['همه', 'زیان‌ده']) {
      await page.getByRole('button', { name: new RegExp(chip) }).first().click();
      await expect(page.locator('#blazor-error-ui')).toBeHidden();
    }

    const render = errors.filter(isRenderException);
    expect(render, `استثنای رندر:\n${render.join('\n')}`).toHaveLength(0);
  });

  test('دیالوگ پیشنهاد جابه‌جایی مواد باز می‌شود و مواد را نشان می‌دهد', async ({ page }) => {
    const errors = collectPageErrors(page);

    await page.goto(`/cost-close/runs/${RUN_ID}/margin`, { waitUntil: 'networkidle' });
    await expect(page.getByText('سود کل')).toBeVisible({ timeout: 60_000 });

    // فیلتر زیان‌ده تا مطمئن باشیم ردیفی با دکمه‌ی پیشنهاد هست
    await page.getByRole('button', { name: /زیان‌ده/ }).first().click();

    const wand = page.locator('table tbody tr button').first();
    await expect(wand).toBeVisible({ timeout: 30_000 });
    await wand.click();

    // دیالوگ باید بیاید و زنجیره را بررسی کند
    await expect(page.getByText('پیشنهاد جابه‌جایی مواد')).toBeVisible({ timeout: 30_000 });

    // ⚠ باگ MudRadio اینجا می‌افتاد: دیالوگ باز می‌شد ولی محتوایش
    // به‌خاطر استثنای رندر خالی می‌ماند.
    await expect(
      page.getByText('عمق زنجیره').or(page.getByText('هیچ ماده‌ای'))
    ).toBeVisible({ timeout: 60_000 });

    await expect(page.locator('#blazor-error-ui')).toBeHidden();

    const render = errors.filter(isRenderException);
    expect(render, `استثنای رندر:\n${render.join('\n')}`).toHaveLength(0);
  });
});

test.describe('اندپوینت پیشنهاد', () => {
  test.skip(!USER || !PASS, 'نیاز به SAFIR_USER و SAFIR_PASS');

  test('پیشنهاد برای یک کالای زیان‌ده مواد و مقصد برمی‌گرداند', async ({ request }) => {
    const login = await request.post(`${BASE}/api/auth/login`,
      { data: { Username: USER, Password: PASS } });
    expect(login.ok(), 'ورود باید موفق باشد').toBeTruthy();
    const { token } = await login.json();

    const margins = await request.get(
      `${BASE}/api/cost-close/runs/${RUN_ID}/margins`,
      { headers: { Authorization: `Bearer ${token}` } });
    expect(margins.ok()).toBeTruthy();

    const loss = (await margins.json()).find(m => m.profit < 0);
    test.skip(!loss, 'این اجرا کالای زیان‌ده ندارد.');

    const res = await request.get(
      `${BASE}/api/cost-close/runs/${RUN_ID}/rebalance-suggest/${loss.code}`,
      { headers: { Authorization: `Bearer ${token}` } });

    expect(res.ok(), await res.text()).toBeTruthy();
    const body = await res.json();

    expect(body.deficit).toBeGreaterThan(0);
    expect(Array.isArray(body.materials)).toBeTruthy();

    // هر ماده‌ای که مقصد دارد باید ظرفیت مثبت گزارش کند — و برعکس.
    // این همان تفکیکی است که حالت «هیچ کالای سوددهی مصرفش نمی‌کند» را
    // از حالت قابل‌استفاده جدا می‌کند.
    for (const m of body.materials) {
      if (m.destCount === 0) expect(m.coverage).toBe(0);
      else expect(m.destCapacity).toBeGreaterThan(0);
    }
  });
});
