using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Safir.Server.Ai
{
    /// <summary>
    /// نام‌ها را پیش از رفتن به سرویسِ مدل با شناسه (N-0001) عوض می‌کند و پیش از
    /// نمایش جواب برمی‌گرداند.
    ///
    /// ── چرا ──
    /// مدل برای جواب دادن به نام مشتری نیازی ندارد؛ «بدهکارترین مشتری N-0003 با
    /// ۴۱۷ میلیارد» همان‌قدر مفید است و Safir قبل از نمایش نام واقعی را می‌گذارد.
    /// پس سرویس خارجی نامِ مشتری‌ها و کالاها را نمی‌بیند؛ فقط عدد و شناسه.
    ///
    /// ── چه چیزی پنهان می‌شود ──
    /// مقدارِ هر فیلدی در خروجی ابزار که اسمش به «name» ختم می‌شود (Name، NAME،
    /// CUSTNAME، ANBNAME…) یا TAFZIL/MOIN است. کد کالا و شماره‌ی حساب پنهان
    /// نمی‌شوند — عددند و بدون جدولِ Safir معنایی ندارند.
    ///
    /// ── محدودیت ──
    /// متنی که کاربر خودش تایپ می‌کند (مثلاً نام کالا در سؤال) همان‌طور به مدل
    /// می‌رود؛ این لایه فقط داده‌ای را می‌پوشاند که از پایگاه بیرون می‌آید.
    /// </summary>
    public sealed class AiNameMasker
    {
        private readonly Dictionary<string, string> _toToken = new();
        private readonly Dictionary<string, string> _toReal  = new();
        private readonly object _lock = new();

        // شناسه ممکن است با ارقام فارسی برگردد (مدل گاهی ارقام را فارسی می‌کند)
        private static readonly Regex TokenRx = new(@"N-([0-9۰-۹]{4,})", RegexOptions.Compiled);

        public int Count { get { lock (_lock) return _toReal.Count; } }

        public static bool IsNameKey(string key) =>
            key.EndsWith("name", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("TAFZIL", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("MOIN", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("NAM", StringComparison.OrdinalIgnoreCase);

        public string Token(string real)
        {
            lock (_lock)
            {
                if (_toToken.TryGetValue(real, out var t)) return t;
                t = $"N-{_toReal.Count + 1:0000}";
                _toToken[real] = t;
                _toReal[t]     = real;
                return t;
            }
        }

        /// <summary>JSON خروجیِ ابزار؛ مقدارِ فیلدهای نام‌دار با شناسه عوض می‌شود.</summary>
        public string MaskJson(string json)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (JsonException) { return json; }
            if (root is null) return json;

            Walk(root);
            return root.ToJsonString();
        }

        private void Walk(JsonNode node)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var key in o.Select(p => p.Key).ToList())
                    {
                        var v = o[key];
                        if (v is JsonValue jv && jv.TryGetValue<string>(out var s) &&
                            IsNameKey(key) && !string.IsNullOrWhiteSpace(s))
                            o[key] = Token(s.Trim());
                        else if (v is not null)
                            Walk(v);
                    }
                    break;
                case JsonArray a:
                    foreach (var item in a)
                        if (item is not null) Walk(item);
                    break;
            }
        }

        /// <summary>شناسه‌ها در متن جواب (یا در پارامترِ ابزار) به نام واقعی برمی‌گردند.</summary>
        public string Unmask(string text) => Unmask(text, jsonEscape: false);

        private string Unmask(string text, bool jsonEscape)
        {
            if (string.IsNullOrEmpty(text)) return text;
            lock (_lock)
            {
                return TokenRx.Replace(text, m =>
                {
                    var digits = new string(m.Groups[1].Value.Select(c =>
                        c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰')) : c).ToArray());
                    if (!_toReal.TryGetValue($"N-{digits}", out var real)) return m.Value;
                    return jsonEscape ? JsonEncodedText.Encode(real).ToString() : real;
                });
            }
        }

        /// <summary>پارامترهای ابزار: اگر مدل شناسه فرستاد، ابزار نام واقعی را بگیرد.</summary>
        public JsonElement UnmaskArgs(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object) return args;
            var raw = args.GetRawText();
            // نامِ واقعی داخل رشته‌ی JSON می‌نشیند، پس escape می‌شود (ممکن است " داشته باشد)
            var un  = Unmask(raw, jsonEscape: true);
            if (un == raw) return args;
            try { return JsonDocument.Parse(un).RootElement.Clone(); }
            catch (JsonException) { return args; }
        }

        /// <summary>
        /// نام‌هایی که قبلاً در همین گفتگو دیده شده‌اند، در متن (تاریخچه) هم شناسه شوند —
        /// وگرنه جوابِ نوبتِ قبل با نام واقعی دوباره به مدل می‌رفت.
        /// </summary>
        public string MaskKnown(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            lock (_lock)
            {
                foreach (var (real, token) in _toToken.OrderByDescending(p => p.Key.Length))
                    if (real.Length >= 3) text = text.Replace(real, token);
                return text;
            }
        }
    }
}
