// node --test Tests/ai-eval/grade.test.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { numbers, mentions, grade } from './grade.mjs';

test('ارقام فارسی، جداکننده و اعشار', () => {
  assert.deepEqual(numbers('۴۱۷٬۶۸۰ میلیون و ۱۶٫۹۷٪'), [417680, 16.97]);
  assert.deepEqual(numbers('5,646,748,692 ریال'), [5646748692]);
});

test('ریال دقیق یا گرد‌شده به میلیون پذیرفته می‌شود', () => {
  assert.ok(mentions([417680], 417679727324));
  assert.ok(mentions([417679727324], 417679727324));
  assert.ok(!mentions([321747], 5646748692));
});

test('سود مرداد به‌جای «این ماه» غلطِ با اطمینان است', () => {
  const q = { abstain: true, forbid: [162428000000] };
  const g = grade(q, 'سود این ماه ۱۶۲٬۴۲۸ میلیون ریال است.', []);
  assert.equal(g.ok, false);
  assert.equal(g.confidentWrong, true);
});

test('«ماه بسته نشده» با تاریخ، درست است', () => {
  const g = grade({ abstain: true }, 'بستن مهر ۱۴۰۵ (۱۴۰۵/۰۷/۰۱ تا ۱۴۰۵/۰۷/۰۳) انجام نشده؛ عدد سود موجود نیست.', ['profit_and_loss']);
  assert.equal(g.ok, true);
});

test('«نمی‌دانم» همراه با مبلغ ساختگی قبول نیست', () => {
  const g = grade({ abstain: true }, 'ماه بسته نشده ولی سود حدود ۱۲۰٬۰۰۰ میلیون است.', []);
  assert.equal(g.ok, false);
  assert.equal(g.confidentWrong, true);
});

test('مانده‌ی بانک با فیلتر OKF غلط است', () => {
  const q = { expectAll: [5646748692], forbid: [321747000000] };
  assert.equal(grade(q, 'مانده‌ی بانک‌ها ۳۲۱٬۷۴۷ میلیون ریال', []).confidentWrong, true);
  assert.equal(grade(q, 'مانده‌ی بانک‌ها ۵٬۶۴۷ میلیون ریال', []).ok, true);
});

test('«فروش صفر» برای کالای اشتباه، غلطِ با اطمینان است', () => {
  const g = grade({ expectAll: [145000000] }, 'فروش خالص پودر شیر خشک در شهریور: ۰ ریال. هیچ فاکتوری ثبت نشد.', []);
  assert.equal(g.ok, false);
  assert.equal(g.confidentWrong, true);
});

test('«ناموجود» برای سال مالی دیگر درست است', () => {
  const g = grade({ abstain: true }, 'سود اسفند ۱۴۰۴ ناموجود. پایگاه فقط داده‌ی سال مالی ۱۴۰۵ دارد.', []);
  assert.equal(g.ok, true);
});

// بازبینی: علامت و واحد نادیده گرفته می‌شد
test('منفیِ عددِ درست، درست نیست', () => {
  assert.equal(grade({ expectAll: [5646748692] }, 'مانده‌ی بانک‌ها منفی ۵٬۶۴۶٬۷۴۸٬۶۹۲ ریال', []).ok, false);
  assert.equal(grade({ expectAll: [5646748692] }, 'مانده‌ی بانک‌ها -5,646,748,692 ریال', []).ok, false);
  assert.equal(grade({ expectAll: [-27334486] }, 'منفی ۲۷٬۳۳۴٬۴۸۶ ریال (بستانکار)', []).ok, true);
});

test('گرد‌شده به میلیون بدون کلمه‌ی «میلیون» قبول نیست', () => {
  assert.equal(grade({ expectAll: [5646748692] }, 'مانده‌ی بانک‌ها ۵٬۶۴۷ ریال', []).ok, false);
});

test('بازه‌ی تاریخ با خط تیره عدد منفی نمی‌سازد', () => {
  assert.deepEqual(numbers('14050601-14050631'), [14050601, 14050631]);
});

test('«،» عربی هم جداکننده‌ی هزارگان است', () => {
  assert.deepEqual(numbers('۲۵٬۱۰۹،۶۲۰،۰۰۰ ریال'), [25109620000]);
});

test('«داده … ندارد» برای سؤال بیرون از حوزه درست است', () => {
  const g = grade({ abstain: true }, 'داده هواشناسی ندارد. سیستم فقط به اطلاعات مالی و انبار سفیر دسترسی دارد.', []);
  assert.equal(g.ok, true);
});

test('«اقدام لازم: ندارد» کنار عدد غلط، غلطِ با اطمینان می‌ماند', () => {
  const g = grade({ expectAll: [145000000] }, 'فروش شهریور: ۲٬۶۹۸٬۸۰۰٬۰۰۰ ریال. اقدام لازم: ندارد.', []);
  assert.equal(g.confidentWrong, true);
});
