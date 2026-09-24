// @ts-check
const { test, expect } = require('@playwright/test');
const { databaseAvailable, apiLogin, uiLogin, field } = require('../helpers/app');

/**
 * گشت انسانیِ بخش‌های غیرحقوقی — هر بخشِ منو همان‌طور که یک کاربر استفاده‌اش
 * می‌کند: باز کردن، پر کردن فرم، زدن دکمه، و دیدن نتیجه روی صفحه.
 *
 * حقوق و دستمزد (02، 03، 04، 06، 07) و CRM (05-crm-acl) آزمون‌های اختصاصی
 * خودشان را دارند؛ این فایل بقیهٔ منو را پوشش می‌دهد.
 *
 * ── پیش‌نیاز ──
 * دیتابیس تستی که scripts/setup-test-env.sh می‌سازد، به‌ویژه
 * Server/Database/test_legacy_tables.sql (کاربر salesrep و دادهٔ فروش) و
 * test_cost_close_grants.sql. بدون دیتابیس همه skip می‌شوند.
 *
 * ── سنجهٔ مشترک ──
 * هر صفحه علاوه بر رفتار خودش باید دو چیز را رعایت کند (watchPage):
 *   ۱. هیچ درخواست API با خطای ۵xx برنگردد — ۵۰۰ یعنی کوئری یا کد سرور شکسته.
 *   ۲. هیچ پیام انگلیسیِ خامِ .NET مثل «Response status code does not
 *      indicate success» به کاربر فارسی‌زبان نشان داده نشود.
 */

/** ثبت ۵xxها و پیام‌های خام، برای بررسی در پایان هر آزمون. */
function watchPage(page) {
  const serverErrors = [];
  page.on('response', r => {
    if (r.url().includes('/api/') && r.status() >= 500)
      serverErrors.push(`${r.status()} ${r.request().method()} ${new URL(r.url()).pathname}`);
  });
  return {
    serverErrors,
    async assertClean() {
      expect(serverErrors, 'هیچ درخواستی نباید ۵xx بگیرد').toEqual([]);
      await expect(page.getByText(/Response status code does not indicate success/),
        'پیام خام HTTP نباید به کاربر نشان داده شود').toHaveCount(0);
      await expect(page.locator('#blazor-error-ui'), 'نوار خطای Blazor نباید ظاهر شود').toBeHidden();
    },
  };
}

/** انتخاب گزینه از یک MudSelect با برچسبش. */
async function pickSelect(page, label, option) {
  await page.locator('.mud-select').filter({ has: page.locator('label', { hasText: label }) }).first().click();
  await page.locator('.mud-popover-open .mud-list-item', { hasText: option }).first().click();
}

const snackbar = page => page.locator('.mud-snackbar');

/** مثل field در helpers/app.js، ولی textarea (فیلدهای چندخطی) را هم می‌گیرد. */
function box(scope, labelText) {
  return scope.locator('.mud-input-control')
    .filter({ has: scope.locator('label', { hasText: labelText }) })
    .locator('input, textarea').first();
}

/** عدد با جداکنندهٔ هزارگان — برنامه با فرهنگ fa-IR «٬» می‌گذارد، جاهایی هم «,». */
const money = n => new RegExp(n.toLocaleString('en-US').replace(/,/g, '[,٬]'));

test.beforeEach(async ({ request }) => {
  test.skip(!(await databaseAvailable(request)), 'دیتابیس تست در دسترس نیست — scripts/setup-test-env.sh');
});

// ═════════════════════════════════════════════════════════════════════════
test.describe('فروش ویزیتوری (salesrep)', () => {
  test.describe.configure({ mode: 'serial' });

  test.beforeAll(async ({ request }) => {
    // beforeAll پیش از beforeEachِ سراسری اجرا می‌شود، پس skipِ آن اینجا اثری ندارد؛
    // بدون این خط، در CI (بدون دیتابیس) apiLogin شکست می‌خورد و کل گروه fail می‌شود.
    test.skip(!(await databaseAvailable(request)), 'دیتابیس تست در دسترس نیست — scripts/setup-test-env.sh');
    // سبد و مشتریِ انتخاب‌شده سمت سرور در UserState می‌مانند؛ بدون پاک کردن،
    // اجرای دوم کالا را از قبل در سبد می‌بیند و دکمهٔ «افزودن» ظاهر نمی‌شود.
    const token = await apiLogin(request, 'sales');
    await request.delete('/api/userstate', { headers: { Authorization: `Bearer ${token}` } });
  });

  test('لیست مشتریانِ برنامهٔ ویزیت، مانده و اعتبار واقعی را نشان می‌دهد', async ({ page }) => {
    const w = watchPage(page);
    await uiLogin(page, 'sales');
    await page.goto('/visitor-customers', { waitUntil: 'networkidle' });

    // مشتریانِ روز ۱۴۰۵/۰۷/۰۱. تعداد دقیق چک نمی‌شود: «تعریف مشتری جدید» پایین‌تر
    // مشتری تازه را خودکار به آخرین روزِ ویزیت اضافه می‌کند و اجرای بعدی آن را هم می‌بیند.
    const cards = page.locator('.customer-card');
    await expect(cards.filter({ hasText: 'فروشگاه آزمایشی ب' })).toHaveCount(1);
    await expect(cards.filter({ hasText: 'مشتری مسدود' }), 'مشتری خارج از برنامه نباید بیاید').toHaveCount(0);
    const alef = cards.filter({ hasText: 'فروشگاه آزمایشی الف' });
    // فروش ۵۰ میلیون − دریافت ۲۰ میلیون (test_legacy_tables.sql)
    await expect(alef).toContainText(money(30_000_000));
    await expect(alef).toContainText(money(100_000_000));           // سقف اعتبار از AZAE

    await field(page, 'جستجوی مشتری').fill('آزمایشی ب');
    await expect(cards).toHaveCount(1);
    await expect(cards.first()).toContainText('فروشگاه آزمایشی ب');
    await w.assertClean();
  });

  test('ثبت سفارش: گروه → کالا → سبد → پیش‌فاکتور و PDF آن', async ({ page }) => {
    const w = watchPage(page);
    await uiLogin(page, 'sales');
    await page.goto('/visitor-customers', { waitUntil: 'networkidle' });
    await page.locator('.customer-card', { hasText: 'فروشگاه آزمایشی الف' })
      .getByRole('button', { name: 'ثبت سفارش' }).click();
    await expect(page).toHaveURL(/\/item-groups/);
    await expect(page.getByText('سفارش برای: فروشگاه آزمایشی الف')).toBeVisible();

    await page.locator('.group-card', { hasText: 'لبنیات' }).click();
    const card = page.locator('.item-card', { hasText: 'ماست ۲ کیلویی' });
    await expect(card).toBeVisible();
    await expect(page.locator('.item-card', { hasText: 'کالای غیرفعال' })).toHaveCount(0);

    await card.getByRole('button', { name: 'افزودن به سبد' }).click();
    await expect(page.locator('.mud-appbar .mud-badge')).toContainText('1');

    // نمای لیستی: یک بلوک دیباگ («تست: داخل بلوک…») داخل حلقه کل لیست را برای هر کالا تکرار می‌کرد
    await page.locator('[title="نمایش کارتی"]').click();
    await expect(page.getByText('تست: داخل بلوک')).toHaveCount(0);
    await expect(page.getByText('ماست ۲ کیلویی')).toHaveCount(1);

    await page.getByRole('button', { name: 'مشاهده سبد سفارش' }).click();
    await expect(page).toHaveURL(/\/cart/);
    await expect(page.getByText('مشتری: فروشگاه آزمایشی الف')).toBeVisible();
    await expect(page.locator('table')).toContainText('ماست ۲ کیلویی');
    await expect(page.locator('table')).toContainText(money(950_000));

    await page.getByRole('button', { name: 'ارسال و ثبت پیش فاکتور' }).click();
    const dialog = page.locator('.mud-dialog', { hasText: 'ثبت موفق' });
    await expect(dialog).toContainText(/پیش فاکتور با شماره \S+ با موفقیت ثبت شد/);
    await expect(page.getByText('سبد سفارش شما خالی است.')).toBeVisible();

    const download = page.waitForEvent('download');
    await dialog.getByRole('button', { name: /دانلود PDF/ }).click();
    const pdf = await download;
    const bytes = require('fs').readFileSync(await pdf.path());
    expect(bytes.subarray(0, 4).toString(), 'فایل دانلودشده باید PDF واقعی باشد').toBe('%PDF');
    await w.assertClean();
  });

  test('صورت‌حساب مشتری: ردیف‌ها و ماندهٔ تجمعی', async ({ page }) => {
    const w = watchPage(page);
    await uiLogin(page, 'sales');
    await page.goto('/visitor-customers', { waitUntil: 'networkidle' });
    await page.locator('.customer-card', { hasText: 'فروشگاه آزمایشی الف' })
      .getByRole('button', { name: 'صورت حساب' }).click();
    await expect(page).toHaveURL(/\/customer-statement\/103-1-1/);

    const rows = page.locator('.mud-table-body tr');
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0)).toContainText(money(50_000_000));
    await expect(rows.nth(1)).toContainText(money(20_000_000));
    await expect(rows.nth(1)).toContainText(money(30_000_000));      // مانده بعد از دریافت
    await w.assertClean();
  });

  // باگ باز: گزارش Stimulsoft 2023.1.1 با SixLabors.Fonts 1.0.0 (که ClosedXML 0.105 می‌آورد)
  // MissingMethodException می‌دهد و /api/report ۵۰۰ برمی‌گرداند — روی ویندوز هم، چون
  // موتور پیش‌فرض Stimulsoft روی همه‌ی سیستم‌عامل‌ها ImageSharp است. تا تصمیم صاحب پروژه
  // (ارتقای Stimulsoft / پایین آوردن ClosedXML / برگشت به PDF QuestPDF) fixme می‌ماند.
  test.fixme('صورت‌حساب مشتری: دکمهٔ «دانلود PDF» فایل PDF می‌دهد', async ({ page }) => {
    const w = watchPage(page);
    await uiLogin(page, 'sales');
    await page.goto('/customer-statement/103-1-1', { waitUntil: 'networkidle' });
    await expect(page.locator('.mud-table-body tr')).toHaveCount(2);

    const download = page.waitForEvent('download', { timeout: 60_000 });
    await page.getByRole('button', { name: /دانلود PDF/ }).click();
    const bytes = require('fs').readFileSync(await (await download).path());
    expect(bytes.subarray(0, 4).toString()).toBe('%PDF');
    await w.assertClean();
  });

  test('مشتری مسدود: API وضعیت مسدودی را درست می‌گوید و در لیست عمومی نیست', async ({ request }) => {
    const token = await apiLogin(request, 'sales');
    const auth = { Authorization: `Bearer ${token}` };
    const blocked = await (await request.get('/api/customers/103-1-5/is-blocked', { headers: auth })).json();
    const open    = await (await request.get('/api/customers/103-1-1/is-blocked', { headers: auth })).json();
    expect(blocked).toBe(true);
    expect(open).toBe(false);

    const list = await (await request.get('/api/customers/list-for-user?pageNumber=1&pageSize=200', { headers: auth })).json();
    const hes = (list.items ?? list.Items).map(c => c.hes);
    expect(hes).toContain('103-1-1');
    expect(hes, 'مشتری مسدود نباید در لیست عمومی بیاید').not.toContain('103-1-5');
  });

  test('تعریف مشتری جدید: ذخیره و هشدار تکراری', async ({ page }) => {
    const w = watchPage(page);
    await uiLogin(page, 'sales');
    await page.goto('/customer-define', { waitUntil: 'networkidle' });
    const name = `مشتری تست ${Date.now() % 100000}`;
    await field(page, 'نام').fill(name);
    await box(page, 'آدرس').fill('یزد، خیابان آزمایش');
    await field(page, 'موبایل جهت ارسال پیامک').fill(`0915${String(Date.now()).slice(-7)}`);
    await pickSelect(page, 'استان', 'یزد');
    await pickSelect(page, 'شهرستان', 'میبد');
    await pickSelect(page, 'نوع مشتری', 'عمده');
    await pickSelect(page, 'شخصیت', 'حقیقی');
    await page.getByRole('button', { name: 'ذخیره' }).click();
    await expect(snackbar(page).filter({ hasText: /موفق|ثبت شد/ })).toBeVisible();

    // مشتری تازه خودکار به آخرین برنامهٔ ویزیتِ همین ویزیتور اضافه می‌شود
    await page.goto('/visitor-customers', { waitUntil: 'networkidle' });
    await field(page, 'جستجوی مشتری').fill(name);
    await expect(page.locator('.customer-card', { hasText: name })).toHaveCount(1);
    await page.goto('/customer-define', { waitUntil: 'networkidle' });

    // همان نام دوباره → نباید دو حساب با یک نام ساخته شود
    await page.getByRole('button', { name: 'مشتری جدید' }).click();
    await field(page, 'نام').fill(name);
    await pickSelect(page, 'نوع مشتری', 'عمده');
    await pickSelect(page, 'شخصیت', 'حقیقی');
    await page.getByRole('button', { name: 'ذخیره' }).click();
    await expect(snackbar(page).filter({ hasText: /تکراری|وجود دارد|قبلا/ })).toBeVisible();
    await w.assertClean();
  });
});

/** تایپ در MudAutocomplete و انتخاب اولین پیشنهادی که متن را دارد. */
async function pickAutocomplete(page, label, typed, option, scope = page) {
  const input = scope.locator('.mud-input-control')
    .filter({ has: page.locator('label', { hasText: label }) }).locator('input').first();
  await input.click();
  await input.fill(typed);
  await page.locator('.mud-popover-open .mud-list-item', { hasText: option }).first().click();
}

// ═════════════════════════════════════════════════════════════════════════
test.describe('کارتابل اتوماسیون (payadmin)', () => {
  test('ثبت وظیفه با مشتریِ خارج از ۵۰ حساب اول و دیدنش در جدول', async ({ page }) => {
    const w = watchPage(page);
    await uiLogin(page, 'admin');
    await page.goto('/automation/tasks', { waitUntil: 'networkidle' });

    const text = `وظیفهٔ آزمایشی ${Date.now()}`;
    await box(page, 'شرح وظیفه').fill(text);
    await pickAutocomplete(page, 'گیرنده (مشتری)', 'آزمایشی الف', 'فروشگاه آزمایشی الف');
    // این مشتری الفبایی بعد از ۵۰ حساب اول است؛ قبلاً جستجو فقط همان ۵۰ را می‌گشت
    // و انتخابش ممکن نبود.
    await page.getByRole('button', { name: 'ذخیره وظیفه' }).click();

    const row = page.locator('.mud-table-body tr', { hasText: text });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText('payadmin');
    await expect(row).toContainText('فروشگاه آزمایشی الف');
    await expect(row).toContainText('انجام نشده');
    await w.assertClean();
  });

});


// ═════════════════════════════════════════════════════════════════════════
test.describe('فرم‌های عمومی', () => {
  test('گزارش اواپراتور: ثبت و پنجرهٔ موفقیت', async ({ page }) => {
    const w = watchPage(page);
    await uiLogin(page, 'admin');
    await page.goto('/evaporation-report', { waitUntil: 'networkidle' });
    await expect(field(page, 'تاریخ شمسی')).toHaveValue(/^14\d\d\/\d\d\/\d\d$/);   // امروز، خودکار
    await pickSelect(page, 'شیفت', 'عصر');
    await field(page, 'اپراتور').fill('اپراتور آزمایشی');
    await field(page, 'درصد مواد خشک خروجی').fill('42');
    await page.getByRole('button', { name: 'ثبت', exact: true }).click();
    const ok = page.locator('.mud-dialog', { hasText: 'گزارش با موفقیت ثبت شد.' });
    await expect(ok).toBeVisible();
    await ok.getByRole('button', { name: 'باشه' }).click();
    await expect(field(page, 'اپراتور')).toHaveValue('');           // فرم ریست شد
    await w.assertClean();
  });

  test('شکایت مشتری بدون ورود: اعتبارسنجی و ثبت', async ({ page }) => {
    const w = watchPage(page);
    await page.goto('/submit-complaint', { waitUntil: 'networkidle' });
    await field(page, 'نام').first().fill('مشتری');
    await field(page, 'نام خانوادگی').fill('آزمایشی');
    await field(page, 'تلفن همراه').fill('09120000099');
    await page.locator('.mud-checkbox', { hasText: 'کپک زدگی' }).click();
    const descBox = page.locator('.mud-input-control').filter({ has: page.locator('label', { hasText: /شرح|توضیح/ }) }).locator('textarea').first();
    await descBox.fill('روی بستهٔ پنیر کپک دیده شد');

    // بدون تیکِ «تأیید صحت اطلاعات» نباید ثبت شود
    await page.getByRole('button', { name: 'ثبت شکایت' }).click();
    await expect(snackbar(page).filter({ hasText: 'با موفقیت ثبت شد' })).toHaveCount(0);

    await page.locator('.mud-checkbox', { hasText: /تأیید|صحت/ }).last().click();
    await page.getByRole('button', { name: 'ثبت شکایت' }).click();
    await expect(snackbar(page).filter({ hasText: 'شکایت شما با موفقیت ثبت شد' })).toBeVisible();
    await w.assertClean();
  });
});
