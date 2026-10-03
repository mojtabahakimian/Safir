// قالب‌بندیِ فارسی: رقمِ فارسی، مبلغِ کوتاه‌شده و تاریخِ شمسی از عددِ yyyymmdd.

const fa = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 0 });
const fa1 = new Intl.NumberFormat('fa-IR', { maximumFractionDigits: 1 });
const faPlain = new Intl.NumberFormat('fa-IR', { useGrouping: false });

export const num = v => fa.format(Math.round(v || 0));
export const pct = v => fa1.format(v) + '٪';

/** مبلغ به ریال، کوتاه: ۱۲٫۴ میلیارد، ۸۵۰ میلیون … */
export function short(v) {
  const a = Math.abs(v || 0);
  const f = x => (Math.abs(x) >= 100 ? fa : fa1).format(x);
  if (a >= 1e9) return f(v / 1e9) + ' میلیارد';
  if (a >= 1e6) return f(v / 1e6) + ' میلیون';
  if (a >= 1e3) return f(v / 1e3) + ' هزار';
  return num(v);
}

export const MONTHS = ['فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور', 'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'];
/** هفته‌ی ایرانی از شنبه. */
export const WEEKDAYS = ['شنبه', 'یکشنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنجشنبه', 'جمعه'];
export const WEEKDAYS_SHORT = ['ش', 'ی', 'د', 'س', 'چ', 'پ', 'ج'];

const month = d => Math.floor(d / 100) % 100;
const day = d => d % 100;

export const dayLabel = d => `${faPlain.format(day(d))} ${MONTHS[month(d) - 1] ?? ''}`;
export const dateFull = d =>
  `${faPlain.format(Math.floor(d / 10000))}/${faPlain.format(month(d)).padStart(2, '۰')}/${faPlain.format(day(d)).padStart(2, '۰')}`;

/** شاخصِ روزِ هفته (شنبه = ۰) برای روزِ iام، با دانستنِ روزِ هفته‌ی اولین روز. */
export const weekdayOf = (firstWeekday, i) => (firstWeekday + i) % 7;

export const sum = arr => arr.reduce((a, b) => a + (b || 0), 0);

/** درصدِ تغییر نسبت به دوره‌ی قبل؛ وقتی دوره‌ی قبل صفر است عددی معنا ندارد. */
export const delta = (cur, prev) => (prev > 0 ? ((cur - prev) / prev) * 100 : null);

/** سقفِ «گرد» برای محورِ عمودی: ۱، ۲، ۲٫۵ یا ۵ ضربدر توانِ ده. */
export function niceMax(v) {
  if (!(v > 0)) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  for (const m of [1, 2, 2.5, 5, 10]) if (v <= m * p) return m * p;
  return 10 * p;
}
