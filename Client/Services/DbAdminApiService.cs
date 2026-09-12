using Safir.Shared.Models.DbAdmin;
using System.Net.Http.Json;

namespace Safir.Client.Services
{
    /// <summary>دسترسی کلاینت به صفحه‌ی به‌روزرسانی دیتابیس.</summary>
    public class DbAdminApiService
    {
        private readonly HttpClient _http;

        /// <summary>
        /// کلاینتِ جداگانه با مهلتِ بلند. مهاجرت روی یک دیتابیس واقعی
        /// می‌تواند چند دقیقه طول بکشد و Timeout پیش‌فرضِ ۱۰۰ ثانیه وسطش
        /// قطع می‌کرد — دقیقاً همان چیزی که هرگز نباید نیمه‌کاره رها شود.
        /// Timeout روی خودِ HttpClient است نه روی درخواست، پس کلاینت دوم
        /// تنها راه بود (همان الگوی AiApiService).
        /// </summary>
        private readonly HttpClient _slow;

        private const string Base = "api/dbadmin";

        public DbAdminApiService(HttpClient http)
        {
            _http = http;
            _slow = new HttpClient
            {
                BaseAddress = http.BaseAddress,
                Timeout     = TimeSpan.FromMinutes(30)
            };
        }

        /// <summary>
        /// همه‌ی هدرهای کلاینت مشترک کپی می‌شوند، نه فقط توکن: سرور
        /// دیتابیس را از X-DB-Connection انتخاب می‌کند و بدون آن هدر،
        /// مهاجرت روی DefaultConnection می‌رفت — یعنی روی دیتابیسی که
        /// کاربر اصلاً انتخابش نکرده بود.
        /// </summary>
        private HttpClient Slow()
        {
            _slow.DefaultRequestHeaders.Clear();
            foreach (var h in _http.DefaultRequestHeaders)
                _slow.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
            return _slow;
        }

        public async Task<(DbUpgradeStatus? Status, string? Error)> GetStatusAsync()
        {
            try
            {
                var res = await _http.GetAsync($"{Base}/status");
                if (res.IsSuccessStatusCode)
                    return (await res.Content.ReadFromJsonAsync<DbUpgradeStatus>(), null);

                return (null, await DescribeAsync(res));
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        public async Task<(DbUpgradeResult? Result, string? Error)> RunAsync(DbUpgradeRequest req)
        {
            try
            {
                var res = await Slow().PostAsJsonAsync($"{Base}/upgrade", req);
                if (res.IsSuccessStatusCode)
                    return (await res.Content.ReadFromJsonAsync<DbUpgradeResult>(), null);

                return (null, await DescribeAsync(res));
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>
        /// بدنه‌ی خالی را با کدِ وضعیت جبران می‌کند. بدون این، کاربر فقط
        /// «خطا» می‌دید — همان چیزی که در بازسازی اسناد گروهی پیش آمد.
        /// </summary>
        private static async Task<string> DescribeAsync(HttpResponseMessage res)
        {
            var body = string.Empty;
            try { body = (await res.Content.ReadAsStringAsync()).Trim(); } catch { }

            if (!string.IsNullOrWhiteSpace(body)) return body;

            return (int)res.StatusCode switch
            {
                401 => "احراز هویت نشده‌اید — دوباره وارد شوید.",
                403 => "دسترسی ندارید (نیازمند مجوز مدیریت دسترسی‌ها).",
                404 => "این قابلیت روی سرور موجود نیست — نسخه‌ی سرور قدیمی است.",
                500 => "خطای داخلی سرور.",
                _   => $"خطای {(int)res.StatusCode}."
            };
        }
    }
}
