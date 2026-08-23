using Safir.Server.Services;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// رگرسیون‌تستِ گزارش مشتری در تیر ۱۴۰۵: «مزایای ماهانه مشمول بیمه» برای یک
/// پرسنل ۱۲۷٬۰۳۴٬۳۸۸ درآمد به‌جای ۶۰٬۰۰۰٬۰۰۰، چون حق شیفت (۶۷٬۰۳۴٬۳۸۸) هم
/// مشمول بیمه و هم مشمول مالیات حساب شده بود.
///
/// نکته‌ی اصلی که این تست نگه می‌دارد: دو ریلِ بیمه و مالیات **مستقل**‌اند.
/// روشن کردنِ تنهای قاعده‌ی بیمه مالیات را کم نمی‌کند، بلکه زیاد می‌کند —
/// چون بیمه‌ی سهم کارگرِ کسرشدنی از مبنای مالیات کوچک‌تر می‌شود در حالی که
/// خود حق شیفت هنوز در آن مبناست. پس دو کلید جدا لازم است و
/// BlocksInsurance/BlocksTax هرگز نباید به هم گره بخورند.
/// </summary>
public class Pay2NonSubjectRuleTests
{
    // تاریخ اثر = اول تیر ۱۴۰۵
    private const long Tir1405 = 14050401;

    [Theory]
    [InlineData("SHIFT")]
    [InlineData("OT_NORMAL")]
    [InlineData("OT_HOLIDAY")]
    [InlineData("OT_ADMIN")]
    public void RuleCoversShiftAndOvertimeItems(string itemCode) =>
        Assert.True(Pay2NonSubjectRule.IsRuleItem(itemCode));

    [Theory]
    [InlineData("HOME")]        // مسکن
    [InlineData("GROCERY")]     // بن کارگری
    [InlineData("FAMILY_ALLOW")] // حق تاهل
    [InlineData("CHILDREN")]    // حق اولاد
    [InlineData("BASE_SAL")]
    [InlineData(null)]
    public void RuleNeverTouchesTheThreeSubjectBenefitsOrBase(string? itemCode) =>
        Assert.False(Pay2NonSubjectRule.IsRuleItem(itemCode));

    [Fact]
    public void BothRailsOffByDefault()
    {
        var rule = new Pay2NonSubjectRule(InsuranceEffectiveFrom: 0, TaxEffectiveFrom: 0);

        Assert.False(rule.BlocksInsurance(null));
        Assert.False(rule.BlocksTax(null));
    }

    /// <summary>
    /// همان حالتی که مشتری در آن گیر کرده بود: قاعده‌ی بیمه روشن، قاعده‌ی
    /// مالیات خاموش. ثبت «مشمول بیمه» باید رد شود ولی «مشمول مالیات» آزاد
    /// بماند — یعنی سیستم هنوز اجازه می‌دهد حق شیفت مالیات بخورد.
    /// </summary>
    [Fact]
    public void InsuranceRuleAloneDoesNotBlockTaxOverride()
    {
        var rule = new Pay2NonSubjectRule(InsuranceEffectiveFrom: Tir1405, TaxEffectiveFrom: 0);

        Assert.True(rule.BlocksInsurance(null));
        Assert.False(rule.BlocksTax(null));
    }

    /// <summary>قرینه‌اش: فعال کردن مالیات نباید ریل بیمه را قفل کند.</summary>
    [Fact]
    public void TaxRuleAloneDoesNotBlockInsuranceOverride()
    {
        var rule = new Pay2NonSubjectRule(InsuranceEffectiveFrom: 0, TaxEffectiveFrom: Tir1405);

        Assert.False(rule.BlocksInsurance(null));
        Assert.True(rule.BlocksTax(null));
    }

    [Fact]
    public void BothRulesOnBlocksBothOverrides()
    {
        var rule = new Pay2NonSubjectRule(InsuranceEffectiveFrom: Tir1405, TaxEffectiveFrom: Tir1405);

        Assert.True(rule.BlocksInsurance(null));
        Assert.True(rule.BlocksTax(null));
    }

    /// <summary>
    /// حکمی که کاملاً قبل از تاریخ اثر تمام شده هنوز باید بتواند Override
    /// مشمول داشته باشد — وگرنه ویرایش سوابق ماه‌های گذشته قفل می‌شود.
    /// مقایسه در سطح ماه است، چون موتور هم PERIOD_DATE را در سطح ماه می‌سنجد.
    /// </summary>
    [Theory]
    [InlineData(14050331L, false)] // خرداد — قبل از تاریخ اثر، آزاد
    [InlineData(14050431L, true)]  // تیر — همان ماهِ اثر، مسدود
    [InlineData(14050531L, true)]  // مرداد — بعد از تاریخ اثر، مسدود
    [InlineData(null, true)]       // بازه‌ی باز، مسدود
    public void OnlyRangesReachingTheEffectiveMonthAreBlocked(long? validTo, bool expected)
    {
        var rule = new Pay2NonSubjectRule(InsuranceEffectiveFrom: Tir1405, TaxEffectiveFrom: Tir1405);

        Assert.Equal(expected, rule.BlocksInsurance(validTo));
        Assert.Equal(expected, rule.BlocksTax(validTo));
    }
}
