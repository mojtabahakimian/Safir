using System.Collections.Generic;
using System.Linq;

namespace Safir.Shared.Models.BugReport
{
    /// <summary>
    /// Central definition of every bug-report workflow status.
    /// ADD NEW STATUSES HERE ONLY. Every page (list, my-reports, detail) reads
    /// this list, so the Persian label, ordering and grouping (open/resolved/closed)
    /// stay in sync across the whole app instead of being copy-pasted in three places.
    ///
    /// NOTE: adding a key here does not require a DB migration — Status is a
    /// free-text NVARCHAR(50) column — but if a CHECK constraint exists on
    /// [dbo].[BugReports].[Status] in the database, it must be updated too.
    /// </summary>
    public static class BugReportStatuses
    {
        // Open / in-progress
        public const string New = "New";
        public const string InReview = "InReview";
        public const string Confirmed = "Confirmed";
        public const string NeedMoreInfo = "NeedMoreInfo";
        public const string InProgress = "InProgress";
        public const string InTesting = "InTesting";
        public const string Reopened = "Reopened";
        public const string Deferred = "Deferred";

        // Resolved
        public const string Fixed = "Fixed";
        public const string Deployed = "Deployed";

        // Closed (no further action)
        public const string Rejected = "Rejected";
        public const string Duplicate = "Duplicate";
        public const string WontFix = "WontFix";
        public const string Closed = "Closed";

        public enum StatusGroup
        {
            Open,
            Resolved,
            Closed
        }

        public class StatusDefinition
        {
            public string Key { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public StatusGroup Group { get; set; }
        }

        /// <summary>Every status, in the order they should appear in dropdowns/legends.</summary>
        public static readonly List<StatusDefinition> All = new()
        {
            new StatusDefinition { Key = New,          Label = "جدید",                          Group = StatusGroup.Open },
            new StatusDefinition { Key = InReview,     Label = "در حال بررسی",                  Group = StatusGroup.Open },
            new StatusDefinition { Key = Confirmed,    Label = "تایید شده (باگ واقعی است)",      Group = StatusGroup.Open },
            new StatusDefinition { Key = NeedMoreInfo, Label = "نیاز به اطلاعات بیشتر",          Group = StatusGroup.Open },
            new StatusDefinition { Key = InProgress,   Label = "در حال رفع (توسعه)",             Group = StatusGroup.Open },
            new StatusDefinition { Key = InTesting,    Label = "در حال تست",                    Group = StatusGroup.Open },
            new StatusDefinition { Key = Reopened,     Label = "بازگشایی شده",                  Group = StatusGroup.Open },
            new StatusDefinition { Key = Deferred,     Label = "به تعویق افتاده",                Group = StatusGroup.Open },
            new StatusDefinition { Key = Fixed,        Label = "رفع شده",                       Group = StatusGroup.Resolved },
            new StatusDefinition { Key = Deployed,     Label = "منتشر شده (روی نسخه جدید)",      Group = StatusGroup.Resolved },
            new StatusDefinition { Key = Rejected,     Label = "رد شده (باگ نیست)",              Group = StatusGroup.Closed },
            new StatusDefinition { Key = Duplicate,    Label = "تکراری",                         Group = StatusGroup.Closed },
            new StatusDefinition { Key = WontFix,      Label = "عدم رفع (طبق طراحی)",            Group = StatusGroup.Closed },
            new StatusDefinition { Key = Closed,       Label = "بسته شده",                       Group = StatusGroup.Closed },
        };

        private static readonly Dictionary<string, StatusDefinition> _byKey =
            All.ToDictionary(s => s.Key, s => s);

        public static string GetLabel(string? key) =>
            key != null && _byKey.TryGetValue(key, out var def) ? def.Label : (key ?? "-");

        public static StatusGroup? GetGroup(string? key) =>
            key != null && _byKey.TryGetValue(key, out var def) ? def.Group : (StatusGroup?)null;
    }
}
