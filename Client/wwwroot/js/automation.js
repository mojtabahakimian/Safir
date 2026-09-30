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
        if (e.key === 'Escape') { dotnet.invokeMethodAsync('HotkeyEscape'); return; }
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
