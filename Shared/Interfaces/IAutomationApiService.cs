using Safir.Shared.Models; // For PagedResult
using Safir.Shared.Models.Automation;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface IAutomationApiService
    {
        // --- Tasks ---
        Task<IEnumerable<TaskModel>?> GetTasksAsync(int statusFilter = 1, int? assignedUserId = null, string? taskTypes = "1000");
        Task<TaskModel?> CreateTaskAsync(TaskModel task); // Returns the created task with IDNUM
        Task<bool> UpdateTaskAsync(long idnum, TaskModel task);
        Task<bool> UpdateTasksBulkAsync(List<long> idnums, TaskModel updateValues); // For bulk edit
        Task<TaskModel?> GetTaskByIdAsync(long idnum); // Optional: if needed

        // --- Events ---
        Task<IEnumerable<EventModel>?> GetEventsAsync(long taskId);
        Task<EventModel?> CreateEventAsync(long taskId, EventModel newEvent); // Returns created event with IDD
        Task<bool> UpdateEventAsync(long taskId, int eventId, EventModel eventData);
        Task<bool> DeleteEventAsync(long taskId, int eventId); // Optional: if needed

        // --- Messages ---
        Task<IEnumerable<MessageModel>?> GetMessagesAsync(bool includeSent = true, bool includeReceived = true);
        Task<bool> SendMessageAsync(MessageSendRequest request);
        Task<int> GetUnreadMessageCountAsync();
        Task<bool> MarkMessageAsReadAsync(long idnum);

        // --- Reminders ---
        Task<IEnumerable<ReminderModel>?> GetRemindersAsync(int? statusFilter = null);
        Task<bool> CreateReminderAsync(ReminderCreateRequest request);
        Task<bool> CancelReminderAsync(long idnum);
        Task<int> GetActiveReminderCountAsync();

        // --- Lookups ---
        Task<IEnumerable<PersonelLookupModel>?> GetPersonelLookupAsync();
        Task<IEnumerable<StatusLookupModel>?> GetStatusLookupAsync();
        Task<IEnumerable<PriorityLookupModel>?> GetPriorityLookupAsync();
        Task<IEnumerable<DocumentTypeLookupModel>?> GetDocumentTypeLookupAsync();
        Task<bool> CanViewSubordinateTasksAsync();
        // Note: Customer lookup might use existing LookupApiService or be added here
    }
}