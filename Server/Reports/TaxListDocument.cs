using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary.Reports;
using System.Globalization;

namespace Safir.Server.Reports;

public sealed class TaxListDocument : IDocument
{
    private readonly TaxReportDto _data;
    private static readonly CultureInfo FaCulture = new("fa-IR");
    private const string Font = "IRANYekanFN";
    public TaxListDocument(TaxReportDto data) => _data = data;
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A3.Landscape());
        page.Margin(8, Unit.Millimetre);
        page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(7));
        page.ContentFromRightToLeft();
        page.Header().Column(c =>
        {
            c.Item().AlignCenter().Text("لیست مالیات بر درآمد حقوق کارکنان").FontSize(14).Bold();
            c.Item().Text($"کارگاه: {_data.WorkshopName} | کارفرما: {_data.EmployerName} | شناسه مالیاتی: {_data.TaxCode} | سال: {_data.PeriodYear} | ماه: {_data.PeriodMonthName}");
        });
        page.Content().PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(c => { c.ConstantColumn(22); c.RelativeColumn(2.3f); for (var i = 0; i < 13; i++) c.RelativeColumn(1.25f); });
            table.Header(h =>
            {
                foreach (var title in new[] { "ردیف", "نام و نام خانوادگی", "شروع کار", "ترک کار", "پایه مزد روزانه", "پایه سنوات روزانه", "دستمزد روزانه کل", "دستمزد ماهانه", "سایر مزایای مشمول", "جمع دستمزد و مزایای مشمول", "جمع ناخالص", "بیمه سهم کارگر", "مالیات حقوق", "مانده قابل پرداخت", "روز" })
                    h.Cell().Element(Header).Text(title).SemiBold();
            });
            foreach (var e in _data.Rows)
            {
                Cell(e.RowIndex.ToString()); Cell(e.FullName); Cell(e.HireDate); Cell(e.FireDate);
                Cell(Money(e.BaseDailyWage)); Cell(Money(e.SeniorityDailyBase)); Cell(Money(e.TotalDailyWage)); Cell(Money(e.MonthlyWage));
                Cell(Money(e.OtherSubjectBenefits)); Cell(Money(e.TotalSubject)); Cell(Money(e.GrossPay)); Cell(Money(e.WorkerPremium));
                Cell(Money(e.TaxAmount)); Cell(Money(e.NetPayable)); Cell(e.WorkDays.ToString("0.##", FaCulture));
            }
            void Cell(string value) => table.Cell().Element(Body).Text(value);
        });
        page.Footer().AlignCenter().Text(t => { t.Span("صفحه "); t.CurrentPageNumber(); t.Span(" از "); t.TotalPages(); });
    });

    private static IContainer Header(IContainer c) => c.Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().AlignMiddle();
    private static IContainer Body(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Medium).Padding(2).AlignCenter().AlignMiddle();
    private static string Money(long value) => value.ToString("N0", FaCulture);
}
