using Safir.Server.Services;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// رگرسیون‌تست دیسکت بیمه‌ی شهریور ۱۴۰۵ یزدسپار: پنج پرسنل شماره بیمه‌ی «0» داشتند و
/// بدون هیچ هشداری با شماره‌ی 0000000000 در DSKWOR00 می‌رفتند — فایلی که سامانه‌ی
/// تأمین اجتماعی رد می‌کند. صفر باید مثل خالی رفتار کند تا پیش از ساخت فایل خطا بدهد.
/// </summary>
public class Pay2DisketteInsuranceCodeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("0000000000")]
    [InlineData(" 00 ")]
    public void ZeroOrBlankIsMissing(string? code) =>
        Assert.True(Pay2DisketteService.IsMissingInsuranceCode(code));

    [Theory]
    [InlineData("80083160")]
    [InlineData("0080083160")]
    [InlineData("10")]
    public void RealNumbersAreAccepted(string code) =>
        Assert.False(Pay2DisketteService.IsMissingInsuranceCode(code));

    // دیسکت پذیرفته‌شده‌ی تیر ۱۴۰۵ «80277820» دارد؛ شماره‌ای که با صفر پیشرو در پرونده ثبت شده
    // هم باید همان‌طور برود، نه «0080277820».
    [Theory]
    [InlineData("80277820", "80277820")]
    [InlineData("0080277820", "80277820")]
    [InlineData(" 0080277820 ", "80277820")]
    public void DisketteCodeHasNoLeadingZeros(string stored, string expected) =>
        Assert.Equal(expected, Pay2DisketteService.CleanInsuranceCode(stored));
}
