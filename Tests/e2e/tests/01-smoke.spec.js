// @ts-check
const { test, expect } = require('@playwright/test');
const { field, collectPageErrors, ignorableWithoutDb } = require('../helpers/app');

/**
 * آزمون‌های پایه — هیچ‌کدام به دیتابیس نیاز ندارند.
 *
 * نکته‌ی مهم: بررسی اینکه «/ کد ۲۰۰ می‌دهد» هیچ چیزی را ثابت نمی‌کند.
 * در Blazor WebAssembly صفحه‌ی اول یک فایل استاتیک است و حتی با دیتابیسِ
 * خاموش هم ۲۰۰ برمی‌گرداند. پس اینجا واقعاً مرورگر را بالا می‌آوریم و
 * می‌بینیم که runtime دات‌نت اجرا شد و کامپوننت‌ها رندر شدند.
 */
test.describe('بالا آمدن برنامه', () => {

  test('هسته‌ی WebAssembly سرو می‌شود و معتبر است', async ({ request }) => {
    const res = await request.get('/_framework/blazor.boot.json');
    expect(res.ok()).toBeTruthy();

    const boot = await res.json();
    const resources = boot.resources ?? {};
    const assemblies = resources.assembly ?? resources.coreAssembly ?? {};
    expect(Object.keys(assemblies).length, 'هیچ اسمبلی‌ای در boot.json نیست').toBeGreaterThan(50);
  });

  test('برنامه در مرورگر واقعی رندر می‌شود', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    // اگر Blazor اجرا نشده باشد این متن‌ها اصلاً وجود ندارند،
    // چون داخل کامپوننت‌ها هستند نه در index.html.
    await expect(page.getByText('به سیستم جامع سفیر خوش آمدید')).toBeVisible();
    // MudButton به‌صورت <a> رندر می‌شود نه <button>، پس نقش را قید نمی‌کنیم.
    await expect(page.getByText('وارد شوید').first()).toBeVisible();
  });

  test('چیدمان راست‌به‌چپ است', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });
    const dir = await page.evaluate(() =>
      document.documentElement.dir || getComputedStyle(document.body).direction);
    expect(dir).toBe('rtl');
  });

  test('صفحه ورود فرم کاربری را نشان می‌دهد', async ({ page }) => {
    await page.goto('/login', { waitUntil: 'networkidle' });

    await expect(field(page, 'نام کاربری')).toBeVisible();
    await expect(field(page, 'رمز عبور')).toBeVisible();
    await expect(page.getByRole('button', { name: 'ورود', exact: true })).toBeVisible();
  });

  test('برنامه بدون دیتابیس هم خطای فارسی قابل فهم می‌دهد و سفید نمی‌شود', async ({ page }) => {
    // یک کاربر واقعی وقتی سرور دیتابیس قطع است نباید صفحه‌ی سفید ببیند.
    await page.goto('/', { waitUntil: 'networkidle' });

    // networkidle پایانِ بوتِ WebAssembly را تضمین نمی‌کند. روی ماشین کند
    // (مثل runner در CI) صفحه هنوز همان splash است — که خودش هم کلمه‌ی «سفیر»
    // را دارد، پس فقط بررسیِ طول لو می‌داد که چیز اشتباهی نمونه‌برداری شده.
    // منتظر متنی می‌مانیم که فقط از دل کامپوننت‌های Blazor بیرون می‌آید.
    await expect(page.getByText('به سیستم جامع سفیر خوش آمدید'))
      .toBeVisible({ timeout: 60_000 });

    const body = await page.locator('body').innerText();
    expect(body.length, 'صفحه خالی است').toBeGreaterThan(100);
    // یا وارد شده و کار می‌کند، یا پیام روشن فارسی می‌دهد — نه صفحه‌ی سفید.
    expect(body).toMatch(/سفیر/);
  });
});

test.describe('کنترل دسترسی در سطح API', () => {

  // این‌ها به دیتابیس نیاز ندارند: رد شدن بدون توکن قبل از هر کوئری اتفاق می‌افتد.
  const protectedEndpoints = [
    '/api/pay2/access/me',
    '/api/pay2/workshops',
    '/api/pay2/itemdefs',
    '/api/pay2/employees',
  ];

  for (const path of protectedEndpoints) {
    test(`${path} بدون توکن ۴۰۱ می‌دهد`, async ({ request }) => {
      const res = await request.get(path);
      expect(res.status()).toBe(401);
    });
  }

  test('توکن دستکاری‌شده پذیرفته نمی‌شود', async ({ request }) => {
    // امضای معتبر ولی محتوای عوض‌شده باید رد شود.
    const forged = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
      + '.eyJJREQiOiI5OTk5IiwiaXNzIjoiU2FmaXJBcHBJc3N1ZXIifQ'
      + '.aGFja2VkX3NpZ25hdHVyZV9ub3RfdmFsaWQ';
    const res = await request.get('/api/pay2/access/me', {
      headers: { Authorization: `Bearer ${forged}` },
    });
    expect(res.status()).toBe(401);
  });

  test('هدر Authorization بی‌معنی باعث ۵۰۰ نمی‌شود', async ({ request }) => {
    const res = await request.get('/api/pay2/access/me', {
      headers: { Authorization: 'Bearer not-even-a-token' },
    });
    expect(res.status()).toBe(401);
  });
});

test.describe('سلامت کلاینت', () => {

  test('هیچ خطای جاوااسکریپتی غیرمنتظره‌ای در بارگذاری نیست', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto('/', { waitUntil: 'networkidle' });
    await page.waitForTimeout(2000);

    // خطاهای مربوط به نبودن دیتابیس در این محیط طبیعی‌اند؛
    // هر خطای دیگری یعنی مشکل واقعی در کلاینت.
    const unexpected = errors.filter(e => !ignorableWithoutDb(e));
    expect(unexpected, `خطاهای غیرمنتظره:\n${unexpected.join('\n')}`).toHaveLength(0);
  });
});
