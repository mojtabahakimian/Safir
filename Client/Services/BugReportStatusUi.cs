using MudBlazor;
using Safir.Shared.Models.BugReport;
using System.Collections.Generic;

namespace Safir.Client.Services
{
    /// <summary>
    /// Maps each bug-report status to how it should look (MudBlazor color/variant/icon).
    /// Lives in the Client project (not Safir.Shared) because Safir.Shared has no
    /// MudBlazor reference. The list of valid statuses and their Persian labels still
    /// come from Safir.Shared.Models.BugReport.BugReportStatuses — this class only
    /// answers "how do I draw it".
    ///
    /// Place this file under Safir.Client/Services/ next to BugReportApiService.
    /// </summary>
    public static class BugReportStatusUi
    {
        public class Style
        {
            public Color Color { get; set; } = Color.Default;
            public Variant Variant { get; set; } = Variant.Outlined;
            public string Icon { get; set; } = Icons.Material.Filled.Circle;
        }

        private static readonly Dictionary<string, Style> _styles = new()
        {
            [BugReportStatuses.New] = new Style { Color = Color.Info, Variant = Variant.Filled, Icon = Icons.Material.Filled.FiberNew },
            [BugReportStatuses.InReview] = new Style { Color = Color.Warning, Variant = Variant.Outlined, Icon = Icons.Material.Filled.Visibility },
            [BugReportStatuses.Confirmed] = new Style { Color = Color.Primary, Variant = Variant.Outlined, Icon = Icons.Material.Filled.CheckCircleOutline },
            [BugReportStatuses.NeedMoreInfo] = new Style { Color = Color.Secondary, Variant = Variant.Outlined, Icon = Icons.Material.Filled.HelpOutline },
            [BugReportStatuses.InProgress] = new Style { Color = Color.Tertiary, Variant = Variant.Filled, Icon = Icons.Material.Filled.Build },
            [BugReportStatuses.InTesting] = new Style { Color = Color.Warning, Variant = Variant.Filled, Icon = Icons.Material.Filled.Science },
            [BugReportStatuses.Reopened] = new Style { Color = Color.Error, Variant = Variant.Outlined, Icon = Icons.Material.Filled.Replay },
            [BugReportStatuses.Deferred] = new Style { Color = Color.Default, Variant = Variant.Outlined, Icon = Icons.Material.Filled.PauseCircleOutline },
            [BugReportStatuses.Fixed] = new Style { Color = Color.Success, Variant = Variant.Outlined, Icon = Icons.Material.Filled.Done },
            [BugReportStatuses.Deployed] = new Style { Color = Color.Success, Variant = Variant.Filled, Icon = Icons.Material.Filled.CloudDone },
            [BugReportStatuses.Rejected] = new Style { Color = Color.Error, Variant = Variant.Filled, Icon = Icons.Material.Filled.Cancel },
            [BugReportStatuses.Duplicate] = new Style { Color = Color.Dark, Variant = Variant.Outlined, Icon = Icons.Material.Filled.ContentCopy },
            [BugReportStatuses.WontFix] = new Style { Color = Color.Dark, Variant = Variant.Outlined, Icon = Icons.Material.Filled.Block },
            [BugReportStatuses.Closed] = new Style { Color = Color.Dark, Variant = Variant.Filled, Icon = Icons.Material.Filled.Lock },
        };

        private static readonly Style _default = new();

        public static Style Get(string? status) =>
            status != null && _styles.TryGetValue(status, out var s) ? s : _default;

        public static Color GetColor(string? status) => Get(status).Color;
        public static Variant GetVariant(string? status) => Get(status).Variant;
        public static string GetIcon(string? status) => Get(status).Icon;
        public static string GetLabel(string? status) => BugReportStatuses.GetLabel(status);
    }
}
