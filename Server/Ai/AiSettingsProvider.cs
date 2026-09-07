using Microsoft.Extensions.Caching.Memory;
using Safir.Shared.Interfaces;

namespace Safir.Server.Ai
{
    /// <summary>
    /// تنظیمات مؤثرِ سرویس هوش مصنوعی.
    ///
    /// ── ترتیب اولویت ──
    /// ۱) جدول AI_Config (اگر فعال باشد)
    /// ۲) متغیر محیطی برای کلید
    /// ۳) appsettings
    ///
    /// متغیر محیطی عمداً بالاتر از appsettings و پایین‌تر از پایگاه است:
    /// نصبی که نمی‌خواهد کلید داخل پایگاه بنشیند، ستون را خالی می‌گذارد و
    /// همان مسیر قبلی کار می‌کند.
    ///
    /// ── چرا کش ──
    /// این در هر پیامِ چت خوانده می‌شود و رفتِ اضافه به پایگاه برای سطری
    /// که ماهی یک بار عوض می‌شود بی‌دلیل است. سی ثانیه کوتاه است تا
    /// ادمین بعد از ذخیره لازم نباشد منتظر بماند.
    /// </summary>
    public interface IAiSettingsProvider
    {
        Task<AiOptions> GetAsync();
        void Invalidate();
    }

    public sealed class AiSettingsProvider : IAiSettingsProvider
    {
        private const string CacheKey = "ai_config_effective";

        private readonly IDatabaseService _db;
        private readonly IMemoryCache     _cache;
        private readonly AiOptions        _fromAppSettings;

        public AiSettingsProvider(IDatabaseService db, IMemoryCache cache, AiOptions fromAppSettings)
        {
            _db              = db;
            _cache           = cache;
            _fromAppSettings = fromAppSettings;
        }

        public void Invalidate() => _cache.Remove(CacheKey);

        public async Task<AiOptions> GetAsync()
        {
            if (_cache.TryGetValue<AiOptions>(CacheKey, out var cached) && cached is not null)
                return cached;

            var row = await ReadRowAsync();

            var eff = new AiOptions
            {
                Provider       = _fromAppSettings.Provider,
                BaseUrl        = _fromAppSettings.BaseUrl,
                Model          = _fromAppSettings.Model,
                ApiKeyEnv      = _fromAppSettings.ApiKeyEnv,
                TimeoutSeconds = _fromAppSettings.TimeoutSeconds,
                MaxToolLoops   = _fromAppSettings.MaxToolLoops
            };

            if (row is { IsEnabled: true })
            {
                // فقط مقادیرِ پرشده جایگزین می‌شوند. ادمینی که فقط مدل را
                // عوض می‌کند نباید ناخواسته آدرس را خالی کند.
                if (!string.IsNullOrWhiteSpace(row.Provider)) eff.Provider = row.Provider;
                if (!string.IsNullOrWhiteSpace(row.BaseUrl))  eff.BaseUrl  = row.BaseUrl;
                if (!string.IsNullOrWhiteSpace(row.Model))    eff.Model    = row.Model;
                if (row.TimeoutSeconds > 0)                   eff.TimeoutSeconds = row.TimeoutSeconds;
                if (row.MaxToolLoops   > 0)                   eff.MaxToolLoops   = row.MaxToolLoops;

                eff.ApiKey = row.ApiKey;
            }

            // کلیدِ پایگاه اولویت دارد؛ نبودش یعنی سراغ متغیر محیطی برویم.
            if (string.IsNullOrWhiteSpace(eff.ApiKey))
                eff.ApiKey = Environment.GetEnvironmentVariable(eff.ApiKeyEnv);

            _cache.Set(CacheKey, eff, TimeSpan.FromSeconds(30));
            return eff;
        }

        public async Task<AiConfigRow?> ReadRowAsync()
            => await _db.DoGetDataSQLAsyncSingle<AiConfigRow>(@"
                SELECT IsEnabled, Provider, BaseUrl, Model, ApiKey,
                       TimeoutSeconds, MaxToolLoops, UpdatedBy, UpdatedAtUtc
                FROM   dbo.AI_Config WHERE Id = 1");
    }

    /// <summary>
    /// ارائه‌دهنده را با تنظیماتِ *همین لحظه* می‌سازد.
    ///
    /// چرا کارخانه و نه ثبتِ مستقیم در DI: تنظیمات از پایگاه می‌آید و
    /// خواندنش async است، ولی سازنده‌ی DI نمی‌تواند await کند. ضمناً ادمین
    /// می‌تواند وسط کار آدرس یا کلید را عوض کند و درخواست بعدی باید با
    /// مقدار تازه برود، نه با نمونه‌ای که موقع بالا آمدن برنامه ساخته شده.
    /// </summary>
    public interface IAiProviderFactory
    {
        Task<(IAiChatProvider Provider, AiOptions Options)> CreateAsync();
    }

    public sealed class AiProviderFactory : IAiProviderFactory
    {
        private readonly IHttpClientFactory  _httpFactory;
        private readonly IAiSettingsProvider _settings;
        private readonly ILoggerFactory      _logs;

        public AiProviderFactory(
            IHttpClientFactory httpFactory, IAiSettingsProvider settings, ILoggerFactory logs)
        {
            _httpFactory = httpFactory;
            _settings    = settings;
            _logs        = logs;
        }

        public async Task<(IAiChatProvider, AiOptions)> CreateAsync()
        {
            var opt  = await _settings.GetAsync();
            var http = _httpFactory.CreateClient("ai");

            IAiChatProvider provider =
                string.Equals(opt.Provider, "anthropic", StringComparison.OrdinalIgnoreCase)
                    ? new AnthropicProvider(http, opt, _logs.CreateLogger<AnthropicProvider>())
                    : new OpenAiCompatibleProvider(http, opt, _logs.CreateLogger<OpenAiCompatibleProvider>());

            return (provider, opt);
        }
    }

    public sealed class AiConfigRow
    {
        public bool      IsEnabled      { get; set; }
        public string?   Provider       { get; set; }
        public string?   BaseUrl        { get; set; }
        public string?   Model          { get; set; }
        public string?   ApiKey         { get; set; }
        public int       TimeoutSeconds { get; set; }
        public int       MaxToolLoops   { get; set; }
        public string?   UpdatedBy      { get; set; }
        public DateTime? UpdatedAtUtc   { get; set; }
    }
}
