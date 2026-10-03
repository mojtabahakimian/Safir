// @ts-check
/**
 * صدور و ویرایش اسناد — تایپ در خانه‌ی «حساب» ِ ردیف.
 *
 * MudBaseInput در WebAssembly با هر رندر، متنِ ورودی را از روی Value بازنویسی می‌کند. ویرایشگرِ ردیف
 * روی خانه‌ی حساب @onkeydown داشت و بعد از هر کلید رندر می‌شد؛ پس هر حرفِ تازه حرف‌های قبلی را پاک
 * می‌کرد و فقط آخرین حرف می‌ماند. این باگ فقط با رویدادِ واقعیِ keydown دیده می‌شود (نه fill/insertText).
 *
 * API ِ /api/sanad با page.route شبیه‌سازی می‌شود و توکن ساختگی است — دیتابیس لازم نیست.
 */
const { test, expect } = require('@playwright/test');

const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
const TOKEN = `${b64({ alg: 'HS256', typ: 'JWT' })}.${b64({ unique_name: 'e2e', exp: 4102444800 })}.sig`;

const HEADER = { ns: 900001, date: 14050711, sharh: 'آزمایش تایپ', noS: 0, base: 1, bayeg: 100000001, userName: 'e2e', rowCount: 0 };
const META = {
  canSee: true, canCreate: true, canUpdate: true, canDelete: true,
  ada: '113-1-1', adv: '113-1-2', apa: '211-1-1', apv: '211-1-2', userName: 'e2e',
  personnel: [], costCenters: [], banks: [], chequeFunds: [], bankAccounts: [],
};

async function mockSanadApi(page) {
  await page.route(/\/api\/sanad(\/|\?|$)/, route => {
    const path = new URL(route.request().url()).pathname.replace(/\/+$/, '');
    const method = route.request().method();
    if (path.endsWith('/meta')) return route.fulfill({ json: META });
    if (path.endsWith('/descriptions') || path.endsWith('/balances')) return route.fulfill({ json: [] });
    if (path.endsWith('/accounts')) return route.fulfill({ json: [{ hes: '115-3-1591', name: 'حساب آزمایشی' }] });
    if (path.endsWith('/api/sanad')) return route.fulfill({ json: method === 'POST' ? { ok: true, ns: HEADER.ns } : [HEADER] });
    if (path.endsWith(`/api/sanad/${HEADER.ns}`)) return route.fulfill({ json: { header: HEADER, rows: [] } });
    return route.fulfill({ json: {} });
  });
}

async function openSanad(page) {
  await mockSanadApi(page);
  await page.goto('/login');
  await page.evaluate(t => {
    sessionStorage.setItem('authToken', JSON.stringify(t));
    localStorage.setItem('authToken', JSON.stringify(t));
  }, TOKEN);
  await page.goto('/sanad');
}

test('تایپ در «حساب» ِ ردیفِ سند همه‌ی حروف را نگه می‌دارد', async ({ page }) => {
  await openSanad(page);
  await page.getByRole('button', { name: 'سند جدید' }).first().click();
  await page.getByRole('button', { name: 'ایجاد سند' }).click();

  const account = page.locator('.snd-ed.is-new [data-f=acc] input');
  await expect(account).toBeFocused();

  // keyboard.type برای هر حرف keydown/keypress/input/keyup ِ واقعی می‌فرستد
  await page.keyboard.type('115-3', { delay: 150 });
  await expect(account).toHaveValue('115-3');
});

/**
 * زیرِ ۱۲۵۰ پیکسل «ثبت» ِ سربرگ پنهان می‌شود؛ قاعده با :last-child نوشته شده بود و در فرمِ «سند جدید»
 * (که فقط تاریخ و شرح دارد) خودِ «شرحِ سند» را پنهان می‌کرد.
 */
test('فرمِ «سند جدید» در عرضِ کم هم «شرحِ سند» را نشان می‌دهد', async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await openSanad(page);
  await page.getByRole('button', { name: 'سند جدید' }).first().click();

  const sharh = page.getByPlaceholder('مثلاً: سند اصلاحی بابت …');
  await expect(sharh).toBeVisible();
  await sharh.fill('شرح آزمایشی');
  await expect(sharh).toHaveValue('شرح آزمایشی');
});
