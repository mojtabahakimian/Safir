import { useId, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { area, line, curveMonotoneX } from 'd3-shape';
import { useSize } from './hooks.js';
import { num, short, dayLabel, dateFull, WEEKDAYS, WEEKDAYS_SHORT, weekdayOf, niceMax, sum } from './format.js';

const EASE = [0.16, 1, 0.3, 1];

// زمان در صفحه‌ی راست‌به‌چپ از راست (قدیم) به چپ (امروز) می‌رود — همان چیدمانِ نمودارهای WPF.
function xScale(n, left, width) {
  const step = n > 1 ? width / (n - 1) : 0;
  return i => left + (n - 1 - i) * step;
}

function indexAt(e, n, left, width) {
  const r = e.currentTarget.getBoundingClientRect();
  const x = e.clientX - r.left;
  const step = n > 1 ? width / (n - 1) : 1;
  return Math.max(0, Math.min(n - 1, n - 1 - Math.round((x - left) / step)));
}

function xTicks(n, max = 7) {
  const every = Math.max(1, Math.ceil(n / max));
  const out = [];
  for (let i = n - 1; i >= 0; i -= every) out.push(i);
  return out;
}

/** جعبه‌ی راهنما که با فنر دنبالِ نشانگر می‌آید و نزدیکِ لبه‌ها برمی‌گردد. */
function Tooltip({ x, width, day, weekday, rows }) {
  // نزدیکِ لبه‌ی راست برمی‌گردد و سمتِ چپِ نشانگر می‌نشیند (لایه‌ی داخلی خودش را به اندازه‌ی عرضش عقب می‌کشد)
  const flip = x > width - 300;
  return (
    <motion.div className="p-tip" initial={{ opacity: 0, scale: 0.92 }}
                animate={{ opacity: 1, scale: 1, x: flip ? x - 14 : x + 14 }}
                exit={{ opacity: 0, scale: 0.92 }}
                transition={{ type: 'spring', stiffness: 520, damping: 40, opacity: { duration: 0.15 } }}>
      <div className={`p-tip-box ${flip ? 'is-flip' : ''}`}>
        <div className="p-tip-day"><b>{dayLabel(day)}</b><span>{weekday} · {dateFull(day)}</span></div>
        {rows.map(r => (
          <div key={r.label} className="p-tip-row">
            <span className="p-dot" style={{ background: `var(${r.color})` }} />
            <span className="p-tip-label">{r.label}</span>
            <b>{r.value}</b>
          </div>
        ))}
      </div>
    </motion.div>
  );
}

function Legend({ series, hidden, onToggle, totals }) {
  return (
    <div className="p-legend">
      {series.map(s => (
        <motion.button key={s.key} type="button" className={`p-legend-item ${hidden[s.key] ? 'is-off' : ''}`}
                       onClick={() => onToggle(s.key)} whileTap={{ scale: 0.94 }} style={{ '--c': `var(${s.color})` }}>
          <span className="p-dot" />
          <span>{s.label}</span>
          {totals && <b>{totals[s.key]}</b>}
        </motion.button>
      ))}
    </div>
  );
}

/** روندِ چند سری (فروش و پیش‌فاکتور): منحنیِ نرم، سایه‌ی شیب‌دار، خطِ نشانگر و راهنمای شناور. */
export function TrendChart({ days, firstWeekday, series, format = short, height = 330, rangeKey }) {
  const uid = useId().replace(/:/g, '');
  const [ref, { w }] = useSize();
  const [hidden, setHidden] = useState({});
  const [hover, setHover] = useState(null);
  const n = days.length;
  const pad = { t: 18, r: 78, b: 36, l: 18 };
  const iw = Math.max(0, w - pad.l - pad.r), ih = height - pad.t - pad.b;
  const visible = series.filter(s => !hidden[s.key]);
  const max = niceMax(Math.max(1, ...visible.flatMap(s => s.values)));
  const x = xScale(n, pad.l, iw);
  const y = v => pad.t + ih - ((v || 0) / max) * ih;
  const base = pad.t + ih;

  const paths = visible.map(s => {
    const pts = s.values.map((v, i) => ({ x: x(i), y: y(v) })).reverse();
    return {
      ...s,
      line: line().x(p => p.x).y(p => p.y).curve(curveMonotoneX)(pts),
      area: area().x(p => p.x).y0(base).y1(p => p.y).curve(curveMonotoneX)(pts)
    };
  });

  const toggle = k => setHidden(h => {
    const next = { ...h, [k]: !h[k] };
    return series.every(s => next[s.key]) ? h : next; // دست‌کم یک سری بماند
  });
  const ticks = [0.25, 0.5, 0.75, 1].map(f => f * max);
  const totals = Object.fromEntries(series.map(s => [s.key, format(sum(s.values))]));
  const empty = visible.every(s => s.values.every(v => !v));

  return (
    <div className="p-chart">
      <Legend series={series} hidden={hidden} onToggle={toggle} totals={totals} />
      <div ref={ref} className="p-plot" style={{ height }}>
        {w > 0 && (
          <svg width={w} height={height} role="img" aria-label="نمودار روند"
               onPointerMove={e => setHover(indexAt(e, n, pad.l, iw))} onPointerLeave={() => setHover(null)}>
            <defs>
              {series.map(s => (
                <linearGradient key={s.key} id={`g-${s.key}`} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" style={{ stopColor: `var(${s.color})`, stopOpacity: 0.38 }} />
                  <stop offset="100%" style={{ stopColor: `var(${s.color})`, stopOpacity: 0 }} />
                </linearGradient>
              ))}
              <clipPath id={`sweep-${uid}`}>
                {/* پرده‌ای که از راست (قدیم) به چپ (امروز) کنار می‌رود — هم‌جهتِ خواندن */}
                <motion.rect key={`${rangeKey}-${w}`} y="0" height={height} initial={{ x: pad.l + iw, width: 0 }}
                             animate={{ x: pad.l - 4, width: iw + 8 }} transition={{ duration: 1.7, ease: EASE }} />
              </clipPath>
              <filter id="p-glow" x="-20%" y="-20%" width="140%" height="140%">
                <feGaussianBlur stdDeviation="4" result="b" />
                <feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge>
              </filter>
            </defs>

            {ticks.map(t => (
              <g key={t}>
                <line x1={pad.l} x2={pad.l + iw} y1={y(t)} y2={y(t)} className="p-grid" />
                <text x={pad.l + iw + 10} y={y(t)} className="p-axis" dominantBaseline="middle" textAnchor="end">{format(t)}</text>
              </g>
            ))}
            <line x1={pad.l} x2={pad.l + iw} y1={base} y2={base} className="p-baseline" />
            {xTicks(n).map(i => (
              <text key={i} x={x(i)} y={height - 12} className="p-axis" textAnchor="middle">{dayLabel(days[i])}</text>
            ))}

            <g clipPath={`url(#sweep-${uid})`}>
              {paths.map((s, k) => (
                <g key={s.key}>
                  <motion.path d={s.area} fill={`url(#g-${s.key})`} initial={false} animate={{ d: s.area }}
                               transition={{ duration: 0.7, ease: EASE }} />
                  <motion.path d={s.line} fill="none" style={{ stroke: `var(${s.color})` }} strokeWidth="2.6" strokeLinecap="round"
                               filter="url(#p-glow)" initial={false} animate={{ d: s.line }} transition={{ duration: 0.7, ease: EASE, delay: k * 0.05 }} />
                </g>
              ))}
            </g>

            {hover !== null && (
              <g pointerEvents="none">
                <motion.line y1={pad.t} y2={base} className="p-cross" animate={{ x1: x(hover), x2: x(hover) }}
                             transition={{ type: 'spring', stiffness: 600, damping: 45 }} />
                {visible.map(s => (
                  <motion.circle key={s.key} r="5.5" className="p-hover-dot" style={{ fill: `var(${s.color})` }}
                                 animate={{ cx: x(hover), cy: y(s.values[hover]) }}
                                 transition={{ type: 'spring', stiffness: 600, damping: 45 }} />
                ))}
              </g>
            )}
          </svg>
        )}
        {empty && <div className="p-empty-overlay">در این بازه گردشی ثبت نشده</div>}
        <AnimatePresence>
          {hover !== null && w > 0 && (
            <Tooltip key="tip" x={x(hover)} width={w} day={days[hover]} weekday={WEEKDAYS[weekdayOf(firstWeekday, hover)]}
                     rows={visible.map(s => ({ label: s.label, color: s.color, value: format === short ? num(s.values[hover]) + ' ریال' : format(s.values[hover]) }))} />
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}

/** فعالیت‌ها: ستونِ کارها که با فنر قد می‌کشد و خطِ رویدادها روی آن. */
export function ActivityChart({ days, firstWeekday, tasks, events, rangeKey, height = 260 }) {
  const uid = useId().replace(/:/g, '');
  const [ref, { w }] = useSize();
  const [hover, setHover] = useState(null);
  const n = days.length;
  const pad = { t: 16, r: 14, b: 34, l: 14 };
  const iw = Math.max(0, w - pad.l - pad.r), ih = height - pad.t - pad.b;
  const tMax = niceMax(Math.max(1, ...tasks)), eMax = niceMax(Math.max(1, ...events));
  const x = xScale(n, pad.l, iw);
  const step = n > 1 ? iw / (n - 1) : iw;
  const bw = Math.max(2, Math.min(26, step * 0.62));
  const base = pad.t + ih;
  const ey = v => pad.t + ih - ((v || 0) / eMax) * ih * 0.92;
  const pts = events.map((v, i) => ({ x: x(i), y: ey(v) })).reverse();
  const eLine = line().x(p => p.x).y(p => p.y).curve(curveMonotoneX)(pts);

  return (
    <div className="p-chart">
      <div className="p-legend">
        <span className="p-legend-item" style={{ '--c': 'var(--p-tasks)' }}><span className="p-dot" />کارها (پرونده‌های کارتابل)<b>{num(sum(tasks))}</b></span>
        <span className="p-legend-item" style={{ '--c': 'var(--p-events)' }}><span className="p-dot" />رویدادها<b>{num(sum(events))}</b></span>
      </div>
      <div ref={ref} className="p-plot" style={{ height }}>
        {w > 0 && (
          <svg width={w} height={height} role="img" aria-label="نمودار فعالیت‌ها"
               onPointerMove={e => setHover(indexAt(e, n, pad.l, iw))} onPointerLeave={() => setHover(null)}>
            <defs>
              <clipPath id={`sweep-${uid}`}>
                <motion.rect key={`${rangeKey}-${w}`} y="0" height={height} initial={{ x: pad.l + iw, width: 0 }}
                             animate={{ x: pad.l - 4, width: iw + 8 }} transition={{ duration: 1.8, ease: EASE, delay: 0.3 }} />
              </clipPath>
              <linearGradient id="g-bar" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" style={{ stopColor: 'var(--p-tasks)' }} />
                <stop offset="100%" style={{ stopColor: 'var(--p-tasks)', stopOpacity: 0.35 }} />
              </linearGradient>
            </defs>
            <line x1={pad.l} x2={pad.l + iw} y1={base} y2={base} className="p-baseline" />
            <g key={rangeKey}>
              {tasks.map((v, i) => {
                const h = ((v || 0) / tMax) * ih;
                return (
                  <motion.rect key={i} x={x(i) - bw / 2} width={bw} rx={Math.min(6, bw / 2)} fill="url(#g-bar)"
                               className={hover === i ? 'is-hover' : ''}
                               initial={{ height: 0, y: base }} animate={{ height: h, y: base - h }}
                               transition={{ type: 'spring', stiffness: 160, damping: 20, delay: Math.min(1.2, i * (0.9 / n)) }} />
                );
              })}
              <path d={eLine} fill="none" style={{ stroke: 'var(--p-events)' }} strokeWidth="2.4" strokeLinecap="round"
                    clipPath={`url(#sweep-${uid})`} />
            </g>
            {xTicks(n).map(i => (
              <text key={i} x={x(i)} y={height - 12} className="p-axis" textAnchor="middle">{dayLabel(days[i])}</text>
            ))}
            {hover !== null && (
              <motion.circle r="5" className="p-hover-dot" style={{ fill: 'var(--p-events)' }}
                             animate={{ cx: x(hover), cy: ey(events[hover]) }} transition={{ type: 'spring', stiffness: 600, damping: 45 }} />
            )}
          </svg>
        )}
        <AnimatePresence>
          {hover !== null && w > 0 && (
            <Tooltip key="tip" x={x(hover)} width={w} day={days[hover]} weekday={WEEKDAYS[weekdayOf(firstWeekday, hover)]}
                     rows={[{ label: 'کارها', color: '--p-tasks', value: num(tasks[hover]) },
                            { label: 'رویدادها', color: '--p-events', value: num(events[hover]) }]} />
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}

/** مالی: دریافتِ نقد بالای محور، چکِ دریافتی پایینِ آن — مقایسه‌ی دو جریان در یک نگاه. */
export function FinanceChart({ days, firstWeekday, cash, cheques, rangeKey, height = 300 }) {
  const [ref, { w }] = useSize();
  const [hover, setHover] = useState(null);
  const n = days.length;
  const pad = { t: 14, r: 78, b: 34, l: 14 };
  const iw = Math.max(0, w - pad.l - pad.r), ih = height - pad.t - pad.b;
  const max = niceMax(Math.max(1, ...cash, ...cheques));
  const mid = pad.t + ih / 2;
  const half = ih / 2 - 4;
  const x = xScale(n, pad.l, iw);
  const step = n > 1 ? iw / (n - 1) : iw;
  const bw = Math.max(2, Math.min(24, step * 0.6));
  const hOf = v => ((v || 0) / max) * half;

  return (
    <div className="p-chart">
      <div className="p-legend">
        <span className="p-legend-item" style={{ '--c': 'var(--p-cash)' }}><span className="p-dot" />دریافتِ نقد<b>{short(sum(cash))}</b></span>
        <span className="p-legend-item" style={{ '--c': 'var(--p-cheq)' }}><span className="p-dot" />چکِ دریافتی<b>{short(sum(cheques))}</b></span>
      </div>
      <div ref={ref} className="p-plot" style={{ height }}>
        {w > 0 && (
          <svg width={w} height={height} role="img" aria-label="نمودار دریافت‌ها"
               onPointerMove={e => setHover(indexAt(e, n, pad.l, iw))} onPointerLeave={() => setHover(null)}>
            {[-1, -0.5, 0.5, 1].map(f => (
              <g key={f}>
                <line x1={pad.l} x2={pad.l + iw} y1={mid - f * half} y2={mid - f * half} className="p-grid" />
                <text x={pad.l + iw + 10} y={mid - f * half} className="p-axis" dominantBaseline="middle" textAnchor="end">{short(Math.abs(f) * max)}</text>
              </g>
            ))}
            <g key={rangeKey}>
              {days.map((_, i) => {
                const hc = hOf(cash[i]), hq = hOf(cheques[i]);
                const delay = Math.min(1.1, i * (0.8 / n));
                return (
                  <g key={i} className={hover === i ? 'is-hover' : ''}>
                    <motion.rect x={x(i) - bw / 2} width={bw} rx={Math.min(5, bw / 2)} style={{ fill: 'var(--p-cash)' }}
                                 initial={{ height: 0, y: mid }} animate={{ height: hc, y: mid - hc }}
                                 transition={{ type: 'spring', stiffness: 170, damping: 22, delay }} />
                    <motion.rect x={x(i) - bw / 2} width={bw} rx={Math.min(5, bw / 2)} style={{ fill: 'var(--p-cheq)' }} y={mid}
                                 initial={{ height: 0 }} animate={{ height: hq }}
                                 transition={{ type: 'spring', stiffness: 170, damping: 22, delay: delay + 0.05 }} />
                  </g>
                );
              })}
            </g>
            <line x1={pad.l} x2={pad.l + iw} y1={mid} y2={mid} className="p-baseline" />
            {xTicks(n).map(i => (
              <text key={i} x={x(i)} y={height - 12} className="p-axis" textAnchor="middle">{dayLabel(days[i])}</text>
            ))}
          </svg>
        )}
        <AnimatePresence>
          {hover !== null && w > 0 && (
            <Tooltip key="tip" x={x(hover)} width={w} day={days[hover]} weekday={WEEKDAYS[weekdayOf(firstWeekday, hover)]}
                     rows={[{ label: 'دریافتِ نقد', color: '--p-cash', value: num(cash[hover]) + ' ریال' },
                            { label: 'چکِ دریافتی', color: '--p-cheq', value: num(cheques[hover]) + ' ریال' }]} />
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}

/** نقشه‌ی حرارتیِ ۹۰ روزِ اخیر: هر ستون یک هفته (از راست به چپ)، هر سطر یک روزِ هفته. */
export function Heatmap({ days, firstWeekday, values, color = '--p-events' }) {
  const n = days.length;
  const cell = 15, gap = 4;
  const firstRow = weekdayOf(firstWeekday, 0);
  const cols = Math.ceil((n + firstRow) / 7);
  const max = Math.max(1, ...values);
  const [hover, setHover] = useState(null);
  const width = cols * (cell + gap) + 30, height = 7 * (cell + gap);

  return (
    <div className="p-heat">
      <svg viewBox={`0 0 ${width} ${height}`} width="100%" role="img" aria-label="نقشه‌ی حرارتیِ فعالیت">
        {WEEKDAYS_SHORT.map((d, r) => (
          <text key={d} x={width - 4} y={r * (cell + gap) + cell / 2 + 1} className="p-axis" textAnchor="start" dominantBaseline="middle">{d}</text>
        ))}
        {values.map((v, i) => {
          const k = i + firstRow;
          const col = Math.floor(k / 7), row = k % 7;
          const cx = width - 30 - (col + 1) * (cell + gap) + gap;
          const t = (v || 0) / max;
          return (
            <motion.rect key={i} x={cx} y={row * (cell + gap)} width={cell} height={cell} rx="4"
                         className="p-heat-cell" style={{ fill: v ? `color-mix(in srgb, var(${color}) ${Math.round(18 + t * 82)}%, transparent)` : undefined }}
                         initial={{ opacity: 0, scale: 0.2 }} whileInView={{ opacity: 1, scale: 1 }} viewport={{ once: true }}
                         transition={{ delay: col * 0.035 + row * 0.02, type: 'spring', stiffness: 260, damping: 18 }}
                         onPointerEnter={() => setHover(i)} onPointerLeave={() => setHover(h => (h === i ? null : h))}
                         whileHover={{ scale: 1.35 }} />
          );
        })}
      </svg>
      <div className="p-heat-foot">
        <AnimatePresence mode="wait">
          {hover !== null ? (
            <motion.span key={hover} initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -6 }}>
              <b>{WEEKDAYS[weekdayOf(firstWeekday, hover)]} {dayLabel(days[hover])}</b> — {num(values[hover])} مورد
            </motion.span>
          ) : (
            <motion.span key="hint" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>روی هر روز بروید</motion.span>
          )}
        </AnimatePresence>
        <span className="p-heat-scale">کم {[0.2, 0.45, 0.7, 1].map(t => (
          <i key={t} style={{ background: `color-mix(in srgb, var(${color}) ${Math.round(18 + t * 82)}%, transparent)` }} />
        ))} زیاد</span>
      </div>
    </div>
  );
}

/** فروش به تفکیکِ روزِ هفته: کدام روز بیشتر می‌فروشیم؟ */
export function WeekdayBars({ firstWeekday, values, color = '--p-sales' }) {
  const totals = Array(7).fill(0);
  values.forEach((v, i) => { totals[weekdayOf(firstWeekday, i)] += v || 0; });
  const max = Math.max(1, ...totals);
  const best = totals.indexOf(Math.max(...totals));
  return (
    <div className="p-wd">
      {totals.map((t, d) => (
        <div key={d} className={`p-wd-row ${d === best && t > 0 ? 'is-best' : ''}`}>
          <span className="p-wd-name">{WEEKDAYS[d]}</span>
          <div className="p-wd-track">
            <motion.div className="p-wd-bar" style={{ background: `var(${color})` }}
                        initial={{ width: 0 }} animate={{ width: `${(t / max) * 100}%` }}
                        transition={{ duration: 1.1, ease: EASE, delay: d * 0.06 }} />
          </div>
          <span className="p-wd-val">{short(t)}</span>
          {d === best && t > 0 && (
            <motion.i className="bi bi-trophy-fill p-wd-crown" initial={{ scale: 0, rotate: -40 }} animate={{ scale: 1, rotate: 0 }}
                      transition={{ type: 'spring', stiffness: 300, damping: 12, delay: 1 }} />
          )}
        </div>
      ))}
    </div>
  );
}

/** پرفروش‌ترین روزهای بازه؛ با عوض شدنِ بازه، ردیف‌ها با layout جابه‌جا می‌شوند. */
export function TopDays({ days, firstWeekday, values, counts, color = '--p-sales', top = 5 }) {
  const rows = values.map((v, i) => ({ i, v: v || 0, c: counts?.[i] || 0 }))
    .filter(r => r.v > 0).sort((a, b) => b.v - a.v).slice(0, top);
  const max = rows[0]?.v || 1;
  if (rows.length === 0) return <div className="p-empty">در این بازه فاکتوری نیست</div>;
  return (
    <ol className="p-top">
      <AnimatePresence initial={true}>
        {rows.map((r, k) => (
          <motion.li key={days[r.i]} layout initial={{ opacity: 0, x: -30 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: 30 }}
                     transition={{ type: 'spring', stiffness: 260, damping: 26, delay: k * 0.07 }}>
            <span className={`p-rank r${k + 1}`}>{num(k + 1)}</span>
            <div className="p-top-main">
              <div className="p-top-line">
                <b>{dayLabel(days[r.i])}</b>
                <small>{WEEKDAYS[weekdayOf(firstWeekday, r.i)]}{r.c ? ` · ${num(r.c)} فاکتور` : ''}</small>
                <span className="p-top-val">{short(r.v)}</span>
              </div>
              <div className="p-top-track">
                <motion.div className="p-top-bar" style={{ background: `var(${color})` }}
                            initial={{ width: 0 }} animate={{ width: `${(r.v / max) * 100}%` }}
                            transition={{ duration: 1, ease: EASE, delay: 0.2 + k * 0.08 }} />
              </div>
            </div>
          </motion.li>
        ))}
      </AnimatePresence>
    </ol>
  );
}
