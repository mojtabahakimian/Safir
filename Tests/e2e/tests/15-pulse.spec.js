// @ts-check
/**
 * «نبض سازمان» (/pulse) — داشبوردِ React + Motion که Blazor با import() سوارش می‌کند.
 *
 * API ِ /api/pulse با page.route شبیه‌سازی می‌شود (توکنِ ساختگی، مثل 10-multi-tab و 14-sanad-typing)؛
 * پس بی‌دیتابیس هم اجرا می‌شود و عددها از پیش معلوم‌اند: فروشِ روزِ iام = (i + 1) میلیارد ریال.
 */
const { test, expect } = require('@playwright/test');

const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
const TOKEN = `${b64({ alg: 'HS256', typ: 'JWT' })}.${b64({ unique_name: 'e2e', exp: 4102444800 })}.sig`;
const fa = n => new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 }).format(n);

/** ۱۸۰ روزِ شمسیِ پشتِ سرِ هم که به ۱۴۰۵/۰۷/۱۱ ختم می‌شود (۶ ماهِ اول ۳۱ روزه، بعدی‌ها ۳۰ روزه). */
function persianDays(count) {
  const out = [];
  let y = 1405, m = 7, d = 11;
  for (let i = 0; i < count; i++) {
    out.unshift(y * 10000 + m * 100 + d);
    if (--d === 0) {
      if (--m === 0) { m = 12; y--; }
      d = m <= 6 ? 31 : m < 12 ? 30 : 29;
    }
  }
  return out;
}

function pulseData() {
  const days = persianDays(180);
  const seq = f => days.map((_, i) => f(i));
  return {
    days, end: days[179], firstWeekday: 1, fiscalYear: 1405,
    sales: seq(i => (i + 1) * 1e9), salesCount: seq(i => (i % 5) + 1),
    preInvoices: seq(i => (i + 1) * 2e9), preInvoiceCount: seq(() => 3),
    tasks: seq(i => 10 + (i % 7)), events: seq(i => 40 + (i % 11)),
    cash: seq(i => (i % 4) * 1e8), cashAccount: '111-1-1',
    cheques: seq(i => (i % 3) * 2e8), lastSalesDay: days[179]
  };
}

async function openPulse(page, respond) {
  await page.route(/\/api\/pulse(\?|$)/, respond);
  await page.goto('/login');
  await page.evaluate(t => {
    sessionStorage.setItem('authToken', JSON.stringify(t));
    localStorage.setItem('authToken', JSON.stringify(t));
  }, TOKEN);
  await page.goto('/pulse');
}

test('داشبوردِ نبض سوار می‌شود و با عوض شدنِ بازه عددها را از نو می‌شمارد', async ({ page }) => {
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await openPulse(page, route => route.fulfill({ json: pulseData() }));

  await expect(page.locator('.p-hero h1')).toHaveText('نبض سازمان');
  await expect(page.locator('.p-kpi')).toHaveCount(6);

  // ۳۰ روزِ آخر: جمعِ ۱۵۱ تا ۱۸۰ میلیارد = ۴٬۹۶۵ میلیارد
  const sales = page.locator('.p-kpi').first().locator('.p-kpi-value');
  await expect(sales).toHaveText(`${fa(4965)} میلیارد`, { timeout: 5000 });

  await page.locator('.p-tabs button', { hasText: '۹۰ روز' }).click();
  await expect(sales).toHaveText(`${fa(12195)} میلیارد`, { timeout: 5000 }); // ۹۱ تا ۱۸۰

  // نشانگر روی نمودارِ روند → راهنمای شناور با تاریخ
  const plot = page.locator('.p-card').first().locator('.p-plot svg');
  await plot.scrollIntoViewIfNeeded();
  await page.waitForTimeout(900); // کارت با رسیدن به دید بالا می‌آید
  const box = await plot.boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await expect(page.locator('.p-tip-day')).toBeVisible();

  expect(errors, errors.join('\n')).toHaveLength(0);
});

test('بدونِ مجوز، پیامِ سرور نشان داده می‌شود نه صفحه‌ی خالی', async ({ page }) => {
  await openPulse(page, route => route.fulfill({
    status: 403, contentType: 'text/plain; charset=utf-8',
    body: 'برای دیدنِ نبض سازمان، دسترسیِ «نبض سازمان» لازم است.'
  }));
  await expect(page.locator('.pulse-msg')).toContainText('دسترسیِ «نبض سازمان» لازم است');
  await expect(page.locator('.pulse')).toHaveCount(0);
});
