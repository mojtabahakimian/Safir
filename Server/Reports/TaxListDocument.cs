using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary.Reports;
using System.Globalization;

namespace Safir.Server.Reports
{
    public class TaxListDocument : IDocument
    {
        private readonly TaxReportDto _data;
        private const string PersianFontName = "IRANYekanFN";
        private static readonly CultureInfo FaCulture = new CultureInfo("fa-IR");

        public TaxListDocument(TaxReportDto data)
        {
            _data = data;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(PersianFontName));
                page.ContentFromRightToLeft();

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.PaddingBottom(10).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().AlignRight().Text($"سال: {_data.PeriodYear}").FontSize(10).SemiBold();
                    row.RelativeItem(2).AlignCenter().Text("لیست مالیات بر درآمد حقوق کارکنان").FontSize(14).Bold();
                    row.RelativeItem().AlignLeft().Text($"ماه: {_data.PeriodMonthName}").FontSize(10).SemiBold();
                });

                col.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                col.Item().Row(row =>
                {
                    row.RelativeItem(2).Text($"نام کارگاه: {_data.WorkshopName}").SemiBold();
                    row.RelativeItem(2).Text($"نام کارفرما: {_data.EmployerName}");
                    row.RelativeItem().Text($"کد اقتصادی/شناسه: {_data.TaxCode}").SemiBold();
                });
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(mainCol =>
            {
                mainCol.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(25); // ردیف
                        c.RelativeColumn(3);  // نام
                        c.RelativeColumn(2);  // کد ملی
                        c.RelativeColumn(2);  // شغل
                        c.ConstantColumn(35); // روز
                        c.RelativeColumn(2.5f); // ناخالص
                        c.RelativeColumn(2.5f); // مشمول مالیات
                        c.RelativeColumn(2.5f); // مالیات
                    });

                    table.Header(h =>
                    {
                        static IContainer HeaderStyle(IContainer c) =>
                            c.Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).Padding(3).AlignCenter().AlignMiddle();

                        h.Cell().Element(HeaderStyle).Text("ردیف").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("نام و نام‌خانوادگی").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("کد ملی").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("شغل").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("کارکرد").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("درآمد ناخالص").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("مشمول مالیات").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("مالیات کسر شده").SemiBold();
                    });

                    foreach (var emp in _data.Rows)
                    {
                        static IContainer CellStyle(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Medium).Padding(3).AlignMiddle();
                        static IContainer Center(IContainer c) => CellStyle(c).AlignCenter();
                        static IContainer Right(IContainer c) => CellStyle(c).AlignRight();

                        table.Cell().Element(Center).Text(emp.RowIndex.ToString());
                        table.Cell().Element(Right).Text(emp.FullName).FontSize(8);
                        table.Cell().Element(Center).Text(emp.NationalCode).FontSize(8);
                        table.Cell().Element(Right).Text(emp.JobTitle).FontSize(8);
                        table.Cell().Element(Center).Text(emp.WorkDays.ToString("0.##", FaCulture));
                        table.Cell().Element(Center).Text(Money(emp.GrossPay));
                        table.Cell().Element(Center).Text(Money(emp.TaxBase));
                        table.Cell().Element(Center).Text(Money(emp.TaxAmount)).SemiBold();
                    }

                    table.Footer(f =>
                    {
                        static IContainer FooterStyle(IContainer c) =>
                            c.Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).Padding(3).AlignCenter().AlignMiddle();

                        f.Cell().ColumnSpan(4).Element(FooterStyle).AlignRight().PaddingRight(5).Text("جمع کل (ریال):").SemiBold();
                        f.Cell().Element(FooterStyle).Text(_data.TotalWorkDays.ToString("0.##", FaCulture)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalGrossPay)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalTaxBase)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalTaxAmount)).Bold();
                    });
                });

                mainCol.Item().PaddingTop(25).Row(row =>
                {
                    row.RelativeItem().AlignCenter().Text("مهر و امضای کارفرما / مدیر مالی").SemiBold();
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(text =>
            {
                text.Span("صفحه ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" از ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        }

        private static string Money(long value) => value == 0 ? "0" : value.ToString("N0", FaCulture);
    }
}