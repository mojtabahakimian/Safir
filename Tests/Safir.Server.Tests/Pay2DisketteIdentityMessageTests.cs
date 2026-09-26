using Safir.Server.Services;
using Xunit;
using Gap = Safir.Server.Services.Pay2DisketteService.InsuranceIdentityGap;

namespace Safir.Server.Tests;

/// <summary>
/// پیام خطای لیست بیمه در یزدسپار فقط اولین نفر را با «EMP_ID=26» نشان می‌داد و از
/// بازمحاسبه حرفی نمی‌زد؛ کاربر پرونده را اصلاح می‌کرد و باز همان خطا را می‌دید.
/// </summary>
public class Pay2DisketteIdentityMessageTests
{
    [Fact]
    public void NoGaps_NoMessage() =>
        Assert.Null(Pay2DisketteService.DescribeInsuranceIdentityGaps(new[]
        {
            new Gap("علی احمدی", "101", false, false, false),
        }));

    [Fact]
    public void ListsEveryPersonByNameAndCode_AndSaysToRecalculate()
    {
        var msg = Pay2DisketteService.DescribeInsuranceIdentityGaps(new[]
        {
            new Gap("علی احمدی", "418", true, false, false),
            new Gap("رضا صالحی", "571", true, false, false),
            new Gap("مریم کریمی", "600", false, false, false),
        })!;

        Assert.Contains("اطلاعات 2 نفر ناقص است", msg);
        Assert.Contains("علی احمدی (کد پرسنلی 418)", msg);
        Assert.Contains("رضا صالحی (کد پرسنلی 571)", msg);
        Assert.DoesNotContain("مریم کریمی", msg);
        Assert.DoesNotContain("EMP_ID", msg);
        Assert.Contains("معاف از بیمه", msg);
        Assert.Contains("پرسنل و احکام", msg);
        Assert.Contains("دوباره محاسبه", msg);
    }

    [Fact]
    public void OnePersonWithTwoGaps_CountedOnce_AppearsInBothGroups()
    {
        var msg = Pay2DisketteService.DescribeInsuranceIdentityGaps(new[]
        {
            new Gap("علی احمدی", "418", true, false, true),
        })!;

        Assert.Contains("اطلاعات 1 نفر ناقص است", msg);
        Assert.Contains("شماره بیمه ندارند", msg);
        Assert.Contains("کد شغل ندارند", msg);
    }

    [Fact]
    public void LongList_IsCapped()
    {
        var gaps = Enumerable.Range(1, 13).Select(i => new Gap($"نفر {i}", i.ToString(), true, false, false));
        var msg = Pay2DisketteService.DescribeInsuranceIdentityGaps(gaps)!;

        Assert.Contains("نفر 10 (کد پرسنلی 10)", msg);
        Assert.DoesNotContain("نفر 11 (", msg);
        Assert.Contains("و 3 نفر دیگر", msg);
    }
}
