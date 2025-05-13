using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Automation;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json; // Important: Add this using
using System.Threading.Tasks;
using Microsoft.Extensions.Logging; // Optional for logging

namespace Safir.Client.Services
{
    public class AutomationApiService : IAutomationApiService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AutomationApiService> _logger;

        public AutomationApiService(HttpClient httpClient, ILogger<AutomationApiService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
        }

        // --- Tasks ---
        public async Task<IEnumerable<TaskModel>?> GetTasksAsync(int statusFilter = 1, int? assignedUserId = null, string? taskTypes = "1000")
        {
            // Build query string based on parameters
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["statusFilter"] = statusFilter.ToString();
            if (assignedUserId.HasValue) query["assignedUserId"] = assignedUserId.Value.ToString();
            if (!string.IsNullOrWhiteSpace(taskTypes)) query["taskTypes"] = taskTypes;

            string requestUri = $"api/tasks?{query}";
            try
            {
                return await _httpClient.GetFromJsonAsync<List<TaskModel>>(requestUri);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching tasks from {RequestUri}", requestUri);
                return null;
            }
        }

        public async Task<TaskModel?> CreateTaskAsync(TaskModel task)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/tasks", task);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<TaskModel>();
                }
                _logger?.LogError("Error creating task. Status: {StatusCode}", response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception creating task.");
                return null;
            }
        }

        public async Task<bool> UpdateTaskAsync(long idnum, TaskModel task)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"api/tasks/{idnum}", task);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception updating task {Idnum}.", idnum);
                return false;
            }
        }

        // در فایل Safir.Client/Services/AutomationApiService.cs

        public async Task<bool> UpdateTasksBulkAsync(List<long> idnums, TaskModel updateValues)
        {
            if (idnums == null || !idnums.Any())
            {
                _logger?.LogWarning("API Call: UpdateTasksBulkAsync called with no task IDs.");
                return false; // یا throw exception
            }

            // ساخت مدل درخواست مطابق با DTO سمت سرور (TasksController > BulkTaskUpdateRequest)
            // فقط فیلدهایی که مقدار دارند باید ارسال شوند.
            var updateRequestPayload = new
            {
                TaskIds = idnums,
                // بررسی می‌کنیم که آیا فیلد مربوطه در مدل updateValues مقداردهی شده یا خیر
                // مقدار 0 برای PERSONEL, STATUS, PERIORITY معتبر نیست و به معنی عدم تغییر است
                PERSONEL = updateValues.PERSONEL > 0 ? updateValues.PERSONEL : (int?)null,
                STATUS = updateValues.STATUS > 0 ? updateValues.STATUS : (int?)null,
                PERIORITY = updateValues.PERIORITY > 0 ? updateValues.PERIORITY : (int?)null
                // سایر فیلدها اگر در آینده به ویرایش گروهی اضافه شوند
            };

            // اگر هیچ فیلدی برای آپدیت مقدار نداشت (نباید اتفاق بیفتد چون دیالوگ چک می‌کند)
            if (updateRequestPayload.PERSONEL == null && updateRequestPayload.STATUS == null && updateRequestPayload.PERIORITY == null)
            {
                _logger?.LogWarning("API Call: UpdateTasksBulkAsync called with no update values.");
                // Optionally return true if no update is needed, or false/throw if it's an error condition
                return true;
            }


            try
            {
                // ارسال درخواست PUT به API Controller
                var response = await _httpClient.PutAsJsonAsync("api/tasks/bulk-update", updateRequestPayload);

                if (!response.IsSuccessStatusCode)
                {
                    // خواندن پیام خطا از سرور (اگر وجود داشته باشد) برای لاگ یا نمایش
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger?.LogError("API Error: Bulk update failed. Status: {StatusCode}. Endpoint: {Endpoint}. Content: {Content}",
                                      response.StatusCode, "api/tasks/bulk-update", errorContent);
                    // می‌توانید اینجا از Snackbar استفاده کنید تا خطا به کاربر نمایش داده شود
                    // _snackbar.Add($"خطای سرور در ویرایش گروهی: {response.ReasonPhrase}", Severity.Error);
                }
                else
                {
                    _logger?.LogInformation("API Call: Bulk update successful for {Count} tasks.", idnums.Count);
                }

                // بازگرداندن true اگر کد وضعیت 2xx بود (مثل 204 No Content)
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception during bulk update tasks API call.");
                // _snackbar.Add($"خطای شبکه یا برنامه: {ex.Message}", Severity.Error);
                return false;
            }
        }
        public Task<TaskModel?> GetTaskByIdAsync(long idnum)
        {
            // Optional implementation if needed
            throw new NotImplementedException();
        }


        // --- Events ---
        public async Task<IEnumerable<EventModel>?> GetEventsAsync(long taskId)
        {
            string requestUri = $"api/tasks/{taskId}/events";
            try
            {
                return await _httpClient.GetFromJsonAsync<List<EventModel>>(requestUri);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching events for task {TaskId} from {RequestUri}", taskId, requestUri);
                return null;
            }
        }

        public async Task<EventModel?> CreateEventAsync(long taskId, EventModel newEvent)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"api/tasks/{taskId}/events", newEvent);
                if (response.IsSuccessStatusCode)
                {
                    // Might return the created event or just a success status/ID
                    // Adjust based on API design
                    return await response.Content.ReadFromJsonAsync<EventModel>();
                }
                _logger?.LogError("Error creating event for task {TaskId}. Status: {StatusCode}", taskId, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception creating event for task {TaskId}.", taskId);
                return null;
            }
        }

        public Task<bool> UpdateEventAsync(long taskId, int eventId, EventModel eventData)
        {
            throw new NotImplementedException();
        }

        public Task<bool> DeleteEventAsync(long taskId, int eventId)
        {
            throw new NotImplementedException();
        }


        // --- Messages ---
        public async Task<IEnumerable<MessageModel>?> GetMessagesAsync(bool includeSent = true, bool includeReceived = true)
        {
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query["includeSent"] = includeSent.ToString();
            query["includeReceived"] = includeReceived.ToString();
            string requestUri = $"api/messages?{query}";
            try
            {
                return await _httpClient.GetFromJsonAsync<List<MessageModel>>(requestUri);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching messages from {RequestUri}", requestUri);
                return null;
            }
        }

        public async Task<bool> SendMessageAsync(MessageSendRequest request)
        {
            try
            {
                // لاگ کردن درخواست ارسالی
                _logger?.LogInformation("AutomationApiService: Sending message request: RecipientUserIds Count = {Count}, First Recipient (if any) = {FirstRecipient}, MessageText = {Text}",
                                      request?.RecipientUserIds?.Count ?? 0,
                                      request?.RecipientUserIds?.FirstOrDefault(),
                                      request?.MessageText);

                var response = await _httpClient.PostAsJsonAsync("api/messages", request);
                if (!response.IsSuccessStatusCode)
                {
                    // لاگ کردن جزئیات بیشتر در صورت عدم موفقیت
                    var errorContent = await response.Content.ReadAsStringAsync();
                    string logMessage = $"API call to send message failed with status code {response.StatusCode}. Response: {errorContent}";
                    _logger.LogError(logMessage); // اگر از _logger استفاده می‌کنید
                    Console.WriteLine(logMessage); // چاپ پیام به صورت یک رشته کامل
                }
                return response.IsSuccessStatusCode; // Or check for 207 Multi-Status if API returns it
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception sending message.");
                Console.WriteLine($"Exception ex Error {ex}");
                return false;
            }
        }

        public async Task<int> GetUnreadMessageCountAsync()
        {
            try
            {
                // Assuming the API returns the count directly
                return await _httpClient.GetFromJsonAsync<int>("api/messages/unread-count");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting unread message count.");
                return 0;
            }
        }

        public async Task<bool> MarkMessageAsReadAsync(long idnum)
        {
            try
            {
                var response = await _httpClient.PutAsync($"api/messages/{idnum}/mark-read", null); // No body needed for this PUT
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception marking message {Idnum} as read.", idnum);
                return false;
            }
        }


        // --- Reminders ---
        public async Task<IEnumerable<ReminderModel>?> GetRemindersAsync(int? statusFilter = null)
        {
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            if (statusFilter.HasValue) query["statusFilter"] = statusFilter.Value.ToString();
            string requestUri = $"api/reminders?{query}";
            try
            {
                return await _httpClient.GetFromJsonAsync<List<ReminderModel>>(requestUri);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching reminders from {RequestUri}", requestUri);
                return null;
            }
        }

        public async Task<bool> CreateReminderAsync(ReminderCreateRequest request)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/reminders", request);
                return response.IsSuccessStatusCode; // Or check for 207
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception creating reminder.");
                return false;
            }
        }

        public async Task<bool> CancelReminderAsync(long idnum)
        {
            try
            {
                var response = await _httpClient.PutAsync($"api/reminders/{idnum}/cancel", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception cancelling reminder {Idnum}.", idnum);
                return false;
            }
        }

        public async Task<int> GetActiveReminderCountAsync()
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<int>("api/reminders/active-count");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting active reminder count.");
                return 0;
            }
        }


        // --- Lookups ---
        // Implementation for lookup methods (calling corresponding API endpoints)
        public async Task<IEnumerable<PersonelLookupModel>?> GetPersonelLookupAsync()
        {
            string requestUri = "api/lookup/personnel"; // آدرس Endpoint باید صحیح باشد
            try
            {
                _logger?.LogInformation("API Call: Fetching personnel lookup from {RequestUri}", requestUri);
                var result = await _httpClient.GetFromJsonAsync<List<PersonelLookupModel>>(requestUri);
                _logger?.LogInformation("API Call: Successfully fetched {Count} personnel.", result?.Count ?? 0);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error fetching personnel lookup from {RequestUri}", requestUri);
                return null; // یا لیست خالی یا throw ex
            }
        }
        public Task<IEnumerable<StatusLookupModel>?> GetStatusLookupAsync()
        {
            // ... (پیاده‌سازی قبلی) ...
            var statuses = new List<StatusLookupModel>
             {
                 new() { STATUS = 1, STATUS_NAME = "انجام نشده" },
                 new() { STATUS = 2, STATUS_NAME = "انجام شده" },
                 new() { STATUS = 3, STATUS_NAME = "لغو شده" }
             };
            return Task.FromResult<IEnumerable<StatusLookupModel>?>(statuses);
        }
        public Task<IEnumerable<PriorityLookupModel>?> GetPriorityLookupAsync()
        {
            // ... (پیاده‌سازی قبلی) ...
            var priorities = new List<PriorityLookupModel>
             {
                 new() { PERIORITY = 1, PERIORITY_NAME = "فوری" },
                 new() { PERIORITY = 2, PERIORITY_NAME = "معمولی" }
             };
            return Task.FromResult<IEnumerable<PriorityLookupModel>?>(priorities);
        }

        public Task<IEnumerable<DocumentTypeLookupModel>?> GetDocumentTypeLookupAsync()
        {
            // TODO: پیاده سازی دریافت انواع سند (skid) از API یا تعریف استاتیک
            _logger?.LogWarning("GetDocumentTypeLookupAsync is not fully implemented yet.");
            // مثال داده استاتیک
            var docTypes = new List<DocumentTypeLookupModel> {
                new DocumentTypeLookupModel { ID=0, NAME="سند حسابداری" },
                new DocumentTypeLookupModel { ID=1, NAME="رسید خرید" },
                new DocumentTypeLookupModel { ID=2, NAME="حواله فروش" },
                 new DocumentTypeLookupModel { ID=12, NAME="فاکتور خرید" },
                 new DocumentTypeLookupModel { ID=13, NAME="فاکتور فروش" },
                 new DocumentTypeLookupModel { ID=20, NAME="پیش فاکتور" },
                 new DocumentTypeLookupModel { ID=100, NAME="درخواست پرداخت" },
                 new DocumentTypeLookupModel { ID=34, NAME="خزانه داری" },
                // ... سایر انواع سند بر اساس کد WPF ...
            };
            return Task.FromResult<IEnumerable<DocumentTypeLookupModel>?>(docTypes);
            // throw new NotImplementedException();
        }

        public async Task<bool> CanViewSubordinateTasksAsync()
        {
            string requestUri = "api/users/permissions/can-view-subordinate-tasks"; // آدرس Endpoint جدید
            try
            {
                // این Endpoint نیازی به ارسال داده ندارد، فقط وضعیت کاربر فعلی را بر اساس توکن چک می‌کند
                var response = await _httpClient.GetAsync(requestUri);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<bool>();
                }
                else
                {
                    _logger.LogError("Error fetching permission 'CanViewSubordinateTasks'. Status: {StatusCode}", response.StatusCode);
                    return false; // پیش‌فرض عدم دسترسی در صورت خطا
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception fetching permission 'CanViewSubordinateTasks' from {RequestUri}", requestUri);
                return false; // پیش‌فرض عدم دسترسی در صورت خطا
            }
        }

    }
}