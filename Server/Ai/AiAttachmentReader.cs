using ClosedXML.Excel;
using System.Text;

namespace Safir.Server.Ai
{
    /// <summary>
    /// تبدیل فایل پیوست به متنی که بشود به مدل داد.
    ///
    /// ── چرا سمت سرور و چرا متن ──
    /// فایل خام به مدل فرستاده نمی‌شود. اکسل و CSV اینجا به جدولِ متنی
    /// تبدیل می‌شوند تا هم حجم کنترل شود و هم مدلی که فایل نمی‌فهمد باز
    /// کار کند. هیچ فایلی روی دیسک ذخیره نمی‌شود؛ فقط همان یک سؤال از آن
    /// استفاده می‌کند.
    ///
    /// ── سقف‌ها ──
    /// حجم فایل و طول متنِ استخراج‌شده هر دو محدودند. یک اکسلِ ۵۰ هزار
    /// سطری بدون سقف، هم درخواست را می‌ترکاند و هم هزینه‌ی توکن را بی‌دلیل
    /// بالا می‌برد — و مدل هم آن‌قدر متن را درست نمی‌خواند.
    /// </summary>
    public static class AiAttachmentReader
    {
        public const int MaxFileBytes = 5 * 1024 * 1024;   // ۵ مگابایت
        public const int MaxTextChars = 20_000;

        private static readonly string[] TextExtensions =
            { ".txt", ".csv", ".md", ".json", ".xml", ".log", ".sql" };

        private static readonly string[] ExcelExtensions = { ".xlsx", ".xlsm" };

        public static bool IsSupported(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return TextExtensions.Contains(ext) || ExcelExtensions.Contains(ext);
        }

        public static string SupportedList =>
            string.Join("، ", TextExtensions.Concat(ExcelExtensions));

        /// <summary>
        /// متنِ فایل، به‌همراه یادداشتِ بریده‌شدن اگر لازم باشد.
        ///
        /// بریده‌شدن صریح گفته می‌شود، وگرنه مدل بخشِ اول را کلِ فایل فرض
        /// می‌کند و جمع می‌زند — همان اشتباهی که برای نتیجه‌ی ابزارها هم
        /// جلویش را گرفتیم.
        /// </summary>
        public static string Read(Stream stream, string fileName)
        {
            var ext  = Path.GetExtension(fileName).ToLowerInvariant();
            var text = ExcelExtensions.Contains(ext) ? ReadExcel(stream) : ReadText(stream);

            if (text.Length <= MaxTextChars) return text;

            return text[..MaxTextChars] +
                   $"\n\n⚠ فایل بلندتر از این بود و فقط {MaxTextChars:N0} نویسه‌ی " +
                   "اولش خوانده شد؛ محتوای کامل نیست.";
        }

        private static string ReadText(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// هر شیت به‌صورت جدولِ متنی با جداکننده‌ی «|». سلولِ خالی هم
        /// جای خودش را نگه می‌دارد تا ستون‌ها به هم نریزند.
        /// </summary>
        private static string ReadExcel(Stream stream)
        {
            using var wb = new XLWorkbook(stream);
            var sb = new StringBuilder();

            foreach (var ws in wb.Worksheets)
            {
                var used = ws.RangeUsed();
                if (used is null) continue;

                sb.AppendLine($"── شیت: {ws.Name} ──");

                foreach (var row in used.Rows())
                {
                    var cells = row.Cells().Select(c => c.GetFormattedString().Replace("|", "/"));
                    sb.AppendLine(string.Join(" | ", cells));

                    // بریدن همین‌جا، نه بعد از ساختنِ کلِ رشته: یک فایل
                    // بزرگ وگرنه قبل از رسیدن به سقف، حافظه را پر می‌کند.
                    if (sb.Length > MaxTextChars) return sb.ToString();
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
