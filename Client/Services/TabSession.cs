using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.JSInterop;

namespace Safir.Client.Services
{
    /// <summary>
    /// نشستِ هر تب: توکن ورود و تنظیم دیتابیس.
    ///
    /// کاربر می‌خواهد یزدسپار و پودر (یا دو سال مالی) را هم‌زمان در دو تب باز کند، مثل دو EXE.
    /// هر تب برنامه‌ی جدای خودش را در حافظه دارد، ولی localStorage بین تب‌ها مشترک است: ورود
    /// در تب دوم، توکن و دیتابیس تب اول را عوض می‌کرد و تب اول با اولین رفرش بی‌صدا به شرکت
    /// دیگر وصل می‌شد. پس هر تب مقدار خودش را در sessionStorage همان تب نگه می‌دارد (با رفرش
    /// می‌ماند، به تب دیگر نمی‌رسد).
    ///
    /// localStorage فقط «آخرین ورود» است: توکن و دیتابیسِ آخرین ورود موفق، همیشه با هم. تبِ واردشده‌ای
    /// که کاربر در آن کلیک یا تایپ می‌کند یا به آن برمی‌گردد هم خودش را آخرین ورود می‌کند (index.html)،
    /// تا Ctrl+کلیک روی منو یا باز کردن دوباره‌ی مرورگر، شرکتِ همان تب را باز کند.
    /// تب فقط یک بار، در اولین بالا آمدنش، هر دو را با هم از آن برمی‌دارد (پرچم safir.tabInit)؛
    /// بعد از آن هرگز. پس خروج از یک تب و رفرش، توکنِ تب دیگری را جایگزین نمی‌کند، و تب نمی‌تواند
    /// توکن یک ورود و دیتابیس ورود دیگری داشته باشد.
    ///
    /// تبی که با «ورود به شرکت دیگر در تب جدید» باز شود (پرچم safir.freshTab که index.html
    /// می‌گذارد) توکن برنمی‌دارد؛ فقط تنظیم دیتابیس برای پر کردن فرم ورود.
    /// </summary>
    public class TabSession
    {
        public const string AuthTokenKey = "authToken";
        public const string DbSettingsKey = "dbConnectionSettings";
        private const string FreshTabFlag = "safir.freshTab";
        private const string InitFlag = "safir.tabInit";

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        private readonly IJSRuntime _js;
        private readonly ILocalStorageService _local;
        private bool _initialized;

        public TabSession(IJSRuntime js, ILocalStorageService local)
        {
            _js = js;
            _local = local;
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            await EnsureInitializedAsync();
            var raw = await SessionGetAsync(key);
            return raw == null ? default : Deserialize<T>(raw);
        }

        /// <summary>فقط برای همین تب؛ «آخرین ورود» را عوض نمی‌کند.</summary>
        public async Task SetAsync<T>(string key, T value)
        {
            await EnsureInitializedAsync();
            await _js.InvokeVoidAsync("sessionStorage.setItem", key, JsonSerializer.Serialize(value));
        }

        /// <summary>
        /// بعد از ورود موفق: توکن و دیتابیسِ همین تب، با هم «آخرین ورود» می‌شوند (برای تب جدید یا
        /// باز کردن دوباره‌ی مرورگر). دیتابیسی که این تب ندارد (پیش‌فرض سرور) از آخرین ورود پاک می‌شود.
        /// </summary>
        public async Task SaveAsLastLoginAsync()
        {
            foreach (var key in new[] { AuthTokenKey, DbSettingsKey })
            {
                var raw = await SessionGetAsync(key);
                if (raw != null)
                    await _local.SetItemAsStringAsync(key, raw);
                else
                    await _local.RemoveItemAsync(key);
            }
        }

        /// <summary>
        /// از این تب پاک می‌کند. «آخرین ورود» فقط وقتی پاک می‌شود که مال همین تب باشد، تا خروج
        /// از یزدسپار، شروعِ تب‌های جدیدِ پودر را خراب نکند.
        /// </summary>
        public async Task RemoveAsync<T>(string key)
        {
            await EnsureInitializedAsync();
            var raw = await SessionGetAsync(key);
            await _js.InvokeVoidAsync("sessionStorage.removeItem", key);
            if (raw == null) return;

            var last = await _local.GetItemAsync<T>(key);
            if (last != null && JsonSerializer.Serialize(last) == JsonSerializer.Serialize(Deserialize<T>(raw)))
                await _local.RemoveItemAsync(key);
        }

        /// <summary>
        /// اولین بالا آمدن تب: توکن و دیتابیس را با هم از «آخرین ورود» برمی‌دارد. رفرش، تب
        /// تکثیرشده یا تب «شرکت دیگر» پرچم را از قبل دارند (مرورگر sessionStorage را کپی می‌کند)
        /// و چیزی برنمی‌دارند.
        /// </summary>
        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            _initialized = true;

            if (await SessionGetAsync(InitFlag) != null) return;
            await _js.InvokeVoidAsync("sessionStorage.setItem", InitFlag, "1");

            bool fresh = await SessionGetAsync(FreshTabFlag) == "1";
            foreach (var key in new[] { DbSettingsKey, AuthTokenKey })
            {
                if (key == AuthTokenKey && fresh) continue;
                if (await SessionGetAsync(key) != null) continue;

                var last = await _local.GetItemAsStringAsync(key);
                if (last != null)
                    await _js.InvokeVoidAsync("sessionStorage.setItem", key, last);
            }
        }

        private ValueTask<string?> SessionGetAsync(string key) =>
            _js.InvokeAsync<string?>("sessionStorage.getItem", key);

        private static T? Deserialize<T>(string raw)
        {
            try { return JsonSerializer.Deserialize<T>(raw, Json); }
            catch (JsonException) { return default; }
        }
    }
}
