using System.Net;
using System.Net.Http;
using Safir.Client.Components.Hesabdari;
using Safir.Client.Services;
using Safir.Server.Hesabdari;
using Safir.Shared.Models.Hesabdari;
using Xunit;
using L = Safir.Shared.Models.Hesabdari.TrialBalanceLevel;

namespace Safir.Server.Tests;

/// <summary>
/// تراز آزمایشی باید عیناً همان رویه‌ها و پارامترهایی را صدا بزند که پنجره‌های WPF
/// (TARAZ_4 … TARAZ_TAF4_DIRECT) می‌زنند؛ وگرنه عددِ سفیر و ویندوزی از هم جدا می‌شود.
/// </summary>
public class TrialBalanceServiceTests
{
    private static TrialBalanceQuery Q(L level) => new()
    {
        Level = level, From = 14050101, To = 14051230,
        Kol = 104, Moin = 9, Tafsili = 2044, Tafsili2 = 3, Tafsili3 = 7
    };

    [Theory]
    [InlineData(L.Kol, "dbo.TARAZ_4", "TARAZ_4")]
    [InlineData(L.Moin, "dbo.TARAZ4_MOIN", "TARAZ_4_MOIN")]
    [InlineData(L.Tafsili, "dbo.TARAZ4_TAFZ_DIRECT", "TARAZ_4_TAFZ")]
    [InlineData(L.Tafsili2, "dbo.TARAZ4_TAFZ2_DIRECT", "TARAZ_4_TAFZ")]
    [InlineData(L.Tafsili3, "dbo.TARAZ4_TAFZ3_DIRECT", "TARAZ_4_TAFZ")]
    [InlineData(L.Tafsili4, "dbo.TARAZ4_TAFZ4_DIRECT", "TARAZ_4_TAFZ")]
    public void EachLevelUsesTheWpfProcedureAndForm(L level, string proc, string form)
    {
        Assert.Equal(proc, TrialBalanceService.ProcedureFor(level));
        Assert.Equal(form, TrialBalanceService.FormFor(level));
    }

    [Fact]
    public void ParametersHaveWpfNamesAndTypes()
    {
        var p = TrialBalanceService.BuildParameters(Q(L.Tafsili4));
        Assert.Equal(14050101L, Assert.IsType<long>(p["Forms___FMENU_TARAZ_4___DT1"]));
        Assert.Equal(14051230L, Assert.IsType<long>(p["Forms___FMENU_TARAZ_4___DT2"]));
        // «همه‌ی اسناد» در WPF: ۰ تا ۹۲۹۲۹۲۹۲۹، از نوع double
        Assert.Equal(0d, Assert.IsType<double>(p["Forms___FMENU_TARAZ_4___SNDNUM1"]));
        Assert.Equal(929292929d, Assert.IsType<double>(p["Forms___FMENU_TARAZ_4___SNDNUM2"]));
        // شماره‌ی حساب‌ها را WPF رشته می‌فرستد
        Assert.Equal("104", p["KOL"]);
        Assert.Equal("9", p["MOIN"]);
        Assert.Equal("2044", p["TAF"]);
        Assert.Equal("3", p["TAF2"]);
        Assert.Equal("7", p["TAF3"]);
    }

    [Fact]
    public void EachLevelSendsOnlyTheParametersItsProcedureDeclares()
    {
        // پارامترِ اضافه برای رویه‌ی SQL خطای «too many arguments» است.
        string[] Keys(L l) => TrialBalanceService.BuildParameters(Q(l)).Keys.Where(k => !k.StartsWith("Forms___")).ToArray();
        Assert.Empty(Keys(L.Kol));
        Assert.Equal(new[] { "KOL" }, Keys(L.Moin));
        Assert.Equal(new[] { "KOL", "MOIN" }, Keys(L.Tafsili));
        Assert.Equal(new[] { "KOL", "MOIN", "TAF" }, Keys(L.Tafsili2));
        Assert.Equal(new[] { "KOL", "MOIN", "TAF", "TAF2" }, Keys(L.Tafsili3));
        Assert.Equal(new[] { "KOL", "MOIN", "TAF", "TAF2", "TAF3" }, Keys(L.Tafsili4));
    }

    [Fact]
    public void SanadRangeIsPassedThroughWhenGiven()
    {
        var q = Q(L.Kol); q.SanadFrom = 10; q.SanadTo = 20;
        var p = TrialBalanceService.BuildParameters(q);
        Assert.Equal(10d, p["Forms___FMENU_TARAZ_4___SNDNUM1"]);
        Assert.Equal(20d, p["Forms___FMENU_TARAZ_4___SNDNUM2"]);
    }

    [Fact]
    public void AmountsGetTheSameFixAsWpf()
    {
        // WPF: Math.Abs(Math.Truncate(x)) روی هر چهار ستون
        var r = TrialBalanceService.Map(L.Kol, new TrialBalanceService.ProcRow
        {
            NUMBER = 104, NAME = "موجودی", SumOfBED = 1500.9, SumOfBES = -200.7, bed = null, bes = -0.4
        });
        Assert.Equal(1500, r.SumBed);
        Assert.Equal(200, r.SumBes);
        Assert.Equal(0, r.Bed);
        Assert.Equal(0, r.Bes);
    }

    [Fact]
    public void NameComesFromTheColumnEachWpfGridShows()
    {
        var src = new TrialBalanceService.ProcRow
        {
            N_KOL = 104, NUMBER = 9, TNUMBER = 2044, HES_T2 = 3, TNUMBER3 = 7, TNUMBER4 = 1,
            NAME = "name", moin = "moin", TAFZIL = "tafzil"
        };
        Assert.Equal("name", TrialBalanceService.Map(L.Kol, src).Name);
        Assert.Equal("moin", TrialBalanceService.Map(L.Moin, src).Name);
        Assert.Equal("tafzil", TrialBalanceService.Map(L.Tafsili, src).Name);
        Assert.Equal("name", TrialBalanceService.Map(L.Tafsili2, src).Name);

        // در سطح کل، NUMBER خودِ حساب کل است؛ پایین‌تر NUMBER معین است و کل در N_KOL.
        Assert.Equal(9, TrialBalanceService.Map(L.Kol, src).Kol);
        var m = TrialBalanceService.Map(L.Moin, src);
        Assert.Equal((104, 9), (m.Kol, m.Moin));
        Assert.Null(m.Tafsili);
        var t4 = TrialBalanceService.Map(L.Tafsili4, src);
        Assert.Equal((104, 9, 2044, 3, 7, 1), (t4.Kol, t4.Moin, t4.Tafsili, t4.Tafsili2, t4.Tafsili3, t4.Tafsili4));
        Assert.Null(TrialBalanceService.Map(L.Tafsili2, src).Tafsili3);
    }

    [Theory]
    [InlineData(L.Moin, "کل")]
    [InlineData(L.Tafsili, "معین")]
    [InlineData(L.Tafsili4, "تفصیلی ۳")]
    public void DrillDownNeedsEveryParentAccount(L level, string missing)
    {
        var q = Q(level);
        switch (level)
        {
            case L.Moin: q.Kol = null; break;
            case L.Tafsili: q.Moin = null; break;
            default: q.Tafsili3 = null; break;
        }
        Assert.Contains(missing, TrialBalanceService.Validate(q));
    }

    [Fact]
    public void ValidationRejectsBadRanges()
    {
        Assert.Null(TrialBalanceService.Validate(Q(L.Tafsili4)));
        var q = Q(L.Kol); q.From = 14051230; q.To = 14050101;
        Assert.NotNull(TrialBalanceService.Validate(q));
        q = Q(L.Kol); q.To = 0;
        Assert.NotNull(TrialBalanceService.Validate(q));
        q = Q(L.Kol); q.SanadFrom = 50; q.SanadTo = 10;
        Assert.NotNull(TrialBalanceService.Validate(q));
        q = Q(L.Kol); q.Level = (L)99;
        Assert.NotNull(TrialBalanceService.Validate(q));
    }

    [Fact]
    public void PermissionIsCheckedPerLevel()
    {
        var onlyKol = (Kol: true, Moin: false, Tafsili: false);
        Assert.True(TrialBalanceService.Allowed(onlyKol, L.Kol));
        Assert.False(TrialBalanceService.Allowed(onlyKol, L.Moin));
        Assert.False(TrialBalanceService.Allowed(onlyKol, L.Tafsili3));
        var noKol = (Kol: false, Moin: true, Tafsili: true);
        Assert.False(TrialBalanceService.Allowed(noKol, L.Kol));
        Assert.True(TrialBalanceService.Allowed(noKol, L.Tafsili4));
    }

    [Fact]
    public void ClientQueryStringCarriesEveryField()
    {
        var q = Q(L.Tafsili4); q.SanadFrom = 5; q.SanadTo = 1234567;
        var s = TrialBalanceApiService.QueryString(q);
        foreach (var part in new[] { "level=Tafsili4", "from=14050101", "to=14051230", "sanadFrom=5", "sanadTo=1234567",
                                     "kol=104", "moin=9", "tafsili=2044", "tafsili2=3", "tafsili3=7" })
            Assert.Contains(part, s.Split('&'));
        Assert.DoesNotContain("sanadFrom", TrialBalanceApiService.QueryString(Q(L.Kol)));
    }

    // ───────────── تراز تفصیلیِ همه‌ی معین‌ها (FT4T با فیلد معینِ خالی) ─────────────

    [Fact]
    public void AllMoinsSendsPercentLikeWpf()
    {
        var q = new TrialBalanceQuery { Level = L.Tafsili, From = 14050101, To = 14051230, Kol = 104, AllMoins = true };
        Assert.Null(TrialBalanceService.Validate(q));
        Assert.Equal("%", TrialBalanceService.BuildParameters(q)["MOIN"]);
    }

    [Theory]
    [InlineData(L.Kol)]
    [InlineData(L.Moin)]
    [InlineData(L.Tafsili2)]
    public void AllMoinsIsOnlyForTheTafsiliLevel(L level)
    {
        var q = Q(level); q.AllMoins = true;
        Assert.NotNull(TrialBalanceService.Validate(q));
    }

    // ───────────── تراز ماهانه ─────────────

    [Fact]
    public void MonthlyGoesOnlyDownToTafsili()
    {
        Assert.Null(TrialBalanceService.ValidateMonthly(new TrialBalanceQuery { Level = L.Kol, From = 14050101, To = 14051230 }));
        Assert.Null(TrialBalanceService.ValidateMonthly(new TrialBalanceQuery { Level = L.Moin, From = 14050101, To = 14051230, Kol = 104 }));
        // تفصیلیِ ماهانه بدون معین = همه‌ی معین‌های آن کل
        Assert.Null(TrialBalanceService.ValidateMonthly(new TrialBalanceQuery { Level = L.Tafsili, From = 14050101, To = 14051230, Kol = 104 }));
        Assert.NotNull(TrialBalanceService.ValidateMonthly(new TrialBalanceQuery { Level = L.Moin, From = 14050101, To = 14051230 }));
        Assert.NotNull(TrialBalanceService.ValidateMonthly(Q(L.Tafsili2)));
    }

    [Fact]
    public void MonthlySqlGroupsByYearMonthAndUsesSafirNameTables()
    {
        var kol = TrialBalanceService.MonthlySql(L.Kol);
        Assert.Contains("dbo.TOTA_HES", kol);
        Assert.DoesNotContain("@kol", kol);
        Assert.Contains("H.DATE_S / 100", kol);     // YYYYMM، نه فقط ماه — سال‌ها قاطی نشوند
        Assert.Contains("H.N_S BETWEEN @sf AND @st", kol);

        var moin = TrialBalanceService.MonthlySql(L.Moin);
        Assert.Contains("dbo.DETA_HES", moin);
        Assert.Contains("D.HES_K = @kol", moin);

        var taf = TrialBalanceService.MonthlySql(L.Tafsili);
        Assert.Contains("dbo.TDETA_HES", taf);
        Assert.Contains("@moin IS NULL OR D.HES_M = @moin", taf);
        Assert.Contains("D.HES_K, D.HES_M, D.HES_T", taf);
        // جدول‌هایی که پنجره‌ی WPF حدس زده و در schema نیستند
        Assert.DoesNotContain("TAFZILI", taf);
    }

    // ───────────── منطقِ نمایش (مشترکِ صفحه و چاپ) ─────────────

    [Fact]
    public void MonthlyPivotNetsEachMonthAndTotals()
    {
        var rows = new List<TrialBalanceMonthlyRowDto>
        {
            new() { Kol = 104, Name = "موجودی", Ym = 140501, Bed = 100 },
            new() { Kol = 104, Name = "موجودی", Ym = 140503, Bes = 30 },
            new() { Kol = 201, Name = "پرداختنی", Ym = 140502, Bes = 50 },
        };
        var pv = TrialBalanceView.Pivot(rows, L.Kol);
        Assert.Equal(new[] { 140501, 140502, 140503 }, pv.Months);
        Assert.Equal(new int?[] { 104, 201 }, pv.Accounts.Select(a => a.Kol));
        Assert.Equal(70, pv.Accounts[0].Total);              // ۱۰۰ بدهکار − ۳۰ بستانکار
        Assert.Equal(-50, pv.MonthTotal(140502));
        Assert.Equal(20, pv.Total);
        Assert.Single(TrialBalanceView.Pivot(rows, L.Kol, q: "پرداختنی").Accounts);
        Assert.Equal("خرداد", TrialBalanceView.MonthName(140503));
    }

    [Fact]
    public void AllMoinsCodesShowTheMoinToo()
    {
        var a = new TrialBalanceRowDto { Kol = 104, Moin = 9, Tafsili = 2044, Name = "a" };
        var b = new TrialBalanceRowDto { Kol = 104, Moin = 3, Tafsili = 7000, Name = "b" };
        Assert.Equal("9/2044", TrialBalanceView.Code(L.Tafsili, a, withMoin: true));
        Assert.Equal("2044", TrialBalanceView.Code(L.Tafsili, a));
        // مرتب‌سازی پیش‌فرض: اول معین، بعد تفصیلی
        Assert.Equal(new[] { "b", "a" }, TrialBalanceView.Filter(new[] { a, b }, L.Tafsili, null, false, "code", false, withMoin: true).Select(r => r.Name));
        Assert.Equal("104-9-2044", TrialBalanceView.Hes(a));
    }

    [Fact]
    public void PrintUrlCarriesTheViewState()
    {
        var q = new TrialBalanceQuery { Level = L.Tafsili, From = 14050101, To = 14050131, Kol = 104, AllMoins = true };
        var url = TrialBalanceView.PrintUrl(q, monthly: true, path: "تراز کل › ۱۰۴", q: "خامه", hideZero: true, sort: "bed", desc: true);
        Assert.StartsWith("/trial-balance/print?", url);
        foreach (var part in new[] { "level=Tafsili", "kol=104", "allMoins=true", "monthly=true", "hideZero=true", "sort=bed", "desc=true" })
            Assert.Contains(part, url.Split('?')[1].Split('&'));
        Assert.Contains("path=" + Uri.EscapeDataString("تراز کل › ۱۰۴"), url);
        Assert.Contains("q=" + Uri.EscapeDataString("خامه"), url);
    }

    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task ServerPersianErrorReachesTheUser()
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.Forbidden, "اجازه‌ی دیدن این تراز (فرم TARAZ_4_MOIN) را ندارید."))
        { BaseAddress = new Uri("https://localhost/") };
        var (rows, error) = await new TrialBalanceApiService(http).LoadAsync(Q(L.Moin));
        Assert.Null(rows);
        Assert.Contains("TARAZ_4_MOIN", error);
    }
}
