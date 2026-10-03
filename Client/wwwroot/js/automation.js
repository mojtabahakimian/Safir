// میان‌بُرهای صفحه‌کلیدِ کارتابل اتوماسیون.
//   /  ← رفتن به جستجو       N ← وظیفه‌ی جدید       R ← بروزرسانی       Esc ← بستن پنل
// با e.code کار می‌کند نه e.key، تا روی صفحه‌کلیدِ فارسی هم (که N را «د» می‌نویسد) کار کند.
// وقتی کاربر در یک فیلد تایپ می‌کند، فقط Esc فعال است.
window.atmKeys = (function () {
    let dotnet = null;

    function typing(el) {
        if (!el) return false;
        const tag = el.tagName;
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
    }

    function onKey(e) {
        if (!dotnet || e.ctrlKey || e.metaKey || e.altKey) return;
        // پنجره‌ی چت Esc و کلیدهای خودش را خودش مدیریت می‌کند.
        const chat = document.querySelector('.atm-chat');
        if (chat && chat.contains(e.target)) return;
        if (e.key === 'Escape') { dotnet.invokeMethodAsync('HotkeyEscape'); return; }
        if (chat) return;
        if (typing(document.activeElement)) return;
        // وقتی یک دیالوگِ MudBlazor باز است، میان‌بُرهای صفحه خاموش‌اند.
        if (document.querySelector('.mud-dialog-container')) return;

        if (e.key === '/' || e.code === 'Slash') {
            const s = document.getElementById('atm-search');
            if (s) { e.preventDefault(); s.focus(); }
        } else if (e.code === 'KeyN') {
            e.preventDefault();
            dotnet.invokeMethodAsync('HotkeyNew');
        } else if (e.code === 'KeyR') {
            e.preventDefault();
            dotnet.invokeMethodAsync('HotkeyRefresh');
        }
    }

    return {
        attach(ref) { dotnet = ref; document.addEventListener('keydown', onKey); },
        detach() { document.removeEventListener('keydown', onKey); dotnet = null; }
    };
})();

// پنجره‌ی چتِ پیام‌های داخلی.
window.atmChat = {
    // Enter = ارسال، Shift+Enter = خط جدید. isComposing برای IME (تایپِ ترکیبی).
    // روی خودِ عنصر علامت می‌خورد تا با هر رندر دوباره وصل نشود.
    bindComposer(el, dotnet) {
        if (!el || el._atmBound) return;
        el._atmBound = true;
        el.addEventListener('keydown', e => {
            if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
                e.preventDefault();
                dotnet.invokeMethodAsync('SendFromKeyboard');
            }
        });
    },
    // اگر نزدیکِ پایین است نرم، وگرنه (باز کردنِ گفتگو) یک‌جا.
    scrollToBottom(el) {
        if (!el) return;
        const far = el.scrollHeight - el.scrollTop - el.clientHeight > 900;
        requestAnimationFrame(() => el.scrollTo({ top: el.scrollHeight, behavior: far ? 'auto' : 'smooth' }));
    }
};

// Treasury: when a row is edited, bring the edit form under that same row into view.
// If the row + form fit on screen, scroll only as much as needed; otherwise put the row
// itself at the top (under the app bar — scroll-margin in CSS) so it's clear which row is being edited.
window.trsReveal = function (el) {
    if (!el || !el.getBoundingClientRect) return;
    const row = el.previousElementSibling;
    const top = (row || el).getBoundingClientRect().top;
    const fits = el.getBoundingClientRect().bottom - top <= window.innerHeight - 100;
    if (fits) el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    else (row || el).scrollIntoView({ behavior: 'smooth', block: 'start' });
};

// Accounting document: focus one field of the row editor (input inside a [data-f] cell) and select its text.
window.sndFocus = function (selector) {
    const el = document.querySelector(selector);
    if (!el) return;
    el.focus();
    if (typeof el.select === 'function') el.select();
};

// Accounting document: Enter inside the cheque section moves to the next field (like WPF's Enter→Tab).
// 'moved' = went to the next field, 'end' = was the last one (caller saves), 'skip' = an autocomplete owns Enter.
window.sndNext = function (rootId) {
    const root = document.getElementById(rootId);
    const active = document.activeElement;
    if (!root || !active) return 'skip';
    // MudAutocomplete acts on Enter's keyup, so focus moved there on keydown would open (and later pick from) its list:
    // autocompletes (the optional cheque owner) stay out of the Enter chain and are reached with Tab or a click.
    if (active.closest('.mud-autocomplete')) return 'skip';
    const els = [...root.querySelectorAll('.snd-ed__chq input:not([type=checkbox]):not([disabled]), .snd-ed__chq select:not([disabled])')]
        .filter(e => !e.closest('.mud-autocomplete'));
    const i = els.indexOf(active);
    if (i < 0) return 'skip';
    if (i < els.length - 1) {
        els[i + 1].focus();
        if (typeof els[i + 1].select === 'function') els[i + 1].select();
        return 'moved';
    }
    return 'end';
};
