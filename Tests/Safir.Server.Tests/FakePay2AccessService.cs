using Safir.Shared.Interfaces;
using Safir.Shared.Models.Permissions;

namespace Safir.Server.Tests;

/// <summary>
/// جایگزین درون‌حافظه‌ای سرویس دسترسی — تست‌ها بدون دیتابیس اجرا می‌شوند.
/// هر چیزی که سرویس واقعی از دیتابیس می‌خواند اینجا مستقیم تنظیم می‌شود.
/// </summary>
public sealed class FakePay2AccessService : IPay2AccessService
{
    private readonly Pay2AccessDto _access;

    /// <summary>هر بار که AuditAsync صدا زده شود اینجا ثبت می‌شود.</summary>
    public List<Pay2AuditEntry> AuditLog { get; } = new();

    /// <summary>کاربرانی که کش‌شان باطل شده است.</summary>
    public List<int> Invalidated { get; } = new();

    public FakePay2AccessService(Pay2AccessDto access) => _access = access;

    /// <summary>کاربری با همه‌ی مجوزها روی فرم‌های داده‌شده.</summary>
    public static FakePay2AccessService FullAccess(params string[] forms) =>
        new(new Pay2AccessDto
        {
            UserCo = 9001,
            AclEnforced = true,
            WsScopeEnforced = true,
            AllowedWorkshopIds = new List<int> { 1, 2 },
            Forms = forms.Select(f => new Pay2FormPermDto
            {
                FormName = f, Caption = f,
                Run = true, See = true, Inp = true, Upd = true, Del = true
            }).ToList()
        });

    /// <summary>کاربری که فقط می‌تواند ببیند — مثل payviewer.</summary>
    public static FakePay2AccessService ViewOnly(params string[] forms) =>
        new(new Pay2AccessDto
        {
            UserCo = 9002,
            AclEnforced = true,
            WsScopeEnforced = true,
            AllowedWorkshopIds = new List<int> { 1, 2 },
            Forms = forms.Select(f => new Pay2FormPermDto
            {
                FormName = f, Caption = f,
                Run = true, See = true, Inp = false, Upd = false, Del = false
            }).ToList()
        });

    /// <summary>دسترسی کامل ولی فقط روی یک کارگاه — مثل payscoped.</summary>
    public static FakePay2AccessService ScopedToWorkshop(int wsId, params string[] forms)
    {
        var svc = FullAccess(forms);
        svc._access.UserCo = 9003;
        svc._access.AllowedWorkshopIds = new List<int> { wsId };
        return svc;
    }

    /// <summary>کنترل دسترسی خاموش — حالت پیش‌فرض نصب روی مشتری.</summary>
    public static FakePay2AccessService Disabled() =>
        new(new Pay2AccessDto { UserCo = 5, AclEnforced = false, WsScopeEnforced = false });

    public Task<Pay2AccessDto> GetAccessAsync(int userCo) => Task.FromResult(_access);

    public Task<bool> HasAsync(int userCo, string form, int permVal)
        => Task.FromResult(_access.Has(form, permVal));

    public Task<bool> CanAccessWorkshopAsync(int userCo, int wsId)
        => Task.FromResult(!_access.AclEnforced
                           || !_access.WsScopeEnforced
                           || _access.AllowedWorkshopIds.Contains(wsId));

    public Task<IReadOnlyList<int>> GetAllowedWorkshopIdsAsync(int userCo)
        => Task.FromResult<IReadOnlyList<int>>(_access.AllowedWorkshopIds);

    public Task InvalidateAsync(int userCo)
    {
        Invalidated.Add(userCo);
        return Task.CompletedTask;
    }

    /// <summary>چند بار کشِ تنظیمات دور ریخته شد — ذخیره‌ی تنظیمات باید این را زیاد کند.</summary>
    public int ConfigInvalidations { get; private set; }

    public Task InvalidateConfigAsync()
    {
        ConfigInvalidations++;
        return Task.CompletedTask;
    }

    public Task AuditAsync(Pay2AuditEntry entry)
    {
        AuditLog.Add(entry);
        return Task.CompletedTask;
    }
}
