using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary.Reports;
using System.Globalization;

namespace Safir.Server.Reports;

public sealed class InsuranceListDocument : IDocument
{
    private readonly InsuranceReportDto _data;
    private const string Font = "IRANYekanFN";
    private static readonly CultureInfo FaCulture = new("fa-IR");
    public InsuranceListDocument(InsuranceReportDto data) => _data = data;
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
            row.RelativeItem(2).AlignCenter().Text("صورت دستمزد، حقوق و مزایای ماهانه").FontSize(14).Bold();
            row.RelativeItem().AlignLeft().Text($"ماه: {_data.PeriodMonthName}").FontSize(10).SemiBold();
        });
        col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);
        col.Item().Row(row =>
        {
            row.RelativeItem(2).Text($"نام کارگاه: {_data.WorkshopName}").SemiBold();
            row.RelativeItem(2).Text($"نام کارفرما: {_data.EmployerName}");
            row.RelativeItem(2).Text($"شماره کارگاه: {_data.WorkshopCode}").SemiBold();
            row.RelativeItem(2).Text($"شعبه تأمین اجتماعی: {_data.BranchName}");
        });
        col.Item().PaddingTop(2).Text($"نشانی کارگاه: {_data.Address}").FontColor(Colors.Grey.Darken2);
    });

    private void ComposeContent(IContainer container) => container.Column(main =>
    {
        main.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(22); c.RelativeColumn(2.5f); c.RelativeColumn(1.25f); c.RelativeColumn(1.1f); c.RelativeColumn(1.1f); c.RelativeColumn(1.25f); c.RelativeColumn(1.5f);
                c.ConstantColumn(16); c.ConstantColumn(16);
                c.RelativeColumn(1.05f); c.RelativeColumn(1.05f); c.ConstantColumn(28);
                for (var i = 0; i < 10; i++) c.RelativeColumn(1.25f);
            });
            table.Header(h =>
            {
                foreach (var title in new[] { "ردیف", "نام و نام خانوادگی", "کد ملی", "شماره شناسنامه", "نام پدر", "شماره بیمه", "شغل", "مرد", "زن", "شروع کار", "ترک کار", "روز", "پایه مزد روزانه", "پایه سنوات روزانه", "دستمزد روزانه کل", "دستمزد ماهانه", "سایر مزایای مشمول", "جمع دستمزد و مزایای مشمول", "جمع ناخالص", "بیمه سهم کارگر", "مالیات حقوق", "مانده قابل پرداخت" })
                    h.Cell().Element(Header).Text(title).SemiBold();
            });
            foreach (var e in _data.Rows)
            {
                Cell(e.RowIndex.ToString()); Cell(e.FullName, false); Cell(e.NationalCode); Cell(e.IdNumber); Cell(e.FatherName, false); Cell(e.InsuranceCode); Cell(e.JobTitle, false);
                GenderCell(e.Gender == 1); GenderCell(e.Gender != 1);
                Cell(e.HireDate); Cell(e.FireDate); Cell(e.WorkDays.ToString("0.##", FaCulture));
                Cell(Money(e.BaseDailyWage)); Cell(Money(e.SeniorityDailyBase)); Cell(Money(e.TotalDailyWage)); Cell(Money(e.MonthlyWage));
                Cell(Money(e.OtherSubjectBenefits)); Cell(Money(e.TotalSubjectToInsurance)); Cell(Money(e.TotalGrossPay));
                Cell(Money(e.WorkerPremium)); Cell(Money(e.TaxAmount)); Cell(Money(e.NetPayable));
            }
            table.Footer(f =>
            {
                f.Cell().ColumnSpan(11).Element(Footer).AlignRight().PaddingRight(5).Text("جمع کل (ریال):").SemiBold();
                f.Cell().Element(Footer).Text(_data.TotalWorkDays.ToString("0.##", FaCulture)).SemiBold();
                f.Cell().Element(Footer).Text("-"); f.Cell().Element(Footer).Text("-"); f.Cell().Element(Footer).Text("-");
                f.Cell().Element(Footer).Text(Money(_data.TotalMonthlyWage)); f.Cell().Element(Footer).Text(Money(_data.TotalOtherBenefits));
                f.Cell().Element(Footer).Text(Money(_data.TotalSubjectToInsurance)).Bold(); f.Cell().Element(Footer).Text(Money(_data.TotalGrossPay));
                f.Cell().Element(Footer).Text(Money(_data.TotalWorkerPremium)).Bold(); f.Cell().Element(Footer).Text(Money(_data.TotalTaxAmount));
                f.Cell().Element(Footer).Text(Money(_data.TotalNetPayable)).Bold();
            });
            void Cell(string value, bool center = true) => table.Cell().Element(center ? BodyCenter : BodyRight).Text(value);
            void GenderCell(bool on)
            {
                var cell = table.Cell().Element(BodyCenter);
                if (on) cell.Width(7).Height(7).Background(Colors.Black);
                else cell.Text("");
            }
        });

        main.Item().PaddingTop(10).Row(row =>
        {
            row.RelativeItem(2).Border(1).Padding(6).Column(c =>
            {
                c.Item().Text("محاسبات حق بیمه Snapshot شده (ریال)").Bold();
                if (_data.HasPremiumBreakdownSnapshot)
                {
                    Premium(c, "جمع حق بیمه سهم کارگر:", _data.TotalWorkerPremium);
                    Premium(c, "جمع سهم پایه کارفرما:", _data.TotalEmployerPremium);
                    Premium(c, "جمع بیمه بیکاری:", _data.TotalUnemploymentPremium);
                    c.Item().PaddingTop(3).LineHorizontal(1);
                    Premium(c, "جمع کل قابل پرداخت:", _data.TotalPayablePremium, true);
                }
                else
                {
                    c.Item().PaddingTop(8).Text("تفکیک سهم کارفرما و بیمه بیکاری در نسخه تاریخی این Run Snapshot نشده است.")
                        .FontColor(Colors.Grey.Darken2);
                }
            });
            row.ConstantItem(20);
            row.RelativeItem().Border(1).MinHeight(75).AlignCenter().AlignMiddle().Text("مهر و امضای کارفرما").SemiBold();
            row.ConstantItem(12);
            row.RelativeItem().Border(1).MinHeight(75).AlignCenter().AlignMiddle().Text("تأیید واحد مالی / منابع انسانی").SemiBold();
        });
    });

    private static void Premium(ColumnDescriptor c, string title, long value, bool bold = false) => c.Item().Row(r =>
    {
        r.RelativeItem().Text(title);
        if (bold) r.RelativeItem().AlignLeft().Text(Money(value)).Bold();
        else r.RelativeItem().AlignLeft().Text(Money(value));
    });
    private static void ComposeFooter(IContainer c) => c.AlignCenter().Text(t => { t.Span("صفحه "); t.CurrentPageNumber(); t.Span(" از "); t.TotalPages(); });
    private static IContainer Header(IContainer c) => c.Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().AlignMiddle();
    private static IContainer BodyCenter(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Medium).Padding(2).AlignCenter().AlignMiddle();
    private static IContainer BodyRight(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Medium).Padding(2).AlignRight().AlignMiddle();
    private static IContainer Footer(IContainer c) => c.Border(1).Background(Colors.Grey.Lighten4).Padding(2).AlignCenter().AlignMiddle();
    private static string Money(long value) => value.ToString("N0", FaCulture);
}
