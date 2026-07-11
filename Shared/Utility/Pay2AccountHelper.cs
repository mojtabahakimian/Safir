using System.Collections.Generic;

namespace Safir.Shared.Utility
{
    public class Pay2ParsedAccount
    {
        public string FullCode { get; set; } = string.Empty;
        public int HesK { get; set; }
        public int HesM { get; set; }
        public int? HesT { get; set; }
        public int? HesT2 { get; set; }
        public int? HesT3 { get; set; }
        public int? HesT4 { get; set; }
        public int LevelCount { get; set; }
    }

    public class Pay2AccountParseResult
    {
        public bool IsValid { get; set; }
        public Pay2ParsedAccount? Account { get; set; }
        public string? ErrorMessage { get; set; }

        public static Pay2AccountParseResult Fail(string message) =>
            new Pay2AccountParseResult { IsValid = false, ErrorMessage = message };

        public static Pay2AccountParseResult Success(Pay2ParsedAccount acc) =>
            new Pay2AccountParseResult { IsValid = true, Account = acc };
    }

    public static class Pay2AccountHelper
    {
        public static Pay2AccountParseResult Parse(string? code, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(code))
                return Pay2AccountParseResult.Fail($"کد حساب برای «{fieldName}» تنظیم نشده یا خالی است.");

            var parts = code.Split('-');
            int len = parts.Length;

            if (len < 2)
                return Pay2AccountParseResult.Fail($"فرمت حساب «{fieldName}» نامعتبر است ({code}). حداقل ۲ سطح با خط‌فاصله الزامی است.");

            if (len > 6)
                return Pay2AccountParseResult.Fail($"فرمت حساب «{fieldName}» نامعتبر است ({code}). سیستم حداکثر ۶ سطح را پشتیبانی می‌کند.");

            var parsedInts = new int[len];
            for (int i = 0; i < len; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]) || !int.TryParse(parts[i], out int val) || val < 0)
                    return Pay2AccountParseResult.Fail($"فرمت حساب «{fieldName}» نامعتبر است ({code}). تمام بخش‌ها باید عدد صحیح مثبت باشند.");

                parsedInts[i] = val;
            }

            var result = new Pay2ParsedAccount
            {
                FullCode = code, // چون ورودی ولید است، نیازی به string.Join مجدد نیست
                LevelCount = len,
                HesK = parsedInts[0],
                HesM = parsedInts[1],
                HesT = len > 2 ? parsedInts[2] : null,
                HesT2 = len > 3 ? parsedInts[3] : null,
                HesT3 = len > 4 ? parsedInts[4] : null,
                HesT4 = len > 5 ? parsedInts[5] : null
            };

            return Pay2AccountParseResult.Success(result);
        }

        public static Pay2AccountParseResult ResolveEmployeeAccount(string? targetBaseCode, string? empAccT, string targetFieldName, string? salaryPayableBase)
        {
            if (string.IsNullOrWhiteSpace(empAccT))
                return Pay2AccountParseResult.Fail($"کد تفصیلی پرسنل (ACC_T) مشخص نیست.");

            var targetBaseResult = Parse(targetBaseCode, targetFieldName);
            if (!targetBaseResult.IsValid) return targetBaseResult;

            var accTResult = Parse(empAccT, "ACC_T پرسنل");
            string finalCode;

            if (!accTResult.IsValid)
            {
                if (int.TryParse(empAccT, out int leaf) && leaf >= 0 && !empAccT.Contains("-"))
                {
                    finalCode = $"{targetBaseResult.Account!.FullCode}-{leaf}";
                }
                else
                {
                    return Pay2AccountParseResult.Fail($"کد تفصیلی پرسنل (ACC_T={empAccT}) فرمت معتبری ندارد.");
                }
            }
            else
            {
                var salaryBaseResult = Parse(salaryPayableBase, "پایه پرداختنی حقوق (SALARY_PAYABLE)");
                if (!salaryBaseResult.IsValid)
                    return Pay2AccountParseResult.Fail("حساب پایه حقوق (SALARY_PAYABLE) برای استخراج تفصیلی پرسنل نامعتبر است.");

                string salaryBaseStr = salaryBaseResult.Account!.FullCode;
                string accTStr = accTResult.Account!.FullCode;

                if (accTStr.StartsWith(salaryBaseStr + "-"))
                {
                    string suffix = accTStr.Substring(salaryBaseStr.Length + 1);
                    finalCode = $"{targetBaseResult.Account.FullCode}-{suffix}";
                }
                else if (accTStr == salaryBaseStr)
                {
                    return Pay2AccountParseResult.Fail($"کد تفصیلی پرسنل ({accTStr}) فاقد شناسه فردی است.");
                }
                else
                {
                    return Pay2AccountParseResult.Fail($"کد تفصیلی پرسنل ({accTStr}) با پایه حساب حقوق ({salaryBaseStr}) مطابقت ندارد.");
                }
            }

            return Parse(finalCode, targetFieldName + " (حساب شخص)");
        }
    }
}