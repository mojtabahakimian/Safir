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
    /// می‌ماند، به تب دیگر نمی‌رسد) و localStorage فقط «آخرین ورود» است برای شروع تب‌های جدید.
    ///
    /// تبی که با «ورود به شرکت دیگر در تب جدید» باز شود (پرچم safir.freshTab که index.html
    /// می‌گذارد) توکن را از آخرین ورود برنمی‌دارد؛ فقط تنظیم دیتابیس برای پر کردن فرم ورود.
    /// </summary>
    public class TabSession
    {
        public const string AuthTokenKey = "authToken";
        public const string DbSettingsKey = "dbConnectionSettings";
        private const string FreshTabFlag = "safir.freshTab";

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        private readonly IJSRuntime _js;
        private readonly ILocalStorageService _local;

        public TabSession(IJSRuntime js, ILocalStorageService local)
        {
            _js = js;
            _local = local;
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var raw = await _js.InvokeAsync<string?>("sessionStorage.getItem", key);
            if (raw != null)
                return Deserialize<T>(raw);

            if (key == AuthTokenKey && await IsFreshTabAsync())
                return default;

            // شروع تب جدید از آخرین ورود؛ از این به بعد این تب مقدار خودش را دارد
            var last = await _local.GetItemAsync<T>(key);
            if (last != null)
                await _js.InvokeVoidAsync("sessionStorage.setItem", key, JsonSerializer.Serialize(last));
            return last;
        }

        public async Task SetAsync<T>(string key, T value)
        {
            await _js.InvokeVoidAsync("sessionStorage.setItem", key, JsonSerializer.Serialize(value));
            await _local.SetItemAsync(key, value);
        }

        /// <summary>
        /// از این تب پاک می‌کند. «آخرین ورود» فقط وقتی پاک می‌شود که مال همین تب باشد، تا خروج
        /// از یزدسپار، شروعِ تب‌های جدیدِ پودر را خراب نکند.
        /// </summary>
        public async Task RemoveAsync<T>(string key)
        {
            var raw = await _js.InvokeAsync<string?>("sessionStorage.getItem", key);
            await _js.InvokeVoidAsync("sessionStorage.removeItem", key);
            if (raw == null) return;

            var last = await _local.GetItemAsync<T>(key);
            if (last != null && JsonSerializer.Serialize(last) == JsonSerializer.Serialize(Deserialize<T>(raw)))
                await _local.RemoveItemAsync(key);
        }

        private async Task<bool> IsFreshTabAsync() =>
            await _js.InvokeAsync<string?>("sessionStorage.getItem", FreshTabFlag) == "1";

        private static T? Deserialize<T>(string raw)
        {
            try { return JsonSerializer.Deserialize<T>(raw, Json); }
            catch (JsonException) { return default; }
        }
    }
}
