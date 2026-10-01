using Safir.Server.Services;
using Safir.Shared.Models.User_Model;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// واحد و شیفتِ کاری — قواعدِ پنجره‌ی DEFAULT ِ WPF (DEFAULT_VSH_SAVE_Click و قفلِ LETSGO("DEFA")).
/// </summary>
public class WorkspaceServiceTests
{
    private static readonly (int, int) Current = (1, 1);

    private static WorkspaceSaveRequest Req(int? dep, int? shift) => new() { Depatman = dep, Shift = shift };

    // ordinal: مقایسه‌ی فرهنگی «واحدِ» (با کسره) را شاملِ «واحد» نمی‌داند
    private static void Has(string part, string? message) => Assert.Contains(part, message, StringComparison.Ordinal);

    [Fact]
    public void Unit_and_shift_are_required_like_wpf()
    {
        Has("واحد جاری", WorkspaceService.Validate(Req(null, 1), Current, true, false, true));
        Has("شیفت", WorkspaceService.Validate(Req(20, null), Current, true, true, false));
    }

    [Fact]
    public void User_with_default_permission_can_pick_any_existing_unit()
        => Assert.Null(WorkspaceService.Validate(Req(20, 2), Current, canChange: true, depExists: true, shiftExists: true));

    [Fact]
    public void User_without_default_permission_is_locked_to_defaultdep()
    {
        Has("اجازه", WorkspaceService.Validate(Req(20, 1), Current, canChange: false, true, true));
        Has("اجازه", WorkspaceService.Validate(Req(1, 2), Current, canChange: false, true, true));
        Assert.Null(WorkspaceService.Validate(Req(1, 1), Current, canChange: false, true, true));
    }

    [Fact]
    public void Unknown_unit_or_shift_is_rejected()
    {
        Has("واحد", WorkspaceService.Validate(Req(999, 1), Current, true, depExists: false, shiftExists: true));
        Has("شیفت", WorkspaceService.Validate(Req(1, 9), Current, true, depExists: true, shiftExists: false));
        // پیش‌فرضِ قفل‌شده‌ای که دیگر وجود ندارد: پیام به مدیر سیستم ارجاع می‌دهد، نه «انتخاب کنید»
        Has("مدیر سیستم", WorkspaceService.Validate(Req(1, 1), Current, false, depExists: false, shiftExists: true));
    }
}
