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

test('آپدیت از منو یک‌بار صفحه را بارگذاری می‌کند و تنظیمات را نگه می‌دارد', async ({ page }) => {
  await page.goto('/login?returnUrl=payroll', { waitUntil: 'networkidle' });
  await expect(field(page, 'نام کاربری')).toBeVisible();
  const updateLink = page.getByText('بررسی و دریافت بروزرسانی', { exact: true });
  if (await page.locator('.safir-drawer.mud-drawer--closed').count()) {
    await page.locator('.safir-appbar button').first().click();
  }
  await page.evaluate(() => localStorage.setItem('update-test-setting', 'preserved'));
  let navigations = 0;
  page.on('framenavigated', frame => { if (frame === page.mainFrame()) navigations++; });
  await updateLink.click();
  await page.getByRole('button', { name: 'بله، آپدیت کن', exact: true }).click();
  await page.waitForURL(url => url.searchParams.has('_appUpdate'));
  await expect(field(page, 'نام کاربری')).toBeVisible();
  expect(navigations).toBe(1);
  expect(new URL(page.url()).pathname).toBe('/login');
  expect(new URL(page.url()).searchParams.get('returnUrl')).toBe('payroll');
  expect(await page.evaluate(() => localStorage.getItem('update-test-setting'))).toBe('preserved');
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

test.describe('اعلان‌های Snackbar', () => {

  /**
   * ساختنِ یک اسنک‌بارِ قابل‌اتکا — مستقل از اینکه دیتابیس بالا هست یا نه.
   *
   * نسخه‌ی اول این تست‌ها به اسنک‌بارِ خطای اتصالِ صفحه‌ی ورود تکیه می‌کرد،
   * ولی آن فقط وقتی ظاهر می‌شود که دیتابیس *در دسترس نباشد*. یعنی تست‌ها
   * بدون دیتابیس سبز می‌شدند و با دیتابیسِ سالم می‌افتادند — تستی که فقط
   * وقتی برنامه خراب است کار کند، ارزشی ندارد.
   *
   * «ذخیره تنظیمات دیتابیس» فقط در localStorage می‌نویسد و بعد اسنک‌بار
   * موفقیت نشان می‌دهد؛ هیچ رفت‌وبرگشتی با سرور ندارد، پس در هر دو حالت
   * یکسان عمل می‌کند.
   */
  async function triggerSnackbar(page) {
    await page.goto('/login', { waitUntil: 'networkidle' });

    await page.getByText('تنظیمات سرور و دیتابیس').click();

    // فرم تا وقتی معتبر نباشد ذخیره نمی‌کند، پس همه‌ی فیلدهای الزامی را پر می‌کنیم.
    await field(page, 'سرور (IP/Name)').fill('127.0.0.1,1433');
    await field(page, 'نام دیتابیس').fill('SafirTestDb');
    const dbUser = field(page, 'نام کاربری دیتابیس');
    if (await dbUser.count() > 0) {
      await dbUser.fill('sa');
      await field(page, 'رمز عبور دیتابیس').fill('placeholder');
    }

    await page.getByRole('button', { name: 'ذخیره تنظیمات دیتابیس' }).click();

    // دقیقاً همان اسنک‌بار را هدف می‌گیریم، نه هر اسنک‌باری که روی صفحه باشد.
    const snackbar = page.locator('.mud-snackbar')
      .filter({ hasText: 'تنظیمات دیتابیس با موفقیت ذخیره شد' }).first();
    await expect(snackbar).toBeVisible({ timeout: 15_000 });
    return snackbar;
  }

  test('اعلان با کلیک روی × واقعاً محو می‌شود، نه اینکه دو ثانیه بی‌حرکت بماند', async ({ page }) => {
    // باگ واقعی: قانون CSS برای «.mud-snackbar» یک انیمیشنِ ورود را با
    // !important روی همان پراپرتیِ animation تحمیل می‌کرد که خودِ
    // MudSnackbarElement برای محو شدن (state=Hiding) به‌صورت این‌لاین
    // تنظیم می‌کند. نتیجه: کلیک روی × ثبت می‌شد ولی هیچ فیدبک بصری‌ای
    // نبود — اسنک‌بار با opacity کامل ثابت می‌ماند تا HideTransitionDuration
    // (پیش‌فرض ۲ ثانیه) تمام شود و بعد یک‌باره از DOM حذف می‌شد؛ برای
    // کاربر یعنی «دکمه‌ی بستن اثر نمی‌کند».
    const snackbar = await triggerSnackbar(page);

    await snackbar.locator('.mud-snackbar-content-action button').first().click();

    // با HideTransitionDuration=200ms (Program.cs)، ۶۰۰ میلی‌ثانیه پس از
    // کلیک باید کاملاً رفته باشد؛ نه اینکه ۲ ثانیه بی‌حرکت روی صفحه بماند.
    await expect(snackbar).toHaveCount(0, { timeout: 600 });
  });

  test('اعلان‌ها سریع ظاهر می‌شوند، نه با یک ثانیه تأخیر محو-به-داخل', async ({ page }) => {
    // شکایت کاربر: محو شدنِ ورودیِ پیش‌فرض MudBlazor یک ثانیه طول می‌کشد
    // و «کند» و «اعصاب خردکن» است. Program.cs حالا ShowTransitionDuration
    // را به ۱۸۰ میلی‌ثانیه کاهش داده؛ اینجا تضمین می‌کنیم دیر نشده باشد.
    const snackbar = await triggerSnackbar(page);

    // نکته: toBeVisible در Playwright به opacity کاری ندارد، پس درست همان
    // لحظه‌ای برمی‌گردد که اسنک‌بار با شفافیت ~۰ وارد شده. سنجه‌ی واقعی این
    // است که «چقدر زود» به شفافیت کامل می‌رسد: با ۱۸۰ میلی‌ثانیه‌ی فعلی
    // خیلی زیر ۵۰۰ است، با پیش‌فرض قدیمیِ ۱۰۰۰ میلی‌ثانیه نمی‌رسید.
    await expect
      .poll(async () => Number(await snackbar.evaluate(el => getComputedStyle(el).opacity)),
            { timeout: 500, message: 'اسنک‌بار باید خیلی سریع کاملاً ظاهر شود' })
      .toBeGreaterThan(0.8);
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

  /**
   * کارتابل اتوماسیون (/automation/tasks) کلِ ظاهرش را از css/automation.css
   * و میان‌بُرهای صفحه‌کلید را از js/automation.js می‌گیرد. خودِ صفحه
   * [Authorize] است و بدون ورود رندر نمی‌شود، پس اینجا دست‌کم سیم‌کشیِ
   * index.html سنجیده می‌شود: اگر یکی از این دو جا بیفتد، صفحه بی‌استایل
   * یا میان‌بُرها بی‌صدا از کار می‌افتند.
   */
  test('فایل‌های کارتابل اتوماسیون بارگذاری می‌شوند', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    const hasKeys = await page.evaluate(() => typeof window.atmKeys?.attach === 'function');
    expect(hasKeys, 'js/automation.js بارگذاری نشده').toBe(true);

    const cssRules = await page.evaluate(() => {
      const sheet = [...document.styleSheets].find(s => (s.href || '').includes('css/automation.css'));
      return sheet ? sheet.cssRules.length : 0;
    });
    expect(cssRules, 'css/automation.css بارگذاری نشده یا خالی است').toBeGreaterThan(50);

    // فیلمِ آموزشیِ دکمه‌ی «آموزش» باید همراهِ برنامه منتشر شود
    const video = await page.request.get('/media/automation-guide.mp4', { headers: { Range: 'bytes=0-1023' } });
    expect(video.status(), 'media/automation-guide.mp4 سرو نمی‌شود').toBeLessThan(300);
    expect(video.headers()['content-type']).toContain('video/mp4');
  });

  /**
   * خزانه‌داری (/treasury) ظاهرش را از automation.css + css/treasury.css می‌گیرد
   * و خودش [Authorize] است. بدون ورود: استایل سیم‌کشی شده باشد، API بدون توکن
   * ۴۰۱ بدهد (نه داده)، و صفحه نشکند.
   */
  test('خزانه‌داری سیم‌کشی شده و بدون ورود نمی‌شکند', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto('/treasury', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    const cssRules = await page.evaluate(() => {
      const sheet = [...document.styleSheets].find(s => (s.href || '').includes('css/treasury.css'));
      return sheet ? sheet.cssRules.length : 0;
    });
    expect(cssRules, 'css/treasury.css بارگذاری نشده یا خالی است').toBeGreaterThan(50);

    const meta = await page.request.get('/api/treasury/meta');
    expect(meta.status(), 'API خزانه‌داری بدون توکن باز است').toBe(401);
    // چک‌ها، چاپ، تصویرِ امضا و اکسل هم داده‌ی مالی/امضا برمی‌گردانند
    for (const url of ['/api/treasury/cheques?mode=assign', '/api/treasury/1/print', '/api/treasury/1/signature/1', '/api/treasury/1/excel']) {
      expect((await page.request.get(url)).status(), `${url} بدون توکن باز است`).toBe(401);
    }

    const crashed = errors.filter(e => /Unhandled exception rendering component/i.test(e));
    expect(crashed, `کامپوننت کرش کرد:\n${crashed.join('\n')}`).toHaveLength(0);
    const trsErrors = errors.filter(e => /TreasuryApiService|Treasury\.TreasuryPage/i.test(e));
    expect(trsErrors, `خطای مدیریت‌نشده در خزانه‌داری:\n${trsErrors.join('\n')}`).toHaveLength(0);
  });

  /**
   * صدور و ویرایش اسناد (/sanad) روی کلاس‌های خزانه + css/sanad.css سوار است و
   * API اش [Authorize] است؛ سند، چاپ، امضا و اکسل بدون توکن نباید چیزی برگردانند.
   */
  test('صدور و ویرایش اسناد سیم‌کشی شده و بدون ورود نمی‌شکند', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto('/sanad', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    const cssRules = await page.evaluate(() => {
      const sheet = [...document.styleSheets].find(s => (s.href || '').includes('css/sanad.css'));
      return sheet ? sheet.cssRules.length : 0;
    });
    expect(cssRules, 'css/sanad.css بارگذاری نشده یا خالی است').toBeGreaterThan(50);

    for (const url of ['/api/sanad/meta', '/api/sanad', '/api/sanad/1', '/api/sanad/1/print', '/api/sanad/1/signature/1', '/api/sanad/1/excel']) {
      expect((await page.request.get(url)).status(), `${url} بدون توکن باز است`).toBe(401);
    }
    expect((await page.request.post('/api/sanad', { data: { Date: 14050101, Sharh: 'x' } })).status(), 'ساختِ سند بدون توکن').toBe(401);

    const crashed = errors.filter(e => /Unhandled exception rendering component/i.test(e));
    expect(crashed, `کامپوننت کرش کرد:\n${crashed.join('\n')}`).toHaveLength(0);
    const sndErrors = errors.filter(e => /SanadApiService|Sanad\.SanadPage/i.test(e));
    expect(sndErrors, `خطای مدیریت‌نشده در صدور اسناد:\n${sndErrors.join('\n')}`).toHaveLength(0);
  });

  /**
   * واحد و شیفتِ کاری (پنجره‌ی DEFAULT ِ WPF) توکنِ تازه صادر می‌کند؛ هیچ‌کدام از
   * سه مسیرش نباید بدونِ توکن کار کند.
   */
  test('API واحد و شیفت بدون توکن بسته است', async ({ request }) => {
    expect((await request.get('/api/auth/workspace')).status()).toBe(401);
    expect((await request.get('/api/auth/workspace/options')).status()).toBe(401);
    expect((await request.post('/api/auth/workspace', { data: { Depatman: 1, Shift: 1 } })).status()).toBe(401);
  });

  /**
   * رگرسیون: صفحه‌ی مغایرت‌های بهای تمام‌شده نباید با ۴۰۱/۴۰۳ بشکند.
   *
   * نسخه‌ی اول این صفحه در Load() هیچ catch نداشت، پس یک ۴۰۱ ساده از
   * OnInitializedAsync بیرون می‌زد و کامپوننت با پیام انگلیسیِ .NET
   * («Response status code does not indicate success») می‌شکست.
   *
   * توجه: نمی‌شود این را با فیلترِ ignorableWithoutDb گرفت، چون متن آن خطا
   * خودش شامل «401» است و ignorable حساب می‌شود. نشانه‌ی درست، عبارت
   * «Unhandled exception rendering component» است.
   */
  test('صفحه مغایرت‌های بهای تمام‌شده بدون ورود هم نمی‌شکند', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto('/cost-close/exceptions', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    const crashed = errors.filter(e => /Unhandled exception rendering component/i.test(e));
    expect(crashed, `کامپوننت کرش کرد:\n${crashed.join('\n')}`).toHaveLength(0);

    // هیچ استثنای مدیریت‌نشده‌ای نباید از خودِ این ماژول بالا بیاید.
    // (دامنه عمداً به CostClose محدود است: سرویس‌های دیگر برنامه مثل
    //  UserStateApiService در محیط بدون دیتابیس ۴۰۱ خودشان را لاگ می‌کنند
    //  و آن نویزِ از پیش موجود ربطی به این تست ندارد.)
    const ccErrors = errors.filter(e => /CostCloseApiService|CostClose\.CostExceptions/i.test(e));
    expect(ccErrors, `خطای مدیریت‌نشده در ماژول بهای تمام‌شده:\n${ccErrors.join('\n')}`)
      .toHaveLength(0);
  });

  /**
   * رگرسیون: همان باگ، همان‌جا رفع شد ولی صفحه‌ی اجرای زنده (CostRunMonitor)
   * یک‌بار دیگر تکرارش کرد — Reload() هم هیچ catch نداشت. این‌بار علاوه بر
   * catch، یک پیام فارسی صریح («برای مشاهده این صفحه باید وارد شوید.») هم
   * روی صفحه نشان داده می‌شود، پس هم نبود کرش و هم وجود آن پیام را می‌سنجیم.
   */
  test('صفحه اجرای بستن ماه بدون ورود هم نمی‌شکند', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto('/cost-close/runs/1', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    const crashed = errors.filter(e => /Unhandled exception rendering component/i.test(e));
    expect(crashed, `کامپوننت کرش کرد:\n${crashed.join('\n')}`).toHaveLength(0);

    const ccErrors = errors.filter(e => /CostCloseApiService|CostClose\.CostRunMonitor/i.test(e));
    expect(ccErrors, `خطای مدیریت‌نشده در ماژول بهای تمام‌شده:\n${ccErrors.join('\n')}`)
      .toHaveLength(0);

    await expect(page.getByText('برای مشاهده این صفحه باید وارد شوید.')).toBeVisible();
  });

  /**
   * رگرسیون: سومین تکرار همان الگو — CostVarianceBoard.Load() هم هیچ
   * catch نداشت. همان الگوی رفع (catch + پیام فارسی روی خود صفحه) هم اینجا
   * به کار رفت.
   */
  test('صفحه انحراف مصرف بدون ورود هم نمی‌شکند', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto('/cost-close/runs/1/variance', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    const crashed = errors.filter(e => /Unhandled exception rendering component/i.test(e));
    expect(crashed, `کامپوننت کرش کرد:\n${crashed.join('\n')}`).toHaveLength(0);

    const ccErrors = errors.filter(e => /CostCloseApiService|CostClose\.CostVarianceBoard/i.test(e));
    expect(ccErrors, `خطای مدیریت‌نشده در ماژول بهای تمام‌شده:\n${ccErrors.join('\n')}`)
      .toHaveLength(0);

    await expect(page.getByText('برای مشاهده این صفحه باید وارد شوید.')).toBeVisible();
  });

  /**
   * رگرسیون: چهارمین تکرار همان الگو — CostMarginBoard.Load() هم هیچ
   * catch نداشت. همان الگوی رفع اینجا هم به کار رفت.
   */
  test('صفحه سود و زیان کالا بدون ورود هم نمی‌شکند', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto('/cost-close/runs/1/margin', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    const crashed = errors.filter(e => /Unhandled exception rendering component/i.test(e));
    expect(crashed, `کامپوننت کرش کرد:\n${crashed.join('\n')}`).toHaveLength(0);

    const ccErrors = errors.filter(e => /CostCloseApiService|CostClose\.CostMarginBoard/i.test(e));
    expect(ccErrors, `خطای مدیریت‌نشده در ماژول بهای تمام‌شده:\n${ccErrors.join('\n')}`)
      .toHaveLength(0);

    await expect(page.getByText('برای مشاهده این صفحه باید وارد شوید.')).toBeVisible();
  });

  /**
   * صفحه داشبورد (/cost-close) تنها راه ورود از داخل برنامه به کل ماژول
   * است — قبلاً وجود نداشت و تنها راه رسیدن به یک اجرا تایپ مستقیم آدرس
   * /cost-close/runs/{id} بود. همان الگوی catch + پیام فارسی اینجا هم
   * رعایت شده، پس همان رگرسیون را می‌سنجیم.
   */
  test('داشبورد بستن ماه بدون ورود هم نمی‌شکند', async ({ page }) => {
    const errors = collectPageErrors(page);
    await page.goto('/cost-close', { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    const crashed = errors.filter(e => /Unhandled exception rendering component/i.test(e));
    expect(crashed, `کامپوننت کرش کرد:\n${crashed.join('\n')}`).toHaveLength(0);

    const ccErrors = errors.filter(e => /CostCloseApiService|CostClose\.CostDashboard/i.test(e));
    expect(ccErrors, `خطای مدیریت‌نشده در ماژول بهای تمام‌شده:\n${ccErrors.join('\n')}`)
      .toHaveLength(0);

    await expect(page.getByText('برای مشاهده این صفحه باید وارد شوید.')).toBeVisible();
  });
});
