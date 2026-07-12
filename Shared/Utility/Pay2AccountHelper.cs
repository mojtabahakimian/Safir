using System;

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

            ReadOnlySpan<char> span = code.AsSpan();
            Span<Range> ranges = stackalloc Range[10];

            int count = span.Split(ranges, '-');

            if (count < 2)
                return Pay2AccountParseResult.Fail($"فرمت حساب «{fieldName}» نامعتبر است ({code}). حداقل ۲ سطح با خط‌فاصله الزامی است.");

            if (count > 6)
                return Pay2AccountParseResult.Fail($"فرمت حساب «{fieldName}» نامعتبر است ({code}). سیستم حداکثر ۶ سطح را پشتیبانی می‌کند.");

            Span<int> parsedInts = stackalloc int[count];
            for (int i = 0; i < count; i++)
            {
                var segment = span[ranges[i]];
                if (segment.IsWhiteSpace() || !int.TryParse(segment, out int val) || val < 0)
                    return Pay2AccountParseResult.Fail($"فرمت حساب «{fieldName}» نامعتبر است ({code}). تمام بخش‌ها باید عدد صحیح مثبت باشند.");

                parsedInts[i] = val;
            }

            var result = new Pay2ParsedAccount
            {
                FullCode = code,
                LevelCount = count,
                HesK = parsedInts[0],
                HesM = parsedInts[1],
                HesT = count > 2 ? parsedInts[2] : null,
                HesT2 = count > 3 ? parsedInts[3] : null,
                HesT3 = count > 4 ? parsedInts[4] : null,
                HesT4 = count > 5 ? parsedInts[5] : null
            };

            return Pay2AccountParseResult.Success(result);
        }
    }
}