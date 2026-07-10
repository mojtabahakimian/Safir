using System.Globalization;

namespace Safir.Server.Services;

public sealed class Pay2ResolvedAccount
{
    public int HesK { get; init; }
    public int HesM { get; init; }
    public int HesT { get; init; }
    public int? HesT2 { get; init; }
    public int? HesT3 { get; init; }
    public int? HesT4 { get; init; }
    public string FullCode { get; init; } = string.Empty;
}

public static class Pay2AccountCodeParser
{
    private static readonly char[] DashChars = ['-', '‐', '–', '—', '−'];

    public static Pay2ResolvedAccount Parse(string? accountCode, string? articleDescription = null)
    {
        string label = string.IsNullOrWhiteSpace(articleDescription) ? "آرتیکل سند" : articleDescription.Trim();
        string normalized = Normalize(accountCode);

        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"کد حساب برای {label} خالی است.");

        if (normalized.StartsWith('-') || normalized.EndsWith('-') || normalized.Contains("--", StringComparison.Ordinal))
            throw new InvalidOperationException($"فرمت کد حساب '{accountCode}' برای {label} نامعتبر است: سطح خالی مجاز نیست.");

        string[] parts = normalized.Split('-');
        if (parts.Length < 2 || parts.Length > 6)
            throw new InvalidOperationException($"کد حساب '{accountCode}' برای {label} باید بین دو تا شش سطح داشته باشد.");

        var numbers = new List<int>(parts.Length);
        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                throw new InvalidOperationException($"فرمت کد حساب '{accountCode}' برای {label} نامعتبر است: سطح خالی مجاز نیست.");

            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                throw new InvalidOperationException($"کد حساب '{accountCode}' برای {label} نامعتبر است: همه سطوح باید عددی باشند.");

            numbers.Add(value);
        }

        return new Pay2ResolvedAccount
        {
            HesK = numbers[0],
            HesM = numbers[1],
            HesT = numbers.Count > 2 ? numbers[2] : 0,
            HesT2 = numbers.Count > 3 ? numbers[3] : null,
            HesT3 = numbers.Count > 4 ? numbers[4] : null,
            HesT4 = numbers.Count > 5 ? numbers[5] : null,
            FullCode = string.Join('-', numbers)
        };
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var chars = new List<char>(value.Length);
        foreach (char ch in value.Trim())
        {
            if (ch >= '0' && ch <= '9') chars.Add(ch);
            else if (ch >= '۰' && ch <= '۹') chars.Add((char)('0' + (ch - '۰')));
            else if (ch >= '٠' && ch <= '٩') chars.Add((char)('0' + (ch - '٠')));
            else if (DashChars.Contains(ch)) chars.Add('-');
            else if (!char.IsWhiteSpace(ch)) chars.Add(ch);
        }

        return new string(chars.ToArray());
    }
}
