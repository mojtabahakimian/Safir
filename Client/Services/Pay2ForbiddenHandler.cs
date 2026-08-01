using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Safir.Client.Services
{
    /// <summary>
    /// پاسخ ۴۰۳ سرور را به استثنایی با پیامِ فارسیِ خود سرور تبدیل می‌کند.
    ///
    /// چرا لازم است: کنترلرهای حقوق و دستمزد هنگام رد کردن یک عملیات، متن روشنی در
    /// بدنه‌ی پاسخ می‌گذارند (مثلاً «دسترسی لازم برای این عملیات را ندارید. («PAY2_DECREE» / See)»)
    /// ولی GetFromJsonAsync پیش از خواندن بدنه، EnsureSuccessStatusCode را صدا می‌زند و
    /// آن متن دور ریخته می‌شود؛ چیزی که به کاربر می‌رسید این بود:
    /// «Response status code does not indicate success: 403 (Forbidden).»
    ///
    /// اینجا بدنه یک بار خوانده و به‌جای آن پیام انگلیسی جایگزین می‌شود، پس همه‌ی جاهایی
    /// که از قبل ex.Message را در Snackbar نشان می‌دهند بدون تغییر درست می‌شوند.
    ///
    /// دامنه عمداً محدود به مسیرهای api/pay2/ است تا رفتار بقیه‌ی برنامه دست‌نخورده بماند.
    /// </summary>
    public class Pay2ForbiddenHandler : DelegatingHandler
    {
        private const string Fallback = "شما دسترسی لازم برای این عملیات را ندارید.";

        public Pay2ForbiddenHandler() : this(new HttpClientHandler()) { }

        /// <summary>برای آزمون — تا بشود بدون شبکه‌ی واقعی رفتارش را سنجید.</summary>
        public Pay2ForbiddenHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Forbidden || !IsPay2(request))
                return response;

            string? body = null;
            try { body = await response.Content.ReadAsStringAsync(cancellationToken); }
            catch { /* بدنه‌ی ناخوانا نباید جلوی پیام پیش‌فرض را بگیرد */ }

            response.Dispose();

            // نوعِ HttpRequestException نگه داشته می‌شود تا catch(HttpRequestException)های
            // موجود در کدبیس هم‌چنان کار کنند؛ فقط Message خوانا می‌شود.
            throw new HttpRequestException(Describe(body), null, HttpStatusCode.Forbidden);
        }

        private static bool IsPay2(HttpRequestMessage request)
            => request.RequestUri?.AbsolutePath.Contains("/api/pay2/", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>بدنه ممکن است متن ساده باشد یا رشته‌ی JSON («"…"»)؛ هر دو را تمیز می‌کند.</summary>
        private static string Describe(string? body)
        {
            var text = body?.Trim();
            if (string.IsNullOrEmpty(text)) return Fallback;

            if (text.Length > 1 && text[0] == '"' && text[^1] == '"')
            {
                try { text = JsonSerializer.Deserialize<string>(text); }
                catch { /* اگر JSON نبود، همان متن خام می‌ماند */ }
            }

            return string.IsNullOrWhiteSpace(text) ? Fallback : text!;
        }
    }
}
