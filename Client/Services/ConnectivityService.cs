// Client/Services/ConnectivityService.cs
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading; // برای CancellationTokenSource

namespace Safir.Client.Services
{
    public class ServerHealthResponse // بدون تغییر
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public string? DatabaseStatus { get; set; }
    }

    public enum ConnectivityStatus // بدون تغییر
    {
        Unknown,
        Healthy,
        ServerUnreachable,
        DatabaseUnreachable,
        ServiceUnavailable,
        Error
    }

    public class ConnectivityService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ConnectivityService> _logger;
        private const string HealthCheckEndpoint = "api/healthcheck/status";
        private readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(7);

        public ConnectivityService(HttpClient httpClient, ILogger<ConnectivityService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<(ConnectivityStatus Status, string UserFriendlyMessage, string? TechnicalMessage)> CheckConnectivityAsync(TimeSpan? timeout = null)
        {
            var requestTimeout = timeout ?? DefaultRequestTimeout;
            _logger.LogInformation("Attempting to check server and database connectivity with timeout: {TimeoutSeconds}s...", requestTimeout.TotalSeconds);

            // ---- تعریف cts در ابتدای متد ----
            using var cts = new CancellationTokenSource(requestTimeout);

            try
            {
                // ---- استفاده از cts.Token در اینجا صحیح است ----
                HttpResponseMessage response = await _httpClient.GetAsync(HealthCheckEndpoint, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var healthResponse = await response.Content.ReadFromJsonAsync<ServerHealthResponse>(cancellationToken: cts.Token);
                    if (healthResponse?.Status?.Equals("Healthy", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _logger.LogInformation("Connectivity check successful: Server and Database are healthy. Message: {ServerMessage}", healthResponse.Message);
                        return (ConnectivityStatus.Healthy,
                                "ارتباط با سرور و پایگاه داده برقرار است.",
                                healthResponse.Message);
                    }
                    else
                    {
                        _logger.LogWarning("Connectivity check returned unhealthy status from server. Status: {ServerStatus}, DB Status: {DbStatus}, Message: {ServerMessage}",
                                           healthResponse?.Status, healthResponse?.DatabaseStatus, healthResponse?.Message);
                        return (ConnectivityStatus.DatabaseUnreachable,
                                "سرور در دسترس است، اما به نظر می‌رسد پایگاه داده با مشکل مواجه است. لطفاً بعداً تلاش کنید یا با پشتیبانی تماس بگیرید.",
                                $"Server: {healthResponse?.Status}, DB: {healthResponse?.DatabaseStatus} - {healthResponse?.Message}");
                    }
                }
                else
                {
                    _logger.LogWarning("Connectivity check failed with HTTP status code: {StatusCode} - {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    {
                        // ---- استفاده از cts.Token در اینجا صحیح است ----
                        var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                        _logger.LogWarning("Service Unavailable (503) content: {ErrorContent}", errorContent);
                        return (ConnectivityStatus.DatabaseUnreachable,
                                "سرویس در حال حاضر در دسترس نیست یا پایگاه داده با مشکل مواجه است. لطفاً دقایقی دیگر مجدداً تلاش فرمایید.",
                                $"HTTP {response.StatusCode}: {response.ReasonPhrase} - {errorContent}");
                    }
                    return (ConnectivityStatus.ServerUnreachable,
                            "خطا در برقراری ارتباط با سرور. لطفاً اتصال اینترنت خود را بررسی کرده و مجدداً تلاش کنید.",
                            $"HTTP {response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HttpRequestException during connectivity check to {Endpoint}.", HealthCheckEndpoint);
                return (ConnectivityStatus.ServerUnreachable,
                        "امکان برقراری ارتباط با سرور وجود ندارد. لطفاً اتصال اینترنت خود و در دسترس بودن سرور برنامه را بررسی نمایید.",
                        ex.Message);
            }
            catch (OperationCanceledException ex) // این هم TaskCanceledException را در بر می‌گیرد
            {
                _logger.LogWarning(ex, "Connectivity check timed out or was canceled for {Endpoint}.", HealthCheckEndpoint);
                // ---- استفاده از cts.IsCancellationRequested در اینجا صحیح است ----
                if (cts.IsCancellationRequested && !ex.CancellationToken.IsCancellationRequested) // اگر cts منقضی شده ولی CancellationToken خود استثنا متفاوت است (یعنی timeout خودمان)
                {
                    return (ConnectivityStatus.ServerUnreachable,
                       "پاسخی از سرور در مدت زمان مشخص دریافت نشد. ممکن است ارتباط شبکه کند باشد یا سرور پاسخگو نباشد.",
                       "Request timed out.");
                }
                else if (ex.CancellationToken == cts.Token) // اگر مستقیما به خاطر cts.Token لغو شده (timeout یا لغو دستی cts)
                {
                    return (ConnectivityStatus.ServerUnreachable,
                        "پاسخی از سرور در مدت زمان مشخص دریافت نشد یا عملیات لغو شد.",
                        "Request timed out or was canceled by our CancellationTokenSource.");
                }
                // اگر به خاطر CancellationToken دیگری لغو شده باشد (مثلاً CancellationToken پاس داده شده از خارج)
                return (ConnectivityStatus.Error, "عملیات بررسی اتصال لغو شد.", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic exception during connectivity check to {Endpoint}.", HealthCheckEndpoint);
                return (ConnectivityStatus.Error,
                        "خطای داخلی هنگام بررسی وضعیت اتصال رخ داده است. لطفاً با پشتیبانی تماس بگیرید.",
                        ex.Message);
            }
        }
    }
}