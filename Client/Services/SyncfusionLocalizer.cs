using System.Collections.Generic;
using System.Resources;
using System.Text.RegularExpressions;
using Syncfusion.Blazor;

namespace Safir.Client.Services
{
    /// <summary>
    /// محلی‌سازِ (Localizer) فارسی برای کامپوننت‌های Syncfusion — به‌ویژه برچسب‌های
    /// فیلترِ اکسلیِ گرید (انتخاب همه، جستجو، تأیید/انصراف، عملگرها و ...).
    /// پیاده‌سازی کاملاً دیکشنری‌محور و دفاعی است؛ هیچ‌گاه استثنا پرتاب نمی‌کند و در صورت
    /// نبودِ کلید، یک متنِ خوانا (به‌جای خودِ کلیدِ خام مثل Grid_EmptyRecord) برمی‌گرداند.
    /// </summary>
    public class SyncfusionLocalizer : ISyncfusionStringLocalizer
    {
        // فقط برای رعایت قرارداد اینترفیس؛ مسیر اصلی ترجمه از GetText و دیکشنری زیر است.
        // ResourceManagerِ امن که هرگز استثنا پرتاب نمی‌کند (در صورت دسترسی مستقیم Syncfusion).
        private static readonly ResourceManager _rm = new SafeResourceManager();

        public ResourceManager ResourceManager => _rm;

        private sealed class SafeResourceManager : ResourceManager
        {
            public SafeResourceManager()
                : base("Safir.Client.Services.SyncfusionLocalizer", typeof(SyncfusionLocalizer).Assembly) { }

            public override string? GetString(string name)
            {
                try { return base.GetString(name); } catch { return null; }
            }

            public override string? GetString(string name, System.Globalization.CultureInfo? culture)
            {
                try { return base.GetString(name, culture); } catch { return null; }
            }
        }

        public string GetText(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (Fa.TryGetValue(key, out var fa)) return fa;

            // فالبکِ خوانا برای کلیدهای ترجمه‌نشده: حذف پیشوندِ کامپوننت و جداکردنِ CamelCase
            var idx = key.IndexOf('_');
            var name = (idx >= 0 && idx < key.Length - 1) ? key.Substring(idx + 1) : key;
            return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
        }

        // ── دیکشنری ترجمهٔ کلیدهای پرکاربردِ گرید (Grid_*) و صفحه‌بند (Pager_*) ──
        private static readonly Dictionary<string, string> Fa = new()
        {
            // عمومی
            ["Grid_EmptyRecord"] = "رکوردی برای نمایش وجود ندارد",
            ["Grid_Search"] = "جستجو",
            ["Grid_SearchColumns"] = "جستجو",
            ["Grid_Item"] = "مورد",
            ["Grid_Items"] = "مورد",
            ["Grid_True"] = "بله",
            ["Grid_False"] = "خیر",
            ["Grid_InvalidFilterMessage"] = "داده‌های فیلتر نامعتبر است",

            // دکمه‌ها
            ["Grid_OKButton"] = "تأیید",
            ["Grid_CancelButton"] = "انصراف",
            ["Grid_ClearButton"] = "پاک کردن",
            ["Grid_FilterButton"] = "اعمال فیلتر",

            // فیلتر اکسلی
            ["Grid_SelectAll"] = "انتخاب همه",
            ["Grid_Blanks"] = "(خالی)",
            ["Grid_NoResult"] = "موردی یافت نشد",
            ["Grid_Matchs"] = "موردی یافت نشد",
            ["Grid_AddCurrentSelection"] = "افزودن موارد انتخابی به فیلتر",
            ["Grid_ClearFilter"] = "حذف فیلتر",
            ["Grid_NumberFilter"] = "فیلترهای عددی",
            ["Grid_TextFilter"] = "فیلترهای متنی",
            ["Grid_DateFilter"] = "فیلترهای تاریخ",
            ["Grid_DateTimeFilter"] = "فیلترهای تاریخ و زمان",
            ["Grid_MatchCase"] = "تطبیق حروف بزرگ/کوچک",
            ["Grid_Between"] = "بین",
            ["Grid_CustomFilter"] = "فیلتر سفارشی",
            ["Grid_CustomFilterPlaceHolder"] = "مقدار را وارد کنید",
            ["Grid_CustomFilterDatePlaceHolder"] = "تاریخ را انتخاب کنید",
            ["Grid_ShowRowsWhere"] = "نمایش ردیف‌هایی که:",
            ["Grid_And"] = "و",
            ["Grid_Or"] = "یا",

            // عملگرهای فیلتر
            ["Grid_StartsWith"] = "شروع با",
            ["Grid_EndsWith"] = "پایان با",
            ["Grid_Contains"] = "شامل",
            ["Grid_NotStartsWith"] = "شروع نشود با",
            ["Grid_NotEndsWith"] = "پایان نیابد با",
            ["Grid_NotContains"] = "شامل نباشد",
            ["Grid_Equal"] = "برابر",
            ["Grid_NotEqual"] = "نابرابر",
            ["Grid_LessThan"] = "کوچک‌تر از",
            ["Grid_LessThanOrEqual"] = "کوچک‌تر یا مساوی",
            ["Grid_GreaterThan"] = "بزرگ‌تر از",
            ["Grid_GreaterThanOrEqual"] = "بزرگ‌تر یا مساوی",
            ["Grid_Empty"] = "خالی",
            ["Grid_NotEmpty"] = "غیرخالی",
            ["Grid_Null"] = "تهی",
            ["Grid_NotNull"] = "غیرتهی",

            // مرتب‌سازی
            ["Grid_SortAscending"] = "مرتب‌سازی صعودی",
            ["Grid_SortDescending"] = "مرتب‌سازی نزولی",
            ["Grid_SortAtoZ"] = "مرتب‌سازی از الف تا ی",
            ["Grid_SortZtoA"] = "مرتب‌سازی از ی تا الف",
            ["Grid_SortSmallestToLargest"] = "از کوچک به بزرگ",
            ["Grid_SortLargestToSmallest"] = "از بزرگ به کوچک",
            ["Grid_SortByOldest"] = "از قدیمی‌ترین",
            ["Grid_SortByNewest"] = "از جدیدترین",

            // منوی ستون / گروه‌بندی
            ["Grid_Filter"] = "فیلتر",
            ["Grid_FilterMenu"] = "فیلتر",
            ["Grid_Columnchooser"] = "ستون‌ها",
            ["Grid_ChooseColumns"] = "انتخاب ستون‌ها",
            ["Grid_GroupDropArea"] = "ستون را برای گروه‌بندی این‌جا بکشید",

            // صفحه‌بند (در صورت فعال شدن)
            ["Pager_currentPageInfo"] = "{0} از {1} صفحه",
            ["Pager_totalItemsInfo"] = "({0} مورد)",
            ["Pager_firstPageTooltip"] = "صفحهٔ اول",
            ["Pager_lastPageTooltip"] = "صفحهٔ آخر",
            ["Pager_nextPageTooltip"] = "صفحهٔ بعد",
            ["Pager_previousPageTooltip"] = "صفحهٔ قبل",
        };
    }
}
