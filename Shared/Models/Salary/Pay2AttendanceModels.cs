using System.Text.Json.Serialization;

namespace Safir.Shared.Models.Salary
{
    public class Pay2PeriodDto
    {
        public int PER_ID { get; set; }
        public int WS_ID { get; set; }
        public long PERIOD_DATE { get; set; }
        public byte HOLIDAY_DAYS { get; set; }
        public bool TENDAR_APPLY { get; set; }
        public double? DEED_N_S_PAY { get; set; }
        public byte STATUS { get; set; }

        public string StatusText => STATUS == 1 ? "باز (قابل ویرایش)" : (STATUS == 2 ? "بسته (آماده محاسبه)" : (STATUS == 3 ? "محاسبه شده" : "سند صادر شده"));
        public bool IsReadOnly => STATUS != 1;
    }

    public class Pay2AttendanceLineDto
    {
        public int EMP_ID { get; set; }
        public string? EMP_CODE { get; set; }
        public string? FULL_NAME { get; set; }
        public bool LOCKED { get; set; }

        [JsonIgnore] public bool IsDirty { get; set; } = false; // پیگیری تغییرات در کلاینت

        // فیلدهای اعشاری برای دیتابیس
        public decimal WORK_DAYS { get; set; }
        public decimal DAYS_TOLID { get; set; }
        public decimal DAYS_EDARI { get; set; }
        public decimal DAYS_KHADAMAT { get; set; }
        public decimal DAYS_FOROSH { get; set; }
        public decimal OT_NORMAL_H { get; set; }
        public decimal OT_HOLIDAY_H { get; set; }
        public decimal OT_ADMIN_H { get; set; }
        public decimal LEAVE_DAYS { get; set; }
        public decimal ABSENT_DAYS { get; set; }
        public decimal MISSION_DAYS { get; set; }
        public decimal DAYS { get; set; }
        public decimal DAYSB { get; set; }
        public byte FRID_COUNT { get; set; }
        public decimal TDAYS { get; set; }

        public long PERF_AMOUNT { get; set; }
        public long TRANSP_AMOUNT { get; set; }
        public long KASR_OTHER { get; set; }

        // --- پراپرتی‌های String برای بایندینگ بدون خطای Blazor به Pay2NumericInput ---
        // هر بار که کاربر مقدار را عوض کند، IsDirty برابر True می‌شود
        public string WORK_DAYS_STR { get => WORK_DAYS.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (WORK_DAYS != v) { WORK_DAYS = v; IsDirty = true; } } }
        public string DAYS_TOLID_STR { get => DAYS_TOLID.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (DAYS_TOLID != v) { DAYS_TOLID = v; IsDirty = true; } } }
        public string DAYS_EDARI_STR { get => DAYS_EDARI.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (DAYS_EDARI != v) { DAYS_EDARI = v; IsDirty = true; } } }
        public string DAYS_KHADAMAT_STR { get => DAYS_KHADAMAT.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (DAYS_KHADAMAT != v) { DAYS_KHADAMAT = v; IsDirty = true; } } }
        public string DAYS_FOROSH_STR { get => DAYS_FOROSH.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (DAYS_FOROSH != v) { DAYS_FOROSH = v; IsDirty = true; } } }

        public string OT_NORMAL_H_STR { get => OT_NORMAL_H.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (OT_NORMAL_H != v) { OT_NORMAL_H = v; IsDirty = true; } } }
        public string OT_HOLIDAY_H_STR { get => OT_HOLIDAY_H.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (OT_HOLIDAY_H != v) { OT_HOLIDAY_H = v; IsDirty = true; } } }
        public string OT_ADMIN_H_STR { get => OT_ADMIN_H.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (OT_ADMIN_H != v) { OT_ADMIN_H = v; IsDirty = true; } } }

        public string LEAVE_DAYS_STR { get => LEAVE_DAYS.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (LEAVE_DAYS != v) { LEAVE_DAYS = v; IsDirty = true; } } }
        public string ABSENT_DAYS_STR { get => ABSENT_DAYS.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (ABSENT_DAYS != v) { ABSENT_DAYS = v; IsDirty = true; } } }
        public string MISSION_DAYS_STR { get => MISSION_DAYS.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (MISSION_DAYS != v) { MISSION_DAYS = v; IsDirty = true; } } }

        public string DAYS_STR { get => DAYS.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (DAYS != v) { DAYS = v; IsDirty = true; } } }
        public string DAYSB_STR { get => DAYSB.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (DAYSB != v) { DAYSB = v; IsDirty = true; } } }
        public string TDAYS_STR { get => TDAYS.ToString("0.##"); set { _ = decimal.TryParse(value?.Replace("/", "."), out decimal v); if (TDAYS != v) { TDAYS = v; IsDirty = true; } } }

        public string FRID_COUNT_STR { get => FRID_COUNT.ToString(); set { _ = byte.TryParse(value, out byte v); if (FRID_COUNT != v) { FRID_COUNT = v; IsDirty = true; } } }

        // مقادیر ریالی
        public string PERF_AMOUNT_STR { get => PERF_AMOUNT.ToString(); set { _ = long.TryParse(value?.Replace(",", ""), out long v); if (PERF_AMOUNT != v) { PERF_AMOUNT = v; IsDirty = true; } } }
        public string TRANSP_AMOUNT_STR { get => TRANSP_AMOUNT.ToString(); set { _ = long.TryParse(value?.Replace(",", ""), out long v); if (TRANSP_AMOUNT != v) { TRANSP_AMOUNT = v; IsDirty = true; } } }
        public string KASR_OTHER_STR { get => KASR_OTHER.ToString(); set { _ = long.TryParse(value?.Replace(",", ""), out long v); if (KASR_OTHER != v) { KASR_OTHER = v; IsDirty = true; } } }
    }

    public class Pay2AttendanceSaveRequest
    {
        public Pay2PeriodDto Period { get; set; } = new();
        public List<Pay2AttendanceLineDto> Lines { get; set; } = new();
    }

    public class Pay2AttValueDto
    {
        public int ITEM_ID { get; set; }
        public string? ITEM_NAME { get; set; }
        public long VALUE { get; set; }
        public string VALUE_STR { get => VALUE.ToString(); set { _ = long.TryParse(value?.Replace(",", ""), out long v); VALUE = v; } }
    }
}