using System;
using System.ComponentModel.DataAnnotations;

namespace Safir.Shared.Models.Automation
{
    public class EventModel
    {
        public long IDNUM { get; set; } // Task ID
        public int IDD { get; set; } // Event ID

        [Required(ErrorMessage = "شرح رویداد الزامی است.")]
        [MaxLength(4000)]
        public string? EVENTS { get; set; } // شرح رویداد

        public DateTime? STDATE { get; set; } // تاریخ ثبت رویداد
        public TimeSpan? STTIME { get; set; } // زمان ثبت رویداد
        public string? USERNAME { get; set; } // کاربر ثبت کننده
        public TimeSpan? SUMTIME { get; set; } // زمان صرف شده

        // فیلدهای سند مرتبط (بدون فایل ضمیمه)
        public int? skid { get; set; }
        public long? num { get; set; }
        // pic و FXTYPE حذف شدند

        // public byte[]? pic { get; set; } // فیلد pic از نوع image در دیتابیس [cite: 31]
        // public string? FXTYPE { get; set; } // فیلد FXTYPE از نوع nvarchar(10) در دیتابیس [cite: 31]

        // --- NEW: Add properties for file attachment (or reuse existing if suitable) ---
        // With current DB schema, pic (byte[]) and FXTYPE (string) are available.
        // Let's explicitly add them to the model if they are not being used implicitly.
        public byte[]? AttachedFileBytes { get; set; } // For uploading file content
        public string? AttachedFileName { get; set; } // For storing original file name or a generated name
        public string? AttachedFileType { get; set; } // For storing file extension/MIME type (.jpg, .pdf)

        // As per the database schema for EVENTS, the `pic` column exists as `image` and `FXTYPE` as `nvarchar(10)`. [cite: 31]
        // We will map these in the server-side, but having distinct client-side properties helps clarity for transfer.
        // We can use AttachedFileBytes for `pic` and AttachedFileType for `FXTYPE`.
        // AttachedFileName is for client-side display and potentially original filename.

    }
}