/* ══════════════════════════════════════════════════════════════════════
   لایه‌ی «حال‌وهوا» — فقط برای ماژول بهای تمام‌شده

   بهای تمام‌شده ذاتاً خشک است: جدول، ریال، مغایرت. این فایل بدون آنکه به
   داده دست بزند، پوسته را زنده می‌کند.

   ── قاعده‌ای که هرگز شکسته نمی‌شود ──
   شوخی فقط در «پوسته» می‌نشیند: حالتِ خالی، بارگذاری، جشنِ موفقیت،
   واکنشِ کلیک. هیچ‌وقت کنار عدد ریالی، نام کالا، یا متنِ مغایرت.
   دلیلش ساده است — کاربر این اعداد را به هیئت‌مدیره می‌برد؛ شوخی کنارِ
   رقم، هم اعتماد را می‌برد هم می‌تواند بد خوانده شود.

   ── چه می‌کند ──
   ۱) جشن: با هر اسنک‌بارِ موفقیت، کانفتی + موجِ رنگی
   ۲) طنز: متن‌های «موردی یافت نشد» با جمله‌های چرخشی جایگزین می‌شوند
   ۳) رفیق: یک شخصیت کوچک گوشه‌ی صفحه که گاهی حرف می‌زند
   ۴) تخم‌مرغ عید: ۷ بار کلیک روی عنوان صفحه → حالت جشن

   همه‌چیز با prefers-reduced-motion خاموش می‌شود و بیرون از ماژول اصلاً
   اجرا نمی‌شود.
   ══════════════════════════════════════════════════════════════════════ */

(() => {
    'use strict';

    // همان ترجیحی که cost-close-fx.js می‌خواند — نگاه کنید motionPref آنجا.
    // localStorage['cc-motion'] = 'on' سیستم را دور می‌زند.
    function reduced() {
        let v = null;
        try { v = localStorage.getItem('cc-motion'); } catch { }
        if (v === 'on')  return false;
        if (v === 'off') return true;
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    let reduceMotion = reduced();

    // ───────────────────────── متن‌ها ─────────────────────────

    const EMPTY_LINES = [
        'هیچی نیست. یا همه‌چیز درست است، یا خیلی خوب پنهان شده.',
        'اینجا خالی است. حسابدارِ درونت می‌تواند نفس بکشد.',
        'صفر مورد. این را قاب بگیر.',
        'خبری نیست — و در حسابداری، بی‌خبری بهترین خبر است.',
        'هیچ مغایرتی پیدا نشد. مشکوکم، ولی خوشحالم.',
        'خالیِ خالی. انگار کسی قبل از تو کارها را کرده.'
    ];

    const CELEBRATE = [
        'ایول! 🎉',
        'انجام شد. برو یک چای بخور ☕',
        'تمیز بود 👌',
        'یکی کم شد، دمت گرم 🔥',
        'همین بود؟ آفرین 🚀',
        'ترکوندی 💥'
    ];

    const BUDDY_IDLE = [
        'دارم ریال‌ها را می‌شمرم…',
        'با کاردکس مذاکره می‌کنم…',
        'شیر خام را قانع کردم. سختش بود.',
        'یادت باشد: دستمزد را دستکاری نمی‌کنیم 😌',
        'انحرافِ مصرف هم آدم است، درکش کن.',
        'اگر عددی عجیب بود، تقصیر من نیست… احتمالاً.',
        'سربارها دوباره صفرند. کسی خبر دارد؟',
        'هر ماه همین است. ولی این بار سریع‌تریم.'
    ];

    const pick = a => a[Math.floor(Math.random() * a.length)];

    // ───────────────────────── کانفتی ─────────────────────────
    // بومِ جدا و کوتاه‌عمر: بعد از فروکش کردن حذف می‌شود تا هیچ حلقه‌ای
    // در پس‌زمینه باقی نماند.

    function confetti(x, y, count = 90) {
        if (reduceMotion) return;

        const cv = document.createElement('canvas');
        cv.className = 'cc-confetti';
        cv.width = innerWidth;
        cv.height = innerHeight;
        document.body.appendChild(cv);

        const g = cv.getContext('2d');
        const parts = Array.from({ length: count }, () => ({
            x, y,
            vx: (Math.random() - 0.5) * 13,
            vy: Math.random() * -13 - 4,
            w: Math.random() * 8 + 4,
            h: Math.random() * 5 + 3,
            rot: Math.random() * 6.283,
            vr: (Math.random() - 0.5) * 0.3,
            hue: Math.floor(Math.random() * 360),
            life: 1
        }));

        let raf = 0;
        const tick = () => {
            g.clearRect(0, 0, cv.width, cv.height);
            let alive = 0;

            for (const p of parts) {
                p.vy += 0.32;            // جاذبه
                p.vx *= 0.995;
                p.x += p.vx;
                p.y += p.vy;
                p.rot += p.vr;
                p.life -= 0.008;
                if (p.life <= 0 || p.y > cv.height + 40) continue;
                alive++;

                g.save();
                g.translate(p.x, p.y);
                g.rotate(p.rot);
                g.globalAlpha = Math.max(0, p.life);
                g.fillStyle = `hsl(${p.hue},90%,62%)`;
                g.fillRect(-p.w / 2, -p.h / 2, p.w, p.h);
                g.restore();
            }

            if (alive) { raf = requestAnimationFrame(tick); }
            else { cancelAnimationFrame(raf); cv.remove(); }
        };
        raf = requestAnimationFrame(tick);
    }

    // ───────────────────────── رفیقِ گوشه‌ی صفحه ─────────────────────────

    let buddy = null, buddyTimer = 0;

    function mountBuddy() {
        if (buddy || reduceMotion) return;

        buddy = document.createElement('div');
        buddy.className = 'cc-buddy';
        buddy.innerHTML =
            '<div class="cc-buddy-bubble" hidden></div>' +
            '<button class="cc-buddy-face" type="button" aria-label="دستیار">🧮</button>';
        document.body.appendChild(buddy);

        const face = buddy.querySelector('.cc-buddy-face');
        const bubble = buddy.querySelector('.cc-buddy-bubble');

        const say = (text, ms = 4200) => {
            bubble.textContent = text;
            bubble.hidden = false;
            bubble.classList.add('show');
            clearTimeout(bubble._t);
            bubble._t = setTimeout(() => {
                bubble.classList.remove('show');
                setTimeout(() => { bubble.hidden = true; }, 250);
            }, ms);
        };

        face.addEventListener('click', e => {
            say(pick(BUDDY_IDLE));
            face.classList.remove('pop');
            void face.offsetWidth;              // ری‌استارتِ انیمیشن
            face.classList.add('pop');
            const r = face.getBoundingClientRect();
            window.ccFx?.shockwave(r.left + r.width / 2, r.top + r.height / 2, 280);
            e.stopPropagation();
        });

        // گاهی خودش حرف می‌زند — با فاصله‌ی زیاد تا مزاحم نشود
        buddyTimer = setInterval(() => {
            if (document.hidden) return;
            if (Math.random() < 0.35) say(pick(BUDDY_IDLE), 3600);
        }, 45000);

        buddy._say = say;
    }

    function unmountBuddy() {
        clearInterval(buddyTimer);
        buddy?.remove();
        buddy = null;
    }

    // ───────────────────── انیشتین در حال فکر ─────────────────────
    /* کِی نشان داده می‌شود: هر وقت صفحه‌ای از این ماژول نوار پیشرفتِ
       نامعین نشان بدهد — یعنی همان الگویی که همه‌ی صفحه‌ها دارند:
           @if (_busy) { <MudProgressLinear Indeterminate ... /> }
       پس لازم نشد هیچ صفحه‌ای خبر بدهد که مشغول است؛ از روی خودِ DOM
       فهمیده می‌شود و اگر روزی صفحه‌ی تازه‌ای اضافه شود، خودبه‌خود
       پوشش داده می‌شود. */

    const THOUGHTS = [
        'بها = مواد + دستمزد + سربار',
        'انحراف مصرف = ؟',
        'Σ MEGHk × نرخ…'
    ];

    let think = null, thinkHideTimer = 0;

    /* دیالوگ‌های MudBlazor بیرون از .cc-page و ته body رندر می‌شوند، پس
       نوار پیشرفتِ داخلشان با انتخابگرِ .cc-page پیدا نمی‌شد و انیشتین
       در «بازسازی اسناد گروهی» — که طولانی‌ترین کار ماژول است — اصلاً
       نمی‌آمد. .cc-fx .mud-dialog همان دامنه را می‌دهد بدون آنکه به
       دیالوگ‌های بقیه‌ی برنامه نشت کند. */
    function isBusy() {
        return !!document.querySelector(
            '.cc-page .mud-progress-linear, .cc-page .mud-progress-circular, ' +
            '.cc-fx .mud-dialog .mud-progress-linear, ' +
            '.cc-fx .mud-dialog .mud-progress-circular');
    }

    /* انیشتینِ کوچک کنارِ هر بخشی که همان لحظه سند می‌زند.
       RebuildGroupDocsDialog برای هر نوع سند یک .rgd-row دارد و وقتی آن
       بخش در حال اجراست یک .rgd-progress داخلش می‌گذارد — پس وضعیت
       واقعی از خودِ DOM خوانده می‌شود، بدون تغییر در آن دیالوگ. */
    function decorateRunningRows() {
        for (const row of document.querySelectorAll('.cc-fx .rgd-row')) {
            const running = !!row.querySelector('.rgd-progress');
            const mini    = row.querySelector('.cc-think-mini');

            if (running && !mini && !reduceMotion) {
                const m = document.createElement('span');
                m.className = 'cc-think-mini';
                m.setAttribute('aria-hidden', 'true');
                m.innerHTML = `<span class="cc-einstein">${einsteinSvg()}</span>`;
                row.appendChild(m);
            } else if (!running && mini) {
                mini.remove();
            }
        }
    }

    function einsteinSvg() {
        /* سر و مو و سبیل در لایه‌های جدا تا با چرخشِ سه‌بعدی اختلافِ منظر
           بدهند (کلاس‌های cc-e-* در CSS مقدار translateZ می‌گیرند).

           برای واقعی‌تر شدن، حجم از گرادیان می‌آید نه از خط: پوست یک
           گرادیانِ شعاعی با سایه‌ی گونه دارد، موها از خاکستری تیره به
           سفید می‌روند، و یک «نور لبه‌ای» فیروزه‌ای/بنفش از همان تم روی
           کناره‌ها می‌نشیند — انگار پس‌زمینه‌ی نئونی صورتش را روشن کرده.
           همین چند گرادیان، تختیِ SVG را می‌شکند. */
        return `
<svg viewBox="0 0 120 140" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <radialGradient id="ccSkin2" cx="38%" cy="30%" r="80%">
      <stop offset="0"    stop-color="#ffe9d2"/>
      <stop offset="58%"  stop-color="#efc79f"/>
      <stop offset="100%" stop-color="#c1926a"/>
    </radialGradient>
    <linearGradient id="ccHair" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0"    stop-color="#ffffff"/>
      <stop offset="55%"  stop-color="#dfe6f5"/>
      <stop offset="100%" stop-color="#9aa8c4"/>
    </linearGradient>
    <linearGradient id="ccStash" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0"    stop-color="#f4f7ff"/>
      <stop offset="100%" stop-color="#b9c4dc"/>
    </linearGradient>
    <linearGradient id="ccCoat" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0"    stop-color="#2a3566"/>
      <stop offset="100%" stop-color="#151d3a"/>
    </linearGradient>
    <!-- نور لبه‌ای: از راست فیروزه‌ای، از چپ بنفش -->
    <linearGradient id="ccRim" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0"    stop-color="#a78bfa" stop-opacity=".85"/>
      <stop offset="42%"  stop-color="#a78bfa" stop-opacity="0"/>
      <stop offset="58%"  stop-color="#22d3ee" stop-opacity="0"/>
      <stop offset="100%" stop-color="#22d3ee" stop-opacity=".85"/>
    </linearGradient>
    <filter id="ccSoft" x="-30%" y="-30%" width="160%" height="160%">
      <feGaussianBlur stdDeviation="1.6"/>
    </filter>
  </defs>

  <!-- یقه و کت: تنها جای رنگِ سرد، تا صورت گرم جدا شود -->
  <g class="cc-e-coat">
    <path d="M28 140 q6 -22 32 -22 q26 0 32 22 z" fill="url(#ccCoat)"/>
    <path d="M52 121 l8 9 l8 -9 l-8 -4 z" fill="#e8eeff"/>
    <path d="M60 130 l6 5 l-6 5 l-6 -5 z" fill="#f472b6"/>
  </g>

  <!-- گوش‌ها زیر مو، برای عمق -->
  <g class="cc-e-face">
    <ellipse cx="30" cy="70" rx="6" ry="8" fill="#e0b389"/>
    <ellipse cx="90" cy="70" rx="6" ry="8" fill="#e0b389"/>

    <ellipse cx="60" cy="66" rx="30" ry="35" fill="url(#ccSkin2)"/>
    <!-- سایه‌ی گونه و زیرچشم -->
    <ellipse cx="42" cy="76" rx="9" ry="6" fill="#d9a97f" opacity=".38" filter="url(#ccSoft)"/>
    <ellipse cx="78" cy="76" rx="9" ry="6" fill="#d9a97f" opacity=".38" filter="url(#ccSoft)"/>
    <!-- بینی با سایه -->
    <path d="M60 58 q-4 12 -1 15 q3 2 6 0" fill="none"
          stroke="#c08d63" stroke-width="2.2" stroke-linecap="round"/>
    <!-- چین پیشانی: نشانه‌ی تمرکز -->
    <path d="M46 44 q14 -5 28 0" fill="none" stroke="#d0a179"
          stroke-width="1.6" opacity=".55" stroke-linecap="round"/>
    <path d="M49 50 q11 -4 22 0" fill="none" stroke="#d0a179"
          stroke-width="1.4" opacity=".4" stroke-linecap="round"/>
  </g>

  <!-- موها: چند دسته با گرادیان، لبه‌های نامنظم -->
  <g class="cc-e-hair" fill="url(#ccHair)">
    <path d="M22 62 q-8 -22 6 -34 q-2 16 8 22 z"/>
    <path d="M98 62 q8 -22 -6 -34 q2 16 -8 22 z"/>
    <ellipse cx="27" cy="45" rx="14" ry="18" transform="rotate(-26 27 45)"/>
    <ellipse cx="93" cy="45" rx="14" ry="18" transform="rotate(26 93 45)"/>
    <ellipse cx="38" cy="24" rx="16" ry="13" transform="rotate(-18 38 24)"/>
    <ellipse cx="60" cy="17" rx="18" ry="12"/>
    <ellipse cx="82" cy="24" rx="16" ry="13" transform="rotate(18 82 24)"/>
    <ellipse cx="60" cy="33" rx="28" ry="14"/>
  </g>

  <g class="cc-e-brow" fill="url(#ccHair)">
    <path d="M37 52 q10 -7 21 -2 q-10 -1 -21 4 z"/>
    <path d="M83 52 q-10 -7 -21 -2 q10 -1 21 4 z"/>
  </g>

  <g class="cc-e-eyes">
    <ellipse cx="48" cy="62" rx="7" ry="5.4" fill="#fdfefe"/>
    <ellipse cx="72" cy="62" rx="7" ry="5.4" fill="#fdfefe"/>
    <g class="cc-e-pupil">
      <circle cx="48" cy="62" r="3.4" fill="#3b2d20"/>
      <circle cx="72" cy="62" r="3.4" fill="#3b2d20"/>
      <circle cx="49.2" cy="60.6" r="1.1" fill="#fff" opacity=".9"/>
      <circle cx="73.2" cy="60.6" r="1.1" fill="#fff" opacity=".9"/>
    </g>
    <g class="cc-e-lid" fill="#efc79f">
      <rect x="40" y="56" width="16" height="11" rx="5"/>
      <rect x="64" y="56" width="16" height="11" rx="5"/>
    </g>
  </g>

  <!-- سبیل پرپشت -->
  <g class="cc-e-stash" fill="url(#ccStash)">
    <path d="M60 80 q-20 -3 -22 8 q0 8 12 5 q7 -2 10 -6 q3 4 10 6 q12 3 12 -5 q-2 -11 -22 -8 z"/>
  </g>

  <!-- نور لبه‌ای روی کل سر -->
  <ellipse class="cc-e-rim" cx="60" cy="66" rx="30" ry="35"
           fill="none" stroke="url(#ccRim)" stroke-width="2.6" opacity=".9"/>
</svg>`;
    }

    function showThinking() {
        if (think || reduceMotion) return;

        clearTimeout(thinkHideTimer);
        think = document.createElement('div');
        think.className = 'cc-think';
        think.setAttribute('aria-hidden', 'true');
        think.innerHTML =
            '<div class="cc-think-stage">' +
              '<div class="cc-think-glow"></div>' +
              `<div class="cc-einstein">${einsteinSvg()}</div>` +
              '<div class="cc-think-bubbles">' +
                THOUGHTS.map(t => `<span>${t}</span>`).join('') +
              '</div>' +
            '</div>' +
            '<div class="cc-think-label">در حال فکر کردن</div>';

        document.body.appendChild(think);
        document.body.classList.add('cc-thinking');
    }

    function hideThinking() {
        if (!think) return;

        // با کلاس out می‌رود تا ناگهانی ناپدید نشود
        const el = think;
        think = null;
        document.body.classList.remove('cc-thinking');
        el.classList.add('out');
        thinkHideTimer = setTimeout(() => el.remove(), 320);
    }

    /* ضدلرزش: نوار پیشرفت بین دو رندرِ Blazor می‌تواند برای یک لحظه
       ناپدید و دوباره ظاهر شود. بدون این تأخیر، انیشتین در کارهای
       پشت‌سرهم چشمک می‌زد. */
    let busyTimer = 0;
    function syncThinking() {
        clearTimeout(busyTimer);
        busyTimer = setTimeout(() => {
            if (isBusy()) showThinking(); else hideThinking();
            decorateRunningRows();
        }, 180);
    }

    // ───────────────────────── حالت‌های خالی ─────────────────────────
    // متنِ «موردی یافت نشد» جای خودش می‌ماند و فقط یک جمله‌ی طنز *کنارش*
    // اضافه می‌شود — نه جایگزینش. اگر روزی متن اصلی عوض شد، این هم
    // بی‌سروصدا کاری نمی‌کند و چیزی نمی‌شکند.

    function decorateEmpty(root) {
        const targets = (root || document).querySelectorAll?.(
            '.cc-page .mud-table-empty-row td, .cc-page .cc-empty') || [];

        for (const el of targets) {
            if (el.dataset.ccFun) continue;
            el.dataset.ccFun = '1';
            const q = document.createElement('div');
            q.className = 'cc-fun-quip';
            q.textContent = pick(EMPTY_LINES);
            el.appendChild(q);
        }
    }

    // ───────────────────────── جشنِ موفقیت ─────────────────────────

    function watchSnackbars(node) {
        if (!(node instanceof HTMLElement)) return;

        const bars = node.matches?.('.mud-snackbar')
            ? [node]
            : node.querySelectorAll?.('.mud-snackbar') || [];

        for (const b of bars) {
            if (b.dataset.ccFun) continue;
            b.dataset.ccFun = '1';

            const ok = /success/.test(b.className);
            if (!ok) continue;

            const r = b.getBoundingClientRect();
            confetti(r.left + r.width / 2, r.top + r.height / 2, 80);
            window.ccFx?.party(2200);
            buddy?._say?.(pick(CELEBRATE), 3200);
        }
    }

    // ───────────────────────── تخم‌مرغ عید ─────────────────────────

    let titleClicks = 0, titleTimer = 0;

    function onTitleClick(e) {
        const t = e.target.closest?.(
            '.cc-page h1, .cc-page h2, .cc-page .mud-typography-h5, .cc-page .mud-typography-h6');
        if (!t) return;

        titleClicks++;
        clearTimeout(titleTimer);
        titleTimer = setTimeout(() => { titleClicks = 0; }, 1600);

        if (titleClicks >= 7) {
            titleClicks = 0;
            window.ccFx?.party(6000);
            confetti(innerWidth / 2, innerHeight * 0.35, 160);
            buddy?._say?.('باشه باشه! معلوم شد حوصله‌ات سر رفته 🎈', 5000);
        }
    }

    // ───────────────────────── چرخه‌ی حیات ─────────────────────────

    let on = false;

    function enable() {
        if (on) return;
        on = true;
        reduceMotion = reduced();          // تغییرِ ترجیح بدون ری‌لود هم اثر کند
        document.body.classList.add('cc-fun');
        mountBuddy();
        decorateEmpty(document);
        document.addEventListener('click', onTitleClick, true);
    }

    function disable() {
        if (!on) return;
        on = false;
        document.body.classList.remove('cc-fun');
        hideThinking();
        unmountBuddy();
        document.removeEventListener('click', onTitleClick, true);
        document.querySelectorAll('.cc-confetti').forEach(c => c.remove());
    }

    const observer = new MutationObserver(muts => {
        const open = !!document.querySelector('.cc-page');
        if (open) enable(); else { disable(); return; }

        for (const m of muts) {
            for (const n of m.addedNodes) watchSnackbars(n);
        }
        decorateEmpty(document);
        syncThinking();
    });

    function boot() {
        observer.observe(document.body, { childList: true, subtree: true });
        if (document.querySelector('.cc-page')) enable();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot, { once: true });
    } else {
        boot();
    }
})();
