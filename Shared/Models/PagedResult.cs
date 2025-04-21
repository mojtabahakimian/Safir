namespace Safir.Shared.Models
{
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new List<T>(); // آیتم های صفحه فعلی
        public int TotalCount { get; set; }             // تعداد کل آیتم ها (بدون صفحه‌بندی)
        public int PageNumber { get; set; }               // شماره صفحه فعلی
        public int PageSize { get; set; }                 // تعداد آیتم در هر صفحه

        // پراپرتی محاسباتی برای تعداد کل صفحات
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }
}
