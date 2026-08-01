using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Safir.Server.Services;
using Safir.Shared.Constants;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// تنظیماتِ کنترل دسترسی ۳۰ ثانیه کش می‌شوند. این کش باعث شده بود روشن کردن
/// ACL_ENFORCE از صفحه‌ی «تنظیمات سیستم» بی‌اثر به‌نظر برسد: مقدار در دیتابیس
/// عوض می‌شد ولی سرور تا سر رسیدن TTL همان مقدار قبلی را می‌خواند و کاربر
/// نتیجه می‌گرفت که ذخیره نشده است.
///
/// این آزمون همان مسیر را می‌سنجد: کهنه ماندنِ عمدی، و تازه شدن پس از
/// InvalidateConfigAsync — کاری که Pay2SettingsController بعد از هر ذخیره می‌کند.
/// </summary>
public class Pay2AccessCacheTests
{
    private const int UserCo = 7;

    private static (Pay2AccessService svc, InMemoryDatabase db) Build(string aclEnforce)
    {
        var db = new InMemoryDatabase();
        db.Config["ACL_ENFORCE"] = aclEnforce;
        db.UserForms[UserCo] = new()
        {
            new(Pay2Forms.Employee, "پرسنل", Run: false, See: false, Inp: false, Upd: false, Del: false),
        };

        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new Pay2AccessService(db, cache), db);
    }

    [Fact]
    public async Task Config_is_cached_so_a_bare_database_change_is_not_seen_yet()
    {
        var (svc, db) = Build("0");

        Assert.False((await svc.GetAccessAsync(UserCo)).AclEnforced);

        db.Config["ACL_ENFORCE"] = "1";

        // بدون باطل کردن کش، همان مقدار قدیمی برمی‌گردد — این خودِ رفتار
        // کش است، نه یک نقص؛ فقط باید یک راه صریح برای دور زدنش وجود داشته باشد.
        Assert.False((await svc.GetAccessAsync(UserCo)).AclEnforced);
    }

    [Fact]
    public async Task InvalidateConfigAsync_makes_a_new_ACL_ENFORCE_take_effect_at_once()
    {
        var (svc, db) = Build("0");

        var before = await svc.GetAccessAsync(UserCo);
        Assert.False(before.AclEnforced);
        // با خاموش بودن کنترل دسترسی، همه چیز مجاز است.
        Assert.True(await svc.HasAsync(UserCo, Pay2Forms.Employee, (int)Server.Security.Pay2Perm.Upd));

        db.Config["ACL_ENFORCE"] = "1";
        await svc.InvalidateConfigAsync();

        var after = await svc.GetAccessAsync(UserCo);
        Assert.True(after.AclEnforced);
        // و حالا نبودِ مجوز در SAL_CHEK واقعاً جلوی کاربر را می‌گیرد.
        Assert.False(await svc.HasAsync(UserCo, Pay2Forms.Employee, (int)Server.Security.Pay2Perm.Upd));
    }

    [Fact]
    public async Task Turning_enforcement_back_off_also_takes_effect_at_once()
    {
        var (svc, db) = Build("1");

        Assert.False(await svc.HasAsync(UserCo, Pay2Forms.Employee, (int)Server.Security.Pay2Perm.Upd));

        db.Config["ACL_ENFORCE"] = "0";
        await svc.InvalidateConfigAsync();

        Assert.True(await svc.HasAsync(UserCo, Pay2Forms.Employee, (int)Server.Security.Pay2Perm.Upd));
    }
}
