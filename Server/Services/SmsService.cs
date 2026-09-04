using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Dapper;
using IPE.SmsIrClient;
using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Utility;

namespace Safir.Server.Services
{
    public class SmsService : ISmsService
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<SmsService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public const string ERRORKEY = "#error#";

        public enum SmsServiceType
        {
            SmsIr = 0,
            TsmsUrl = 1
        }

        public SmsService(IDatabaseService dbService, ILogger<SmsService> logger, IHttpClientFactory httpClientFactory)
        {
            _dbService = dbService;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<SmsSendResult> SendSmsAsync(string recipientNumber, string messageText, long? relatedRecordId = null, int messageType = 1)
        {
            var result = new SmsSendResult();

            if (string.IsNullOrWhiteSpace(recipientNumber) || string.IsNullOrWhiteSpace(messageText))
            {
                result.IsSuccess = false;
                result.Message = "شماره گیرنده و متن پیامک الزامی است.";
                return result;
            }

            try
            {
                // ۱. خواندن تنظیمات SMS از جدول SAZMAN
                var sazmanSql = @"
                    SELECT
                        SMSACT, RB_TUBA, RB_SMSIR, SMS_LIBKEY,
                        SMS_USERNAME, SMS_PASSWORD, SMS_TSMSHOST, SMS_OWNER, DSMS
                    FROM SAZMAN";

                var sazman = (await _dbService.DoGetDataSQLAsync<dynamic>(sazmanSql)).FirstOrDefault();
                if (sazman == null)
                {
                    result.IsSuccess = false;
                    result.Message = "تنظیمات سازمان در دیتابیس یافت نشد.";
                    return result;
                }

                var sazmanDict = (IDictionary<string, object>)sazman;

                bool isSmsActive = sazmanDict.ContainsKey("SMSACT") && sazmanDict["SMSACT"] != null && Convert.ToBoolean(sazmanDict["SMSACT"]);
                if (!isSmsActive)
                {
                    result.IsSuccess = false;
                    result.Message = "قابلیت ارسال پیامک در تنظیمات سیستم غیرفعال است.";
                    return result;
                }

                bool isSmsIr = sazmanDict.ContainsKey("RB_SMSIR") && sazmanDict["RB_SMSIR"] != null && Convert.ToBoolean(sazmanDict["RB_SMSIR"]);
                string? apiKey = sazmanDict.ContainsKey("SMS_LIBKEY") && sazmanDict["SMS_LIBKEY"] != null ? sazmanDict["SMS_LIBKEY"].ToString() : null;
                string? username = sazmanDict.ContainsKey("SMS_USERNAME") && sazmanDict["SMS_USERNAME"] != null ? sazmanDict["SMS_USERNAME"].ToString() : null;
                string? password = sazmanDict.ContainsKey("SMS_PASSWORD") && sazmanDict["SMS_PASSWORD"] != null ? sazmanDict["SMS_PASSWORD"].ToString() : null;
                string? lineNumberStr = sazmanDict.ContainsKey("SMS_TSMSHOST") && sazmanDict["SMS_TSMSHOST"] != null ? sazmanDict["SMS_TSMSHOST"].ToString() : null;

                long lineNumber = 0;
                if (!string.IsNullOrWhiteSpace(lineNumberStr))
                {
                    long.TryParse(lineNumberStr.Trim(), out lineNumber);
                }

                string cleanMobile = recipientNumber.Trim();
                if (cleanMobile.StartsWith("0"))
                {
                    cleanMobile = cleanMobile.TrimStart('0');
                }

                string messageId = string.Empty;

                // ۲. انتخاب سرویس دهنده و ارسال پیامک
                if (isSmsIr)
                {
                    // SMS.ir
                    if (string.IsNullOrWhiteSpace(apiKey))
                    {
                        result.IsSuccess = false;
                        result.Message = "کلید وب‌سرویس SMS.ir (API Key) تنظیم نشده است.";
                        return result;
                    }

                    var smsIr = new SmsIr(apiKey);
                    var bulkRes = await smsIr.BulkSendAsync(lineNumber, messageText, new[] { cleanMobile });
                    if (bulkRes != null && bulkRes.Status == 1)
                    {
                        result.IsSuccess = true;
                        result.ReferenceId = bulkRes.Data?.PackId.ToString();
                        result.Message = "پیامک با موفقیت به درگاه SMS.ir ارسال شد.";
                    }
                    else
                    {
                        result.IsSuccess = false;
                        result.Message = bulkRes?.Message ?? "خطا در ارسال پیامک توسط SMS.ir";
                    }
                }
                else
                {
                    // TSMS.ir
                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    {
                        result.IsSuccess = false;
                        result.Message = "نام کاربری و رمز عبور سامانه TSMS تنظیم نشده است.";
                        return result;
                    }

                    var client = _httpClientFactory.CreateClient();
                    var url = $"http://tsms.ir/url/tsmshttp.php?from={lineNumber}&to=0{cleanMobile}&username={username}&password={password}&message={HttpUtility.UrlEncode(messageText)}";
                    var response = await client.GetAsync(url);
                    var responseStr = await response.Content.ReadAsStringAsync();

                    if (!string.IsNullOrWhiteSpace(responseStr) && !responseStr.Contains("error") && !responseStr.Contains("-"))
                    {
                        result.IsSuccess = true;
                        result.ReferenceId = responseStr.Trim();
                        result.Message = "پیامک با موفقیت ارسال شد.";
                    }
                    else
                    {
                        result.IsSuccess = false;
                        result.Message = $"خطا در وب‌سرویس TSMS: {responseStr}";
                    }
                }

                // ۳. ثبت لاگ کامل در جدول SMS_SENDS همانند MrCorrect
                var today = CL_Tarikh.GetCurrentPersianDateAsLong();
                var now = DateTime.Now;
                long timeLong = (now.Hour * 10000) + (now.Minute * 100) + now.Second;

                var insertLogSql = @"
                    INSERT INTO SMS_SENDS (
                        SM_DT, SM_TT, SM_DTQ, SM_TTQ, SM_AMobiles, AMSG, NUMBER, TAGS, id_sms, CUST_NO, USERNAME, STATUSSMS, CRT
                    )
                    VALUES (
                        @Today, @Time, @Today, @Time, @Mobile, @Msg, @Number, @Tag, @IdSms, @CustNo, @User, @Status, GETDATE()
                    )";

                await _dbService.DoExecuteSQLAsync(insertLogSql, new
                {
                    Today = today,
                    Time = timeLong,
                    Mobile = recipientNumber,
                    Msg = messageText,
                    Number = relatedRecordId ?? 0,
                    Tag = messageType,
                    IdSms = result.ReferenceId ?? "",
                    CustNo = cleanMobile,
                    User = "Controller",
                    Status = result.IsSuccess ? 1 : 0
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception sending SMS to {Recipient}", recipientNumber);
                result.IsSuccess = false;
                result.Message = $"خطای سیستمی در ارسال پیامک: {ex.Message}";
                return result;
            }
        }
    }
}
