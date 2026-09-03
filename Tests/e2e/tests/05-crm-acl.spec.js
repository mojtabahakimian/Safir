// @ts-check
const { test, expect } = require('@playwright/test');
const { databaseAvailable, apiLogin, uiLogin } = require('../helpers/app');

/**
 * کنترل دسترسی CRM — «همه را ببیند» یا «فقط داده‌ی خودش».
 *
 * داده‌ی آزمایشی از test_crm_tables.sql:
 *   شرکت ۱، ۲  → payadmin  (۹۰۰۱) — و فقط او مجوز CRMALL دارد
 *   شرکت ۳، ۴  → payviewer (۹۰۰۲)
 *   شرکت ۵     → payscoped (۹۰۰۳)
 *   شرکت ۶     → userid تهی، USER_NAME = payviewer (رکورد سبک WPF)
 *
 * کلید CRM_ACL_ENFORCE پیش‌فرض خاموش است، پس این تست‌ها خودشان روشن و
 * دوباره خاموشش می‌کنند و وضعیت اولیه را برمی‌گردانند.
 */

const CFG = '/api/pay2/settings/configs';

/** روشن/خاموش کردن محدودیت از همان مسیری که کاربر واقعی استفاده می‌کند. */
async function setEnforce(request, token, value) {
  const res = await request.post(`${CFG}/save`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { Items: [{ CFG_KEY: 'CRM_ACL_ENFORCE', CFG_VALUE: value }] },
  });
  expect(res.ok(), `تغییر CRM_ACL_ENFORCE به ${value} ناموفق بود`).toBeTruthy();
}

const auth = (t) => ({ Authorization: `Bearer ${t}` });

/** نام شرکت‌هایی که این کاربر واقعاً از سرور می‌گیرد. */
async function companyIds(request, token, onlyMine = false) {
  const res = await request.post('/api/crm/companies', {
    headers: auth(token),
    data: { OnlyMyCompanies: onlyMine },
  });
  expect(res.ok()).toBeTruthy();
  return (await res.json()).map((c) => c.iD ?? c.ID ?? c.id).sort((a, b) => a - b);
}

test.describe('کنترل دسترسی CRM', () => {
  let admin, viewer, scoped;

  test.beforeEach(async ({ request }) => {
    test.skip(!await databaseAvailable(request),
      'دیتابیس تست در دسترس نیست — ابتدا scripts/setup-test-env.sh را اجرا کنید.');
    admin  = await apiLogin(request, 'admin');
    viewer = await apiLogin(request, 'viewer');
    scoped = await apiLogin(request, 'scoped');
  });

  test.afterEach(async ({ request }) => {
    // وضعیت اولیه (خاموش) را برگردان تا تست‌های دیگر تحت تأثیر نباشند.
    if (admin) await setEnforce(request, admin, '0');
  });

  test('خاموش: هر کاربر می‌تواند با یک کلیک داده‌ی بقیه را ببیند', async ({ request }) => {
    await setEnforce(request, admin, '0');

    const all = await companyIds(request, viewer, false);
    // این همان نشتی است که این تغییر می‌بندد — payviewer شرکت‌های
    // payadmin و payscoped را هم می‌گیرد.
    expect(all).toEqual(expect.arrayContaining([1, 2, 5]));

    expect((await request.get('/api/crm/companies/1', { headers: auth(viewer) })).status()).toBe(200);
  });

  test('روشن: کاربر بدون CRMALL فقط رکوردهای خودش را می‌گیرد', async ({ request }) => {
    await setEnforce(request, admin, '1');

    // حتی وقتی کلاینت صریحاً OnlyMyCompanies=false بفرستد.
    const seen = await companyIds(request, viewer, false);
    expect(seen).toContain(3);
    expect(seen).toContain(4);
    expect(seen).toContain(6);          // رکورد بی‌مالک، از راه USER_NAME
    expect(seen).not.toContain(1);
    expect(seen).not.toContain(2);
    expect(seen).not.toContain(5);
  });

  test('روشن: تطبیق USER_NAME دقیق است و به کاربر دیگر سرایت نمی‌کند', async ({ request }) => {
    await setEnforce(request, admin, '1');

    // شرکت ۶ فقط USER_NAME = payviewer دارد؛ payscoped نباید ببیندش.
    const seen = await companyIds(request, scoped, false);
    expect(seen).toEqual([5]);
  });

  test('روشن: دسترسی مستقیم با شناسه هم بسته است، نه فقط لیست', async ({ request }) => {
    await setEnforce(request, admin, '1');

    expect((await request.get('/api/crm/companies/1', { headers: auth(viewer) })).status()).toBe(403);
    expect((await request.get('/api/crm/companies/3', { headers: auth(viewer) })).status()).toBe(200);
    expect((await request.get('/api/crm/events/1',    { headers: auth(viewer) })).status()).toBe(403);
    expect((await request.delete('/api/crm/companies/1', { headers: auth(viewer) })).status()).toBe(403);
  });

  test('روشن: کاربر دارای CRMALL همه را می‌بیند', async ({ request }) => {
    await setEnforce(request, admin, '1');

    const seen = await companyIds(request, admin, false);
    expect(seen).toEqual(expect.arrayContaining([1, 2, 3, 4, 5, 6]));
    expect((await request.get('/api/crm/companies/3', { headers: auth(admin) })).status()).toBe(200);
  });

  test('مالکیت از بدنه‌ی درخواست پذیرفته نمی‌شود', async ({ request }) => {
    await setEnforce(request, admin, '1');

    const res = await request.post('/api/crm/save-company', {
      headers: auth(viewer),
      data: { COMPANY_NAME: 'تلاش برای ثبت به نام دیگری', USERID: 9001, USER_NAME: 'payadmin', STATUS: 1 },
    });
    expect(res.ok()).toBeTruthy();
    const newId = await res.json();

    // اگر سرور USERID کلاینت را می‌پذیرفت، سازنده دیگر آن را نمی‌دید.
    expect(await companyIds(request, viewer, false)).toContain(newId);
    expect((await request.get(`/api/crm/companies/${newId}`, { headers: auth(admin) })).status()).toBe(200);

    await request.delete(`/api/crm/companies/${newId}`, { headers: auth(admin) });
  });

  test('روشن: شمارنده‌ها و داشبورد هم محدود می‌شوند، نه فقط لیست', async ({ request }) => {
    await setEnforce(request, admin, '1');

    const total = async (token) => {
      const rows = await (await request.get('/api/crm/status-list', { headers: auth(token) })).json();
      return rows.reduce((s, r) => s + (r.count ?? r.Count ?? 0), 0);
    };
    const viewerTotal = await total(viewer);
    const adminTotal  = await total(admin);
    expect(viewerTotal).toBeLessThan(adminTotal);

    const dash = await (await request.get('/api/crm/dashboard-summary', { headers: auth(viewer) })).json();
    expect(dash.totalCompanies ?? dash.TotalCompanies).toBe(viewerTotal);
  });

  test('روشن: چک‌باکس «مشتریان من» برای کاربر محدود پنهان می‌شود', async ({ request, page }) => {
    await setEnforce(request, admin, '1');

    await uiLogin(page, 'viewer');
    await page.goto('/crm', { waitUntil: 'networkidle' });
    await expect(page.getByText('لیست مخاطبان')).toBeVisible({ timeout: 30_000 });

    // بی‌اثر است، پس نباید نشان داده شود.
    await expect(page.getByText('مشتریان من')).toHaveCount(0);
    // ولی فیلتر واقعیِ کنارش سر جایش می‌ماند.
    await expect(page.getByText('دارای پیگیری آتی')).toBeVisible();
  });

  test('روشن: کاربر دارای CRMALL همچنان چک‌باکس را دارد', async ({ request, page }) => {
    await setEnforce(request, admin, '1');

    await uiLogin(page, 'admin');
    await page.goto('/crm', { waitUntil: 'networkidle' });
    await expect(page.getByText('لیست مخاطبان')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('مشتریان من')).toBeVisible();
  });
});
