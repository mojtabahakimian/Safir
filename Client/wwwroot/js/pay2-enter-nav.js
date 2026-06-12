// ════════════════════════════════════════════════════════════════
// ناوبری با کلید Enter در فرم‌های حقوق و دستمزد (Pay2)
// با زدن Enter فوکوس به فیلد بعدی منتقل می‌شود و نیازی به موس نیست.
// فقط روی ورودی‌های ماژول Pay2 اعمال می‌شود تا سایر بخش‌ها تغییری نکنند.
// ════════════════════════════════════════════════════════════════
(function () {
    const PAY2_INPUT_SELECTOR =
        "input.pay2-input, input.p2-grid-input, input.pay2-select-input, input.pay2-search-input";

    const FOCUSABLE_SELECTOR =
        "input.pay2-input, input.p2-grid-input, input.pay2-select-input, input.pay2-search-input, " +
        ".pay2-tab-container input:not([type=checkbox]):not([type=radio]), " +
        ".pay2-modal-container input:not([type=checkbox]):not([type=radio])";

    function isFocusable(el) {
        if (el.disabled || el.readOnly) return false;
        if (el.getAttribute("tabindex") === "-1") return false;
        // المان‌های مخفی (داخل مودال‌های بسته و ...) قابل فوکوس نیستند
        return el.offsetParent !== null;
    }

    document.addEventListener("keydown", function (e) {
        if (e.key !== "Enter" || e.shiftKey || e.ctrlKey || e.altKey || e.isComposing) return;

        const target = e.target;
        if (!target || !target.matches || !target.matches(PAY2_INPUT_SELECTOR)) return;

        // اگر دراپ‌داون Pay2Select باز است، اجازه می‌دهیم Enter ابتدا آیتم را انتخاب کند؛
        // (هندلر Blazor قبل از این هندلر اجرا شده است) سپس فوکوس جلو می‌رود.
        const candidates = Array.from(document.querySelectorAll(FOCUSABLE_SELECTOR))
            .filter(isFocusable);

        // حذف موارد تکراری با حفظ ترتیب DOM
        const unique = [...new Set(candidates)];

        const index = unique.indexOf(target);
        if (index < 0) return;

        e.preventDefault();

        const next = unique[index + 1];
        if (!next) return;

        next.focus();

        try {
            if (typeof next.select === "function") next.select();
        } catch {
            // ignored
        }
    });
})();
