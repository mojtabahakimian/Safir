namespace Safir.Shared.Models.Salary
{
    public class Pay2SmartAdvanceCalcRequest
    {
        public int WS_ID { get; set; }
        public long PERIOD_DATE { get; set; }
        public double? PAYROLL_N_S { get; set; }
    }

    public class Pay2SmartAdvanceRowDto
    {
        public int EMP_ID { get; set; }
        public string? PCODE { get; set; }
        public string? FULL_NAME { get; set; }
        public long RAW_BALANCE { get; set; }
        public long MANUAL_EXCL { get; set; }
        public long ADVANCE_DEDUCTION { get; set; }
    }

    public class Pay2SmartAdvanceSettingsDto
    {
        public int WS_ID { get; set; }
        public string? ADV_HES { get; set; }
        public string ADV_SCOPE { get; set; } = "CURRENT_MONTH";
        public bool ADV_MIN_POSITIVE { get; set; } = true;
        public bool ADV_USE_HES_T_FILTER { get; set; } = true;
    }
}