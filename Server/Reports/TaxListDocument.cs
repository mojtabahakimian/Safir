using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary.Reports;
using System.Globalization;

namespace Safir.Server.Reports;

public sealed class TaxListDocument : IDocument
{
    private readonly TaxReportDto _data;
    private const string Font = "IRANYekanFN";
    private static readonly CultureInfo FaCulture = new("fa-IR");
    public TaxListDocument(TaxReportDto data) => _data = data;
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A3.Landscape());
        page.Margin(8, Unit.Millimetre);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(6.5f));
        page.ContentFromRightToLeft();
        page.Header().Element(ComposeHeader);
        page.Content().Element(ComposeContent);
        page.Footer().Element(ComposeFooter);
    });

    private void ComposeHeader(IContainer container) => container.PaddingBottom(8).Column(col =>
    {
        col.Item().Row(row =>
        {
            row.RelativeItem().AlignRight().Text($"سال: {_data.PeriodYear}").FontSize(10).SemiBold();
            row.RelativeItem(2).AlignCenter().Text("لیست مالیات بر درآمد حقوق کارکنان").FontSize(14).Bold();
            row.RelativeItem().AlignLeft().Text($"ماه: {_data.PeriodMonthName}").FontSize(10).SemiBold();
        });
        col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);
        col.Item().Row(row =>
        {
            row.RelativeItem(2).Text($"نام کارگاه: {_data.WorkshopName}").SemiBold();
            row.RelativeItem(2).Text($"نام کارفرما: {_data.EmployerName}");
            row.RelativeItem().Text($"شماره کارگاه: {_data.WorkshopCode}");
            row.RelativeItem().Text($"شناسه مالیاتی: {_data.TaxCode}").SemiBold();
        });
        col.Item().PaddingTop(2).Text($"نشانی کارگاه: {_data.Address}").FontColor(Colors.Grey.Darken2);
    });

    private void ComposeContent(IContainer container) => container.Column(main =>
    {
        main.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(22); c.RelativeColumn(2.5f); c.RelativeColumn(1.3f); c.RelativeColumn(1.6f);
                c.RelativeColumn(1.05f); c.RelativeColumn(1.05f); c.ConstantColumn(28);
                for (var i = 0; i < 11; i++) c.RelativeColumn(1.25f);
            });
            table.Header(h =>
            {
                foreach (var title in new[] { "ردیف", "نام و نام خانوادگی", "کد ملی", "شغل", "شروع کار", "ترک کار", "روز", "پایه مزد روزانه", "پایه سنوات روزانه", "دستمزد روزانه کل", "دستمزد ماهانه", "سایر مزایای مشمول", "جمع دستمزد و مزایای مشمول", "جمع ناخالص", "درآمد مشمول مالیات", "بیمه سهم کارگر", "مالیات حقوق", "مانده قابل پرداخت" })
                    h.Cell().Element(Header).Text(title).SemiBold();
            });
            foreach (var e in _data.Rows)
            {
                Cell(e.RowIndex.ToString()); Cell(e.FullName, false); Cell(e.NationalCode); Cell(e.JobTitle, false); Cell(e.HireDate); Cell(e.FireDate);
                Cell(e.WorkDays.ToString("0.##", FaCulture)); Cell(Money(e.BaseDailyWage)); Cell(Money(e.SeniorityDailyBase)); Cell(Money(e.TotalDailyWage));
                Cell(Money(e.MonthlyWage)); Cell(Money(e.OtherSubjectBenefits)); Cell(Money(e.TotalSubject)); Cell(Money(e.GrossPay));
                Cell(Money(e.TaxBase)); Cell(Money(e.WorkerPremium)); Cell(Money(e.TaxAmount)); Cell(Money(e.NetPayable));
            }
            table.Footer(f =>
            {
                f.Cell().ColumnSpan(6).Element(Footer).AlignRight().PaddingRight(5).Text("جمع کل (ریال):").SemiBold();
                f.Cell().Element(Footer).Text(_data.TotalWorkDays.ToString("0.##", FaCulture));
                for (var i = 0; i < 3; i++) f.Cell().Element(Footer).Text("-");
                f.Cell().Element(Footer).Text(Money(_data.Rows.Sum(x => x.MonthlyWage)));
                f.Cell().Element(Footer).Text(Money(_data.Rows.Sum(x => x.OtherSubjectBenefits)));
                f.Cell().Element(Footer).Text(Money(_data.Rows.Sum(x => x.TotalSubject)));
                f.Cell().Element(Footer).Text(Money(_data.TotalGrossPay)); f.Cell().Element(Footer).Text(Money(_data.TotalTaxBase)).Bold();
                f.Cell().Element(Footer).Text(Money(_data.Rows.Sum(x => x.WorkerPremium)));
                f.Cell().Element(Footer).Text(Money(_data.TotalTaxAmount)).Bold();
                f.Cell().Element(Footer).Text(Money(_data.Rows.Sum(x => x.NetPayable))).Bold();
            });
            void Cell(string value, bool center = true) => table.Cell().Element(center ? BodyCenter : BodyRight).Text(value);
        });
        main.Item().PaddingTop(18).Row(row =>
        {
            row.RelativeItem().Border(1).MinHeight(75).AlignCenter().AlignMiddle().Text("مهر و امضای کارفرما").SemiBold();
            row.ConstantItem(20);
            row.RelativeItem().Border(1).MinHeight(75).AlignCenter().AlignMiddle().Text("تأیید مدیر مالی / مسئول تهیه لیست").SemiBold();
        });
    });

    private static void ComposeFooter(IContainer c) => c.AlignCenter().Text(t => { t.Span("صفحه "); t.CurrentPageNumber(); t.Span(" از "); t.TotalPages(); });
    private static IContainer Header(IContainer c) => c.Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().AlignMiddle();
    private static IContainer BodyCenter(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Medium).Padding(2).AlignCenter().AlignMiddle();
    private static IContainer BodyRight(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Medium).Padding(2).AlignRight().AlignMiddle();
    private static IContainer Footer(IContainer c) => c.Border(1).Background(Colors.Grey.Lighten4).Padding(2).AlignCenter().AlignMiddle();
    private static string Money(long value) => value.ToString("N0", FaCulture);
}
