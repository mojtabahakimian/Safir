namespace Safir.Shared.Models.Salary
{
    public class Pay2PeriodLookupDto
    {
        public long PERIOD_DATE { get; set; }
        public string PERIOD_TITLE { get; set; } = string.Empty;
        public byte STATUS { get; set; }

        public string StatusText => STATUS switch
        {
            1 => "باز (در حال ورود)",
            2 => "بسته (آماده محاسبه)",
            3 => "محاسبه شده",
            4 => "سند صادر شده",
            _ => "نامشخص"
        };
    }
}