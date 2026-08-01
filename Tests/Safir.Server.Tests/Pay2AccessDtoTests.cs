using Safir.Server.Security;
using Safir.Shared.Models.Permissions;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// آزمون ریاضیِ بیتیِ مجوزها. این همان کدی است که هم سرور و هم کلاینت
/// (برای پنهان‌کردن دکمه‌ها) از آن استفاده می‌کنند، پس باید دقیقاً یکسان
/// جواب بدهد وگرنه رابط کاربری چیزی را نشان می‌دهد که سرور رد می‌کند.
/// </summary>
public class Pay2AccessDtoTests
{
    private const string Form = "PAY2_EMPLOYEE";

    private static Pay2AccessDto Access(bool run, bool see, bool inp, bool upd, bool del,
                                        bool enforced = true) =>
        new()
        {
            AclEnforced = enforced,
            WsScopeEnforced = true,
            Forms = new List<Pay2FormPermDto>
            {
                new() { FormName = Form, Run = run, See = see, Inp = inp, Upd = upd, Del = del }
            }
        };

    [Theory]
    [InlineData((int)Pay2Perm.Run)]
    [InlineData((int)Pay2Perm.See)]
    [InlineData((int)Pay2Perm.Inp)]
    [InlineData((int)Pay2Perm.Upd)]
    [InlineData((int)Pay2Perm.Del)]
    public void Each_flag_maps_to_its_own_column(int perm)
    {
        Assert.True(Access(true, true, true, true, true).Has(Form, perm));
        Assert.False(Access(false, false, false, false, false).Has(Form, perm));
    }

    [Fact]
    public void A_single_missing_column_only_blocks_that_flag()
    {
        var a = Access(run: true, see: true, inp: true, upd: false, del: true);

        Assert.True(a.Has(Form, (int)Pay2Perm.Inp));
        Assert.True(a.Has(Form, (int)Pay2Perm.Del));
        Assert.False(a.Has(Form, (int)Pay2Perm.Upd));
    }

    [Fact]
    public void Combined_flags_require_every_one_of_them()
    {
        var a = Access(run: true, see: true, inp: true, upd: false, del: false);

        Assert.True(a.Has(Form, (int)(Pay2Perm.Run | Pay2Perm.See)));
        Assert.True(a.Has(Form, (int)(Pay2Perm.Run | Pay2Perm.Inp)));
        // ویرایش را ندارد، پس ترکیبی که شامل ویرایش است باید رد شود.
        Assert.False(a.Has(Form, (int)(Pay2Perm.Inp | Pay2Perm.Upd)));
        Assert.False(a.Has(Form, (int)(Pay2Perm.Run | Pay2Perm.Del)));
    }

    [Fact]
    public void An_unknown_form_is_denied_rather_than_allowed()
    {
        // اگر فرمی در جدول دسترسی نباشد، پیش‌فرض باید «نه» باشد نه «بله».
        var a = Access(true, true, true, true, true);

        Assert.False(a.Has("PAY2_SOMETHING_NEW", (int)Pay2Perm.See));
    }

    [Fact]
    public void When_enforcement_is_off_even_an_unknown_form_is_allowed()
    {
        var a = Access(false, false, false, false, false, enforced: false);

        Assert.True(a.Has(Form, (int)Pay2Perm.Del));
        Assert.True(a.Has("PAY2_ANYTHING", (int)Pay2Perm.Del));
    }

    [Fact]
    public void Permission_value_of_zero_is_not_a_way_around_the_check()
    {
        // Pay2Perm.None هیچ بیتی ندارد. مهم است که این یک راه فرار نشود،
        // پس هر اکشن باید مجوز مشخص و غیرصفر بگیرد — نگهبان CI همین را می‌پاید.
        var a = Access(false, false, false, false, false);

        // با هیچ بیتی، حلقه‌ی بررسی چیزی را رد نمی‌کند و نتیجه true است.
        // این رفتار مستند است، نه تصادفی: اتریبیوت بدون مجوز معنا ندارد.
        Assert.True(a.Has(Form, (int)Pay2Perm.None));
    }

    [Fact]
    public void CanRun_is_the_same_as_asking_for_the_Run_flag()
    {
        Assert.True(Access(true, false, false, false, false).CanRun(Form));
        Assert.False(Access(false, true, true, true, true).CanRun(Form));
    }

    // ── محدوده کارگاه ────────────────────────────────────────────────────

    [Fact]
    public async Task Scoped_user_sees_only_the_workshops_they_are_assigned()
    {
        var svc = FakePay2AccessService.ScopedToWorkshop(1, Form);

        Assert.True(await svc.CanAccessWorkshopAsync(9003, 1));
        Assert.False(await svc.CanAccessWorkshopAsync(9003, 2));
        Assert.False(await svc.CanAccessWorkshopAsync(9003, 99));
    }

    [Fact]
    public async Task Unscoped_user_reaches_every_workshop()
    {
        var svc = FakePay2AccessService.FullAccess(Form);

        Assert.True(await svc.CanAccessWorkshopAsync(9001, 1));
        Assert.True(await svc.CanAccessWorkshopAsync(9001, 2));
    }

    [Fact]
    public async Task Workshop_scoping_is_bypassed_when_enforcement_is_off()
    {
        var svc = FakePay2AccessService.Disabled();

        Assert.True(await svc.CanAccessWorkshopAsync(5, 12345));
    }
}
