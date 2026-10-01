namespace Safir.Shared.Models.Automation;

public static class EventAttachmentPolicy
{
    public const long MaxFileSize = 10 * 1024 * 1024;
    /// <summary>عکس، PDF، اکسل و ورد — نسخه‌های ماکرودار (xlsm/docm) عمداً نه.</summary>
    public static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf", ".xlsx", ".xls", ".docx", ".doc" };

    /// <summary>MIME برای ارسال از کلاینت؛ سرور فقط به پسوند نگاه می‌کند.</summary>
    public static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc" => "application/msword",
        _ => "application/octet-stream"
    };
    public static bool IsAllowed(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
}
