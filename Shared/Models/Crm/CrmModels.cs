using System;
using System.Collections.Generic;

namespace Safir.Shared.Models.Crm
{
    public class CrmCompanyDto
    {
        public int? ID { get; set; }
        public string? COMPANY_NAME { get; set; }
        public string? CITY { get; set; }
        public string? MANAGER { get; set; }
        public string? FACT_TEL { get; set; }
        public string? MOBILE { get; set; }
        public int? PERNUM { get; set; }
        public string? STATUS_FACT { get; set; }
        public string? PRODUCTS { get; set; }
        public string? ADDR { get; set; }
        public string? ACCOUNTANT { get; set; }
        public string? SOFTWARE { get; set; }
        public string? ESP_PERSON { get; set; }
        public string? REAGENT { get; set; }
        public int? STATUS { get; set; }
        public string? COMMENT { get; set; }
        public DateTime? DATE_SABT { get; set; }
        public string? USER_NAME { get; set; }
        public byte[]? PIC { get; set; }
        public int? DT { get; set; }
        public int? USERID { get; set; }
        public double? LONGITUDE { get; set; }
        public double? LATITUDE { get; set; }
        public int? OSTANID { get; set; }
        public int? SHAHRID { get; set; }
        public DateTime? CRT { get; set; }
        public int? UID { get; set; }
        public int? IDCN { get; set; } // تعداد رویدادها

        // عنوان وضعیت متناظر
        public string? StatusTitle { get; set; }
    }

    public class CrmEventDto
    {
        public int? IDDE { get; set; }
        public string? COMPANY_NAME { get; set; }
        public int? INFO_DATE { get; set; }
        public int? INFO_TIME { get; set; }
        public string? SALER { get; set; }
        public string? BUYER { get; set; }
        public string? COMMENT { get; set; }
        public int? NEXT_DATE { get; set; }
        public int? NEXT_TIME { get; set; }
        public int? STATUS { get; set; }
        public byte[]? PIC { get; set; }
        public int? IDC { get; set; }
        public string? PAYAM { get; set; }
        public int? MITING { get; set; }
        public int? USERID { get; set; }
        public DateTime? CDATETI { get; set; }
        public DateTime? CRT { get; set; }
        public int? UID { get; set; }

        public string? StatusTitle { get; set; }
    }

    public class CrmStatusDto
    {
        public int Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class CrmDashboardSummaryDto
    {
        public int TotalCompanies { get; set; }
        public int TotalEvents { get; set; }
        public int TodayFollowUps { get; set; }
        public int OverdueFollowUps { get; set; }
        public int TodayMeetings { get; set; }
        public List<CrmStatusDto> StatusSummary { get; set; } = new();
        public List<CrmEventDto> UpcomingEvents { get; set; } = new();
    }

    public class CrmDuplicateCheckResultDto
    {
        public bool IsDuplicateNameInCustomers { get; set; }
        public bool IsDuplicateNameInCrm { get; set; }
        public bool IsDuplicateTelInCustomers { get; set; }
        public bool IsDuplicateMobileInCustomers { get; set; }
        public bool IsDuplicateMobileInCrm { get; set; }

        public bool HasAnyDuplicate => IsDuplicateNameInCustomers ||
                                       IsDuplicateNameInCrm ||
                                       IsDuplicateTelInCustomers ||
                                       IsDuplicateMobileInCustomers ||
                                       IsDuplicateMobileInCrm;

        public string? Message { get; set; }
    }

    /// <summary>
    /// وضعیت دسترسی کاربر جاری به CRM.
    ///
    /// دو حالت بیشتر ندارد و عمداً همین‌قدر ساده نگه داشته شده:
    ///   • <see cref="RestrictToOwn"/> = true  → فقط رکوردهای خودش
    ///   • <see cref="RestrictToOwn"/> = false → همه‌ی رکوردها
    ///
    /// وقتی کلید <c>CRM_ACL_ENFORCE</c> در PAY2_CONFIG خاموش باشد (پیش‌فرض)،
    /// هیچ محدودیتی اعمال نمی‌شود و رفتار دقیقاً مثل قبل از این تغییر است.
    /// </summary>
    public class CrmAccessDto
    {
        /// <summary>کد کاربر جاری (SALA_DTL.IDD) — مبنای مالکیت رکوردها</summary>
        public int UserId { get; set; }

        /// <summary>
        /// نام کاربری رمزگشایی‌شده. فقط برای رکوردهایی به کار می‌رود که
        /// <c>userid</c> ندارند ولی <c>USER_NAME</c> دارند (رکوردهای ساخته‌شده
        /// توسط نرم‌افزار WPF).
        /// </summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>آیا کلید CRM_ACL_ENFORCE روشن است؟</summary>
        public bool Enforced { get; set; }

        /// <summary>آیا کاربر مجوز فرم CRMALL را دارد؟ (یعنی همه را می‌بیند)</summary>
        public bool CanSeeAllUsers { get; set; }

        /// <summary>نتیجه‌ی نهایی: آیا باید به رکوردهای خودِ کاربر محدود شود؟</summary>
        public bool RestrictToOwn => Enforced && !CanSeeAllUsers;
    }

    public class CrmFilterDto
    {
        public string? SearchTerm { get; set; }
        public int? Status { get; set; }
        public string? City { get; set; }
        public string? StatusFact { get; set; }
        public int? NextDateFrom { get; set; }
        public int? NextDateTo { get; set; }
        public bool OnlyMyCompanies { get; set; } = true;
        public bool OnlyWithUpcomingFollowUps { get; set; }
    }

    public class CrmPhoneBookItemDto
    {
        public string HesCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Tel { get; set; }
        public string? Mobile { get; set; }
        public string? Address { get; set; }
        public string? Description { get; set; }
        public string SourceType { get; set; } = "مشتری"; // مشتری یا شرکت CRM
    }

    public class CrmNoteDto
    {
        public int? idd { get; set; }
        public string? Note { get; set; }
        public int? Ndate { get; set; }
        public string? Ntime { get; set; }
        public int? userid { get; set; }
        public bool Ndone { get; set; }
        public string? NoteDateFormatted => Ndate.HasValue && Ndate.Value > 0 ?
            (Ndate.Value.ToString().Length == 8 ? $"{Ndate.Value.ToString().Substring(0, 4)}/{Ndate.Value.ToString().Substring(4, 2)}/{Ndate.Value.ToString().Substring(6, 2)}" : Ndate.Value.ToString())
            : "--";
    }

    public class CrmSendSmsRequestDto
    {
        public string Mobile { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? CompanyName { get; set; }
    }
}
