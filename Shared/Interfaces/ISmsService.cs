// مسیر پیشنهادی: Safir.Shared/Interfaces/ISmsService.cs
using System.Threading.Tasks;
// ممکنه نیاز به اضافه کردن مدل برای نتیجه ارسال SMS باشه
// using Safir.Shared.Models.Sms;

namespace Safir.Shared.Interfaces
{
    // تعریف یک مدل ساده برای نتیجه ارسال SMS (دلخواه)
    public class SmsSendResult
    {
        public bool IsSuccess { get; set; }
        public string? Message { get; set; } // پیام خطا یا موفقیت
        public string? ReferenceId { get; set; } // شناسه پیگیری (اگر سرویس SMS ارائه می‌دهد)
    }

    public interface ISmsService
    {
        /// <summary>
        /// متد برای ارسال پیامک
        /// </summary>
        /// <param name="recipientNumber">شماره گیرنده (می‌تونه کد مشتری یا شماره موبایل مستقیم باشه)</param>
        /// <param name="messageText">متن پیامک</param>
        /// <param name="relatedRecordId">شناسه رکورد مرتبط در سیستم (مثلا IDNUM پیام)</param>
        /// <param name="messageType">نوع پیام (مثلا 1 برای عادی, 2 برای یادآوری و...)</param>
        /// <returns>نتیجه عملیات ارسال</returns>
        Task<SmsSendResult> SendSmsAsync(string recipientNumber, string messageText, long? relatedRecordId = null, int messageType = 1);

        // ممکنه متدهای دیگری هم برای چک کردن اعتبار یا وضعیت ارسال لازم باشه
        // Task<SmsStatusResult> GetSmsStatusAsync(string referenceId);
    }
}