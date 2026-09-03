/* ══════════════════════════════════════════════════════════════════════
   بومِ پس‌زمینه‌ی ماژول بهای تمام‌شده

   سه لایه روی یک بوم:
     ۱) شفق (aurora)  — چند لکه‌ی نورِ کند که رنگ فضا را می‌سازند
     ۲) شبکه‌ی ذرات   — نقطه‌هایی که وقتی نزدیک می‌شوند خط می‌کشند
     ۳) کشش نشانگر    — ذرات به مکان‌نما تمایل نشان می‌دهند

   ── چرا بدون تغییر در Razor ──
   هر هفت صفحه‌ی ماژول ریشه‌شان .cc-page است. یک MutationObserver همین را
   می‌پاید و کلاس .cc-fx را روی <body> می‌گذارد/برمی‌دارد. نتیجه: نه فایل
   Razor ای عوض شد، نه امکان دارد افکت به صفحات دیگر نشت کند.

   ── هزینه ──
   Blazor WASM است و این بوم روی همان نخِ UI می‌دود، پس هر تصمیمی اینجا
   محافظه‌کارانه گرفته شده:
     • با نبودن .cc-page اصلاً حلقه‌ای در کار نیست (بوم حذف می‌شود)
     • با پنهان شدن تب، حلقه می‌ایستد
     • prefers-reduced-motion → یک فریمِ ثابت، بدون حلقه
     • DPR سقف ۱٫۵ (نه ۳) — بوم مه‌آلود است و رزولوشن بیشتر دیده نمی‌شود
     • تعداد ذره از مساحت می‌آید، سقف ۹۰؛ روی موبایل نصف
     • جفت‌های ذره با گریدِ همسایگی حساب می‌شوند نه O(n²) کامل
   ══════════════════════════════════════════════════════════════════════ */

(() => {
    'use strict';

    const CANVAS_ID = 'cc-fx-canvas';
    const BODY_CLASS = 'cc-fx';
    const LINK_DIST = 132;          // پیکسل — آستانه‌ی کشیدن خط بین دو ذره
    const CELL = LINK_DIST;         // اندازه‌ی خانه‌ی گرید = آستانه، تا فقط ۹ خانه چک شود

    /* احترام به تنظیم سیستم، با یک درِ خروج.
       اگر ویندوز «جلوه‌های انیمیشن» را خاموش داشته باشد، کروم
       prefers-reduced-motion: reduce گزارش می‌کند و همه‌ی حرکت‌ها
       خاموش می‌شوند — که پیش‌فرضِ درستی است. ولی کاربری که خودش این
       افکت‌ها را خواسته باید بتواند روشنشان کند:

           localStorage.setItem('cc-motion','on')    → همیشه روشن
           localStorage.setItem('cc-motion','off')   → همیشه خاموش
           localStorage.removeItem('cc-motion')      → پیروی از سیستم

       window.ccMotion() هم همین را از کنسول عوض می‌کند. */
    function motionPref() {
        let v = null;
        try { v = localStorage.getItem('cc-motion'); } catch { }
        if (v === 'on')  return false;   // reduceMotion = false یعنی حرکت آزاد
        if (v === 'off') return true;
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    let reduceMotion = motionPref();

    let canvas = null, ctx = null, raf = 0;
    let w = 0, h = 0, dpr = 1;
    let particles = [];
    let blobs = [];
    let pointer = { x: -9999, y: -9999, active: false };
    let running = false;

    // موج‌های ضربه‌ی کلیک و «حالت جشن» — لایه‌ی بازیگوشی که
    // cost-close-fun.js از بیرون روشنشان می‌کند.
    let waves = [];
    let partyUntil = 0;

    // ───────────────────────── راه‌اندازی ─────────────────────────

    function isModuleOpen() {
        return !!document.querySelector('.cc-page');
    }

    function sizeCanvas() {
        dpr = Math.min(window.devicePixelRatio || 1, 1.5);
        w = window.innerWidth;
        h = window.innerHeight;
        canvas.width = Math.floor(w * dpr);
        canvas.height = Math.floor(h * dpr);
        canvas.style.width = w + 'px';
        canvas.style.height = h + 'px';
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function seed() {
        const mobile = w < 768;
        // چگالی از مساحت می‌آید تا روی مانیتور بزرگ خلوت و روی لپ‌تاپ شلوغ نشود
        const target = Math.round((w * h) / 26000);
        const count = Math.min(mobile ? 34 : 90, Math.max(18, target));

        particles = Array.from({ length: count }, () => ({
            x: Math.random() * w,
            y: Math.random() * h,
            vx: (Math.random() - 0.5) * 0.32,
            vy: (Math.random() - 0.5) * 0.32,
            r: Math.random() * 1.6 + 0.7
        }));

        /* لکه‌های شفق — کم‌تعداد و بزرگ، چون هرکدام یک گرادیانِ شعاعی است.
           پالت عمداً گرم و سرد را با هم دارد: فقط آبی/بنفش، فضا را سرد و
           «شبانه» نشان می‌داد. صورتی، کهربایی و سبزِ نعنایی همان چیزی است
           که رنگ را شاد می‌کند. */
        blobs = [
            { x: 0.20, y: 0.16, r: 0.52, c: [34, 211, 238],  t: Math.random() * 6.283 }, // فیروزه‌ای
            { x: 0.78, y: 0.24, r: 0.48, c: [192, 132, 252], t: Math.random() * 6.283 }, // بنفش روشن
            { x: 0.52, y: 0.80, r: 0.58, c: [244, 114, 182], t: Math.random() * 6.283 }, // صورتی
            { x: 0.10, y: 0.70, r: 0.44, c: [251, 146, 60],  t: Math.random() * 6.283 }, // کهربایی
            { x: 0.88, y: 0.68, r: 0.42, c: [52, 211, 153],  t: Math.random() * 6.283 }, // نعنایی
            { x: 0.42, y: 0.40, r: 0.36, c: [250, 204, 21],  t: Math.random() * 6.283 }  // زرد
        ];
    }

    // ───────────────────────── ترسیم ─────────────────────────

    function drawAurora(time) {
        /* پایه دیگر یک مشکیِ تخت نیست: یک گرادیانِ اریب از بنفشِ سیر به
           سرمه‌ای و بعد فیروزه‌ای عمیق. همان تیرگیِ لازم برای خواناییِ
           جدول‌ها را دارد ولی «رنگ» دارد، نه «سیاهی» — کاربر گفت
           «رنگ غالب شاد باشه الان مشکیه». */
        const base = ctx.createLinearGradient(0, 0, w, h);
        base.addColorStop(0,    '#2a0f5e');   // بنفش سیر
        base.addColorStop(0.42, '#1b2270');   // نیلی
        base.addColorStop(0.72, '#0f3f6b');   // آبی عمیق
        base.addColorStop(1,    '#10405c');   // فیروزه‌ای تیره
        ctx.fillStyle = base;
        ctx.fillRect(0, 0, w, h);

        ctx.globalCompositeOperation = 'lighter';

        for (const b of blobs) {
            // مسیر لیساژو — حرکتی که تکرارش به چشم نمی‌آید
            const phase = reduceMotion ? 0 : time * 0.00006 + b.t;
            const cx = (b.x + Math.sin(phase * 1.3) * 0.06) * w;
            const cy = (b.y + Math.cos(phase * 1.7) * 0.06) * h;
            const rad = b.r * Math.min(w, h);

            // شفافیت بالاتر از قبل (۰.۱۶ → ۰.۲۶): لکه‌ها باید دیده شوند،
            // نه اینکه فقط تیرگی را کمی بشکنند.
            const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, rad);
            const [r, gg, bb] = b.c;
            g.addColorStop(0,   `rgba(${r},${gg},${bb},0.26)`);
            g.addColorStop(0.5, `rgba(${r},${gg},${bb},0.09)`);
            g.addColorStop(1,   `rgba(${r},${gg},${bb},0)`);

            ctx.fillStyle = g;
            ctx.beginPath();
            ctx.arc(cx, cy, rad, 0, 6.283);
            ctx.fill();
        }

        ctx.globalCompositeOperation = 'source-over';
    }

    function step() {
        for (const p of particles) {
            p.x += p.vx;
            p.y += p.vy;

            // پیچیدن به‌جای بازتاب: مرزها دیده نمی‌شوند
            if (p.x < -20) p.x = w + 20; else if (p.x > w + 20) p.x = -20;
            if (p.y < -20) p.y = h + 20; else if (p.y > h + 20) p.y = -20;

            if (pointer.active) {
                const dx = pointer.x - p.x, dy = pointer.y - p.y;
                const d2 = dx * dx + dy * dy;
                // فقط در شعاع ۱۸۰ پیکسل، و با ضریب بسیار کوچک تا ذرات
                // به مکان‌نما «نچسبند»
                if (d2 < 32400 && d2 > 1) {
                    const f = 0.00035;
                    p.vx += dx * f;
                    p.vy += dy * f;
                }
            }

            // اصطکاک — وگرنه کششِ بالا سرعت را بی‌نهایت می‌کند
            p.vx *= 0.992;
            p.vy *= 0.992;

            // کفِ سرعت تا ذرات نایستند
            const sp = Math.hypot(p.vx, p.vy);
            if (sp < 0.06) {
                const a = Math.random() * 6.283;
                p.vx += Math.cos(a) * 0.03;
                p.vy += Math.sin(a) * 0.03;
            }
        }
    }

    /* موجِ ضربه: یک حلقه‌ی نور که از نقطه‌ی کلیک باز می‌شود و سرِ راهش
       ذرات را کنار می‌زند. عمداً «هُل دادن» است نه «جابه‌جا کردن» — ذرات
       سرعت می‌گیرند و اصطکاکِ حلقه‌ی step کم‌کم آرامشان می‌کند، پس حرکت
       طبیعی به نظر می‌رسد نه پرش. */
    function drawWaves(dt) {
        for (let i = waves.length - 1; i >= 0; i--) {
            const wv = waves[i];
            wv.r += dt * 0.62;
            wv.life -= dt;

            if (wv.life <= 0 || wv.r > Math.max(w, h)) {
                waves.splice(i, 1);
                continue;
            }

            const a = Math.max(0, wv.life / wv.max) * 0.5;
            ctx.strokeStyle = `hsla(${wv.hue},95%,68%,${a.toFixed(3)})`;
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(wv.x, wv.y, wv.r, 0, 6.283);
            ctx.stroke();

            // فقط ذراتی که نزدیکِ *جبهه‌ی* موج‌اند هل داده می‌شوند
            for (const p of particles) {
                const dx = p.x - wv.x, dy = p.y - wv.y;
                const d = Math.hypot(dx, dy) || 1;
                if (Math.abs(d - wv.r) < 34) {
                    const f = 0.55 / d;
                    p.vx += dx * f;
                    p.vy += dy * f;
                }
            }
        }
    }

    function drawNetwork() {
        const party = performance.now() < partyUntil;
        const hueBase = (performance.now() * 0.18) % 360;

        // گرید همسایگی: هر ذره فقط با ذراتِ ۹ خانه‌ی اطرافش سنجیده می‌شود.
        // با ۹۰ ذره فرق O(n²) محسوس نیست، ولی روی مانیتور ۴K که تعداد به
        // سقف می‌رسد همین کار حلقه را کوتاه نگه می‌دارد.
        const cols = Math.max(1, Math.ceil(w / CELL));
        const rows = Math.max(1, Math.ceil(h / CELL));
        const grid = new Map();

        for (let i = 0; i < particles.length; i++) {
            const p = particles[i];
            const cxi = Math.min(cols - 1, Math.max(0, Math.floor(p.x / CELL)));
            const cyi = Math.min(rows - 1, Math.max(0, Math.floor(p.y / CELL)));
            const key = cyi * cols + cxi;
            let cell = grid.get(key);
            if (!cell) { cell = []; grid.set(key, cell); }
            cell.push(i);
        }

        ctx.lineWidth = 1;

        for (let i = 0; i < particles.length; i++) {
            const p = particles[i];
            const cxi = Math.min(cols - 1, Math.max(0, Math.floor(p.x / CELL)));
            const cyi = Math.min(rows - 1, Math.max(0, Math.floor(p.y / CELL)));

            for (let oy = -1; oy <= 1; oy++) {
                for (let ox = -1; ox <= 1; ox++) {
                    const nx = cxi + ox, ny = cyi + oy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                    const cell = grid.get(ny * cols + nx);
                    if (!cell) continue;

                    for (const j of cell) {
                        if (j <= i) continue;          // هر جفت یک بار
                        const q = particles[j];
                        const dx = p.x - q.x, dy = p.y - q.y;
                        const d2 = dx * dx + dy * dy;
                        if (d2 > LINK_DIST * LINK_DIST) continue;

                        const t = 1 - Math.sqrt(d2) / LINK_DIST;
                        // در حالت جشن خطوط رنگین‌کمانی می‌شوند و پررنگ‌تر
                        ctx.strokeStyle = party
                            ? `hsla(${(hueBase + (p.x + p.y) * 0.35) % 360},95%,70%,${(t * 0.5).toFixed(3)})`
                            : `rgba(215,185,255,${(t * 0.22).toFixed(3)})`;
                        ctx.beginPath();
                        ctx.moveTo(p.x, p.y);
                        ctx.lineTo(q.x, q.y);
                        ctx.stroke();
                    }
                }
            }
        }

        // خودِ ذرات — بعد از خط‌ها تا رویشان بنشینند
        for (const p of particles) {
            ctx.fillStyle = party
                ? `hsl(${(hueBase + p.x * 0.5) % 360},95%,72%)`
                : 'rgba(240,225,255,0.85)';
            ctx.beginPath();
            ctx.arc(p.x, p.y, party ? p.r * 1.7 : p.r, 0, 6.283);
            ctx.fill();
        }

        // هاله‌ی دور مکان‌نما
        if (pointer.active) {
            const g = ctx.createRadialGradient(
                pointer.x, pointer.y, 0, pointer.x, pointer.y, 170);
            g.addColorStop(0, 'rgba(34,211,238,0.10)');
            g.addColorStop(1, 'rgba(34,211,238,0)');
            ctx.fillStyle = g;
            ctx.beginPath();
            ctx.arc(pointer.x, pointer.y, 170, 0, 6.283);
            ctx.fill();
        }
    }

    let lastT = 0;
    function frame(time) {
        // dt نرمال‌شده به فریمِ ۶۰: اگر مرورگر فریم بیندازد، موج‌ها کند
        // نمی‌شوند. سقف ۳ تا بعد از برگشتن از تبِ پنهان یک‌باره نپرند.
        const dt = lastT ? Math.min(3, (time - lastT) / 16.67) : 1;
        lastT = time;

        drawAurora(time);
        step();
        drawWaves(dt);
        drawNetwork();
        raf = requestAnimationFrame(frame);
    }

    // ───────────────────────── چرخه‌ی حیات ─────────────────────────

    function start() {
        if (running) return;

        canvas = document.getElementById(CANVAS_ID);
        if (!canvas) {
            canvas = document.createElement('canvas');
            canvas.id = CANVAS_ID;
            canvas.setAttribute('aria-hidden', 'true');
            document.body.insertBefore(canvas, document.body.firstChild);
        }

        ctx = canvas.getContext('2d', { alpha: false });
        if (!ctx) return;                       // بومِ غیرقابل‌ساخت: بی‌سروصدا رد شو

        // هر بار ورود به ماژول دوباره خوانده می‌شود تا تغییرِ ترجیح بدون
        // ری‌لود هم اثر کند.
        reduceMotion = motionPref();
        document.body.classList.toggle('cc-motion-ok', !reduceMotion);

        document.body.classList.add(BODY_CLASS);
        sizeCanvas();
        seed();
        running = true;

        window.addEventListener('resize', onResize, { passive: true });
        window.addEventListener('pointermove', onPointerMove, { passive: true });
        window.addEventListener('pointerleave', onPointerLeave, { passive: true });
        window.addEventListener('pointerdown', onPointerDown, { passive: true });

        if (reduceMotion) {
            // یک فریمِ ثابت: فضا و شبکه دیده می‌شود ولی هیچ حلقه‌ای نمی‌دود
            drawAurora(0);
            drawNetwork();
        } else {
            raf = requestAnimationFrame(frame);
        }
    }

    function stop() {
        if (!running) return;
        running = false;

        cancelAnimationFrame(raf);
        raf = 0;

        window.removeEventListener('resize', onResize);
        window.removeEventListener('pointermove', onPointerMove);
        window.removeEventListener('pointerleave', onPointerLeave);
        window.removeEventListener('pointerdown', onPointerDown);
        waves = [];
        partyUntil = 0;

        document.body.classList.remove(BODY_CLASS, 'cc-motion-ok');
        canvas?.remove();
        canvas = null;
        ctx = null;
        particles = [];
    }

    // ───────────────────────── رویدادها ─────────────────────────

    let resizeTimer = 0;
    function onResize() {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            if (!running) return;
            sizeCanvas();
            seed();
            if (reduceMotion) { drawAurora(0); drawNetwork(); }
        }, 150);
    }

    function onPointerMove(e) {
        pointer.x = e.clientX;
        pointer.y = e.clientY;
        pointer.active = true;
    }

    function onPointerLeave() {
        pointer.active = false;
        pointer.x = pointer.y = -9999;
    }

    function onPointerDown(e) {
        // هر کلیکی روی صفحه یک موج می‌سازد — از دکمه گرفته تا فضای خالی.
        // سقفِ ۴ موجِ هم‌زمان تا کلیکِ پیاپی حلقه را سنگین نکند.
        if (reduceMotion || waves.length > 4) return;
        waves.push({
            x: e.clientX, y: e.clientY,
            r: 6, life: 46, max: 46,
            hue: 170 + Math.random() * 120     // فیروزه‌ای تا بنفش
        });
    }

    // تب که پنهان شود حلقه می‌ایستد — مرورگر خودش rAF را کند می‌کند ولی
    // متوقف نمی‌کند، و روی لپ‌تاپِ روی باتری این تفاوت دیده می‌شود.
    document.addEventListener('visibilitychange', () => {
        if (!running || reduceMotion) return;
        if (document.hidden) {
            cancelAnimationFrame(raf);
            raf = 0;
        } else if (!raf) {
            raf = requestAnimationFrame(frame);
        }
    });

    // ───────────── پایش ورود/خروج از ماژول ─────────────
    // Blazor مسیریابی سمت کلاینت دارد، پس load تنها یک بار می‌آید؛
    // تنها راهِ مطمئنِ فهمیدنِ تعویض صفحه، خودِ DOM است.
    function sync() {
        if (isModuleOpen()) start(); else stop();
    }

    const observer = new MutationObserver(() => {
        // در یک تیک جمع می‌شود تا رندرهای پیاپیِ Blazor بارها صدایش نزنند
        clearTimeout(observer._t);
        observer._t = setTimeout(sync, 60);
    });

    function boot() {
        observer.observe(document.body, { childList: true, subtree: true });
        sync();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot, { once: true });
    } else {
        boot();
    }

    /* رابطِ بیرونی — cost-close-fun.js از این استفاده می‌کند.
       عمداً کوچک و بی‌حالت: هر تابع اگر بوم فعال نباشد بی‌سروصدا رد می‌شود،
       تا صدا زدنش از هر جای برنامه بی‌خطر باشد. */
    window.ccFx = {
        /** موج ضربه در مختصات صفحه */
        shockwave(x, y, hue) {
            if (!running || reduceMotion) return;
            waves.push({ x, y, r: 6, life: 46, max: 46, hue: hue ?? 190 });
        },
        /** حالت جشن: ذرات رنگین‌کمانی و درشت‌تر، برای ms مشخص */
        party(ms = 5000) {
            if (!running || reduceMotion) return;
            partyUntil = performance.now() + ms;
            for (const p of particles) {          // یک تکانِ اولیه
                p.vx += (Math.random() - 0.5) * 2.4;
                p.vy += (Math.random() - 0.5) * 2.4;
            }
        },
        get active() { return running; }
    };

    /** ترجیح فعلی: 'on' | 'off' | 'auto' — برای صفحه‌ی تنظیمات */
    window.ccMotionGet = () => {
        try { return localStorage.getItem('cc-motion') || 'auto'; }
        catch { return 'auto'; }
    };

    /** آیا خودِ سیستم حرکت را کم کرده؟ برای نمایش راهنما در تنظیمات */
    window.ccSystemReduced = () =>
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    /** روشن/خاموش کردن حرکت از کنسول یا تنظیمات: ccMotion('on'|'off'|'auto') */
    window.ccMotion = (mode) => {
        try {
            if (mode === 'auto') localStorage.removeItem('cc-motion');
            else localStorage.setItem('cc-motion', mode === 'on' ? 'on' : 'off');
        } catch { }
        location.reload();
    };
})();
