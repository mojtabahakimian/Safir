// @ts-check
/**
 * پنجره‌ی «مدیریت پله‌های مالیاتی» در «تنظیمات سیستم» — مثل یک کاربر واقعی.
 *
 * نیاز به دیتابیس تست دارد (scripts/setup-test-env.sh) که پله‌های ۱۴۰۳ و ۱۴۰۵ و
 * TAX_YEAR=1405 را می‌سازد. اگر دیتابیس نباشد skip می‌شود، نه fail.
 *
 * سال «جدید» ثابت نیست: اولین سالِ بدون پله از ۱۴۰۶ به بعد انتخاب می‌شود تا اجرای
 * دوباره روی همان دیتابیس هم معنی داشته باشد (API حذف سال وجود ندارد).
 * TAX_YEAR در پایان به ۱۴۰۵ برمی‌گردد، چون آزمون زنجیره‌ی حقوق به آن وابسته است.
 */
const { test, expect } = require('@playwright/test');
const { databaseAvailable, apiLogin, uiLogin } = require('../helpers/app');

test.describe.configure({ mode: 'serial' });

const ACTIVE_YEAR = '1405';
const COPY_SOURCE = '1403';
let NEW_YEAR = '';
let auth;

/** ارقام فارسی/عربی را لاتین می‌کند و هر چیز غیررقمی را حذف. */
const digits = s => String(s ?? '')
  .replace(/[۰-۹]/g, d => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(d)))
  .replace(/[٠-٩]/g, d => String('٠١٢٣٤٥٦٧٨٩'.indexOf(d)))
  .replace(/\D/g, '');

async function bracketsOf(request, year) {
  const res = await request.get(`/api/pay2/settings/tax/brackets?year=${year}`, { headers: auth });
  expect(res.ok()).toBeTruthy();
  return (await res.json()).map(r => ({
    upper: Number(r.uppeR_LIMIT ?? r.UPPER_LIMIT), rate: Number(r.ratE_PCT ?? r.RATE_PCT),
  }));
}

async function taxYearInDb(request) {
  const cfgs = await (await request.get('/api/pay2/settings/configs', { headers: auth })).json();
  const row = cfgs.find(c => (c.cfG_KEY ?? c.CFG_KEY) === 'TAX_YEAR');
  return String(row.cfG_VALUE ?? row.CFG_VALUE);
}

async function setTaxYear(request, year) {
  const res = await request.post('/api/pay2/settings/configs/save', {
    headers: auth,
    data: { Items: [{ CFG_KEY: 'TAX_YEAR', CFG_VALUE: year }], ChangeNote: 'e2e: بازگرداندن سال مالیاتی' },
  });
  expect(res.ok(), await res.text()).toBeTruthy();
}

/** اسکرین‌شات: همیشه پیوست گزارش، و اگر E2E_SHOTS ست باشد آنجا هم ذخیره می‌شود. */
async function shot(page, name) {
  const buf = await page.screenshot();
  await test.info().attach(name, { body: buf, contentType: 'image/png' });
  if (process.env.E2E_SHOTS) require('fs').writeFileSync(require('path').join(process.env.E2E_SHOTS, `${name}.png`), buf);
}

/** عناصر پنجره — همه از روی متن‌هایی که کاربر می‌بیند. */
function ui(page) {
  const modal = page.locator('.pay2-modal-container');
  const fieldBox = label => modal.locator('.form-field').filter({ has: page.locator('label', { hasText: label }) });
  const rows = modal.locator('table.p2-table tbody tr').filter({ has: page.locator('input') });
  return {
    modal,
    yearSelect: fieldBox('سال مالیاتی').locator('input.pay2-select-input'),
    newYear: fieldBox('سال جدید').locator('input'),
    createYear: modal.getByRole('button', { name: 'ایجاد سال' }),
    copyBox: fieldBox('کپی پله‌ها از سال'),
    copyButton: modal.locator('button').filter({ hasText: /^\s*کپی\s*$/ }),
    addRow: modal.getByRole('button', { name: 'افزودن پله' }),
    save: modal.getByRole('button', { name: /ذخیره پله‌ها برای سال/ }),
    cancel: modal.getByRole('button', { name: 'انصراف' }),
    close: modal.locator('.pay2-modal-header button'),
    activate: modal.getByRole('button', { name: /محاسبه‌ی حقوق با سال \d+ انجام شود/ }),
    dirtyNote: modal.getByText('تغییرات این جدول هنوز ذخیره نشده است'),
    closeWarning: modal.getByText('تغییرات ذخیره نشده است. برای بستن'),
    rows,
    upper: i => rows.nth(i).locator('td').nth(2).locator('input'),
    rate: i => rows.nth(i).locator('td').nth(3).locator('input'),
    from: i => rows.nth(i).locator('td').nth(1),
    trash: i => rows.nth(i).getByTitle('حذف این پله'),
  };
}

const snack = (page, text) => page.locator('.mud-snackbar').filter({ hasText: text }).last();

async function openSettings(page, who) {
  await uiLogin(page, who);
  await page.goto('/salary/manage', { waitUntil: 'networkidle' });
  await page.locator('li.pay2-nav-item').filter({ hasText: 'تنظیمات سیستم' }).first().click();
  await expect(page.getByRole('button', { name: 'مدیریت پله‌های مالیاتی' })).toBeVisible();
}

async function openModal(page) {
  await page.getByRole('button', { name: 'مدیریت پله‌های مالیاتی' }).click();
  await expect(ui(page).modal).toBeVisible();
  await expect(ui(page).yearSelect).not.toHaveValue('');
}

/** باز کردن فهرست با فلش کنارش — کلیک روی ورودیِ از قبل فوکوس‌شده فهرست را دوباره باز نمی‌کند. */
async function openSelect(selectInput) {
  await selectInput.locator('xpath=..').locator('.pay2-select-arrow-button').click();
}

async function pickYear(page, selectInput, label) {
  await openSelect(selectInput);
  await page.locator('.pay2-select-menu .pay2-select-item').filter({ hasText: label }).first().click();
}

/** تایپ مثل کاربر: کلیک، پاک کردن، تایپ، و خروج با Tab. */
async function type(locator, text) {
  await locator.click();
  await locator.fill('');
  if (text !== '') await locator.pressSequentially(text);
  await locator.press('Tab');
}

test.beforeAll(async ({ request }) => {
  if (!await databaseAvailable(request)) return;
  auth = { Authorization: `Bearer ${await apiLogin(request, 'admin')}` };
  const years = (await (await request.get('/api/pay2/settings/tax/years', { headers: auth })).json()).map(String);
  for (let y = 1406; y <= 1499 && !NEW_YEAR; y++) if (!years.includes(String(y))) NEW_YEAR = String(y);
  // پیش‌شرط‌ها — اگر نباشند، آزمون چیزی را ثابت نمی‌کند
  expect(years).toContain(COPY_SOURCE);
  expect(years).toContain(ACTIVE_YEAR);
  await setTaxYear(request, ACTIVE_YEAR);
});

test.afterAll(async ({ request }) => {
  if (auth) await setTaxYear(request, ACTIVE_YEAR);
});

test.beforeEach(async ({ request }) => {
  test.skip(!await databaseAvailable(request),
    'دیتابیس تست در دسترس نیست — ابتدا scripts/setup-test-env.sh را اجرا کنید.');
});

test.describe('مدیریت پله‌های مالیاتی', () => {

  test('۱) روی سال TAX_YEAR باز می‌شود؛ «از» هر ردیف همان «تا»ی ردیف قبل است', async ({ page }) => {
    await openSettings(page, 'admin');
    await openModal(page);
    const u = ui(page);
    await shot(page, '01-modal-open');

    await expect(u.yearSelect).toHaveValue(`سال ${ACTIVE_YEAR}`);
    await expect(u.modal).toContainText(`محاسبه‌ی حقوق با پله‌های سال ${ACTIVE_YEAR} انجام می‌شود`);
    await expect(u.activate).toHaveCount(0);

    const headers = (await u.modal.locator('table.p2-table thead th').allInnerTexts()).map(s => s.trim());
    expect(headers.slice(0, 5)).toEqual(
      ['پله', 'از درآمد سالانه (ریال)', 'تا درآمد سالانه (ریال)', 'نرخ (%)', 'مالیات پله‌های قبل (خودکار)']);

    const n = await u.rows.count();
    expect(n).toBeGreaterThan(1);
    expect(digits(await u.from(0).innerText())).toBe('0');
    for (let i = 1; i < n; i++) {
      expect(digits(await u.from(i).innerText()), `ردیف ${i + 1}`).toBe(digits(await u.upper(i - 1).inputValue()));
    }

    await expect(u.modal).toContainText('معافیت ماهانه‌ی فعلی');
    expect(digits(await u.modal.locator('b.p2-text-mono').innerText())).toBe('400000000');
  });

  test('۳) انتخاب سال فقط از فهرست است و تایپ روی آن متن را خراب نمی‌کند', async ({ page }) => {
    await openSettings(page, 'admin');
    await openModal(page);
    const u = ui(page);

    await expect(u.yearSelect).toHaveAttribute('readonly', '');
    // باگ قبلی: تایپ روی «سال ۱۴۰۵» متن «۱۴۰۵۵۴» می‌ساخت
    await u.yearSelect.click();
    await page.keyboard.type('54');
    await u.yearSelect.press('End');
    await page.keyboard.type('54');
    await expect(u.yearSelect).toHaveValue(`سال ${ACTIVE_YEAR}`);
    await shot(page, '03-typing-on-year-select');
    await page.keyboard.press('Escape');

    // انتخاب از فهرست: سال ۱۴۰۳ با نوار زرد و دکمه‌ی فعال
    await pickYear(page, u.yearSelect, `سال ${COPY_SOURCE}`);
    await expect(u.yearSelect).toHaveValue(`سال ${COPY_SOURCE}`);
    await expect(u.modal).toContainText(`این جدول سال ${COPY_SOURCE} است، ولی محاسبه‌ی حقوق الان با سال ${ACTIVE_YEAR}`);
    await expect(u.activate).toBeEnabled();
    await shot(page, '02-other-year-yellow-bar');
  });

  test('۴) «سال جدید»: ورودی نامعتبر هشدار فارسی می‌دهد، سال معتبر جدول خالی می‌سازد', async ({ page }) => {
    await openSettings(page, 'admin');
    await openModal(page);
    const u = ui(page);

    for (const bad of ['1200', '14']) {
      await type(u.newYear, bad);
      await u.createYear.click();
      await expect(snack(page, 'سال را ۴ رقمی وارد کنید'), `ورودی ${bad}`).toBeVisible();
    }
    await shot(page, '04-invalid-year-warning');
    await expect(u.yearSelect).toHaveValue(`سال ${ACTIVE_YEAR}`);

    await type(u.newYear, NEW_YEAR);
    await u.createYear.click();
    await expect(u.yearSelect).toHaveValue(`سال ${NEW_YEAR} (جدید)`);
    await expect(u.rows).toHaveCount(0);
    await expect(u.modal).toContainText(`برای سال ${NEW_YEAR} پله‌ای ثبت نشده است`);

    await openSelect(u.yearSelect);
    await expect(page.locator('.pay2-select-menu .pay2-select-item').filter({ hasText: `سال ${NEW_YEAR} (جدید)` })).toBeVisible();
    await page.keyboard.press('Escape');

    // هنوز پله‌ای ذخیره نشده → «محاسبه با این سال» نباید ممکن باشد
    await expect(u.activate).toBeDisabled();
    await expect(u.activate).toHaveAttribute('title', new RegExp(`برای سال ${NEW_YEAR} هنوز پله‌ای ذخیره نشده است`));
    await shot(page, '04-new-year-empty');
  });

  test('۵) کپی از هر سال ذخیره‌شده (۱۴۰۳، نه فقط سال قبل) و ذخیره در دیتابیس', async ({ page, request }) => {
    await openSettings(page, 'admin');
    await openModal(page);
    const u = ui(page);
    await type(u.newYear, NEW_YEAR);
    await u.createYear.click();
    await expect(u.rows).toHaveCount(0);

    await expect(u.copyBox).toBeVisible();
    const copySelect = u.copyBox.locator('input.pay2-select-input');
    await openSelect(copySelect);
    const offered = await page.locator('.pay2-select-menu .pay2-select-item').allInnerTexts();
    expect(offered.map(s => s.trim())).toEqual(expect.arrayContaining([`سال ${ACTIVE_YEAR}`, `سال ${COPY_SOURCE}`]));
    await page.locator('.pay2-select-menu .pay2-select-item').filter({ hasText: `سال ${COPY_SOURCE}` }).click();
    await expect(copySelect).toHaveValue(`سال ${COPY_SOURCE}`);
    await shot(page, '05-copy-source-picked');

    await u.copyButton.click();
    await expect(snack(page, `پله‌های سال ${COPY_SOURCE} به سال ${NEW_YEAR} کپی و ذخیره شد`)).toBeVisible();

    const source = await bracketsOf(request, COPY_SOURCE);
    await expect(u.rows).toHaveCount(source.length);
    await expect(u.copyBox, 'با جدول پر، کپی نباید دیده شود').toHaveCount(0);
    expect(await bracketsOf(request, NEW_YEAR), 'کپی باید در دیتابیس ذخیره شده باشد').toEqual(source);
    await shot(page, '05-after-copy');
  });

  test('۶ و ۷) «افزودن پله» سطر خالی می‌سازد؛ پیام‌های ذخیره شماره‌ی پله را می‌گویند', async ({ page, request }) => {
    await openSettings(page, 'admin');
    await openModal(page);
    const u = ui(page);
    await pickYear(page, u.yearSelect, `سال ${NEW_YEAR}`);
    const saved = await bracketsOf(request, NEW_YEAR);
    await expect(u.rows).toHaveCount(saved.length);
    const n = saved.length + 1;

    await u.addRow.click();
    await expect(u.rows).toHaveCount(n);
    await expect(u.upper(n - 1)).toHaveValue('');
    await expect(u.rate(n - 1)).toHaveValue('');
    await shot(page, '06-empty-row-added');

    // «تا» خالی
    await u.save.click();
    await expect(snack(page, `پله ${n}: ستون «تا درآمد سالانه» را وارد کنید`)).toBeVisible();

    // «تا» مساوی پله‌ی قبل
    await type(u.upper(n - 1), String(saved[n - 2].upper));
    await type(u.rate(n - 1), '30');
    await u.save.click();
    await expect(snack(page, `پله ${n}: «تا» باید از سقف پله‌ی قبل`)).toBeVisible();

    // نرخ بیشتر از ۱۰۰
    await type(u.upper(n - 1), String(saved[n - 2].upper + 1_000_000_000));
    await type(u.rate(n - 1), '150');
    await u.save.click();
    await expect(snack(page, `پله ${n}: نرخ را بین ۰ تا ۱۰۰ وارد کنید`)).toBeVisible();
    await shot(page, '07-rate-over-100');

    // جدول بدون پله
    for (let i = n - 1; i >= 0; i--) await u.trash(i).click();
    await expect(u.rows).toHaveCount(0);
    await u.save.click();
    await expect(snack(page, 'حداقل یک پله وارد کنید')).toBeVisible();

    // هیچ‌کدام از تلاش‌های ناموفق چیزی در دیتابیس عوض نکرده است
    expect(await bracketsOf(request, NEW_YEAR)).toEqual(saved);
  });

  test('۷-ب) ذخیره‌ی موفق پله‌ی جدید', async ({ page, request }) => {
    await openSettings(page, 'admin');
    await openModal(page);
    const u = ui(page);
    await pickYear(page, u.yearSelect, `سال ${NEW_YEAR}`);
    const saved = await bracketsOf(request, NEW_YEAR);
    await expect(u.rows).toHaveCount(saved.length);

    await u.addRow.click();
    const top = saved[saved.length - 1].upper + 50_000_000_000;
    await type(u.upper(saved.length), String(top));
    await type(u.rate(saved.length), '35');
    await u.save.click();
    await expect(snack(page, `پله‌های مالیاتی سال ${NEW_YEAR} ذخیره شد`)).toBeVisible();
    await expect(u.dirtyNote).toHaveCount(0);
    expect(await bracketsOf(request, NEW_YEAR)).toEqual([...saved, { upper: top, rate: 35 }]);
  });

  test('۸) تغییر ذخیره‌نشده: قفل انتخاب سال، هشدار بستن دو مرحله‌ای، focus/blur بی‌اثر', async ({ page, request }) => {
    await openSettings(page, 'admin');
    await openModal(page);
    const u = ui(page);

    // فقط focus و blur — نباید «تغییریافته» حساب شود
    await u.upper(0).click();
    await u.upper(0).press('Tab');
    await u.rate(0).click();
    await u.rate(0).press('Tab');
    await expect(u.dirtyNote).toHaveCount(0);
    await expect(u.yearSelect).toBeEnabled();
    await u.cancel.click();
    await expect(u.modal, 'بدون تغییر، «انصراف» باید همان بار اول ببندد').toHaveCount(0);

    await openModal(page);
    const before = await bracketsOf(request, ACTIVE_YEAR);
    await type(u.rate(0), String(before[0].rate + 1));
    await expect(u.dirtyNote).toBeVisible();
    await expect(u.yearSelect).toBeDisabled();
    await expect(u.newYear).toBeDisabled();
    await expect(u.createYear).toBeDisabled();

    await u.cancel.click();
    await expect(u.closeWarning).toBeVisible();
    await expect(u.modal).toBeVisible();
    await shot(page, '08-close-warning');
    await u.close.click();
    await expect(u.modal).toHaveCount(0);

    expect(await bracketsOf(request, ACTIVE_YEAR), 'بستن بدون ذخیره نباید چیزی ذخیره کند').toEqual(before);
    await openModal(page);
    await expect(u.rate(0)).toHaveValue(String(before[0].rate));
  });

  test('۲ و ۹) «محاسبه با سال X» TAX_YEAR را ذخیره می‌کند و ویرایش‌های دیگر صفحه را نگه می‌دارد', async ({ page, request }) => {
    await openSettings(page, 'admin');

    // ویرایش ذخیره‌نشده در یک فیلد دیگرِ همین صفحه
    const otField = page.locator('.form-field')
      .filter({ has: page.locator('label', { hasText: 'ضریب اضافه‌کار عادی' }) }).locator('input').first();
    const otOriginal = await otField.inputValue();
    const otEdited = otOriginal === '1.55' ? '1.45' : '1.55';
    await type(otField, otEdited);
    await expect(page.getByRole('button', { name: /ذخیره 1 مورد تغییریافته/ })).toBeVisible();

    await openModal(page);
    const u = ui(page);
    await pickYear(page, u.yearSelect, `سال ${NEW_YEAR}`);
    await expect(u.activate).toBeEnabled();

    // با تغییر ذخیره‌نشده غیرفعال است
    await u.addRow.click();
    await expect(u.activate).toBeDisabled();
    await expect(u.activate).toHaveAttribute('title', 'اول پله‌ها را ذخیره کنید');
    await shot(page, '02-activate-disabled-when-dirty');
    await u.cancel.click();
    await u.cancel.click();
    await openModal(page);
    await pickYear(page, u.yearSelect, `سال ${NEW_YEAR}`);

    await u.activate.click();
    await expect(snack(page, `از این به بعد محاسبه‌ی حقوق با پله‌های سال ${NEW_YEAR} انجام می‌شود`)).toBeVisible();
    await expect(u.modal).toContainText(`محاسبه‌ی حقوق با پله‌های سال ${NEW_YEAR} انجام می‌شود`);
    await shot(page, '02-activated-green');
    expect(await taxYearInDb(request), 'TAX_YEAR باید در دیتابیس ذخیره شده باشد').toBe(NEW_YEAR);

    await u.close.click();
    await expect(otField, 'ویرایش ذخیره‌نشده‌ی دیگر نباید از بین برود').toHaveValue(otEdited);
    await expect(page.getByRole('button', { name: /ذخیره 1 مورد تغییریافته/ })).toBeVisible();
    await shot(page, '09-other-edits-kept');

    await setTaxYear(request, ACTIVE_YEAR);
  });
});
