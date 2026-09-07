using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Safir.Server.Ai
{
    // ═══════════════════ قرارداد مشترک ═══════════════════
    //
    // یک شکلِ واحد که هر دو ارائه‌دهنده به آن ترجمه می‌شوند. هدف این است
    // که حلقه‌ی گفتگو (AiConversationService) اصلاً نداند پشتش Ollama است
    // یا سرویس ابری — عوض کردن ارائه‌دهنده باید تغییر تنظیمات باشد، نه
    // تغییر کد.

    public sealed class AiMessage
    {
        /// <summary>system | user | assistant | tool</summary>
        public string Role    { get; set; } = "user";
        public string? Content { get; set; }

        /// <summary>وقتی مدل خواسته ابزاری اجرا شود.</summary>
        public List<AiToolInvocation>? ToolCalls { get; set; }

        /// <summary>وقتی این پیام نتیجه‌ی یک ابزار است.</summary>
        public string? ToolCallId { get; set; }
        public string? ToolName   { get; set; }
    }

    public sealed class AiToolInvocation
    {
        public string Id        { get; set; } = "";
        public string Name      { get; set; } = "";
        public JsonElement Args { get; set; }
    }

    public sealed class AiModelReply
    {
        public string? Text { get; set; }
        public List<AiToolInvocation> ToolCalls { get; set; } = new();
        public string? Error { get; set; }
        public bool Ok => Error is null;
    }

    public interface IAiChatProvider
    {
        /// <summary>نامی که در لاگ و در پیام خطا دیده می‌شود.</summary>
        string Describe { get; }

        Task<AiModelReply> CompleteAsync(
            IReadOnlyList<AiMessage> messages,
            IReadOnlyList<IAiTool> tools,
            CancellationToken ct = default);
    }


    /// <summary>
    /// چسباندن مسیر به آدرس پایه، با تحملِ شکل‌هایی که آدم واقعاً وارد
    /// می‌کند.
    ///
    /// روی همین پروژه هر دو اشتباه رخ داد: یک بار آدرسِ صفحه‌ی وبِ درگاه
    /// («/dashboard/endpoint») که ۳۰۷ به صفحه‌ی ورود می‌داد، و یک بار
    /// ریشه به‌همراه «/v1» که با افزودنِ مسیر می‌شد «/v1/v1/...». هر دو
    /// خطایی می‌دادند که به تنظیمات اشاره نمی‌کرد.
    /// </summary>
    public static class AiUrl
    {
        public static string Combine(string baseUrl, string path)
        {
            var b = (baseUrl ?? "").Trim().TrimEnd('/');

            // «/v1» انتهایی حذف می‌شود چون خودِ مسیرها آن را دارند.
            if (b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                b = b[..^3];

            return b.TrimEnd('/') + path;
        }
    }


    // ═══════════════════ تنظیمات ═══════════════════

    public sealed class AiOptions
    {
        /// <summary>ollama | anthropic</summary>
        public string Provider { get; set; } = "ollama";

        public string BaseUrl  { get; set; } = "http://localhost:11434";
        public string Model    { get; set; } = "qwen2.5:14b";

        /// <summary>
        /// ⚠ نامِ متغیر محیطی، نه خودِ کلید. کلید هرگز داخل appsettings
        /// نوشته نمی‌شود چون آن فایل در گیت است؛ یک بار جا افتادن یعنی
        /// کلید در تاریخچه‌ی مخزن می‌ماند حتی بعد از پاک کردن.
        /// </summary>
        public string ApiKeyEnv { get; set; } = "SAFIR_AI_API_KEY";

        /// <summary>
        /// کلیدِ واقعی — از جدول AI_Config یا از متغیر محیطی. هیچ‌وقت از
        /// appsettings خوانده نمی‌شود و هیچ‌وقت به کلاینت برنمی‌گردد.
        /// </summary>
        public string? ApiKey { get; set; }

        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>سقف رفت‌وبرگشت با ابزار در یک سؤال.</summary>
        public int MaxToolLoops { get; set; } = 6;
    }


    // ═══════════════════ Ollama و هر چیز سازگار با OpenAI ═══════════════════

    /// <summary>
    /// Ollama، LM Studio و بیشتر درگاه‌های واسط همین شکل را می‌پذیرند
    /// (POST /v1/chat/completions با tools). پس یک پیاده‌سازی، هر سه را
    /// پوشش می‌دهد و فقط BaseUrl عوض می‌شود.
    /// </summary>
    public sealed class OpenAiCompatibleProvider : IAiChatProvider
    {
        private readonly HttpClient _http;
        private readonly AiOptions  _opt;
        private readonly ILogger<OpenAiCompatibleProvider> _log;

        public OpenAiCompatibleProvider(
            HttpClient http, AiOptions opt, ILogger<OpenAiCompatibleProvider> log)
        {
            _http = http;
            _opt  = opt;
            _log  = log;

            _http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);

            if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opt.ApiKey);
        }

        public string Describe => $"{_opt.Provider} ({_opt.Model})";

        public async Task<AiModelReply> CompleteAsync(
            IReadOnlyList<AiMessage> messages,
            IReadOnlyList<IAiTool> tools,
            CancellationToken ct = default)
        {
            var body = new
            {
                model  = _opt.Model,
                stream = false,
                messages = messages.Select(ToWire).ToArray(),
                tools = tools.Select(t => new
                {
                    type = "function",
                    function = new
                    {
                        name        = t.Name,
                        description = t.Description + " پارامترها: " + t.Parameters,
                        // شِمای باز: ابزارها خودشان ورودی را اعتبارسنجی
                        // می‌کنند و پارامترِ ناشناخته را نادیده می‌گیرند.
                        // شِمای دقیقِ JSON اینجا سود کمی داشت و هر ابزار
                        // تازه را پرهزینه می‌کرد.
                        parameters = new { type = "object" }
                    }
                }).ToArray()
            };

            try
            {
                // ── تلاش دوباره برای خطای گذرا ──
                // درگاه گاهی ۵۰۳ یا ۴۲۹ می‌دهد چون خودش لحظه‌ای شلوغ است.
                // شکستنِ کل گفتگو به‌خاطر آن، بعد از چند مرحله کارِ
                // انجام‌شده را هم دور می‌ریزد؛ یک بار صبر و تکرار تقریباً
                // همیشه کافی است.
                HttpResponseMessage res;
                string raw;
                var attempt = 0;

                while (true)
                {
                    res = await _http.PostAsJsonAsync(
                        AiUrl.Combine(_opt.BaseUrl, "/v1/chat/completions"), body, ct);

                    raw = await res.Content.ReadAsStringAsync(ct);

                    var transient = (int)res.StatusCode is 429 or 500 or 502 or 503 or 504;

                    if (res.IsSuccessStatusCode || !transient || attempt >= 2) break;

                    attempt++;
                    _log.LogWarning("AI provider {Status}, retry {Attempt}", res.StatusCode, attempt);

                    // مکث فزاینده: اگر سرویس لحظه‌ای پر است، فشار آوردنِ
                    // فوری وضع را بدتر می‌کند.
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }

                if (!res.IsSuccessStatusCode)
                {
                    _log.LogWarning("AI provider {Status}: {Body}", res.StatusCode, raw);
                    return new AiModelReply { Error = FriendlyHttp((int)res.StatusCode) };
                }

                using var doc = JsonDocument.Parse(raw);
                var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

                var reply = new AiModelReply
                {
                    Text = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                         ? c.GetString() : null
                };

                if (msg.TryGetProperty("tool_calls", out var calls) &&
                    calls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var call in calls.EnumerateArray())
                    {
                        var fn = call.GetProperty("function");

                        // arguments یک *رشته‌ی* JSON است، نه شیء — و بعضی
                        // مدل‌ها رشته‌ی خالی یا ناقص می‌فرستند. شکستنِ کل
                        // گفتگو به‌خاطر آن، بدترین رفتار ممکن است.
                        var argsText = fn.TryGetProperty("arguments", out var a)
                                     ? (a.ValueKind == JsonValueKind.String ? a.GetString() : a.GetRawText())
                                     : "{}";

                        reply.ToolCalls.Add(new AiToolInvocation
                        {
                            Id   = call.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Name = fn.GetProperty("name").GetString() ?? "",
                            Args = ParseArgs(argsText)
                        });
                    }
                }

                return reply;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return new AiModelReply
                {
                    Error = $"مدل در {_opt.TimeoutSeconds} ثانیه جواب نداد."
                };
            }
            catch (HttpRequestException ex)
            {
                _log.LogWarning(ex, "AI provider unreachable at {Url}", _opt.BaseUrl);
                return new AiModelReply
                {
                    Error = $"اتصال به سرویس هوش مصنوعی ({_opt.BaseUrl}) برقرار نشد."
                };
            }
        }

        private static object ToWire(AiMessage m)
        {
            if (m.Role == "tool")
                return new { role = "tool", tool_call_id = m.ToolCallId ?? "", content = m.Content ?? "" };

            if (m.ToolCalls is { Count: > 0 })
                return new
                {
                    role = m.Role,
                    content = m.Content ?? "",
                    tool_calls = m.ToolCalls.Select(t => new
                    {
                        id = t.Id,
                        type = "function",
                        function = new { name = t.Name, arguments = t.Args.GetRawText() }
                    }).ToArray()
                };

            return new { role = m.Role, content = m.Content ?? "" };
        }

        internal static JsonElement ParseArgs(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) text = "{}";
            try   { return JsonDocument.Parse(text).RootElement.Clone(); }
            catch { return JsonDocument.Parse("{}").RootElement.Clone(); }
        }

        internal static string FriendlyHttp(int status) => status switch
        {
            401 or 403 => "کلید سرویس هوش مصنوعی پذیرفته نشد.",
            404        => "مدل یا آدرس سرویس پیدا نشد.",
            429        => "سقف درخواست سرویس هوش مصنوعی پر شده است.",
            // بعد از سه تلاش هنوز ۵۰۳ یعنی مشکل از سمت سرویس است، نه
            // چیزی که کاربر بتواند درستش کند — پس همین را بگوییم.
            502 or 503 or 504 => "سرویس هوش مصنوعی موقتاً در دسترس نیست. چند لحظه بعد دوباره بپرسید.",
            _          => $"سرویس هوش مصنوعی خطا داد (کد {status})."
        };

    }


    // ═══════════════════ Anthropic ═══════════════════

    /// <summary>
    /// Messages API شکل متفاوتی دارد: system جدا از پیام‌هاست، و نتیجه‌ی
    /// ابزار به‌جای نقشِ «tool»، یک بلوکِ tool_result داخل پیامِ user
    /// می‌نشیند. برای همین ترجمه‌ی جدا لازم است و نمی‌شد همان کلاسِ بالا
    /// را با آدرس دیگری استفاده کرد.
    /// </summary>
    public sealed class AnthropicProvider : IAiChatProvider
    {
        private readonly HttpClient _http;
        private readonly AiOptions  _opt;
        private readonly ILogger<AnthropicProvider> _log;

        public AnthropicProvider(HttpClient http, AiOptions opt, ILogger<AnthropicProvider> log)
        {
            _http = http;
            _opt  = opt;
            _log  = log;

            _http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);

            if (!string.IsNullOrWhiteSpace(opt.ApiKey))
                _http.DefaultRequestHeaders.Add("x-api-key", opt.ApiKey);

            _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        public string Describe => $"anthropic ({_opt.Model})";

        public async Task<AiModelReply> CompleteAsync(
            IReadOnlyList<AiMessage> messages,
            IReadOnlyList<IAiTool> tools,
            CancellationToken ct = default)
        {
            var system = string.Join("\n\n",
                messages.Where(m => m.Role == "system").Select(m => m.Content));

            var wire = new List<object>();

            foreach (var m in messages.Where(m => m.Role != "system"))
            {
                if (m.Role == "tool")
                {
                    wire.Add(new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "tool_result", tool_use_id = m.ToolCallId ?? "",
                                  content = m.Content ?? "" }
                        }
                    });
                    continue;
                }

                if (m.ToolCalls is { Count: > 0 })
                {
                    var blocks = new List<object>();
                    if (!string.IsNullOrWhiteSpace(m.Content))
                        blocks.Add(new { type = "text", text = m.Content });

                    foreach (var t in m.ToolCalls)
                        blocks.Add(new { type = "tool_use", id = t.Id, name = t.Name,
                                         input = JsonSerializer.Deserialize<object>(t.Args.GetRawText()) });

                    wire.Add(new { role = "assistant", content = blocks.ToArray() });
                    continue;
                }

                wire.Add(new { role = m.Role, content = m.Content ?? "" });
            }

            var body = new
            {
                model      = _opt.Model,
                max_tokens = 4096,
                system,
                messages   = wire.ToArray(),
                tools = tools.Select(t => new
                {
                    name         = t.Name,
                    description  = t.Description + " پارامترها: " + t.Parameters,
                    input_schema = new { type = "object" }
                }).ToArray()
            };

            try
            {
                var res = await _http.PostAsJsonAsync(
                    AiUrl.Combine(_opt.BaseUrl, "/v1/messages"), body, ct);

                var raw = await res.Content.ReadAsStringAsync(ct);

                if (!res.IsSuccessStatusCode)
                {
                    _log.LogWarning("AI provider {Status}: {Body}", res.StatusCode, raw);
                    return new AiModelReply
                    {
                        Error = OpenAiCompatibleProvider.FriendlyHttp((int)res.StatusCode)
                    };
                }

                using var doc = JsonDocument.Parse(raw);
                var reply = new AiModelReply();
                var text  = new StringBuilder();

                foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
                {
                    var type = block.GetProperty("type").GetString();

                    if (type == "text")
                        text.Append(block.GetProperty("text").GetString());

                    else if (type == "tool_use")
                        reply.ToolCalls.Add(new AiToolInvocation
                        {
                            Id   = block.GetProperty("id").GetString() ?? "",
                            Name = block.GetProperty("name").GetString() ?? "",
                            Args = block.GetProperty("input").Clone()
                        });
                }

                reply.Text = text.Length > 0 ? text.ToString() : null;
                return reply;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return new AiModelReply { Error = $"مدل در {_opt.TimeoutSeconds} ثانیه جواب نداد." };
            }
            catch (HttpRequestException ex)
            {
                _log.LogWarning(ex, "AI provider unreachable at {Url}", _opt.BaseUrl);
                return new AiModelReply
                {
                    Error = $"اتصال به سرویس هوش مصنوعی ({_opt.BaseUrl}) برقرار نشد."
                };
            }
        }
    }
}
