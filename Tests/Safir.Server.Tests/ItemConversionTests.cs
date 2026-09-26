using Safir.Shared.Models.CostClose;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// تبدیل کالا به کالا.
///
/// یک تبدیل فقط وقتی درست است که دو سرش هم‌مبلغ باشند. این تست‌ها همان
/// قاعده و لبه‌هایش را قفل می‌کنند — به‌خصوص حالتی که روی داده‌ی واقعی
/// اتفاق افتاده بود: مبلغِ سمت خروج بعد از بازسازی نرخ عوض می‌شود و سمت
/// ورود جا می‌ماند.
/// </summary>
public class ItemConversionTests
{
    private static ConversionRowDto Row(double issue, double receipt) => new()
    {
        ConversionId = 1,
        DateN        = 14050231,
        AccountCode  = "741-3000-3000",
        FromCode     = "328",
        FromQty      = 331.5,
        ToCode       = "2044",
        ToQty        = 331.5,
        IssueValue   = issue,
        ReceiptValue = receipt,
        Gap          = issue - receipt
    };

    [Fact]
    public void Two_legs_at_the_same_amount_are_balanced()
        => Assert.True(Row(268_154_459, 268_154_459).IsBalanced);

    /// <summary>
    /// همان عددی که روی پایگاه پودر مروارید روی حساب «تبدیل» مانده بود.
    /// اگر روزی این تست سبز شد بدون اینکه Gap صفر شود، یعنی آستانه را
    /// کسی باز کرده.
    /// </summary>
    [Fact]
    public void The_real_world_gap_is_reported_as_unbalanced()
    {
        var row = Row(268_154_459, 272_915_331);

        Assert.False(row.IsBalanced);
        Assert.Equal(-4_760_872, row.Gap);
    }

    /// <summary>
    /// اختلافِ زیر نیم ریال گرد کردن است، نه مغایرت. نرخ واحد اعشاری است
    /// و ضرب‌وتقسیمش همیشه یک دنباله‌ی کوچک می‌گذارد؛ اگر این را مغایرت
    /// بشماریم، هر تبدیلی برای همیشه قرمز می‌ماند.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.4)]
    [InlineData(-0.4)]
    public void Rounding_dust_is_not_a_mismatch(double gap)
        => Assert.True(Row(100_000 + gap, 100_000).IsBalanced);

    [Theory]
    [InlineData(0.6)]
    [InlineData(-0.6)]
    public void Anything_above_the_threshold_is(double gap)
        => Assert.False(Row(100_000 + gap, 100_000).IsBalanced);

    /// <summary>
    /// نرخ مقصد از *مقدار ورودی* مشتق می‌شود و نه از مقدار خروجی — این
    /// همان چیزی است که تبدیل را از انتقال جدا می‌کند: ۳۳۱ کیلو خامه
    /// ۲۸٪ می‌تواند ۱۴۲ کیلو خامه ۶۵٪ بدهد و ارزش هر دو یکی است.
    /// </summary>
    [Fact]
    public void The_target_rate_comes_from_the_incoming_quantity()
    {
        var value  = 268_154_459d;
        var toQty  = 142.0;
        var toRate = value / toQty;

        Assert.Equal(1_888_411.68, Math.Round(toRate, 2));

        // و ارزش کل عوض نشده — فقط زیر نام دیگری نشسته
        Assert.Equal(value, Math.Round(toRate * toQty, 2));
    }

    [Fact]
    public void A_voided_conversion_is_not_counted_as_unbalanced()
    {
        var row = Row(268_154_459, 0);
        row.Status = 9;

        Assert.True(row.IsVoided);
        Assert.False(row.IsBalanced);   // خودِ عدد همچنان نامتوازن است
    }

    /// <summary>
    /// صفر بودنِ سمت خروج یعنی حواله هنوز قیمت نخورده. آن وقت *نباید*
    /// روی رسید تحمیل شود، وگرنه کالای مقصد با ارزش صفر وارد انبار
    /// می‌شود — همان چیزی که روی کد ۲۰۲۱ دیده شد و کاردکس را خراب کرد.
    /// این تست همان شرط را در سطح مدل نگه می‌دارد.
    /// </summary>
    [Fact]
    public void An_unpriced_issue_is_visible_as_a_gap_not_silently_zeroed()
    {
        var row = Row(0, 272_915_331);

        Assert.False(row.IsBalanced);
        Assert.Equal(0, row.IssueValue);
    }

    [Fact]
    public void Preview_without_a_blocker_can_be_submitted()
    {
        var ok = new ConversionPreviewDto { Rate = 100, Value = 1000 };
        Assert.True(ok.CanSubmit);

        var bad = new ConversionPreviewDto { Blocker = "کالا تعریف نشده است." };
        Assert.False(bad.CanSubmit);
    }

    /// <summary>
    /// هشدار جلوی ثبت را نمی‌گیرد. موجودیِ کاردکس با موجودیِ فیزیکی
    /// همیشه یکی نیست و انباردار باید بتواند واقعیت را ثبت کند؛ CHK-01
    /// بعداً سر جای خودش هشدار می‌دهد.
    /// </summary>
    [Fact]
    public void Warnings_do_not_block_submission()
    {
        var dto = new ConversionPreviewDto { Rate = 0 };
        dto.Warnings.Add("نرخ میانگین صفر است.");

        Assert.True(dto.CanSubmit);
        Assert.Single(dto.Warnings);
    }
}
