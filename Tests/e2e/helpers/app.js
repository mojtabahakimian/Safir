// @ts-check
const { expect } = require('@playwright/test');

const BASE = process.env.APP_URL || 'http://127.0.0.1:5080';

/** کاربران آزمایشی که test_auth_and_acl_users.sql می‌سازد. */
const USERS = {
  admin:  { username: 'payadmin',  password: '111111', userCo: 9001 },
  viewer: { username: 'payviewer', password: '222222', userCo: 9002 },
  scoped: { username: 'payscoped', password: '333333', userCo: 9003 },
};

/**
 * آیا دیتابیس در دسترس است؟
 *
 * بدون دیتابیس نمی‌شود وارد شد، پس تست‌هایی که به ورود نیاز دارند باید
 * skip شوند نه اینکه fail بدهند — وگرنه شکست محیط با شکست برنامه قاطی
 * می‌شود و کسی به گزارش تست اعتماد نمی‌کند.
 *
 * نتیجه یک بار حساب و کش می‌شود.
 */
let _dbReady;
async function databaseAvailable(request) {
  if (_dbReady !== undefined) return _dbReady;
  try {
    const res = await request.post(`${BASE}/api/auth/login`, {
      data: { Username: USERS.admin.username, Password: USERS.admin.password },
      timeout: 20_000,
    });
    const body = await res.json().catch(() => ({}));
    _dbReady = res.ok() && Boolean(body.token);
  } catch {
    _dbReady = false;
  }
  return _dbReady;
}

/** گرفتن توکن از API — برای تست‌های سطح API بدون رابط کاربری. */
async function apiLogin(request, who) {
  const u = USERS[who];
  const res = await request.post(`${BASE}/api/auth/login`, {
    data: { Username: u.username, Password: u.password },
  });
  expect(res.ok(), `ورود ${u.username} باید موفق باشد`).toBeTruthy();
  const body = await res.json();
  expect(body.token, `توکن برای ${u.username} صادر نشد`).toBeTruthy();
  return body.token;
}

/**
 * ورود از طریق رابط کاربری — همان کاری که یک آدم می‌کند.
 * Blazor WebAssembly اولین بار کند است، پس مهلت‌ها سخاوتمندانه‌اند.
 */
async function uiLogin(page, who) {
  const u = USERS[who];
  await page.goto('/login', { waitUntil: 'networkidle' });
  await field(page, 'نام کاربری').fill(u.username);
  await field(page, 'رمز عبور').fill(u.password);
  await page.getByRole('button', { name: 'ورود', exact: true }).click();
  await expect(page).not.toHaveURL(/\/login/, { timeout: 60_000 });
}

/**
 * پیدا کردن ورودی از روی متن برچسبش.
 *
 * چرا getByLabel کار نمی‌کند: MudBlazor 6.21.0 برچسب را با
 * for="mudinput-{guid}" می‌سازد ولی هیچ id ای روی خود input نمی‌گذارد،
 * پس اتصال برچسب به ورودی شکسته است. (این یک ایراد دسترس‌پذیری واقعی است
 * که روی همه‌ی فرم‌های برنامه اثر دارد، نه فقط تست.)
 * تا وقتی رفع نشده، از ساختار DOM خود MudBlazor استفاده می‌کنیم.
 */
function field(page, labelText) {
  return page.locator('.mud-input-control')
             .filter({ has: page.locator('label', { hasText: labelText }) })
             .locator('input')
             .first();
}

/** جمع‌کردن خطاهای کنسول و صفحه تا بشود در پایان تست بررسی‌شان کرد. */
function collectPageErrors(page) {
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
  return errors;
}

/** خطاهایی که به نبودِ دیتابیس مربوط‌اند و در محیط بدون DB طبیعی‌اند. */
function ignorableWithoutDb(text) {
  return /401|503|ERR_CONNECTION_RESET|SQL Server|پایگاه داده|در دسترس نیست/i.test(text);
}

module.exports = { BASE, USERS, databaseAvailable, apiLogin, uiLogin, field, collectPageErrors, ignorableWithoutDb };
