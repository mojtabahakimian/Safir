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

    for (const f of me.forms) {
      expect(f.see, `${f.formName} باید قابل مشاهده باشد`).toBe(true);
      expect(f.inp || f.upd || f.del,
        `${f.formName} نباید هیچ مجوز تغییری داشته باشد`).toBe(false);
    }
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
    const token = await apiLogin(request, 'viewer');

    const res = await request.post('/api/pay2/workshops/save', {
      headers: { Authorization: `Bearer ${token}` },
      data: { WS_ID: 0, WS_CODE: 'HACK', WS_NAME: 'کارگاه غیرمجاز' },
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
    expect(scoped.length).toBeLessThanOrEqual(all.length);
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

  test('کاربر فقط‌خواندنی در بخش کارگاه‌ها دکمه تغییر نمی‌بیند', async ({ page }) => {
    await uiLogin(page, 'viewer');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'مدیریت کارگاه‌ها');

    for (const label of ['کارگاه جدید', 'حذف', 'ذخیره']) {
      await expect(page.getByRole('button', { name: new RegExp(label) }),
        `دکمه «${label}» نباید برای کاربر فقط‌خواندنی دیده شود`).toHaveCount(0);
    }
  });

  test('مدیر در بخش کارگاه‌ها دکمه ساخت را می‌بیند', async ({ page }) => {
    await uiLogin(page, 'admin');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'مدیریت کارگاه‌ها');

    await expect(page.getByRole('button', { name: /کارگاه جدید|جدید/ }).first()).toBeVisible();
  });

  test('کاربر محدود در لیست کارگاه‌ها فقط کارگاه خودش را می‌بیند', async ({ page }) => {
    await uiLogin(page, 'scoped');
    await page.goto(PAYROLL_URL, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    await openTab(page, 'مدیریت کارگاه‌ها');

    const rows = page.locator('table tbody tr');
    const count = await rows.count();
    expect(count, 'کاربر محدود باید حداقل کارگاه خودش را ببیند').toBeGreaterThan(0);
    expect(count, 'کاربر محدود بیش از یک کارگاه دید').toBeLessThanOrEqual(1);
  });
});
