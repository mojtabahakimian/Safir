using System.Text;
using System.Text.RegularExpressions;

namespace Safir.Shared.Utility
{
    public static class CL_METHODS
    {
        public static string ToStandardSearchText(this string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            return input
                .RemoveInvisibleChars()
                .FixPersianChars()
                .NormalizeSpaces()
                .ToLowerInvariant();
        }
        public static string FixPersianChars(this string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return str;

            return str.Replace("ﮎ", "ک")
                .Replace("ﮏ", "ک")
                .Replace("ﮐ", "ک")
                .Replace("ﮑ", "ک")
                .Replace("ك", "ک")
                .Replace("ي", "ی")
                .Replace("ھ", "ه")
                .Replace('\u064A', 'ی') // Arabic Yeh
                .Replace('\u0643', 'ک') // Arabic Kaf
                .Replace('\u06C0', 'ه') // Heh with Yeh Above
                .Replace('۰', '0')
                .Replace('۱', '1')
                .Replace('۲', '2')
                .Replace('۳', '3')
                .Replace('۴', '4')
                .Replace('۵', '5')
                .Replace('۶', '6')
                .Replace('۷', '7')
                .Replace('۸', '8')
                .Replace('۹', '9');
        }
        public static string RemoveInvisibleChars(this string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            char[] invisibleChars = new char[]
            {
                '\u200B', // Zero-width space
                '\u200C', // Zero-width non-joiner (نیم‌فاصله)
                '\u200D', // Zero-width joiner
                '\u200E', // Left-to-right mark (LRM)
                '\u200F', // Right-to-left mark (RLM)
                '\u202A', '\u202B', '\u202C', '\u202D', '\u202E', '\u2060', '\uFEFF'
            };

            foreach (char c in invisibleChars)
            {
                // به جای حذف کامل نیم‌فاصله، آن را تبدیل به فاصله عادی می‌کنیم تا کلماتی مثل "آی‌تی" تبدیل به "آی تی" شوند نه "آیتی"
                input = input.Replace(c, ' ');
            }

            input = input
                .Replace('\t', ' ')
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace('\f', ' ')
                .Replace('\v', ' ');

            input = new string(input.Where(ch => !char.IsControl(ch)).ToArray());
            return input.Trim();
        }
        public static string NormalizeSpaces(this string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
        public static string DECODEUN(string cody)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            byte[] RawCoded = Encoding.GetEncoding(1256).GetBytes(cody);// ی 237

            var Parsy = Encoding.GetEncoding(1256);
            for (byte i = 0; i < RawCoded.Count(); i++)
            {
                RawCoded[i] = (byte)(RawCoded[i] + 20);
            }
            var result = Parsy.GetString(RawCoded);
            cody = result;
            return cody;
        }
        public static string DECODEPS(string cody)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            byte[] RawCoded = Encoding.GetEncoding(1256).GetBytes(cody);// ی 237
            var Parsy = Encoding.GetEncoding(1256);
            for (byte i = 0; i < RawCoded.Count(); i++)
            {
                RawCoded[i] = (byte)(RawCoded[i] + 10);
            }

            var result = Parsy.GetString(RawCoded);
            result = result.Substring(3, result.Length - 6);
            cody = result;
            return cody;
        }
        public static string Fixp(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;
            // Replace with your actual Fixp implementation
            return str.Replace("ي", "ی").Replace("ك", "ک");
        }
    }
}
