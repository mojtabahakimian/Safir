using System;
using System.Collections.Generic;

namespace Safir.Server.Services
{
    // یک قلم ریز از PAY2_RUN_DETAIL برای گزارش/دیسکت بیمه
    public readonly struct Pay2DetailItem
    {
        public Pay2DetailItem(string code, int itemType, long amount, bool insSubject)
        {
            Code = code ?? string.Empty;
            ItemType = itemType;
            Amount = amount;
            InsSubject = insSubject;
        }

        public string Code { get; }
        public int ItemType { get; }   // 1=پرداختی ثابت، 2=پرداختی متغیر، 3/4=کسور، 5=آگاهی
        public long Amount { get; }
        public bool InsSubject { get; } // مشمولیت بیمه از Snapshot همان Run (PAY2_RUN_DETAIL.INS_SUBJECT)
    }

    // نتیجه تفکیک اقلام بر پایه «ریل اسمی» برای گزارش و دیسکت بیمه.
    // قاعده قطعی: BASE_SAL (اسمی) مبنای بیمه است و BASE_SAL_B (رسمی) هرگز با آن جمع نمی‌شود.
    public sealed class Pay2InsuranceAggregate
    {
        public long DailyBaseWage { get; set; }        // پایه مزد روزانه (بدون سنوات)
        public long DailySeniority { get; set; }       // پایه سنوات روزانه
        public long TotalDailyWage => DailyBaseWage + DailySeniority; // دستمزد روزانه کل
        public long BaseMonthly { get; set; }          // پایه مزد ماهانه اسمی (بدون سنوات)
        public long SeniorityMonthly { get; set; }     // پایه سنوات ماهانه
        public long MonthlyWage => BaseMonthly + SeniorityMonthly;   // دستمزد ماهانه = (پایه مزد + سنوات)
        public long MaritalAllowance { get; set; }     // حق تأهل (فقط داخل سایر مزایای مشمول شمرده می‌شود)
        public long OtherSubjectBenefits { get; set; } // سایر مزایای مشمول = HOME + GROCERY + FAMILY_ALLOW + ...
        public long TotalSubjectToInsurance { get; set; } // جمع دستمزد و مزایای مشمول (اسمی)
        public long NominalGross { get; set; }         // جمع ناخالص اسمی (تمام پرداختی‌های اسمی مشمول و غیرمشمول)
    }

    public static class Pay2InsuranceAggregator
    {
        private static readonly HashSet<string> SeniorityCodes =
            new(StringComparer.OrdinalIgnoreCase) { "SANOVAT_PAYE", "SANAVAT", "SENIORITY" };

        // تفکیک اقلام یک پرسنل بر پایه ریل اسمی. BASE_SAL_B (رسمی) از تمام جمع‌های این گزارش کنار گذاشته می‌شود؛
        // فقط اگر پرسنل حقوق اسمی نداشته باشد، حقوق رسمی به‌عنوان جایگزین پایه استفاده می‌شود (تک‌ریلی).
        public static Pay2InsuranceAggregate Aggregate(IEnumerable<Pay2DetailItem> details, decimal insuranceWorkDays)
        {
            long baseNominal = 0, baseNominalSubject = 0;
            long baseOfficial = 0, baseOfficialSubject = 0;
            long seniorityMonthly = 0, seniorityMonthlySubject = 0;
            long maritalAllowance = 0;
            long subjectOther = 0;      // مشمول، غیر از پایه و سنوات (شامل حق تأهل)
            long grossOther = 0;        // ناخالص، غیر از پایه و سنوات

            foreach (var d in details)
            {
                bool isPayment = d.ItemType == 1 || d.ItemType == 2;

                if (string.Equals(d.Code, "BASE_SAL", StringComparison.OrdinalIgnoreCase))
                {
                    baseNominal += d.Amount;
                    if (d.InsSubject) baseNominalSubject += d.Amount;
                }
                else if (string.Equals(d.Code, "BASE_SAL_B", StringComparison.OrdinalIgnoreCase))
                {
                    baseOfficial += d.Amount;
                    if (d.InsSubject) baseOfficialSubject += d.Amount;
                }
                else if (SeniorityCodes.Contains(d.Code))
                {
                    seniorityMonthly += d.Amount;
                    if (d.InsSubject) seniorityMonthlySubject += d.Amount;
                }
                else
                {
                    if (isPayment) grossOther += d.Amount;
                    if (d.InsSubject)
                    {
                        subjectOther += d.Amount;
                        if (string.Equals(d.Code, "FAMILY_ALLOW", StringComparison.OrdinalIgnoreCase))
                            maritalAllowance += d.Amount;
                    }
                }
            }

            // ریل اسمی؛ در نبود حقوق اسمی، حقوق رسمی جایگزین پایه می‌شود (بدون جمع‌شدن این دو).
            long baseEffective = baseNominal > 0 ? baseNominal : baseOfficial;
            long baseSubjectEffective = baseNominal > 0 ? baseNominalSubject : baseOfficialSubject;

            var agg = new Pay2InsuranceAggregate
            {
                BaseMonthly = baseEffective,
                SeniorityMonthly = seniorityMonthly,
                MaritalAllowance = maritalAllowance,
                OtherSubjectBenefits = subjectOther,
                TotalSubjectToInsurance = baseSubjectEffective + seniorityMonthlySubject + subjectOther,
                NominalGross = baseEffective + seniorityMonthly + grossOther
            };

            agg.DailyBaseWage = insuranceWorkDays > 0 ? (long)(baseEffective / insuranceWorkDays) : 0;
            agg.DailySeniority = insuranceWorkDays > 0 ? (long)(seniorityMonthly / insuranceWorkDays) : 0;
            return agg;
        }

        // تاریخ رویداد (استخدام/ترک) فقط در همان ماه دوره برگردانده می‌شود؛ در سایر ماه‌ها خالی است.
        // ورودی تاریخ شمسی به‌صورت YYYYMMDD (BIGINT) است.
        public static string EventDateForMonth(object? eventDateObj, int periodYear, int periodMonth)
        {
            if (eventDateObj == null) return "";

            long eventDate;
            try { eventDate = Convert.ToInt64(eventDateObj); }
            catch { return ""; }

            if (eventDate <= 0) return "";

            int y = (int)(eventDate / 10000);
            int m = (int)((eventDate / 100) % 100);

            return (y == periodYear && m == periodMonth) ? eventDate.ToString() : "";
        }
    }
}
