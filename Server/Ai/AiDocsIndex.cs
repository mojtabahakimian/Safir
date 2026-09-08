using System.Text;
using System.Text.RegularExpressions;

namespace Safir.Server.Ai
{
    /// <summary>
    /// فهرستِ مستندِ ساختار پایگاه.
    ///
    /// ── این چه چیزی می‌دهد که describe_table نمی‌دهد ──
    /// describe_table نام و نوع ستون را از خودِ SQL Server می‌گیرد و
    /// همیشه تازه است. ولی «MEGHK یعنی چه» را نمی‌گوید. این پایگاه
    /// نام‌های کوتاهِ فینگلیش دارد (MEGH، BES، RADAH، TAGCODE) و بدون
    /// معنیِ فارسی‌شان، مدل ستون را از روی شباهتِ نام حدس می‌زند —
    /// همان اشتباهی که کوئریِ اجراشدنی ولی معناً غلط می‌سازد.
    ///
    /// مستندِ نگین برای هر ستون عنوان فارسی دارد، برای هر جدول کلید
    /// اصلی و فرمِ مرتبط، و برای هر ویو متنِ کاملِ SQL و اینکه کدام
    /// گزارشِ برنامه از آن ساخته شده. ۳۸۳ ویو یعنی خیلی از گزارش‌ها از
    /// قبل درست نوشته شده‌اند؛ پیدا کردنشان از بازنویسی بهتر است.
    ///
    /// ── چرا فایل و نه داخلِ system prompt ──
    /// ۸۶۰ کیلوبایت است. کلِ آن در هیچ پنجره‌ای جا نمی‌شود و اگر هم
    /// جا می‌شد، هزینه‌ی هر پیام را بی‌دلیل چند برابر می‌کرد. اینجا یک
    /// بار خوانده و به بخش‌های جدول‌به‌جدول تقسیم می‌شود، و مدل فقط
    /// همان چند بخشی را که لازم دارد می‌گیرد.
    ///
    /// ── چرا snapshot اینجا اشکالی ندارد ──
    /// برای DDL خام نگرفتمش، چون کهنه می‌شود و describe_table زنده
    /// همان را بهتر می‌دهد. ولی *معنی* کهنه نمی‌شود: اگر ستونی اضافه
    /// شود، مستند آن یکی را ندارد و describe_table دارد؛ این دو
    /// همدیگر را پوشش می‌دهند.
    /// </summary>
    public interface IAiDocsIndex
    {
        /// <summary>بخشِ مستندِ یک جدول یا ویو، یا null.</summary>
        string? Section(string objectName);

        /// <summary>نام‌هایی که متنشان شاملِ عبارت است، همراه یک سطر زمینه.</summary>
        IReadOnlyList<(string Name, string Kind, string Hit)> Search(string term, int max);

        bool Loaded { get; }
    }

    public sealed class AiDocsIndex : IAiDocsIndex
    {
        private readonly Dictionary<string, Entry> _byName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Entry> _all = new();

        public bool Loaded => _all.Count > 0;

        private sealed record Entry(string Name, string Kind, string Module, string Body);

        public AiDocsIndex(IWebHostEnvironment env, ILogger<AiDocsIndex> log)
        {
            var path = Path.Combine(env.ContentRootPath, "Ai", "Docs",
                                    "DATABASE_DOCUMENTATION.md");
            if (!File.Exists(path))
            {
                // نبودِ فایل نباید دستیار را بخواباند؛ فقط این ابزار خاموش
                // می‌شود و بقیه سرِ جایشان هستند.
                log.LogWarning("مستند ساختار پایگاه پیدا نشد: {Path}", path);
                return;
            }

            Parse(File.ReadAllLines(path, Encoding.UTF8));
            log.LogInformation("مستند ساختار پایگاه بارگذاری شد: {Count} جدول و ویو", _all.Count);
        }

        /// <summary>
        /// هر بخش با «#### جدول: `NAME`» یا «#### ویو: `NAME`» شروع می‌شود و
        /// تا بخشِ بعدی ادامه دارد. عنوانِ «## ...» ماژول را مشخص می‌کند
        /// (حسابداری، فروش، انبار …) که خودش برای مدل سرنخِ خوبی است.
        /// </summary>
        private void Parse(string[] lines)
        {
            var head   = new Regex(@"^####\s+(جدول|ویو):\s*`([^`]+)`");
            // «##» ماژول است و «###» زیربخشِ جدول‌ها/ویوها؛ هر دو پایانِ
            // بخشِ جاری‌اند. «####» نباید اینجا بیفتد، برای همین {2,3}.
            var module = new Regex(@"^#{2,3}(?!#)\s+[۰-۹\d.]*\s*(.+)$");

            string  currentModule = "";
            string? name = null, kind = null;
            var     buf  = new StringBuilder();

            void Flush()
            {
                if (name is null) return;
                var e = new Entry(name, kind!, currentModule, buf.ToString().TrimEnd());
                _all.Add(e);
                _byName[name] = e;   // نامِ تکراری: آخری برنده است
                buf.Clear();
            }

            foreach (var line in lines)
            {
                var h = head.Match(line);
                if (h.Success)
                {
                    Flush();
                    kind = h.Groups[1].Value;
                    name = h.Groups[2].Value.Trim();
                    buf.AppendLine(line);
                    continue;
                }

                if (name is null)
                {
                    var m = module.Match(line);
                    if (m.Success) currentModule = m.Groups[1].Value.Trim();
                    continue;
                }

                // عنوانِ ماژولِ بعدی، پایانِ بخشِ جاری هم هست
                var m2 = module.Match(line);
                if (m2.Success)
                {
                    Flush();
                    name = null; kind = null;
                    currentModule = m2.Groups[1].Value.Trim();
                    continue;
                }

                buf.AppendLine(line);
            }

            Flush();
        }

        public string? Section(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName)) return null;
            var key = objectName.Trim().Replace("dbo.", "", StringComparison.OrdinalIgnoreCase);
            if (!_byName.TryGetValue(key, out var e)) return null;

            return string.IsNullOrEmpty(e.Module)
                ? e.Body
                : $"ماژول: {e.Module}\n\n{e.Body}";
        }

        public IReadOnlyList<(string Name, string Kind, string Hit)> Search(string term, int max)
        {
            var hits = new List<(string, string, string)>();
            if (string.IsNullOrWhiteSpace(term)) return hits;

            // دو گذر، چون ترتیبِ فایل الفبایی است و نه مرتبط: اگر یک‌جا
            // ببُریم، جدولی که *نامش* عبارت را دارد ممکن است پشتِ ده‌ها
            // تطبیقِ داخلِ متن بماند و اصلاً به مدل نرسد.
            void Collect(bool namePass)
            {
                foreach (var e in _all)
                {
                    if (hits.Count >= max) return;

                    var inName = e.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
                    if (namePass != inName) continue;

                    // عنوانِ فارسیِ ستون‌ها همان چیزی است که جست‌وجو را
                    // واقعاً مفید می‌کند: کاربر «تخفیف» می‌پرسد نه «TAKHFIF».
                    var idx = e.Body.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                    if (!inName && idx < 0) continue;

                    hits.Add((e.Name, e.Kind, idx < 0 ? FirstLine(e.Body) : LineAt(e.Body, idx)));
                }
            }

            Collect(namePass: true);
            Collect(namePass: false);
            return hits;
        }

        private static string FirstLine(string body)
        {
            var i = body.IndexOf('\n');
            return (i < 0 ? body : body[..i]).Trim();
        }

        private static string LineAt(string body, int idx)
        {
            var s = body.LastIndexOf('\n', Math.Min(idx, body.Length - 1)) + 1;
            var e = body.IndexOf('\n', idx);
            if (e < 0) e = body.Length;
            return body[s..e].Trim();
        }
    }
}
