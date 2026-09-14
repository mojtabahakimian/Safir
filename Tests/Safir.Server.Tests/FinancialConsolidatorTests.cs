using Safir.Server.CostClose;
using Safir.Shared.Models.CostClose;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// تجمیعِ صورت‌های مالیِ چند ماه.
///
/// حساس‌ترین بخشِ گزارشِ چندماهه همین است: جمعِ ساده‌ی سطرها عددِ غلط
/// می‌دهد، چون موجودیِ پایانِ هر ماه همان موجودیِ آغازِ ماه بعد است. این
/// تست‌ها دقیقاً همان را قفل می‌کنند.
/// </summary>
public class FinancialConsolidatorTests
{
    private static PeriodStatementsDto Month(
        short year, byte month,
        double matOpen, double purchase, double matClose,
        double wage, double oh,
        double fgOpen, double fgClose,
        double sales, double s12Cost, double expenses)
    {
        var matAvail = matOpen + purchase;
        var matUsed  = matAvail - matClose;
        var cogm     = matUsed + wage + oh;
        var cogs     = fgOpen + cogm - fgClose;
        var gross    = sales - s12Cost;

        return new PeriodStatementsDto
        {
            Year  = year,
            Month = month,
            Label = $"{FinancialConsolidator.MonthName(month)} {year}",
            Statements = new FinancialStatementsDto
            {
                Cogm = new()
                {
                    new() { Row = 10, Text = "موجودی اول دوره مواد", Amount = matOpen,  Kind = 0 },
                    new() { Row = 20, Text = "خرید مواد طی دوره",     Amount = purchase, Kind = 0 },
                    new() { Row = 30, Text = "مواد آماده مصرف",       Amount = matAvail, Kind = 1 },
                    new() { Row = 40, Text = "کسر: موجودی پایان دوره مواد", Amount = -matClose, Kind = 0 },
                    new() { Row = 50, Text = "مواد مصرف‌شده",          Amount = matUsed,  Kind = 1 },
                    new() { Row = 60, Text = "دستمزد",                Amount = wage,     Kind = 0 },
                    new() { Row = 70, Text = "سربار ساخت",            Amount = oh,       Kind = 0 },
                    new() { Row = 80, Text = "بهای تمام‌شده کالای ساخته‌شده", Amount = cogm, Kind = 2 },
                },
                Cogs = new()
                {
                    new() { Row = 10, Text = "موجودی اول دوره کالای ساخته‌شده", Amount = fgOpen, Kind = 0 },
                    new() { Row = 20, Text = "بهای تمام‌شده کالای ساخته‌شده",   Amount = cogm,   Kind = 0 },
                    new() { Row = 30, Text = "کالای آماده فروش",              Amount = fgOpen + cogm, Kind = 1 },
                    new() { Row = 40, Text = "کسر: موجودی پایان دوره",         Amount = -fgClose, Kind = 0 },
                    new() { Row = 50, Text = "بهای تمام‌شده کالای فروش‌رفته",   Amount = cogs,   Kind = 2 },
                    new() { Row = 60, Text = "ــ تطبیق",                      Amount = s12Cost, Kind = 3 },
                    new() { Row = 70, Text = "ــ اختلاف",                     Amount = cogs - s12Cost, Kind = 3 },
                },
                Income = new()
                {
                    new() { Row = 10,  Text = "فروش خالص",         Amount = sales,     Kind = 0 },
                    new() { Row = 20,  Text = "کسر: بهای فروش‌رفته", Amount = -s12Cost, Kind = 0 },
                    new() { Row = 30,  Text = "سود ناخالص",        Amount = gross,     Kind = 1 },
                    new() { Row = 80,  Text = "جمع هزینه‌های دوره",  Amount = -expenses, Kind = 1 },
                    new() { Row = 90,  Text = "سود عملیاتی",       Amount = gross - expenses, Kind = 2 },
                    new() { Row = 100, Text = "ــ درصد سود ناخالص",
                            Amount = sales != 0 ? Math.Round(gross / sales * 100, 1) : null, Kind = 3 },
                    new() { Row = 110, Text = "ــ درصد سود عملیاتی",
                            Amount = sales != 0 ? Math.Round((gross - expenses) / sales * 100, 1) : null, Kind = 3 },
                }
            }
        };
    }

    private static double Row(List<FinLineDto> lines, int row) =>
        lines.First(l => l.Row == row).Amount ?? 0;

    /// <summary>
    /// دو ماهِ پشت‌سرهم که موجودیِ پایانِ اولی همان موجودیِ آغازِ دومی است —
    /// یعنی همان حالتی که جمعِ ساده در آن غلط می‌شود.
    /// </summary>
    private static List<PeriodStatementsDto> TwoMonths() => new()
    {
        //             open  buy   close wage oh   fgOpen fgClose sales  s12   exp
        Month(1405, 1, 1000, 5000, 2000, 800, 400, 300,   900,    9000,  6000, 1200),
        Month(1405, 2, 2000, 7000, 2500, 900, 500, 900,   1100,  11000,  7500, 1300),
    };

    [Fact]
    public void Opening_comes_from_the_first_month_not_the_sum()
    {
        var notes = new List<string>();
        var c = FinancialConsolidator.Consolidate(TwoMonths(), notes);

        // فروردین ۱۰۰۰ داشت و اردیبهشت ۲۰۰۰؛ جمعشان ۳۰۰۰ است و غلط.
        Assert.Equal(1000, Row(c.Cogm, 10));
        Assert.Equal(300,  Row(c.Cogs, 10));
    }

    [Fact]
    public void Closing_comes_from_the_last_month_not_the_sum()
    {
        var c = FinancialConsolidator.Consolidate(TwoMonths(), new());

        // سطر «کسر» منفی ذخیره می‌شود، پس ۲۵۰۰- یعنی موجودی پایانِ اردیبهشت.
        Assert.Equal(-2500, Row(c.Cogm, 40));
        Assert.Equal(-1100, Row(c.Cogs, 40));
    }

    [Fact]
    public void Flow_lines_are_summed()
    {
        var c = FinancialConsolidator.Consolidate(TwoMonths(), new());

        Assert.Equal(12000, Row(c.Cogm, 20));   // خرید: ۵۰۰۰ + ۷۰۰۰
        Assert.Equal(1700,  Row(c.Cogm, 60));   // دستمزد
        Assert.Equal(900,   Row(c.Cogm, 70));   // سربار
        Assert.Equal(20000, Row(c.Income, 10)); // فروش
    }

    [Fact]
    public void Totals_are_recomputed_from_the_consolidated_parts()
    {
        var c = FinancialConsolidator.Consolidate(TwoMonths(), new());

        // مواد مصرف‌شده = (۱۰۰۰ + ۱۲۰۰۰) − ۲۵۰۰
        Assert.Equal(13000, Row(c.Cogm, 30));
        Assert.Equal(10500, Row(c.Cogm, 50));
        Assert.Equal(13100, Row(c.Cogm, 80));   // + ۱۷۰۰ دستمزد + ۹۰۰ سربار
    }

    /// <summary>
    /// وقتی ماه‌ها پیوسته‌اند، موجودیِ میانی در جمعِ ساده حذف می‌شود و
    /// *جمعِ نهایی* اتفاقاً درست درمی‌آید. خطای جمعِ ساده در سطرهای
    /// نمایشی است: موجودی اول و پایان دوره را چند برابر نشان می‌دهد.
    ///
    /// این تست همان تفکیک را قفل می‌کند — اولین نسخه‌اش ادعا می‌کرد جمع
    /// هم غلط است و روی همین داده رد شد.
    /// </summary>
    [Fact]
    public void With_contiguous_months_the_total_telescopes_but_the_inventory_lines_do_not()
    {
        var months = TwoMonths();
        var c = FinancialConsolidator.Consolidate(months, new());

        // جمعِ نهایی: یکی است، چون ۹۰۰ـِ پایانِ فروردین همان ۹۰۰ـِ آغازِ
        // اردیبهشت است و در جمع حذف می‌شود.
        Assert.Equal(months.Sum(m => Row(m.Statements.Cogs, 50)), Row(c.Cogs, 50));

        // ولی سطرِ موجودی: جمعِ ساده ۱۲۰۰ می‌داد (۳۰۰ + ۹۰۰) که هیچ‌وقت
        // موجودیِ آغازِ این بازه نبوده.
        Assert.NotEqual(months.Sum(m => Row(m.Statements.Cogs, 10)), Row(c.Cogs, 10));
        Assert.Equal(300, Row(c.Cogs, 10));
    }

    /// <summary>
    /// و وقتی ماهی جا افتاده باشد، حتی جمعِ نهایی هم فرق می‌کند — چون
    /// دیگر چیزی برای حذف‌شدن نیست.
    /// </summary>
    [Fact]
    public void With_a_missing_month_even_the_total_differs()
    {
        var months = new List<PeriodStatementsDto>
        {
            Month(1405, 1, 1000, 5000, 2000, 800, 400, 300,  900,  9000, 6000, 1200),
            // خرداد: موجودیِ آغازش با پایانِ فروردین نمی‌خواند، چون
            // اردیبهشت انتخاب نشده.
            Month(1405, 3, 4000, 7000, 2500, 900, 500, 2200, 1100, 11000, 7500, 1300),
        };

        var c = FinancialConsolidator.Consolidate(months, new());

        Assert.NotEqual(months.Sum(m => Row(m.Statements.Cogs, 50)), Row(c.Cogs, 50));
    }

    [Fact]
    public void Percentages_are_recomputed_not_added()
    {
        var c = FinancialConsolidator.Consolidate(TwoMonths(), new());

        var sales = Row(c.Income, 10);
        var gross = Row(c.Income, 30);

        // جمعِ دو درصد بی‌معناست و می‌توانست از ۱۰۰ رد شود.
        Assert.Equal(Math.Round(gross / sales * 100, 1), Row(c.Income, 100));
        Assert.InRange(Row(c.Income, 100), -100, 100);
    }

    [Fact]
    public void A_gap_between_the_chosen_months_is_reported()
    {
        var notes = new List<string>();
        FinancialConsolidator.Consolidate(new List<PeriodStatementsDto>
        {
            Month(1405, 1, 1000, 5000, 2000, 800, 400, 300, 900, 9000, 6000, 1200),
            Month(1405, 3, 2000, 7000, 2500, 900, 500, 900, 1100, 11000, 7500, 1300),
        }, notes);

        Assert.Contains(notes, n => n.Contains("اردیبهشت"));
    }

    [Fact]
    public void A_single_month_consolidates_to_itself()
    {
        var one = new List<PeriodStatementsDto>
        {
            Month(1405, 1, 1000, 5000, 2000, 800, 400, 300, 900, 9000, 6000, 1200)
        };

        var c = FinancialConsolidator.Consolidate(one, new());

        Assert.Equal(Row(one[0].Statements.Cogm, 80),   Row(c.Cogm, 80));
        Assert.Equal(Row(one[0].Statements.Income, 90), Row(c.Income, 90));
    }
}
