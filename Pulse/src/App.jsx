import { useMemo, useState } from 'react';
import { motion, MotionConfig } from 'motion/react';
import { KpiCard, RangeTabs, Ecg, Ring, Section } from './ui.jsx';
import { TrendChart, ActivityChart, FinanceChart, Heatmap, WeekdayBars, TopDays } from './charts.jsx';
import { num, short, pct, sum, delta, dayLabel, dateFull, MONTHS } from './format.js';

const RANGES = [
  { value: 7, label: '۷ روز' },
  { value: 30, label: '۳۰ روز' },
  { value: 90, label: '۹۰ روز' }
];
const faPlain = new Intl.NumberFormat('fa-IR', { useGrouping: false });

/** بازه‌ی انتخاب‌شده → [شروع، پایان) در آرایه‌ی روزها و بازه‌ی قبلی برای مقایسه.
 *  عدد = چند روزِ آخر؛ «m<ماه>» = یک ماهِ سالِ مالی، که با ماهِ قبلش مقایسه می‌شود. */
export function windowOf(days, fiscalYear, range) {
  const total = days.length;
  if (typeof range === 'number') {
    const a = Math.max(0, total - range);
    return { a, b: total, pa: Math.max(0, a - range), pb: a };
  }
  const m = Number(range.slice(1));
  const ym = fiscalYear * 100 + m;
  const prev = m > 1 ? ym - 1 : (fiscalYear - 1) * 100 + 12;
  const span = key => {
    const a = days.findIndex(d => Math.floor(d / 100) === key);
    if (a < 0) return [0, 0];
    let b = a;
    while (b < total && Math.floor(days[b] / 100) === key) b++;
    return [a, b];
  };
  const [a, b] = span(ym);
  const [pa, pb] = span(prev);
  return { a, b, pa, pb };
}

/** ماه‌های سالِ مالی که در داده هستند (فروردین تا ماهِ جاری). */
export function monthOptions(days, fiscalYear) {
  const seen = new Set(days.filter(d => Math.floor(d / 10000) === fiscalYear).map(d => Math.floor(d / 100) % 100));
  return [...seen].sort((x, y) => x - y).map(m => ({ value: `m${m}`, label: MONTHS[m - 1] }));
}

/** حبابِ رنگیِ پس‌زمینه‌ی سربرگ که آرام شناور است. */
function Blob({ className, dur, path }) {
  return (
    <motion.span className={`p-blob ${className}`} animate={path}
                 transition={{ duration: dur, repeat: Infinity, ease: 'easeInOut' }} />
  );
}

export function App({ data, dotnet }) {
  const [range, setRange] = useState(30);
  const [busy, setBusy] = useState(false);
  const stamp = useMemo(() => Math.random().toString(36).slice(2), [data]);

  const total = data.days.length;
  const months = useMemo(() => monthOptions(data.days, data.fiscalYear), [data]);
  const { a: start, b: stop, pa, pb } = windowOf(data.days, data.fiscalYear, range);
  const cut = (arr, back = 0) => (back ? arr.slice(pa, pb) : arr.slice(start, stop));
  const fw = (data.firstWeekday + start) % 7;
  const days = cut(data.days);
  const len = Math.max(1, days.length);
  const rangeLabel = typeof range === 'number' ? `${RANGES.find(r => r.value === range).label}ِ اخیر` : `${MONTHS[Number(range.slice(1)) - 1]} ${faPlain.format(data.fiscalYear)}`;

  const s = {
    sales: cut(data.sales), salesPrev: cut(data.sales, 1), salesCount: cut(data.salesCount),
    pre: cut(data.preInvoices), prePrev: cut(data.preInvoices, 1), preCount: cut(data.preInvoiceCount),
    cash: cut(data.cash), cashPrev: cut(data.cash, 1),
    cheq: cut(data.cheques), cheqPrev: cut(data.cheques, 1),
    tasks: cut(data.tasks), tasksPrev: cut(data.tasks, 1),
    events: cut(data.events)
  };
  const T = Object.fromEntries(Object.entries(s).map(([k, v]) => [k, sum(v)]));
  const ratio = T.pre > 0 ? T.sales / T.pre : 0;

  // نقشه‌ی حرارتی همیشه ۹۰ روزِ آخر را نشان می‌دهد
  const h0 = Math.max(0, total - 90);
  const heatDays = data.days.slice(h0);
  const heat = data.tasks.slice(h0).map((t, i) => (t || 0) + (data.events[h0 + i] || 0));

  const key = `${range}-${stamp}`;
  const staleSales = data.lastSalesDay && data.lastSalesDay < data.end && data.days.indexOf(data.lastSalesDay) < total - 3;

  const refresh = async () => {
    if (!dotnet || busy) return;
    setBusy(true);
    try { await dotnet.invokeMethodAsync('Refresh'); } finally { setBusy(false); }
  };

  return (
    <MotionConfig reducedMotion="user">
      <div className="pulse" dir="rtl">
        <header className="p-hero">
          <div className="p-aurora" aria-hidden="true">
            <Blob className="b1" dur={16} path={{ x: [0, 80, -30, 0], y: [0, -40, 30, 0], scale: [1, 1.25, 0.9, 1] }} />
            <Blob className="b2" dur={20} path={{ x: [0, -70, 40, 0], y: [0, 30, -30, 0], scale: [1, 0.85, 1.2, 1] }} />
            <Blob className="b3" dur={24} path={{ x: [0, 40, -60, 0], y: [0, 50, -10, 0], scale: [1, 1.15, 1, 1] }} />
          </div>
          <div className="p-hero-main">
            <div className="p-hero-title">
              <motion.span className="p-hero-ic" animate={{ scale: [1, 1.14, 1, 1.07, 1] }}
                           transition={{ duration: 1.4, repeat: Infinity, repeatDelay: 0.6 }}>
                <i className="bi bi-heart-pulse-fill" />
              </motion.span>
              <div>
                <motion.h1 initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.6 }}>نبض سازمان</motion.h1>
                <p>
                  {dateFull(days[0])} تا {dateFull(days[days.length - 1])} · سال مالی {faPlain.format(data.fiscalYear)}
                </p>
              </div>
            </div>
            <Ecg />
            <div className="p-hero-actions">
              <RangeTabs value={range} options={RANGES} onChange={setRange} />
              {months.length > 0 && (
                <RangeTabs value={range} options={months} onChange={setRange} className="p-tabs--months" />
              )}
              <motion.button type="button" className="p-refresh" onClick={refresh} disabled={busy}
                             whileTap={{ scale: 0.9 }} title="به‌روزرسانی از دیتابیس">
                <motion.i className="bi bi-arrow-clockwise" animate={busy ? { rotate: 360 } : { rotate: 0 }}
                          transition={busy ? { duration: 0.8, repeat: Infinity, ease: 'linear' } : { duration: 0.3 }} />
              </motion.button>
            </div>
          </div>
          {staleSales && (
            <motion.div className="p-note" initial={{ opacity: 0, y: -6 }} animate={{ opacity: 1, y: 0 }}>
              <i className="bi bi-info-circle" /> آخرین فاکتور یا پیش‌فاکتورِ این دیتابیس: {dayLabel(data.lastSalesDay)} — بعد از آن فروش ثبت نشده است.
            </motion.div>
          )}
        </header>

        <div className="p-kpis">
          <KpiCard index={0} title="فروش" icon="bi-bag-check-fill" color="--p-sales" value={T.sales} format={short}
                   delta={delta(T.sales, T.salesPrev)} spark={s.sales} sub={`${num(T.salesCount)} فاکتور · میانگینِ روزانه ${short(T.sales / len)}`} />
          <KpiCard index={1} title="پیش‌فاکتور" icon="bi-file-earmark-text-fill" color="--p-pre" value={T.pre} format={short}
                   delta={delta(T.pre, T.prePrev)} spark={s.pre} sub={`${num(T.preCount)} پیش‌فاکتور`} />
          <KpiCard index={2} title="نرخِ تبدیل" icon="bi-bullseye" color="--p-ratio" value={ratio * 100} format={pct}
                   className="p-kpi--ring" sub="فروش ÷ پیش‌فاکتورِ همین بازه">
            <Ring ratio={ratio} color="--p-ratio" />
          </KpiCard>
          <KpiCard index={3} title="دریافتِ نقد" icon="bi-cash-coin" color="--p-cash" value={T.cash} format={short}
                   delta={delta(T.cash, T.cashPrev)} spark={s.cash} sub={data.cashAccount ? `حسابِ صندوق ${data.cashAccount}` : 'حسابِ صندوق تعریف نشده'} />
          <KpiCard index={4} title="چکِ دریافتی" icon="bi-credit-card-2-front-fill" color="--p-cheq" value={T.cheq} format={short}
                   delta={delta(T.cheq, T.cheqPrev)} spark={s.cheq} sub="به تاریخِ دریافتِ چک" />
          <KpiCard index={5} title="فعالیت‌ها" icon="bi-lightning-charge-fill" color="--p-tasks" value={T.tasks} format={num}
                   delta={delta(T.tasks, T.tasksPrev)} spark={s.tasks} sub={`${num(T.events)} رویداد در کارتابل`} />
        </div>

        <Section title="روندِ فروش و پیش‌فاکتور" icon="bi-graph-up-arrow"
                 hint="جمعِ مبلغِ ردیف‌ها منهای تخفیف در هر روز — همان «نبض فروش» ِ نرم‌افزار ویندوزی. روی نمودار حرکت کنید؛ با کلیک روی راهنما سری را خاموش کنید.">
          <TrendChart rangeKey={key} days={days} firstWeekday={fw}
                      series={[
                        { key: 'sales', label: 'فاکتورِ فروش', color: '--p-sales', values: s.sales },
                        { key: 'pre', label: 'پیش‌فاکتور', color: '--p-pre', values: s.pre }
                      ]} />
        </Section>

        <div className="p-grid2">
          <Section title="پرفروش‌ترین روزها" icon="bi-award-fill" hint={rangeLabel}>
            <TopDays days={days} firstWeekday={fw} values={s.sales} counts={s.salesCount} />
          </Section>
          <Section title="فروش در روزهای هفته" icon="bi-calendar-week-fill" hint="کدام روزِ هفته بیشتر می‌فروشیم؟">
            <WeekdayBars firstWeekday={fw} values={s.sales} />
          </Section>
        </div>

        <div className="p-grid2 p-grid2--wide">
          <Section title="فعالیت‌های سازمان" icon="bi-activity" hint="پرونده‌های کارتابل (TASKS) و رویدادهایشان (EVENTS) در هر روز">
            <ActivityChart rangeKey={key} days={days} firstWeekday={fw} tasks={s.tasks} events={s.events} />
          </Section>
          <Section title="نقشه‌ی حرارتیِ فعالیت" icon="bi-grid-3x3-gap-fill" hint="۹۰ روزِ اخیر — کار و رویداد با هم">
            <Heatmap days={heatDays} firstWeekday={(data.firstWeekday + h0) % 7} values={heat} />
          </Section>
        </div>

        <Section title="نبضِ مالی" icon="bi-wallet2"
                 hint={`بالای محور: بدهکارِ حسابِ صندوق${data.cashAccount ? ` (${data.cashAccount})` : ''} در اسناد · پایینِ محور: چک‌های دریافتی`}>
          <FinanceChart rangeKey={key} days={days} firstWeekday={fw} cash={s.cash} cheques={s.cheq} />
        </Section>
      </div>
    </MotionConfig>
  );
}
