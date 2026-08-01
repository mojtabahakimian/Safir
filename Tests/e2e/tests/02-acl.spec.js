// @ts-check
const { test, expect } = require('@playwright/test');
const { USERS, databaseAvailable, apiLogin, uiLogin, field } = require('../helpers/app');

/**
 * سفر سه کاربر با نقش‌های متفاوت — قلب آزمون کنترل دسترسی.
 *
 * این‌ها به دیتابیسِ تستِ ساخته‌شده با scripts/setup-test-env.sh نیاز دارند.
 * اگر دیتابیس نباشد skip می‌شوند، نه fail — چون شکستِ محیط با شکستِ برنامه
 * فرق دارد و قاطی کردنشان گزارش تست را بی‌اعتبار می‌کند.
 */
test.beforeEach(async ({ request }) => {
  test.skip(!await databaseAvailable(request),
    'دیتابیس تست در دسترس نیست — ابتدا scripts/setup-test-env.sh را اجرا کنید.');
});

/**
 * فرمی که در test_auth_and_acl_users.sql برای payviewer کاملاً بسته شده است.
 * «فقط‌خواندنی» یعنی See دارد ولی Inp/Upd/Del ندارد؛ اینجا هیچ‌کدام را ندارد.
 */
const CLOSED_FORM = 'PAY2_DECREE';

test.describe('ورود', () => {

  test('هر سه کاربر آزمایشی می‌توانند وارد شوند', async ({ request }) => {
    for (const who of ['admin', 'viewer', 'scoped']) {
      const token = await apiLogin(request, who);
      expect(token.split('.')).toHaveLength(3);   // یک JWT درست سه بخش دارد
    }
  });

  test('رمز اشتباه پذیرفته نمی‌شود', async ({ request }) => {
    const res = await request.post('/api/auth/login', {
      data: { Username: USERS.admin.username, Password: 'wrong-password' },
    });
    expect(res.status()).toBe(401);
  });

  test('ورود از رابط کاربری کار می‌کند', async ({ page }) => {
    await uiLogin(page, 'admin');
    await expect(page).not.toHaveURL(/\/login/);
  });
});

test.describe('دسترسی‌های اعلام‌شده به کلاینت', () => {

  test('مدیر همه‌ی مجوزها را دارد', async ({ request }) => {
    const token = await apiLogin(request, 'admin');
    const me = await (await request.get('/api/pay2/access/me', {
      headers: { Authorization: `Bearer ${token}` },
    })).json();

    expect(me.aclEnforced, 'ACL_ENFORCE باید در دیتابیس تست روشن باشد').toBe(true);
    expect(me.forms.length).toBeGreaterThan(0);
    for (const f of me.forms) {
      expect(f.run && f.see && f.inp && f.upd && f.del,
        `مدیر باید همه‌ی مجوزهای ${f.formName} را داشته باشد`).toBe(true);
    }
  });

  test('کاربر فقط‌خواندنی هیچ مجوز تغییری ندارد', async ({ request }) => {
    const token = await apiLogin(request, 'viewer');
    const me = await (await request.get('/api/pay2/access/me', {
      headers: { Authorization: `Bearer ${token}` },
    })).json();

    for (const f of me.forms.filter(f => f.formName !== CLOSED_FORM)) {
      expect(f.see, `${f.formName} باید قابل مشاهده باشد`).toBe(true);
      expect(f.inp || f.upd || f.del,
        `${f.formName} نباید هیچ مجوز تغییری داشته باشد`).toBe(false);
    }
  });

  test('فرمِ کاملاً بسته برای کاربر فقط‌خواندنی هیچ مجوزی ندارد', async ({ request }) => {
    // «فقط‌خواندنی» و «اصلاً دسترسی ندارد» دو حالت متفاوتند و رفتار رابط کاربری
    // در حالت دوم بود که خراب بود؛ پس خودِ داده‌ی آزمون هم باید بررسی شود.
    const token = await apiLogin(request, 'viewer');
    const me = await (await request.get('/api/pay2/access/me', {
      headers: { Authorization: `Bearer ${token}` },
    })).json();

    const closed = me.forms.find(f => f.formName === CLOSED_FORM);
    expect(closed, `${CLOSED_FORM} باید در فهرست فرم‌ها باشد`).toBeTruthy();
    expect(closed.run || closed.see || closed.inp || closed.upd || closed.del).toBe(false);
  });

  test('کاربر محدود فقط یک کارگاه را می‌بیند', async ({ request }) => {
    const token = await apiLogin(request, 'scoped');
    const me = await (await request.get('/api/pay2/access/me', {
      headers: { Authorization: `Bearer ${token}` },
    })).json();

    expect(me.wsScopeEnforced).toBe(true);
    expect(me.allowedWorkshopIds).toEqual([1]);
  });
});

test.describe('اعمال شدن دسترسی روی API — نه فقط پنهان کردن دکمه', () => {

  test('کاربر فقط‌خواندنی برای نوشتن ۴۰۳ می‌گیرد', async ({ request }) => {
    // مهم‌ترین تست کل مجموعه: کسی که دکمه را نمی‌بیند اگر مستقیم
    // API را صدا بزند هم باید رد شود.
    //
    // بدنه باید از اعتبارسنجی رد شود تا واقعاً به لایه‌ی کنترل دسترسی
    // برسد؛ WS_CODE باید عددی باشد و در قالب { Workshop, Accounts } باشد،
    // وگرنه سرور قبل از بررسی مجوز با ۴۰۰ رد می‌کند و تست چیزی را
    // ثابت نمی‌کند (نمونه‌ی این رفتار در Validation_runs_before_the_
    // permission_check_on_workshops_save در AclEndToEndTests.cs).
    const token = await apiLogin(request, 'viewer');

    const res = await request.post('/api/pay2/workshops/save', {
      headers: { Authorization: `Bearer ${token}` },
      data: {
        Workshop: { WS_ID: 0, WS_CODE: '994', WS_NAME: 'کارگاه غیرمجاز' },
        Accounts: {},
      },
    });

    expect(res.status(), 'کاربر فقط‌خواندنی نباید بتواند کارگاه بسازد').toBe(403);
  });

  test('کاربر فقط‌خواندنی حق حذف ندارد', async ({ request }) => {
    const token = await apiLogin(request, 'viewer');
    const res = await request.delete('/api/pay2/itemdefs/1', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.status()).toBe(403);
  });

  test('کاربر فقط‌خواندنی می‌تواند بخواند', async ({ request }) => {
    const token = await apiLogin(request, 'viewer');
    const res = await request.get('/api/pay2/itemdefs', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.status()).toBe(200);
  });

  test('لیست کارگاه‌ها برای کاربر محدود فیلتر می‌شود', async ({ request }) => {
    // محدودسازی باید روی خود داده اعمال شود، نه فقط روی دکمه‌ها.
    const scopedToken = await apiLogin(request, 'scoped');
    const adminToken  = await apiLogin(request, 'admin');

    const scoped = await (await request.get('/api/pay2/workshops', {
      headers: { Authorization: `Bearer ${scopedToken}` } })).json();
    const all = await (await request.get('/api/pay2/workshops', {
      headers: { Authorization: `Bearer ${adminToken}` } })).json();

    // نام فیلد بسته به تنظیم JSON ممکن است WS_ID یا wS_ID باشد؛ به هر دو مقاوم باش.
    const idOf = w => w.WS_ID ?? w.wS_ID ?? w.ws_ID ?? w.wsId;
    expect(scoped.map(idOf), 'کاربر محدود کارگاه دیگری دید').toEqual([1]);
    // اگر مدیر هم فقط یک کارگاه ببیند، این آزمون هیچ‌وقت شکست نمی‌خورد و
    // چیزی را ثابت نمی‌کند — پس خودِ این پیش‌شرط را هم بررسی می‌کنیم.
    expect(all.length, 'داده‌ی تست باید بیش از یک کارگاه داشته باشد وگرنه محدودسازی قابل مشاهده نیست')
        .toBeGreaterThan(1);
    expect(scoped.length).toBeLessThan(all.length);
  });
});

/**
 * حقوق و دستمزد یک صفحه است با نوار کناری (/salary/manage)، نه چند مسیر جدا.
 * بخش‌ها با کلیک روی آیتم‌های <li class="pay2-nav-item"> عوض می‌شوند.
 */
const PAYROLL_URL = '/salary/manage';

/** رفتن به یک بخش از نوار کناری حقوق و دستمزد. */
async function openTab(page, label) {
  await page.locator('li.pay2-nav-item').filter({ hasText: label }).first().click();
  await page.waitForTimeout(1500);
}

test.describe('رابط کاربری با نقش‌های مختلف', () => {

  test('صفحه حقوق و دستمزد برای مدیر باز می‌شود', async ({ page }) => {
    await uiLogin(page, 'admin');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);

    await expect(page.locator('li.pay2-nav-item').first()).toBeVisible();
    const body = await page.locator('body').innerText();
    expect(body).not.toMatch(/دسترسی لازم برای این عملیات را ندارید/);
  });

  test('بخش «آیتم‌های حقوقی» فقط برای کسی که مجوزش را دارد پیدا است', async ({ page, request }) => {
    // این بخش در کد با CanRun(Pay2Forms.ItemDef) شرطی شده است.
    const token = await apiLogin(request, 'viewer');
    const me = await (await request.get('/api/pay2/access/me', {
      headers: { Authorization: `Bearer ${token}` } })).json();
    const itemDef = me.forms.find(f => f.formName === 'PAY2_ITEM_DEF');

    await uiLogin(page, 'viewer');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);

    const tab = page.locator('li.pay2-nav-item').filter({ hasText: 'آیتم‌های حقوقی' });
    await expect(tab).toHaveCount(itemDef?.run ? 1 : 0);
  });

  test('کاربر فقط‌خواندنی در بخش کارگاه‌ها نمی‌تواند ذخیره یا حذف کند', async ({ page }) => {
    // الگوی این کدبیس disabled کردن دکمه است، نه حذفش از DOM (مثل
    // AdvanceTab و AttendanceTab). پس بررسی درست toHaveCount(0) نیست؛
    // باید غیرفعال بودن را چک کنیم — و مهم‌تر، اینکه سرور هم رد می‌کند
    // (تست جدا در بخش «اعمال شدن دسترسی روی API»).
    await uiLogin(page, 'viewer');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'مدیریت کارگاه‌ها');

    const save = page.getByRole('button', { name: /ذخیره/ });
    await expect(save, 'دکمه ذخیره باید غیرفعال باشد').toBeDisabled();

    // دکمه‌ی حذف فقط وقتی یک کارگاه انتخاب شده باشد رندر می‌شود.
    const rows = page.locator('table tbody tr');
    if (await rows.count() > 0) {
      await rows.first().click();
      await page.waitForTimeout(500);
      const del = page.getByRole('button', { name: /حذف کارگاه/ });
      if (await del.count() > 0) {
        await expect(del, 'دکمه حذف باید غیرفعال باشد').toBeDisabled();
      }
    }
  });

  test('مدیر در بخش کارگاه‌ها دکمه ساخت را می‌بیند', async ({ page }) => {
    await uiLogin(page, 'admin');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'مدیریت کارگاه‌ها');

    await expect(page.getByRole('button', { name: /کارگاه جدید|جدید/ }).first()).toBeVisible();
  });

  test('دکمه «احکام» برای کاربری که دسترسی احکام ندارد غیرفعال است', async ({ page }) => {
    // باگی که کاربر گزارش کرد: با بسته بودن دسترسی احکام، دکمه فعال بود،
    // مدال باز می‌شد، و بعد یک پیام انگلیسی خام ۴۰۳ روی صفحه می‌نشست.
    await uiLogin(page, 'viewer');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'پرسنل و احکام');

    const decreeButtons = page.getByRole('button', { name: /احکام/ });
    const count = await decreeButtons.count();
    expect(count, 'برای این آزمون باید دست‌کم یک پرسنل در فهرست باشد').toBeGreaterThan(0);
    await expect(decreeButtons.first(), 'دکمه احکام باید غیرفعال باشد').toBeDisabled();

    // و مدال هرگز باز نشده باشد.
    await expect(page.getByText('احکام کارگزینی:')).toHaveCount(0);
  });

  test('همان دکمه برای مدیر فعال است', async ({ page }) => {
    // بدون این، آزمون بالا می‌توانست صرفاً به‌خاطر خرابی رندر سبز بماند.
    await uiLogin(page, 'admin');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'پرسنل و احکام');

    await expect(page.getByRole('button', { name: /احکام/ }).first()).toBeEnabled();
  });

  test('پیام رد دسترسی فارسی است، نه متن خام HTTP', async ({ request }) => {
    // سرور متن فارسی می‌فرستد؛ قبلاً GetFromJsonAsync آن را دور می‌ریخت و
    // کاربر «Response status code does not indicate success: 403» می‌دید.
    const token = await apiLogin(request, 'viewer');
    const res = await request.get('/api/pay2/employees/1/decrees', {
      headers: { Authorization: `Bearer ${token}` },
    });

    expect(res.status()).toBe(403);
    const body = await res.text();
    expect(body).toContain('دسترسی لازم برای این عملیات را ندارید');
  });

  test('کاربر محدود در لیست کارگاه‌ها فقط کارگاه خودش را می‌بیند', async ({ page }) => {
    await uiLogin(page, 'scoped');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'مدیریت کارگاه‌ها');

    const scopedRows = await page.locator('table tbody tr').count();
    expect(scopedRows, 'کاربر محدود باید کارگاه خودش را ببیند').toBe(1);

    // مقایسه با مدیر — بدون این، «یک ردیف دیدن» می‌تواند صرفاً به‌خاطر
    // کم بودن داده باشد نه به‌خاطر اعمال شدن محدودسازی.
    await uiLogin(page, 'admin');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'مدیریت کارگاه‌ها');
    const adminRows = await page.locator('table tbody tr').count();

    expect(adminRows, 'مدیر باید کارگاه‌های بیشتری ببیند').toBeGreaterThan(scopedRows);
  });
});
