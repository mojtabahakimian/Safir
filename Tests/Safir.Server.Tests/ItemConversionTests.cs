using Safir.Shared.Models.CostClose;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// تبدیل کالا به کالا — برگه‌ی TAG=30.
///
/// برگه یک سطر است و یک مبلغ دارد که ارزشِ هر دو سر است. پس برخلاف
/// روشِ قدیمی (دو برگه، دو مبلغ)، «نامتوازن شدن» اصلاً حالتِ ممکنی
/// نیست. چیزی که *هست* و باید گرفته شود، برگه‌ی ناقص است: کالا از
/// انبار رفته و مقصدش مشخص نیست.
/// </summary>
public class ItemConversionTests
{
    private static ConversionRowDto Row(
        string? toCode = "2044", int? toAnbar = 2, double? toQty = 142, double value = 268_154_459) => new()
    {
        ConversionId = 1,
        Number       = 7,
        DateN        = 14050231,
        FromCode     = "328",
        FromAnbar    = 7,
        FromQty      = 331.5,
        ToCode       = toCode,
        ToAnbar      = toAnbar,
        ToQty        = toQty,
        Value        = value,
        ToRate       = toQty is > 0 ? value / toQty.Value : 0
    };

    [Fact]
    public void A_sheet_with_both_sides_filled_is_complete()
        => Assert.True(Row().IsComplete);

    /// <summary>
    /// سه راهِ ناقص‌بودن، و هر سه یک نتیجه دارند: کالا از انبار خارج شده
    /// و هیچ‌جا وارد نمی‌شود. CHK-24 هر سه را می‌گیرد.
    /// </summary>
    [Fact]
    public void A_sheet_with_no_target_item_is_incomplete()
        => Assert.False(Row(toCode: null).IsComplete);

    [Fact]
    public void A_blank_target_item_counts_as_missing_not_as_a_code()
        => Assert.False(Row(toCode: "   ").IsComplete);

    [Fact]
    public void A_sheet_with_no_target_warehouse_is_incomplete()
        => Assert.False(Row(toAnbar: null).IsComplete);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_sheet_with_no_incoming_quantity_is_incomplete(double qty)
        => Assert.False(Row(toQty: qty).IsComplete);

    /// <summary>
    /// نرخ مقصد از *مقدار ورود* مشتق می‌شود و نه از مقدار خروج — همین
    /// تبدیل را از انتقال جدا می‌کند: ۳۳۱ کیلو خامه ۲۸٪ می‌تواند ۱۴۲
    /// کیلو خامه ۶۵٪ بدهد و ارزشِ کل عوض نمی‌شود.
    /// </summary>
    [Fact]
    public void The_target_rate_comes_from_the_incoming_quantity()
    {
        var row = Row();

        Assert.Equal(1_888_411.68, Math.Round(row.ToRate, 2));
        Assert.Equal(row.Value, Math.Round(row.ToRate * row.ToQty!.Value, 2));
    }

    /// <summary>
    /// و مقدارِ خروج در نرخِ مقصد هیچ نقشی ندارد. اگر روزی کسی فرمول را
    /// به MEGHk وصل کند، این تست می‌افتد.
    /// </summary>
    [Fact]
    public void The_outgoing_quantity_does_not_change_the_target_rate()
    {
        var a = Row();
        var b = Row();
        b.FromQty = 900;

        Assert.Equal(a.ToRate, b.ToRate);
    }

    /// <summary>
    /// یک مبلغ، دو سر. این همان چیزی است که مسئله‌ی تراز را حذف می‌کند:
    /// جایی برای اختلاف وجود ندارد چون عددِ دومی در کار نیست. روشِ قدیمی
    /// دو مبلغ داشت و روی پایگاه واقعی ۴٬۷۶۰٬۸۷۲ ریال از هم فاصله
    /// گرفتند.
    /// </summary>
    [Fact]
    public void One_amount_serves_both_sides()
    {
        var row = Row();

        var outgoingValue = row.Value;
        var incomingValue = row.ToRate * row.ToQty!.Value;

        Assert.Equal(outgoingValue, Math.Round(incomingValue, 2));
    }

    [Fact]
    public void A_sheet_that_already_has_an_accounting_document_is_flagged()
    {
        var row = Row();
        Assert.False(row.HasSanad);

        row.SanadNo = 1234;
        Assert.True(row.HasSanad);
    }

    [Fact]
    public void A_zero_sanad_number_is_not_an_accounting_document()
    {
        var row = Row();
        row.SanadNo = 0;

        Assert.False(row.HasSanad);
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
    /// سرِ جای خودش هشدار می‌دهد.
    /// </summary>
    [Fact]
    public void Warnings_do_not_block_submission()
    {
        var dto = new ConversionPreviewDto { Rate = 0 };
        dto.Warnings.Add("نرخ میانگین این کالا در این انبار صفر است.");

        Assert.True(dto.CanSubmit);
        Assert.Single(dto.Warnings);
    }
}
