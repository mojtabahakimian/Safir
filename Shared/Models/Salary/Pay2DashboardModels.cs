namespace Safir.Shared.Models.Salary
{
    public class Pay2DashboardDataDto
    {
        public int WS_ID { get; set; }
        public string WorkshopName { get; set; } = "";

        public long LatestPeriodDate { get; set; }
        public string PeriodTitle { get; set; } = "";
        public byte PeriodStatus { get; set; }

        // 🚀 اصلاح مهم: هندل کردن وضعیت 0 (عدم وجود دوره) و استفاده از Switch Expression
        public string PeriodStatusText => PeriodStatus switch
        {
            0 => "تعریف نشده",
            1 => "باز",
            2 => "بسته",
            3 => "محاسبه شده",
            4 => "سند صادر شده",
            _ => "نامشخص"
        };

        public int ActiveEmployeesCount { get; set; }
        public long EstimatedNetPay { get; set; }
        public long TotalAdvances { get; set; }

        public int LeaveMinsPerDay { get; set; } = 440;

        public List<Pay2DashboardAdvanceRowDto> RecentAdvances { get; set; } = new();
    }

    public class Pay2DashboardAdvanceRowDto
    {
        public string FULL_NAME { get; set; } = "";
        public string PCODE { get; set; } = "";
        public long ADVANCE_DEDUCTION { get; set; }
    }
}