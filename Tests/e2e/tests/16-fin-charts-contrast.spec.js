// @ts-check
/**
 * نمودارهای «صورت‌های مالی» (FinCharts) باید در هر سه پوسته‌ی بهای تمام‌شده خوانا باشند.
 *
 * متنِ نمودارها رنگِ تمِ روشن (--p2-text-main ≈ #1e293b) را داشت و روی کارتِ بنفش‌آبیِ پوسته‌ی
 * پیش‌فرض (neon) عملاً دیده نمی‌شد. اینجا نسبتِ کنتراستِ WCAG بینِ رنگِ هر متن و رنگِ کارت
 * (روی رنگِ پایه‌ی همان پوسته) اندازه گرفته می‌شود و باید دست‌کم ۴٫۵ باشد.
 *
 * API با page.route شبیه‌سازی می‌شود و توکن ساختگی است — دیتابیس لازم نیست.
 */
const { test, expect } = require('@playwright/test');

const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
const TOKEN = `${b64({ alg: 'HS256', typ: 'JWT' })}.${b64({ unique_name: 'e2e', exp: 4102444800 })}.sig`;

const STATEMENTS = {
  cogm: [{ row: 50, text: 'مواد', amount: 713e9 }, { row: 60, text: 'دستمزد', amount: 68e9 }, { row: 70, text: 'سربار', amount: 12e9 }],
  cogs: [],
  income: [
    { row: 10, text: 'فروش', amount: 1145e9 }, { row: 20, text: 'بهای فروش', amount: -983e9 },
    { row: 30, text: 'سود ناخالص', amount: 162e9 }, { row: 80, text: 'هزینه‌ها', amount: -44e9 },
    { row: 90, text: 'سود عملیاتی', amount: 118e9 }
  ],
  expenses: [{ category: 'اداری', kol: 81, share: 30e9 }, { category: 'فروش', kol: 82, share: 14e9 }]
};

// متن‌هایی که روی کارت می‌نشینند (نه روی ستون)
const TEXTS = ['.cc-bar-val', '.cc-bar-lbl', '.cc-donut-top', '.cc-legend-row .lbl', '.cc-legend-row .val', '.cc-hbar-row .lbl', '.cc-hbar-row .val'];

for (const skin of ['neon', 'party', 'classic']) {
  test(`متنِ نمودارهای صورت‌های مالی در پوسته‌ی ${skin} خواناست`, async ({ page }) => {
    await page.route(/\/api\/cost-close\/runs\/1\/financial-statements/, r => r.fulfill({ json: STATEMENTS }));
    await page.route(/\/api\/pay2\/access\/me/, r => r.fulfill({ json: {} }));
    await page.goto('/login');
    await page.evaluate(([t, s]) => {
      sessionStorage.setItem('authToken', JSON.stringify(t));
      localStorage.setItem('authToken', JSON.stringify(t));
      localStorage.setItem('cc-theme-mode', s);
    }, [TOKEN, skin]);

    await page.goto('/cost-close/runs/1/statements');
    await expect(page.locator('.cc-charts .cc-bar-val').first()).toBeVisible();
    await expect(page.locator('body')).toHaveClass(new RegExp(`cc-theme-${skin}`));

    const ratios = await page.evaluate(selectors => {
      const parse = c => {
        c = c.trim();
        if (c.startsWith('#')) {
          const h = c.length === 4 ? c.slice(1).split('').map(x => x + x).join('') : c.slice(1);
          return { r: parseInt(h.slice(0, 2), 16), g: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16), a: 1 };
        }
        const p = c.match(/rgba?\(([^)]+)\)/)[1].split(/[\s,/]+/).filter(Boolean).map(Number);
        return { r: p[0], g: p[1], b: p[2], a: p[3] ?? 1 };
      };
      const over = (top, base) => ({ r: top.r * top.a + base.r * (1 - top.a), g: top.g * top.a + base.g * (1 - top.a), b: top.b * top.a + base.b * (1 - top.a), a: 1 });
      const lum = ({ r, g, b }) => {
        const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
        return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
      };
      const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };

      // پوسته‌های تیره رنگِ پایه‌ی خودشان را دارند (پس‌زمینه‌ی متحرکِ پشتِ کارتِ نیمه‌شفاف)؛ classic سفید است
      const darkSkin = !document.body.classList.contains('cc-theme-classic');
      const base = darkSkin ? parse(getComputedStyle(document.body).getPropertyValue('--ccn-bg-0') || '#241058') : { r: 255, g: 255, b: 255, a: 1 };
      const card = over(parse(getComputedStyle(document.querySelector('.cc-charts .cc-rule-card')).backgroundColor), base);

      return selectors.map(sel => {
        const el = document.querySelector(sel);
        const cs = getComputedStyle(el);
        const color = el instanceof SVGElement ? cs.fill : cs.color;
        return { sel, color, ratio: Math.round(ratio(parse(color), card) * 10) / 10 };
      });
    }, TEXTS);

    for (const r of ratios)
      expect(r.ratio, `${r.sel} با رنگِ ${r.color} روی کارت کنتراستِ ${r.ratio} دارد`).toBeGreaterThanOrEqual(4.5);
  });
}
