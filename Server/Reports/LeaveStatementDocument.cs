using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary;

namespace Safir.Server.Reports;

/// <summary>
/// صورت‌حساب قابل فهم مانده مرخصی یک پرسنل. اعداد خام دیتابیس عمداً در
/// کنار معادل روز/ساعت/دقیقه چاپ می‌شوند تا گزارش هم قابل رسیدگی باشد و هم
/// برای پرسنل غیرمالی قابل فهم بماند.
/// </summary>
public class LeaveStatementDocument : IDocument
{
    private const string PersianFontName = "IRANYekanFN";
    private readonly Pay2LeaveStatementDto _statement;

    public LeaveStatementDocument(Pay2LeaveStatementDto statement) => _statement = statement;

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.2f, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontFamily(PersianFontName).FontSize(10));
            page.ContentFromRightToLeft();
            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("این گزارش از اطلاعات ثبت‌شده در سیستم PAY2 تهیه شده است.  |  صفحه ").FontSize(8).FontColor(Colors.Grey.Darken1);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" از ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(12).Column(column =>
        {
            column.Item().AlignCenter().Text("صورت‌حساب شفاف مرخصی").FontSize(18).Bold().FontColor(Colors.Blue.Darken3);
            column.Item().PaddingTop(4).AlignCenter().Text($"سال {_statement.Year}").FontSize(12).SemiBold();
            column.Item().PaddingTop(10).Border(1).BorderColor(Colors.Grey.Lighten1).Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"نام پرسنل: {_statement.EmployeeName}").SemiBold();
                    c.Item().Text($"کد پرسنلی: {_statement.EmployeeCode}");
                });
                row.RelativeItem().AlignLeft().Text($"تاریخ چاپ: {_statement.PrintDate}");
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(12);
            column.Item().Element(ComposePlainLanguageSummary);
            column.Item().Element(ComposeEquation);
            column.Item().Element(ComposeCarryover);
            column.Item().Element(ComposeRules);
            column.Item().Text("ریز مرخصی‌های ثبت‌شده در این سال").FontSize(13).Bold();
            column.Item().Element(ComposeHistory);
        });
    }

    private void ComposeCarryover(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("انتقال مرخصی از سال قبل").FontSize(13).Bold();
            column.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(3);
                });

                AddCarryoverRow(table, "مانده پایان سال قبل", _statement.PreviousYearBalanceMin);
                AddCarryoverRow(table, $"سقف انتقال ({_statement.LeaveCarryoverMax} روز)", _statement.CarryoverLimitMin);
                AddCarryoverRow(table, "مقدار قابل انتقال طبق سقف", _statement.EligibleCarryoverMin);
                AddCarryoverRow(table, "مقدار واقعاً منتقل‌شده به امسال", _statement.CarriedInMin, true);
                AddCarryoverRow(table, "منتقل‌نشده / سوخت‌شده", _statement.ExpiredCarryoverMin, true);
            });
        });
    }

    private void AddCarryoverRow(TableDescriptor table, string title, int minutes, bool bold = false)
    {
        static IContainer Cell(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(7).AlignMiddle();
        table.Cell().Element(Cell).Text(title).Style(bold ? TextStyle.Default.Bold() : TextStyle.Default);
        table.Cell().Element(Cell).Text($"{FormatDuration(minutes, _statement.LeaveMinsPerDay)}  ({minutes:N0} دقیقه)")
            .Style(bold ? TextStyle.Default.Bold() : TextStyle.Default);
    }

    private void ComposePlainLanguageSummary(IContainer container)
    {
        container.Border(1).BorderColor(Colors.Blue.Lighten2).Background(Colors.Blue.Lighten5).Padding(12).Column(column =>
        {
            column.Item().Text("خلاصه خیلی ساده").FontSize(13).Bold().FontColor(Colors.Blue.Darken3);
            column.Item().PaddingTop(6).Text(text =>
            {
                text.Span("مانده قابل استفاده شما: ");
                text.Span(FormatDuration(_statement.BalanceMin, _statement.LeaveMinsPerDay)).FontSize(14).Bold().FontColor(_statement.BalanceMin < 0 ? Colors.Red.Darken2 : Colors.Green.Darken2);
                text.Span($"  ({_statement.BalanceMin:N0} دقیقه)").FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private void ComposeEquation(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("مانده شما چگونه حساب شده است؟").FontSize(13).Bold();
            column.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(3);
                    c.ConstantColumn(38);
                });

                AddEquationRow(table, "استحقاق مرخصی امسال", _statement.EntitlementMin, "+", Colors.Green.Lighten5);
                AddEquationRow(table, "مرخصی منتقل‌شده از سال قبل", _statement.CarriedInMin, "+", Colors.Green.Lighten5);
                AddEquationRow(table, "مرخصی استفاده‌شده", _statement.UsedMin, "−", Colors.Red.Lighten5);
                AddEquationRow(table, "مانده نهایی", _statement.BalanceMin, "=", Colors.Blue.Lighten5, true);
            });
        });
    }

    private void AddEquationRow(TableDescriptor table, string title, int minutes, string sign, string background, bool bold = false)
    {
        static IContainer Cell(IContainer c, string color) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Background(color).Padding(7).AlignMiddle();
        table.Cell().Element(c => Cell(c, background)).Text(title).Style(bold ? TextStyle.Default.Bold() : TextStyle.Default);
        table.Cell().Element(c => Cell(c, background)).Text($"{FormatDuration(minutes, _statement.LeaveMinsPerDay)}  ({minutes:N0} دقیقه)").Style(bold ? TextStyle.Default.Bold() : TextStyle.Default);
        table.Cell().Element(c => Cell(c, background)).AlignCenter().Text(sign).FontSize(14).Bold();
    }

    private void ComposeRules(IContainer container)
    {
        container.Border(1).BorderColor(Colors.Orange.Lighten2).Background(Colors.Orange.Lighten5).Padding(10).Column(column =>
        {
            column.Item().Text("دو نکته مهم").Bold().FontColor(Colors.Orange.Darken3);
            column.Item().PaddingTop(5).Text($"• طبق قانون، حداکثر {_statement.LeaveCarryoverMax} روز مرخصی از سال قبل قابل انتقال است؛ مقدار بیشتر از این سقف به سال جاری منتقل نمی‌شود.");
            column.Item().PaddingTop(3).Text($"• در این گزارش هر روز مرخصی دقیقاً {_statement.LeaveMinsPerDay:N0} دقیقه است. مدت هر درخواست از «روز/ساعت/دقیقه ثبت‌شده» محاسبه می‌شود، نه صرفاً فاصله تقویمی تاریخ شروع و پایان؛ بنابراین تعطیلاتی که در روز درخواستی نیامده‌اند در مدت درخواست منظور نشده‌اند.");
            column.Item().PaddingTop(3).Text("• عدد قطعیِ مصرف‌شده از مانده سالانه می‌آید. جدول پایین درخواست‌های نهایی را برای رسیدگی نشان می‌دهد و اختلاف احتمالی آن با مانده، جداگانه و بدون پنهان‌کاری چاپ می‌شود.");
        });
    }

    private void ComposeHistory(IContainer container)
    {
        container.Column(section =>
        {
            if (_statement.History.Count == 0)
            {
                section.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(15).AlignCenter()
                    .Text("برای این سال مرخصی نهایی‌شده‌ای ثبت نشده است.").FontColor(Colors.Grey.Darken1);
            }
            else
            {
                section.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(28);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2.2f);
                        c.RelativeColumn(2.7f);
                        c.RelativeColumn(2.5f);
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header, "ردیف");
                        HeaderCell(header, "از تاریخ");
                        HeaderCell(header, "تا تاریخ");
                        HeaderCell(header, "مدت درخواستی");
                        HeaderCell(header, "مدت درخواست نهایی");
                        HeaderCell(header, "توضیحات");
                    });

                    for (var index = 0; index < _statement.History.Count; index++)
                    {
                        var line = _statement.History[index];
                        BodyCell(table, (index + 1).ToString(), true);
                        BodyCell(table, FormatDate(line.START_DATE), true);
                        BodyCell(table, FormatDate(line.END_DATE), true);
                        BodyCell(table, FormatRequested(line), true);
                        BodyCell(table, $"{FormatDuration(line.TotalDeductedMinutes, _statement.LeaveMinsPerDay)}\n({line.TotalDeductedMinutes:N0} دقیقه)", true, true);
                        BodyCell(table, string.IsNullOrWhiteSpace(line.DESCRIPTION) ? "—" : line.DESCRIPTION!);
                    }
                });
            }

            section.Item().PaddingTop(8).Border(1)
                .BorderColor(_statement.HistoryToBalanceDifferenceMin == 0 ? Colors.Green.Lighten2 : Colors.Orange.Lighten2)
                .Background(_statement.HistoryToBalanceDifferenceMin == 0 ? Colors.Green.Lighten5 : Colors.Orange.Lighten5)
                .Padding(8).Column(column =>
                {
                    column.Item().Text($"جمع درخواست‌های نهایی نمایش‌داده‌شده: {FormatDuration(_statement.HistoryRequestedMin, _statement.LeaveMinsPerDay)} ({_statement.HistoryRequestedMin:N0} دقیقه)");
                    column.Item().Text($"مصرف قطعی ثبت‌شده در مانده: {FormatDuration(_statement.UsedMin, _statement.LeaveMinsPerDay)} ({_statement.UsedMin:N0} دقیقه)");
                    column.Item().Text($"اختلاف برای رسیدگی: {FormatDuration(_statement.HistoryToBalanceDifferenceMin, _statement.LeaveMinsPerDay)} ({_statement.HistoryToBalanceDifferenceMin:N0} دقیقه)")
                        .Bold();
                });
        });
    }

    private static void HeaderCell(TableCellDescriptor table, string value) =>
        table.Cell().Border(1).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().AlignMiddle().Text(value).SemiBold();

    private static void BodyCell(TableDescriptor table, string value, bool center = false, bool bold = false)
    {
        var cell = table.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(6).AlignMiddle();
        if (center) cell = cell.AlignCenter();
        var text = cell.Text(value);
        if (bold) text.Bold();
    }

    public static string FormatDuration(int totalMinutes, int minutesPerDay)
    {
        minutesPerDay = minutesPerDay > 0 ? minutesPerDay : 440;
        var sign = totalMinutes < 0 ? "منفی " : string.Empty;
        var remaining = Math.Abs((long)totalMinutes);
        var days = remaining / minutesPerDay;
        remaining %= minutesPerDay;
        var hours = remaining / 60;
        var minutes = remaining % 60;
        return $"{sign}{days:N0} روز، {hours:N0} ساعت و {minutes:N0} دقیقه";
    }

    private static string FormatRequested(Pay2LeaveStatementLineDto line) =>
        $"{line.REQ_DAYS:N0} روز، {line.REQ_HOURS:N0} ساعت و {line.REQ_MINUTES:N0} دقیقه";

    private static string FormatDate(long date)
    {
        var value = date.ToString("00000000");
        return $"{value[..4]}/{value.Substring(4, 2)}/{value.Substring(6, 2)}";
    }
}
