import { useEffect } from 'react';
import { motion, animate, useMotionValue, useTransform, useSpring, useMotionTemplate } from 'motion/react';
import { area, line, curveMonotoneX } from 'd3-shape';
import { pct } from './format.js';

const EASE = [0.16, 1, 0.3, 1];

/** عددی که از مقدارِ قبلی تا مقدارِ تازه می‌شمارد. */
export function Counter({ value, format }) {
  const mv = useMotionValue(0);
  const text = useTransform(mv, v => format(v));
  useEffect(() => {
    const c = animate(mv, value, { duration: 1.4, ease: EASE });
    return () => c.stop();
  }, [value, mv]);
  return <motion.span>{text}</motion.span>;
}

/** انتخابِ بازه؛ پس‌زمینه‌ی انتخاب‌شده با layoutId بینِ گزینه‌ها می‌لغزد. */
export function RangeTabs({ value, options, onChange, className = '' }) {
  return (
    <div className={`p-tabs ${className}`} role="tablist">
      {options.map(o => (
        <button key={o.value} type="button" role="tab" aria-selected={value === o.value}
                className={value === o.value ? 'is-on' : ''} onClick={() => onChange(o.value)}>
          {value === o.value && (
            <motion.span layoutId="p-tab-pill" className="p-tab-pill" transition={{ type: 'spring', stiffness: 420, damping: 34 }} />
          )}
          <span className="p-tab-text">{o.label}</span>
        </button>
      ))}
    </div>
  );
}

/** خطِ نبض (ECG) که پیوسته کشیده می‌شود — نشانِ «نبض سازمان». */
export function Ecg() {
  const d = 'M0 20 H22 L28 8 L34 32 L40 4 L46 26 L50 20 H72 L76 14 L80 20 H120';
  return (
    <svg className="p-ecg" viewBox="0 0 120 40" aria-hidden="true">
      <path d={d} className="p-ecg-base" />
      <motion.path d={d} className="p-ecg-line"
                   initial={{ pathLength: 0, pathOffset: 0 }}
                   animate={{ pathLength: [0, 0.45, 0], pathOffset: [0, 0.55, 1] }}
                   transition={{ duration: 2.2, repeat: Infinity, ease: 'easeInOut' }} />
    </svg>
  );
}

/** نمودارِ کوچکِ داخلِ کارت: خط و سایه، از راست (قدیم) به چپ (امروز). */
export function Spark({ values, color }) {
  const w = 120, h = 36, n = values.length;
  if (n < 2) return null;
  const max = Math.max(...values, 1);
  const pts = values.map((v, i) => ({ x: ((n - 1 - i) / (n - 1)) * w, y: h - 2 - ((v || 0) / max) * (h - 6) })).reverse();
  const l = line().x(p => p.x).y(p => p.y).curve(curveMonotoneX)(pts);
  const a = area().x(p => p.x).y0(h).y1(p => p.y).curve(curveMonotoneX)(pts);
  const id = `sp-${color.replace(/[^a-z]/gi, '')}`;
  return (
    <svg className="p-spark" viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" style={{ stopColor: `var(${color})`, stopOpacity: 0.35 }} />
          <stop offset="100%" style={{ stopColor: `var(${color})`, stopOpacity: 0 }} />
        </linearGradient>
      </defs>
      <motion.path d={a} fill={`url(#${id})`} initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 1, delay: 0.3 }} />
      <motion.path d={l} fill="none" style={{ stroke: `var(${color})` }} strokeWidth="2" strokeLinecap="round"
                   initial={{ pathLength: 0 }} animate={{ pathLength: 1 }} transition={{ duration: 1.2, ease: EASE }} />
    </svg>
  );
}

/** نشانِ درصدِ تغییر نسبت به دوره‌ی قبل. */
export function Delta({ value, invert = false }) {
  if (value === null || !isFinite(value)) return <span className="p-delta is-flat" title="دوره‌ی هم‌اندازه‌ی قبل گردشی نداشت">—</span>;
  const up = value >= 0;
  const good = invert ? !up : up;
  return (
    <span className={`p-delta ${good ? 'is-up' : 'is-down'}`} title="نسبت به دوره‌ی هم‌اندازه‌ی قبل">
      <i className={`bi ${up ? 'bi-arrow-up-left' : 'bi-arrow-down-left'}`} />
      {pct(Math.abs(value))}
    </span>
  );
}

/** کارتِ شاخص با چرخشِ سه‌بعدی و برقی که دنبالِ نشانگر می‌آید. */
export function KpiCard({ title, icon, color, value, format, delta, spark, sub, index = 0, className = '', children }) {
  const rx = useMotionValue(0), ry = useMotionValue(0);
  const srx = useSpring(rx, { stiffness: 200, damping: 18 }), sry = useSpring(ry, { stiffness: 200, damping: 18 });
  const mx = useMotionValue(50), my = useMotionValue(50);
  const shine = useMotionTemplate`radial-gradient(240px circle at ${mx}% ${my}%, color-mix(in srgb, var(${color}) 22%, transparent), transparent 70%)`;

  const onMove = e => {
    const r = e.currentTarget.getBoundingClientRect();
    const px = (e.clientX - r.left) / r.width, py = (e.clientY - r.top) / r.height;
    ry.set((px - 0.5) * 10); rx.set((0.5 - py) * 10);
    mx.set(px * 100); my.set(py * 100);
  };
  const onLeave = () => { rx.set(0); ry.set(0); };

  return (
    <motion.article className={`p-kpi ${className}`} style={{ rotateX: srx, rotateY: sry, '--c': `var(${color})` }}
                    onPointerMove={onMove} onPointerLeave={onLeave}
                    initial={{ opacity: 0, y: 24, scale: 0.96 }} animate={{ opacity: 1, y: 0, scale: 1 }}
                    transition={{ duration: 0.7, delay: 0.08 * index, ease: EASE }}
                    whileHover={{ y: -4 }}>
      <motion.div className="p-kpi-shine" style={{ background: shine }} />
      <header className="p-kpi-head">
        <span className="p-kpi-ic"><i className={`bi ${icon}`} /></span>
        <span className="p-kpi-title">{title}</span>
        {delta !== undefined && <Delta value={delta} />}
      </header>
      <div className="p-kpi-value"><Counter value={value} format={format} /></div>
      {sub && <div className="p-kpi-sub">{sub}</div>}
      {spark && <Spark values={spark} color={color} />}
      {children}
    </motion.article>
  );
}

/** حلقه‌ی درصد (نرخ تبدیلِ پیش‌فاکتور به فروش). */
export function Ring({ ratio, color, size = 64 }) {
  const r = size / 2 - 6;
  const p = Math.max(0, Math.min(1, ratio));
  return (
    <svg className="p-ring" width={size} height={size} viewBox={`0 0 ${size} ${size}`} aria-hidden="true">
      <circle cx={size / 2} cy={size / 2} r={r} className="p-ring-bg" />
      <motion.circle cx={size / 2} cy={size / 2} r={r} className="p-ring-fg" style={{ stroke: `var(${color})` }}
                     transform={`rotate(-90 ${size / 2} ${size / 2})`}
                     initial={{ pathLength: 0 }} animate={{ pathLength: p }} transition={{ duration: 1.6, ease: EASE }} />
    </svg>
  );
}

/** کارتِ بخش: با رسیدن به دیدِ کاربر بالا می‌آید. */
export function Section({ title, icon, hint, actions, children, className = '' }) {
  return (
    <motion.section className={`p-card ${className}`}
                    initial={{ opacity: 0, y: 40 }} whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true, amount: 0.15 }} transition={{ duration: 0.8, ease: EASE }}>
      <header className="p-card-head">
        <div className="p-card-title">
          {icon && <i className={`bi ${icon}`} />}
          <div>
            <h3>{title}</h3>
            {hint && <small>{hint}</small>}
          </div>
        </div>
        {actions}
      </header>
      {children}
    </motion.section>
  );
}
