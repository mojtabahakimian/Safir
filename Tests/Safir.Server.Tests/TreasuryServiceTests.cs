using Microsoft.Data.SqlClient;
using Dapper;
using Safir.Server.Treasury;
using Safir.Shared.Models.Treasury;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// خزانه‌داری — پورتِ فرمِ PGET_HEDِ WPF. قواعدِ خالص اینجا، و برابریِ سندی که
/// سفیر می‌سازد با سندی که WPF ساخته در <see cref="TreasurySanadEquivalenceTests"/>.
/// </summary>
public class TreasuryServiceTests
{
    [Theory]
    [InlineData("111-1-1", new[] { 111, 1, 1 })]
    [InlineData("115-1-25-3-4-6", new[] { 115, 1, 25, 3, 4, 6 })]
    [InlineData(" 211-1-1 ", new[] { 211, 1, 1 })]
    public void SplitHes_reads_all_levels(string hes, int[] expected)
    {
        var parts = TreasuryService.SplitHes(hes)!;
        Assert.Equal(expected, parts.Take(expected.Length));
        Assert.All(parts.Skip(expected.Length), p => Assert.Equal(int.MinValue, p));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("111-1")]
    [InlineData("111-a-1")]
    [InlineData("1-2-3-4-5-6-7")]
    public void SplitHes_rejects_bad_codes(string? hes) => Assert.Null(TreasuryService.SplitHes(hes));

    [Theory]
    [InlineData(14050708, true)]
    [InlineData(14000231, true)]   // اردیبهشت ۳۱ روز
    [InlineData(14050731, false)]  // مهر ۳۰ روز
    [InlineData(14051301, false)]
    [InlineData(1405078, false)]
    public void ValidDate_follows_persian_calendar(long d, bool ok) => Assert.Equal(ok, TreasuryService.ValidDate(d));

    /// <summary>عیناً BuildKhazSharh در GENSANADKHAZ: «خزانه داري شماره X مورخ yyyy/mm/dd» با ی عربی.</summary>
    [Fact]
    public void SanadSharh_matches_wpf_text()
        => Assert.Equal("خزانه داري شماره 12 مورخ 1405/07/08", TreasuryService.SanadSharh(12, 14050708));

    [Fact]
    public void Final_or_signed_treasury_is_locked_plain_approval_is_not()
    {
        Assert.NotNull(TreasuryService.LockReason(new TreasuryListItemDto { Final = true }));
        Assert.Contains("امضا", TreasuryService.LockReason(new TreasuryListItemDto { Sgn2 = true }), StringComparison.Ordinal);
        Assert.Null(TreasuryService.LockReason(new TreasuryListItemDto { Okf = true }));
    }

    [Theory]
    [InlineData("000000000519", true)]
    [InlineData("000000000518", false)]
    [InlineData("000000000019", false)]
    [InlineData("00000", false)]
    [InlineData(null, false)]
    public void Markaz_column_follows_wpf_special_print_code(string? options, bool on)
        => Assert.Equal(on, TreasuryService.MarkazEnabled(options));

    [Fact]
    public void Visibility_see_all_dept_or_own_user()
    {
        var all = TreasuryService.VisibilityFilter(new TreasuryService.UserPerms { SeeAll = true, OwnDept = true }, 5, "x");
        Assert.Equal("1 = 1", all.Sql);

        var dept = TreasuryService.VisibilityFilter(new TreasuryService.UserPerms { OwnDept = true }, 5, "x");
        Assert.Contains("DEPATMAN", dept.Sql);

        var own = TreasuryService.VisibilityFilter(new TreasuryService.UserPerms(), 5, "آقاي كاظمي");
        Assert.Contains("USER_NAME IN", own.Sql);
        var vals = own.Args.GetType().GetProperties().Select(p => (string)p.GetValue(own.Args)!).ToList();
        Assert.Contains("آقای کاظمی", vals);   // فارسی
        Assert.Contains("آقاي كاظمي", vals);   // عربی
    }

    [Fact]
    public void Others_treasury_needs_dpsee()
    {
        var mine = new TreasuryListItemDto { UserName = "آقاي كاظمي" };
        Assert.Null(TreasuryService.OthersBlock(mine, new TreasuryService.UserPerms(), "آقای کاظمی")); // ی/ک یکسان
        var other = new TreasuryListItemDto { UserName = "Mr-kazemi" };
        Assert.NotNull(TreasuryService.OthersBlock(other, new TreasuryService.UserPerms(), "آقای کاظمی"));
        Assert.Null(TreasuryService.OthersBlock(other, new TreasuryService.UserPerms { EditOthers = true }, "آقای کاظمی"));
    }

    [Theory]
    [InlineData(1, 2, false)]  // دریافت چک → PAY_GETD
    [InlineData(1, 6, false)]
    [InlineData(1, 5, true)]   // برگشت چکِ پرداختی → PAY_GETP
    [InlineData(2, 2, true)]   // پرداخت چک → PAY_GETP
    [InlineData(2, 6, true)]
    [InlineData(2, 4, false)]  // واگذاری → PAY_GETD
    [InlineData(2, 5, false)]  // برگشت چکِ دریافتی → PAY_GETD
    public void Cheque_table_per_row(int noAm, int nahva, bool payable)
        => Assert.Equal(payable, TreasuryService.PayableTable(noAm, nahva));

    [Fact]
    public void Assign_is_payment_only_like_wpf()
    {
        Assert.False(TreasuryMethod.Allowed(TreasuryOp.Receipt, TreasuryMethod.ChequeAssign));
        Assert.True(TreasuryMethod.Allowed(TreasuryOp.Payment, TreasuryMethod.ChequeAssign));
        Assert.True(TreasuryMethod.Allowed(TreasuryOp.Receipt, TreasuryMethod.ChequeReturn));
    }

    /// <summary>چکِ گروهی: ماه به ماه، و روزِ ۳۱ در نیمه‌ی دوم سال به آخرِ ماه.</summary>
    [Theory]
    [InlineData(14050131, 1, 14050231)]
    [InlineData(14050631, 1, 14050730)]
    [InlineData(14051115, 2, 14060115)]
    [InlineData(14051130, 1, 14051229)]   // اسفندِ ۱۴۰۵ ۲۹ روز
    [InlineData(14050710, 0, 14050710)]
    public void Group_cheque_due_dates(long start, int months, long expected)
        => Assert.Equal(expected, TreasuryService.AddMonthsFa(start, months));

    [Fact]
    public void Assigned_received_cheque_is_in_circulation()
    {
        const int bankha = 112;
        Assert.False(new TreasuryChequeDto { Vaz = 1 }.InCirculation(bankha));                 // نزد صندوق
        Assert.False(new TreasuryChequeDto { Vaz = 1, NKol = 112 }.InCirculation(bankha));     // واگذار به بانک هنگامِ دریافت
        Assert.False(new TreasuryChequeDto { NKol = 911 }.InCirculation(bankha));              // پارک‌شده
        Assert.True(new TreasuryChequeDto { Vaz = 4, NKol = 115 }.InCirculation(bankha));      // واگذار به شخص
        Assert.True(new TreasuryChequeDto { NKol2 = 115 }.InCirculation(bankha));              // برگشت
        Assert.True(new TreasuryChequeDto { NKol3 = 112 }.InCirculation(bankha));              // وصول
        Assert.False(new TreasuryChequeDto { Payable = true, NKol = 112 }.InCirculation(bankha));
    }

    /// <summary>EMZA ِ Access: سربرگِ OLE («Paint.Picture») و BMP داخلش؛ PNG/JPEG ِ خام دست‌نخورده.</summary>
    [Fact]
    public void Signature_image_is_unwrapped_from_access_ole()
    {
        var bmp = new byte[40];
        bmp[0] = 0x42; bmp[1] = 0x4D;                       // BM
        BitConverter.GetBytes(40).CopyTo(bmp, 2);           // اندازه‌ی کل
        BitConverter.GetBytes(26).CopyTo(bmp, 10);          // شروعِ پیکسل‌ها
        var ole = new byte[] { 0x15, 0x1C, 0x2F, 0x00, 0x02, 0x00, 0x00, 0x00, 0x0D, 0x00, 0x0E, 0x00, 0x14, 0x00, 0x21, 0x00, 0xFF, 0xFF, 0xFF, 0xFF }
                  .Concat(System.Text.Encoding.ASCII.GetBytes("Bitmap Image\0Paint.Picture\0"))
                  .Concat(bmp).Concat(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 }).ToArray();

        var img = TreasuryService.ExtractImage(ole)!;
        Assert.Equal(bmp, img);
        Assert.Equal("image/bmp", TreasuryService.ImageContentType(img));

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0 };
        Assert.Same(png, TreasuryService.ExtractImage(png));
        Assert.Null(TreasuryService.ExtractImage(new byte[] { 1, 2, 3 }));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    public void Cheque_methods(int nahva, bool cheque) => Assert.Equal(cheque, TreasuryMethod.IsCheque(nahva));
}

/// <summary>فقط با SAFIR_TREASURY_TEST_DB (connection string به یک کپیِ دیتابیسِ واقعی) اجرا می‌شود.</summary>
public sealed class TreasuryDbFactAttribute : FactAttribute
{
    public TreasuryDbFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAFIR_TREASURY_TEST_DB")))
            Skip = "SAFIR_TREASURY_TEST_DB تنظیم نشده (connection string به یک کپیِ دیتابیس با خزانه‌های WPF).";
    }
}

/// <summary>
/// سندی که <see cref="TreasuryService.RebuildSanadAsync"/> می‌سازد باید ردیف‌به‌ردیف همان
/// سندی باشد که WPF (GENSANADKHAZ) برای همان خزانه ساخته. روی پایگاهِ واقعی، داخلِ
/// تراکنشی که همیشه برگردانده می‌شود. بدون SAFIR_TREASURY_TEST_DB رد می‌شود.
/// </summary>
public class TreasurySanadEquivalenceTests
{
    private static string? Cs => Environment.GetEnvironmentVariable("SAFIR_TREASURY_TEST_DB");

    private sealed record Line(string? HES, double BED, double BES, string? SHARH, double? N_SERI, int? BANK, int? MHAZ_NO, int? HES_K, int? HES_M, int? HES_T);

    [TreasuryDbFact]
    public async Task Rebuilt_sanad_equals_the_one_wpf_built()
    {
        await using var conn = new SqlConnection(Cs);
        await conn.OpenAsync();
        var ids = (await conn.QueryAsync<int>(@"
            SELECT TOP 25 h.ID FROM dbo.PGET_HED h
            JOIN dbo.DEED_HED d ON d.N_S = h.N_S AND d.NO_S = 5 AND ISNULL(d.GHATEI, 0) = 0
            WHERE EXISTS (SELECT 1 FROM dbo.PGET_LST l WHERE l.ID = h.ID)
            ORDER BY h.ID DESC")).ToList();
        Assert.NotEmpty(ids); // دیتابیسِ داده‌شده باید خزانه‌ی سنددار داشته باشد

        const string lines = @"SELECT HES, ISNULL(BED,0) BED, ISNULL(BES,0) BES, SHARH, N_SERI, BANK, MHAZ_NO, HES_K, HES_M, HES_T
                               FROM dbo.DEED_DTL WHERE N_S = (SELECT N_S FROM dbo.PGET_HED WHERE ID = @id)";
        foreach (var id in ids)
        {
            using var tx = conn.BeginTransaction();
            try
            {
                var before = (await conn.QueryAsync<Line>(lines, new { id }, tx)).OrderBy(Key).ToList();
                var nsBefore = await conn.ExecuteScalarAsync<double>("SELECT N_S FROM dbo.PGET_HED WHERE ID = @id", new { id }, tx);

                await TreasuryService.RebuildSanadAsync(conn, tx, id);

                var after = (await conn.QueryAsync<Line>(lines, new { id }, tx)).OrderBy(Key).ToList();
                var nsAfter = await conn.ExecuteScalarAsync<double>("SELECT N_S FROM dbo.PGET_HED WHERE ID = @id", new { id }, tx);

                Assert.Equal(nsBefore, nsAfter);           // همان شماره سند
                Assert.Equal(before.Count, after.Count);   // همان تعداد آرتیکل
                Assert.Equal(before, after);               // ردیف‌به‌ردیف یکی
            }
            finally
            {
                tx.Rollback();
            }
        }
    }

    private static string Key(Line l) => $"{l.HES}|{l.BED:F2}|{l.BES:F2}|{l.SHARH}|{l.N_SERI}|{l.BANK}|{l.MHAZ_NO}";
}
