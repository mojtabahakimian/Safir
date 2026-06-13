namespace Safir.Shared.Models.Salary
{
    public class Pay2ItemDefDto
    {
        public int ITEM_ID { get; set; }
        public string? ITEM_CODE { get; set; }
        public string? ITEM_NAME { get; set; }
        public byte ITEM_TYPE { get; set; } = 1;
        public byte CALC_BASIS { get; set; } = 2;
        public bool INS_SUBJECT { get; set; } = true;
        public bool TAX_SUBJECT { get; set; } = true;
        public byte INS_BASE_DAYS { get; set; } = 1;
        public byte PAY_BASE_DAYS { get; set; } = 2;
        public bool IS_SYSTEM { get; set; } = false;
        public bool SHOW_IN_SLIP { get; set; } = true;
        public short SORT_ORDER { get; set; } = 100;
        public bool IS_ACTIVE { get; set; } = true;
        public string? NOTES { get; set; }

        // پراپرتی‌های نمایشی برای گرید کلاینت
        public string ItemTypeText => ITEM_TYPE switch
        {
            1 => "پرداختی ثابت",
            2 => "پرداختی متغیر",
            3 => "کسر ثابت",
            4 => "کسر متغیر",
            5 => "آگاهی/نمایش",
            _ => "نامشخص"
        };

        public string CalcBasisText => CALC_BASIS switch
        {
            1 => "روزانه",
            3 => "ساعتی",
            _ => "ماهیانه"
        };
    }
}