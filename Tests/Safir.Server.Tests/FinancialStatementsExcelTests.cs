using System.IO.Compression;
using System.Xml.Linq;
using Safir.Client.Components.CostClose;
using Safir.Shared.Models.CostClose;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// فایلِ xlsx دستی ساخته می‌شود، پس یک اشتباهِ کوچک در XML باعث می‌شود
/// اکسل فایل را «خراب» بخواند — و آن را فقط موقعِ باز کردن می‌فهمیم، نه
/// موقعِ ساختن. این تست‌ها همان بررسی را خودکار می‌کنند: هر بخش باید
/// وجود داشته باشد، XMLـش معتبر باشد، و ارجاع‌های نمودار به سلول‌های
/// درست بخورند.
/// </summary>
public class FinancialStatementsExcelTests
{
    private static FinLineDto L(int row, string text, double? amount, byte kind = 0) =>
        new() { Row = row, Text = text, Amount = amount, Kind = kind };

    private static PeriodStatementsDto Period(short year, byte month, double sales) => new()
    {
        Year = year, Month = month, Label = $"ماه {month} {year}",
        Statements = new FinancialStatementsDto
        {
            Income = new()
            {
                L(10, "فروش خالص", sales),
                L(20, "کسر: بهای تمام‌شده کالای فروش‌رفته", -sales * 0.7),
                L(30, "سود ناخالص", sales * 0.3, 1),
                L(90, "سود عملیاتی", sales * 0.2, 2),
                L(100, "ــ درصد سود ناخالص", 30, 3),
            },
            Cogm = new()
            {
                L(10, "موجودی اول دوره مواد", 1000),
                L(20, "خرید مواد طی دوره", 5000),
                L(80, "بهای تمام‌شده کالای ساخته‌شده", 6000, 2),
            },
            Cogs = new()
            {
                L(10, "موجودی اول دوره کالای ساخته‌شده", 300),
                L(50, "بهای تمام‌شده کالای فروش‌رفته", 5200, 2),
            },
            Expenses = new()
        }
    };

    private static MultiPeriodStatementsDto TwoPeriods()
    {
        var d = new MultiPeriodStatementsDto
        {
            Periods = new() { Period(1405, 1, 10_000), Period(1405, 2, 12_000) },
            ConsolidatedLabel = "فروردین تا اردیبهشت 1405"
        };
        d.Consolidated = d.Periods[0].Statements;   // برای این تست کافی است
        return d;
    }

    private static Dictionary<string, string> Unzip(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(
            e => e.FullName,
            e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); });
    }

    [Fact]
    public void Every_part_the_chart_needs_is_in_the_package()
    {
        var parts = Unzip(FinancialStatementsExcel.Build(TwoPeriods()));

        Assert.Contains("[Content_Types].xml", parts.Keys);
        Assert.Contains("xl/workbook.xml", parts.Keys);
        Assert.Contains("xl/worksheets/sheet1.xml", parts.Keys);
        Assert.Contains("xl/charts/chart1.xml", parts.Keys);
        Assert.Contains("xl/drawings/drawing1.xml", parts.Keys);
        Assert.Contains("xl/drawings/_rels/drawing1.xml.rels", parts.Keys);
        Assert.Contains("xl/worksheets/_rels/sheet1.xml.rels", parts.Keys);
    }

    /// <summary>
    /// بخشی که در Content_Types اعلام نشده باشد، اکسل کل فایل را رد
    /// می‌کند — و پیامش هم نمی‌گوید کدام بخش.
    /// </summary>
    [Fact]
    public void Chart_and_drawing_are_declared_in_content_types()
    {
        var parts = Unzip(FinancialStatementsExcel.Build(TwoPeriods()));
        var ct = parts["[Content_Types].xml"];

        Assert.Contains("/xl/charts/chart1.xml", ct);
        Assert.Contains("/xl/drawings/drawing1.xml", ct);
        Assert.Contains("drawingml.chart+xml", ct);
    }

    [Fact]
    public void Every_part_is_well_formed_xml()
    {
        var parts = Unzip(FinancialStatementsExcel.Build(TwoPeriods()));

        foreach (var (name, xml) in parts)
        {
            var ex = Record.Exception(() => XDocument.Parse(xml));
            Assert.True(ex is null, $"{name} معتبر نیست: {ex?.Message}");
        }
    }

    /// <summary>
    /// نمودار باید به همان سطری اشاره کند که «فروش خالص» در آن نشسته.
    /// اگر ترتیب سطرها عوض شود و این ارجاع به‌روز نشود، نمودار بی‌صدا رقمِ
    /// دیگری را رسم می‌کند — بدترین حالتِ ممکن، چون غلط بودنش دیده نمی‌شود.
    /// </summary>
    [Fact]
    public void The_chart_points_at_the_rows_that_actually_hold_those_figures()
    {
        var parts = Unzip(FinancialStatementsExcel.Build(TwoPeriods()));
        var sheet = XDocument.Parse(parts["xl/worksheets/sheet1.xml"]);
        var chart = parts["xl/charts/chart1.xml"];

        var ns = sheet.Root!.Name.Namespace;

        // سطری که در ستون B نوشته «فروش خالص»
        int RowOf(string label) => sheet.Descendants(ns + "row")
            .First(r => r.Elements(ns + "c")
                         .Any(c => (string?)c.Attribute("r") is { } a && a.StartsWith("B") &&
                                   c.Descendants(ns + "t").Any(t => t.Value == label)))
            .Attribute("r")!.Value.Let(int.Parse);

        var salesRow = RowOf("فروش خالص");
        var grossRow = RowOf("سود ناخالص");

        Assert.Contains($"Sheet1!$B${salesRow}", chart);
        Assert.Contains($"Sheet1!$C${salesRow}:$D${salesRow}", chart);
        Assert.Contains($"Sheet1!$B${grossRow}", chart);
    }

    [Fact]
    public void Infinity_is_written_as_a_label_not_a_number()
    {
        var d = TwoPeriods();
        d.Periods[0].Statements.Income[0].Amount = double.PositiveInfinity;

        var parts = Unzip(FinancialStatementsExcel.Build(d));
        var sheet = parts["xl/worksheets/sheet1.xml"];

        Assert.DoesNotContain("<v>∞</v>", sheet);
        Assert.DoesNotContain("<v>Infinity</v>", sheet);
        Assert.Contains("بی‌نهایت", sheet);
    }

    /// <summary>یک دوره هم باید فایلِ معتبر بدهد، حتی بدون ستون تجمیعی.</summary>
    [Fact]
    public void A_single_period_still_produces_a_valid_file()
    {
        var d = new MultiPeriodStatementsDto { Periods = { Period(1405, 1, 10_000) } };

        var parts = Unzip(FinancialStatementsExcel.Build(d));
        foreach (var (name, xml) in parts)
            Assert.True(Record.Exception(() => XDocument.Parse(xml)) is null, name);
    }
}

internal static class LetExtensions
{
    public static TOut Let<TIn, TOut>(this TIn v, Func<TIn, TOut> f) => f(v);
}
