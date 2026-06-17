using System.Text;
using System.Text.RegularExpressions;

namespace Safir.Shared.Utility
{
    public static class CL_METHODS
    {
        /// <summary>
        /// متد جامع برای تمیز کردن متن جهت جستجوی دقیق در دیتابیس
        /// </summary>
        public static string ToStandardSearchText(this string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // ترتیب اصولی: ابتدا کاراکترهای عجیب حذف شوند، سپس حروف فارسی اصلاح شوند،
            // سپس فاصله‌های اضافه پاک شوند و در نهایت متن یکدست شود.
            return input
                .RemoveInvisibleChars()
                .FixPersianChars()
                .NormalizeSpaces()
                .ToLowerInvariant();
        }
        public static string FixPersianChars(this string? str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return string.Empty;

            // بهینه‌سازی: تبدیل مستقیم به یک استرینگ بیلدِر برای جلوگیری از تخصیص اضافی حافظه (String Allocation)
            // در حالت عادی، هر .Replace یک کپی جدید از String در مموری می‌سازد.
            // استفاده از StringBuilder برای بیش از ۵ جایگزینی، پرفورمنس را به شدت بالا می‌برد.
            var sb = new StringBuilder(str);

            sb.Replace("ﮎ", "ک")
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

            return sb.ToString();
        }
        public static string RemoveInvisibleChars(this string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var sb = new StringBuilder(input);

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
                // جایگزینی کاراکترهای نامرئی با یک فاصله ساده
                sb.Replace(c, ' ');
            }

            // جایگزینی کاراکترهای قالب‌بندی با فاصله
            sb.Replace('\t', ' ')
              .Replace('\n', ' ')
              .Replace('\r', ' ')
              .Replace('\f', ' ')
              .Replace('\v', ' ');

            // فیلتر کردن نهایی با LINQ (حذف کاراکترهای کنترلی باقی‌مانده)
            string cleanStr = new string(sb.ToString().Where(ch => !char.IsControl(ch)).ToArray());

            return cleanStr.Trim();
        }
        public static string NormalizeSpaces(this string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

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
