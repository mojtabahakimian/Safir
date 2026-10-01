namespace Safir.Shared.Models.Automation;

public static class EventAttachmentPolicy
{
    public const long MaxFileSize = 10 * 1024 * 1024;
    public static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf", ".xlsx", ".xls" };
    public static bool IsAllowed(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
}
