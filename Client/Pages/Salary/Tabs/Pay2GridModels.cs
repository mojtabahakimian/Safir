using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;

namespace Safir.Client.Pages.Salary.Tabs
{
    /// <summary>
    /// تعریف یک ستون برای کامپوننت عمومی <see cref="Pay2DataGrid{TItem}"/>.
    /// همهٔ رفتارها (نمایش، مرتب‌سازی، جستجو، جمع فوتر، خروجی Excel) از طریق همین مدل پیکربندی می‌شوند.
    /// </summary>
    public class Pay2GridColumn<TItem>
    {
        /// <summary>عنوان ستون در هدر.</summary>
        public string Title { get; set; } = "";

        /// <summary>عرض پیش‌فرض ستون (px). کاربر می‌تواند با Resize تغییرش دهد.</summary>
        public int Width { get; set; } = 120;

        /// <summary>تراز افقی محتوای سلول (مقدار CSS: left/right/center).</summary>
        public string Align { get; set; } = "left";

        /// <summary>رنگ اختیاری عنوان هدر (مثلاً "#2563eb").</summary>
        public string? HeaderColor { get; set; }

        /// <summary>کلاس(های) CSS اضافه برای سلول‌های این ستون.</summary>
        public string? CellClass { get; set; }

        /// <summary>استایل inline اضافه برای سلول‌های این ستون.</summary>
        public string? CellStyle { get; set; }

        /// <summary>اگر true باشد، این ستون به لبهٔ راست می‌چسبد (Freeze). فقط یک ستون باید Sticky شود.</summary>
        public bool Sticky { get; set; }

        /// <summary>آیا ستون قابل مرتب‌سازی است (نیازمند <see cref="SortBy"/>).</summary>
        public bool Sortable { get; set; } = true;

        /// <summary>قالب سفارشی نمایش سلول. اگر null باشد از <see cref="Display"/> استفاده می‌شود.</summary>
        public RenderFragment<TItem>? CellTemplate { get; set; }

        /// <summary>متن سادهٔ سلول وقتی CellTemplate تعریف نشده است.</summary>
        public Func<TItem, string>? Display { get; set; }

        /// <summary>کلید مرتب‌سازی. مقدار string با مقایسهٔ فارسی و مقادیر عددی با مقایسهٔ عددی مرتب می‌شوند.</summary>
        public Func<TItem, object?>? SortBy { get; set; }

        /// <summary>آیا محتوای این ستون در جستجوی سراسری لحاظ شود.</summary>
        public bool Searchable { get; set; }

        /// <summary>متن مورد استفاده در جستجو. اگر null و Searchable=true باشد از <see cref="Display"/> استفاده می‌شود.</summary>
        public Func<TItem, string?>? SearchText { get; set; }

        /// <summary>اگر مقدار داشته باشد، فوتر برای این ستون جمع نمایش می‌دهد.</summary>
        public Func<TItem, long>? Sum { get; set; }

        /// <summary>قالب نمایش جمع فوتر (پیش‌فرض "N0").</summary>
        public Func<long, string>? SumDisplay { get; set; }

        // ── خروجی Excel ────────────────────────────────────────────────────────────
        /// <summary>عناوین ستون(های) خروجی. اگر null باشد، [Title] استفاده می‌شود.</summary>
        public IReadOnlyList<string>? ExportTitles { get; set; }

        /// <summary>
        /// تولید سلول(های) خروجی برای هر ردیف. تعداد سلول‌ها باید با <see cref="ExportTitles"/> برابر باشد.
        /// اگر null باشد، یک سلول از <see cref="ExportText"/> یا <see cref="Display"/> ساخته می‌شود.
        /// </summary>
        public Func<TItem, IReadOnlyList<Pay2GridCell>>? ExportCells { get; set; }

        /// <summary>مقدار خروجیِ متنیِ تک‌سلولی (وقتی نه ExportCells و نه ExportNumber تعریف نشده). اگر null از Display استفاده می‌شود.</summary>
        public Func<TItem, string>? ExportText { get; set; }

        /// <summary>
        /// مقدار عددیِ خروجی برای XLSX. اگر ست شود، سلول به‌صورت «عددِ واقعی» (نه متن) نوشته می‌شود؛
        /// پس بدون نماد علمی، بدون مثلث سبز و جمع‌پذیر است. برای ستون‌های مبلغی این را ست کن.
        /// </summary>
        public Func<TItem, double?>? ExportNumber { get; set; }

        /// <summary>اگر true و ExportNumber ست شده باشد، سلول با فرمت «#,##0» (جداکنندهٔ هزارگان) نمایش داده می‌شود.</summary>
        public bool ExportMoney { get; set; }
    }

    /// <summary>یک سلول خروجی Excel: مقدار + اینکه آیا باید به‌صورت متنِ امن (="...") نوشته شود.</summary>
    public readonly struct Pay2GridCell
    {
        public string Value { get; }
        public bool AsText { get; }
        public Pay2GridCell(string value, bool asText)
        {
            Value = value;
            AsText = asText;
        }
    }
}
