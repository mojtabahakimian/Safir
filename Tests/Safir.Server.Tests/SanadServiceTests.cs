using Safir.Server.Sanad;
using Safir.Shared.Models.Sanad;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>قواعدِ «صدور و ویرایش اسناد» که مستقل از دیتابیس‌اند (DEED_HEAD ِ WPF).</summary>
public class SanadServiceTests
{
    private const string Ada = "113-1-1", Adv = "113-1-2", Apa = "211-1-1", Apv = "211-1-2";

    /// <summary>Child14_CellEditEnding: بدهکار روی اسناد دریافتنی → SGETCHEK، بستانکار روی پرداختنی → SPAYCHEK.</summary>
    [Theory]
    [InlineData(Ada, 100, 0, SanadChequeRole.Received)]
    [InlineData(Adv, 100, 0, SanadChequeRole.Received)]
    [InlineData(Ada, 0, 100, SanadChequeRole.None)]      // بستانکارِ اسناد دریافتنی (وصول/واگذاری) چکِ تازه نمی‌گیرد
    [InlineData(Apa, 0, 100, SanadChequeRole.Paid)]
    [InlineData(Apv, 0, 100, SanadChequeRole.Paid)]
    [InlineData(Apa, 100, 0, SanadChequeRole.None)]
    [InlineData("115-3-1591", 100, 0, SanadChequeRole.None)]
    [InlineData(" 113-1-1 ", 100, 0, SanadChequeRole.Received)]
    [InlineData("", 100, 0, SanadChequeRole.None)]
    public void Cheque_role_follows_wpf(string hes, double bed, double bes, SanadChequeRole role)
        => Assert.Equal(role, SanadRules.RoleOf(hes, bed, bes, Ada, Adv, Apa, Apv));

    [Fact]
    public void Empty_adv_does_not_match_every_account()
        => Assert.Equal(SanadChequeRole.None, SanadRules.RoleOf("115-1-1", 100, 0, Ada, "", Apa, null));

    /// <summary>Child14_RowEditEnding: «بدهکار یا بستانکار نمیتواند خالی باشد»، «بدهكار و بستانكار سند صحيح نمي باشد».</summary>
    [Theory]
    [InlineData(100, 0, true)]
    [InlineData(0, 250000, true)]
    [InlineData(0, 0, false)]
    [InlineData(100, 100, false)]
    [InlineData(-5, 0, false)]
    [InlineData(10.5, 0, false)]   // ریالی، بی‌اعشار (NumericTextBox با DoesAcceptDouble = false)
    [InlineData(double.NaN, 0, false)]
    public void Amounts_exactly_one_side_positive_integer(double bed, double bes, bool ok)
        => Assert.Equal(ok, SanadRules.AmountError(bed, bes) is null);

    /// <summary>Form_Current: امضای بالاتر امضاهای پایین‌تر را قفل می‌کند.</summary>
    [Fact]
    public void Higher_signature_locks_lower_slots()
    {
        Assert.Null(SanadService.SlotBlock(1, sgn2: false, sgn3: false));
        Assert.NotNull(SanadService.SlotBlock(1, sgn2: true, sgn3: false));
        Assert.Null(SanadService.SlotBlock(2, sgn2: true, sgn3: false));   // خودِ مدیر مالی امضایش را برمی‌دارد
        Assert.NotNull(SanadService.SlotBlock(2, sgn2: false, sgn3: true));
        Assert.Null(SanadService.SlotBlock(3, sgn2: true, sgn3: true));
    }

    [Fact]
    public void Lock_reasons_match_eslah_click()
    {
        var manual = new SanadListItemDto { NoS = 0 };
        Assert.Null(SanadService.LockReason(manual));
        Assert.Contains("قطعی", SanadService.LockReason(new SanadListItemDto { NoS = 0, Final = true }));
        Assert.Contains("اتوماتیک", SanadService.LockReason(new SanadListItemDto { NoS = 5 }));
        Assert.Contains("امضا", SanadService.LockReason(new SanadListItemDto { NoS = 0, Sgn2 = true }));
        // تأیید (OKF) به‌تنهایی قفل نیست — فقط یعنی «اصلاح سند» لازم است
        Assert.Null(SanadService.LockReason(new SanadListItemDto { NoS = 0, Okf = true }));
    }

    /// <summary>IsBalanceSanadOk: اختلافِ گردشده.</summary>
    [Fact]
    public void Balance_uses_rounded_difference()
    {
        Assert.True(new SanadListItemDto { SumBed = 1000.4, SumBes = 1000 }.Balanced);
        Assert.False(new SanadListItemDto { SumBed = 1001, SumBes = 1000 }.Balanced);
        Assert.Equal(-500, new SanadListItemDto { SumBed = 500, SumBes = 1000 }.Difference);
    }

    /// <summary>شرحِ ردیفِ چکی — SGETCHEK (بی‌فاصله) و SPAYCHEK (با فاصله بعد از «چك» و «بانك»).</summary>
    [Fact]
    public void Auto_description_matches_cheque_windows()
    {
        Assert.Equal("چك9900001بانكملت مرکزی مورخ 1405/08/30-آزمایش",
            SanadService.AutoSharh(SanadChequeRole.Received, 9900001, "ملت", "مرکزی", 14050830, "آزمایش"));
        Assert.Equal("چك 123بانك ملی  مورخ 1405/09/01-شرکت",
            SanadService.AutoSharh(SanadChequeRole.Paid, 123, "ملی", null, 14050901, "شرکت"));
        Assert.True(SanadService.AutoSharh(SanadChequeRole.Received, 1, "ب", "ش", 14050101, new string('x', 400)).Length <= 255);
    }

    [Fact]
    public void Kind_names_follow_no_s_mapping()
    {
        Assert.Equal("عمومی", SanadKinds.NameOf(0));
        Assert.Equal("خزانه‌داری", SanadKinds.NameOf(5));
        Assert.Equal("انبارگردانی", SanadKinds.NameOf(17));
        Assert.Equal("نوع 99", SanadKinds.NameOf(99));
    }

    [Theory]
    [InlineData(new[] { 115, 3, 1591, int.MinValue, int.MinValue, int.MinValue }, 3)]
    [InlineData(new[] { 115, 3, 26, 1, int.MinValue, int.MinValue }, 4)]
    public void Account_depth_counts_real_levels(int[] parts, int depth)
        => Assert.Equal(depth, SanadService.Depth(parts));
}
