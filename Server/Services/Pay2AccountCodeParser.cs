using System;

namespace Safir.Server.Services
{
    public class Pay2ParsedAccount
    {
        public int HesK { get; set; }
        public int HesM { get; set; }
        public int HesT { get; set; }
        public int? HesT2 { get; set; }
        public int? HesT3 { get; set; }
        public int? HesT4 { get; set; }
        public string FullCode { get; set; } = "";

        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = "";
    }

    public static class Pay2AccountCodeParser
    {
        public static Pay2ParsedAccount Parse(string? accountCode, string articleDescription = "")
        {
            var result = new Pay2ParsedAccount { IsValid = false };

            if (string.IsNullOrWhiteSpace(accountCode))
            {
                result.ErrorMessage = $"کد حساب نامعتبر است (خالی). شرح آرتیکل: {articleDescription}";
                return result;
            }

            string cleanCode = accountCode.Trim();

            // Protect against empty segments and trailing/leading dashes
            if (cleanCode.Contains("--") || cleanCode.StartsWith("-") || cleanCode.EndsWith("-"))
            {
                result.ErrorMessage = $"فرمت حساب نامعتبر است (حاوی خط‌تیره اضافی). کد: {accountCode} | شرح آرتیکل: {articleDescription}";
                return result;
            }

            var parts = cleanCode.Split('-');

            if (parts.Length < 2)
            {
                result.ErrorMessage = $"حساب باید حداقل دارای سطح کل و معین باشد. کد: {accountCode} | شرح آرتیکل: {articleDescription}";
                return result;
            }

            if (parts.Length > 6)
            {
                result.ErrorMessage = $"حساب نمی‌تواند بیشتر از شش سطح داشته باشد. کد: {accountCode} | شرح آرتیکل: {articleDescription}";
                return result;
            }

            int[] parsedParts = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int num) || num < 0)
                {
                    result.ErrorMessage = $"تمام بخش‌های کد حساب باید اعداد صحیح و مثبت باشند. کد: {accountCode} | شرح آرتیکل: {articleDescription}";
                    return result;
                }
                parsedParts[i] = num;
            }

            result.HesK = parsedParts[0];
            result.HesM = parsedParts[1];

            // HES_T defaults to 0 if not present, as required by DEED_DTL schema constraints
            result.HesT = parts.Length > 2 ? parsedParts[2] : 0;

            result.HesT2 = parts.Length > 3 ? parsedParts[3] : (int?)null;
            result.HesT3 = parts.Length > 4 ? parsedParts[4] : (int?)null;
            result.HesT4 = parts.Length > 5 ? parsedParts[5] : (int?)null;

            result.FullCode = cleanCode;
            result.IsValid = true;
            return result;
        }
    }
}
