using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Salary
{
    public class Pay2LeaveDto
    {
        public int LEV_ID { get; set; }
        public int EMP_ID { get; set; }
        public byte LEV_TYPE { get; set; } = 1;
        public long REQUEST_DATE { get; set; }
        public long START_DATE { get; set; }
        public long END_DATE { get; set; }

        public short REQ_DAYS { get; set; } = 0;
        public byte REQ_HOURS { get; set; } = 0;
        public byte REQ_MINUTES { get; set; } = 0;

        public int? BAL_BEFORE { get; set; }
        public string? DESCRIPTION { get; set; }
        public int? REFER_TO { get; set; }
        public byte STATUS { get; set; } = 1;

        // نوع مرخصی ساعتی
        public const byte HOURLY_TYPE = 6;

        // --- پراپرتی‌های محاسباتی و نمایشی ---
        public int TotalMinutes => (REQ_DAYS * 440) + (REQ_HOURS * 60) + REQ_MINUTES;

        public string LeaveTypeText => LEV_TYPE switch
        {
            1 => "استحقاقی",
            2 => "استعلاجی",
            3 => "بدون حقوق",
            4 => "زایمان",
            5 => "مأموریت",
            6 => "ساعتی",
            _ => "نامشخص"
        };

        public string StatusText => STATUS switch
        {
            1 => "ثبت اولیه",
            2 => "تأیید درخواست",
            3 => "تأیید مدیر واحد",
            4 => "تأیید مدیرعامل",
            _ => "نامشخص"
        };
    }

    // کلاس به بیرون منتقل شد و مستقل است
    public class Pay2ContractDto
    {
        public int CON_ID { get; set; }
        public int EMP_ID { get; set; }
        public byte CON_TYPE { get; set; } = 1;
        public long START_DATE { get; set; }
        public long? END_DATE { get; set; }
        public long? TRIAL_END { get; set; }
        public decimal WEEKLY_HOURS { get; set; } = 44.0m;
        public string? NOTES { get; set; }

        // --- پراپرتی نمایشی ---
        public string ConTypeText => CON_TYPE switch
        {
            1 => "دائم",
            2 => "موقت",
            3 => "پیمانی",
            4 => "ساعتی",
            _ => "نامشخص"
        };
    }
}
