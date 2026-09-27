// @ts-check
/**
 * دو شرکت (دو دیتابیس) در دو تب هم‌زمان، مثل دو EXE — درخواست یزدسپار/پودر.
 *
 * قبلاً تنظیم دیتابیس و توکن در localStorage بود که بین تب‌ها مشترک است: ذخیره‌ی پودر در تب دوم،
 * تب یزدسپار را با اولین رفرش بی‌صدا به پودر وصل می‌کرد. حالا هر تب نشست خودش را در
 * sessionStorage دارد (TabSession) و localStorage فقط «آخرین ورود» برای شروع تب جدید است.
 *
 * به دیتابیس و ورود نیاز ندارد: تنظیم دیتابیس از فرم صفحه‌ی ورود ذخیره می‌شود و نام شرکت
 * در نوار بالا (.safir-db-chip) و اول عنوان تب نشان داده می‌شود.
 */
const { test, expect } = require('@playwright/test');
const { field } = require('../helpers/app');

async function saveDatabase(page, server, database) {
  await page.getByText('تنظیمات سرور و دیتابیس').click();
  await field(page, 'سرور').fill(server);
  await field(page, 'نام دیتابیس').fill(database);
  await page.getByRole('button', { name: 'ذخیره تنظیمات دیتابیس' }).click();
  await expect(page.getByText('تنظیمات دیتابیس با موفقیت ذخیره شد')).toBeVisible();
}

const chip = page => page.locator('.safir-db-chip');

test('هر تب شرکت خودش را نگه می‌دارد و رفرش آن را عوض نمی‌کند', async ({ context }) => {
  const yazd = await context.newPage();
  await yazd.goto('/login');
  await saveDatabase(yazd, 'SRV-A', 'YAZDSEPAR1405');
  await yazd.reload();
  await expect(chip(yazd)).toHaveText(/YAZDSEPAR1405/);

  // توکن ساختگی (ساختار JWT و منقضی‌نشده، امضای بی‌اعتبار) در نشست تب یزدسپار؛
  // تب جدیدِ «شرکت دیگر» نباید آن را به ارث ببرد و تب یزدسپار باید نگهش دارد
  const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
  const fakeToken = `${b64({ alg: 'HS256', typ: 'JWT' })}.${b64({ unique_name: 'yazd', exp: 4102444800 })}.sig`;
  await yazd.evaluate(t => sessionStorage.setItem('authToken', JSON.stringify(t)), fakeToken);

  const [poodr] = await Promise.all([
    context.waitForEvent('page'),
    yazd.evaluate(() => window.open('/login?newtab=1', '_blank')),
  ]);
  await poodr.waitForLoadState();
  await expect(chip(poodr)).toBeVisible();
  expect(await poodr.evaluate(() => sessionStorage.getItem('authToken'))).toBeNull();
  expect(poodr.url()).not.toContain('newtab');

  await saveDatabase(poodr, 'SRV-A', 'NEWPOODR1405');
  await poodr.reload();
  await expect(chip(poodr)).toHaveText(/NEWPOODR1405/);

  // مهم‌ترین قسمت: تب یزدسپار بعد از رفرش هنوز یزدسپار است، با وجود اینکه آخرین ذخیره پودر بود
  await yazd.reload();
  await expect(chip(yazd)).toHaveText(/YAZDSEPAR1405/);
  await expect(yazd).toHaveTitle(/^YAZDSEPAR1405 \| /);
  await expect(poodr).toHaveTitle(/^NEWPOODR1405 \| /);
  expect(await yazd.evaluate(() => sessionStorage.getItem('authToken'))).toBe(JSON.stringify(fakeToken));

  // دو شرکت دو رنگ متفاوت
  const hue = p => chip(p).evaluate(el => el.getAttribute('style'));
  expect(await hue(yazd)).not.toBe(await hue(poodr));

  // تب کاملاً جدید (نه از منو) با آخرین شرکت ذخیره‌شده شروع می‌شود
  const third = await context.newPage();
  await third.goto('/login');
  await expect(chip(third)).toHaveText(/NEWPOODR1405/);
});
