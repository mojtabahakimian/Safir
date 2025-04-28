using Safir.Shared.Models.Kharid;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace Safir.Client.Services
{
    public class ProformaApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ProformaApiService> _logger;

        public ProformaApiService(HttpClient httpClient, ILogger<ProformaApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<ProformaSaveResponseDto> SaveProformaAsync(ProformaSaveRequestDto request)
        {
            if (request == null)
            {
                return new ProformaSaveResponseDto { Success = false, Message = "درخواست نامعتبر است." };
            }

            try
            {
                _logger.LogInformation("Sending request to save proforma for customer {CustomerHes}", request.Header?.CustomerHesCode);
                var response = await _httpClient.PostAsJsonAsync("api/proformas", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ProformaSaveResponseDto>();
                    _logger.LogInformation("Proforma save API call successful. Server message: {Message}", result?.Message);
                    return result ?? new ProformaSaveResponseDto { Success = true, Message = "پاسخ موفق از سرور دریافت شد اما محتوای آن قابل پردازش نبود." }; // Handle potential null response content
                }
                else
                {
                    // Attempt to read error details from the response body
                    ProformaSaveResponseDto? errorResponse = null;
                    string rawErrorContent = string.Empty;
                    try
                    {
                        // Try reading structured error first
                        errorResponse = await response.Content.ReadFromJsonAsync<ProformaSaveResponseDto>();
                    }
                    catch
                    {
                        // Fallback to reading raw content if structured reading fails
                        try { rawErrorContent = await response.Content.ReadAsStringAsync(); } catch { /* Ignore secondary error */ }
                    }

                    var errorMessage = errorResponse?.Message ?? rawErrorContent;
                    if (string.IsNullOrWhiteSpace(errorMessage))
                    {
                        errorMessage = $"خطای سرور: {response.StatusCode}";
                    }

                    _logger.LogError("Failed to save proforma. Status: {StatusCode}, Error: {Error}", response.StatusCode, errorMessage);
                    return new ProformaSaveResponseDto { Success = false, Message = errorMessage };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during SaveProformaAsync API call.");
                return new ProformaSaveResponseDto { Success = false, Message = $"خطای کلاینت هنگام ارتباط با سرور: {ex.Message}" };
            }
        }

        // --- <<< NEW Method: GetProformaPdfBytesAsync >>> ---
        /// <summary>
        /// Fetches the PDF byte array for a given proforma number from the server.
        /// </summary>
        /// <param name="proformaNumber">The proforma number (usually a double).</param>
        /// <returns>Byte array of the PDF file or null if an error occurs.</returns>
        public async Task<(byte[]? PdfBytes, string? ErrorMessage, HttpStatusCode? StatusCode)> GetProformaPdfBytesAsync(double proformaNumber)
        {
            string requestUri = $"api/proformas/{proformaNumber}/pdf";
            _logger.LogInformation("Requesting Proforma PDF from: {RequestUri}", requestUri);

            try
            {
                var response = await _httpClient.GetAsync(requestUri);

                if (response.IsSuccessStatusCode)
                {
                    var pdfBytes = await response.Content.ReadAsByteArrayAsync();
                    _logger.LogInformation("Successfully received {PdfSize} bytes for Proforma PDF {ProformaNumber}.", pdfBytes.Length, proformaNumber);
                    return (pdfBytes, null, response.StatusCode);
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get Proforma PDF bytes. Status: {StatusCode}, Reason: {ReasonPhrase}, Content: {ErrorContent}",
                                     response.StatusCode, response.ReasonPhrase, errorContent);
                    return (null, errorContent ?? $"خطای سرور: {response.ReasonPhrase}", response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error getting Proforma PDF bytes for Number: {ProformaNumber} - StatusCode: {StatusCode}", proformaNumber, ex.StatusCode);
                return (null, $"خطای شبکه: {ex.Message}", ex.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error getting Proforma PDF bytes for Number: {ProformaNumber}", proformaNumber);
                return (null, $"خطای کلاینت: {ex.Message}", null);
            }
        }
        // --- <<< END NEW Method >>> ---
    }
}