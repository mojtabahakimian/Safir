using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Safir.Client.Services
{
    /// <summary>
    /// سرور درخواستی را که دیتابیس هدرش با دیتابیسِ توکن ورود نخواند با ۴۰۱ و هدر
    /// X-Safir-Db-Mismatch رد می‌کند (مثلاً تبی که تنظیم دیتابیسش جای دیگری عوض شده).
    /// به‌جای اینکه هر صفحه خطای نامفهوم نشان بدهد، MainLayout با این رویداد کاربر را به
    /// صفحه‌ی ورود می‌برد.
    /// </summary>
    public class DbMismatchHandler : DelegatingHandler
    {
        public static event Action? Mismatch;

        public DbMismatchHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && response.Headers.Contains("X-Safir-Db-Mismatch"))
                Mismatch?.Invoke();
            return response;
        }
    }
}
