// نام شرکت (دیتابیس) اول عنوان تب مرورگر، مثل «YAZDSEPAR1405 | حقوق و دستمزد»، تا وقتی
// چند شرکت در چند تب باز است، کاربر از روی نوار تب‌ها هم بداند کدام کدام است.
// هر صفحه با <PageTitle> عنوان خودش را می‌گذارد؛ این ناظر پیشوند را دوباره اضافه می‌کند.
window.safirTab = (function () {
    let label = '';
    let observer = null;

    function apply() {
        if (!label) return;
        const prefix = label + ' | ';
        if (!document.title.startsWith(prefix)) document.title = prefix + document.title;
    }

    return {
        setLabel: function (value) {
            label = (value || '').trim();
            apply();
            // کل <head> زیر نظر است، نه خودِ <title>: HeadOutlet ممکن است عنصر title را عوض کند
            if (!observer) {
                observer = new MutationObserver(apply);
                observer.observe(document.head, { childList: true, characterData: true, subtree: true });
            }
        }
    };
})();
