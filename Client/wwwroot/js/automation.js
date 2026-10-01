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
