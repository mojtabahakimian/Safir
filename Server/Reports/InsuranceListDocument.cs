using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary.Reports;
using System.Globalization;

namespace Safir.Server.Reports
{
    public class InsuranceListDocument : IDocument
    {
        private readonly InsuranceReportDto _data;
        private const string PersianFontName = "IRANYekanFN";
        private static readonly CultureInfo FaCulture = new CultureInfo("fa-IR");

        public InsuranceListDocument(InsuranceReportDto data)
        {
            _data = data;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                // برگه A4 افقی برای جا شدن تمام ستون‌ها
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily(PersianFontName));
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
                // سطر اول: عنوان، ماه، سال
                col.Item().Row(row =>
                {
                    row.RelativeItem().AlignRight().Text($"سال: {_data.PeriodYear}").FontSize(10).SemiBold();
                    row.RelativeItem().AlignCenter().Text("صورت دستمزد، حقوق و مزایای ماهانه").FontSize(14).Bold();
                    row.RelativeItem().AlignLeft().Text($"ماه: {_data.PeriodMonthName}").FontSize(10).SemiBold();
                });

                col.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                // سطر دوم: مشخصات کارگاه
                col.Item().Row(row =>
                {
                    row.RelativeItem(2).Text($"نام کارگاه: {_data.WorkshopName}").SemiBold();
                    row.RelativeItem(2).Text($"نام کارفرما: {_data.EmployerName}");
                    row.RelativeItem(2).Text($"شماره کارگاه: {_data.WorkshopCode}").SemiBold();
                    row.RelativeItem(2).Text($"شعبه تامین اجتماعی: {_data.BranchName}");
                });

                // سطر سوم: آدرس
                col.Item().PaddingTop(2).Text($"نشانی کارگاه: {_data.Address}").FontSize(8).FontColor(Colors.Grey.Darken2);
            });
        }

        private void ComposeContent(IContainer container)
        {
            // 🚀 فیکس باگ QuestPDF: چون Content فقط یک فرزند می‌پذیرد، کل محتوا (جدول + امضا) را داخل یک Column قرار می‌دهیم.
            container.Column(mainCol =>
            {
                // ۱. جدول لیست پرسنل
                mainCol.Item().Table(table =>
                {
                    // تعریف عرض ستون‌ها
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(25); // ردیف
                        c.RelativeColumn(3);  // نام
                        c.RelativeColumn(2);  // کد ملی
                        c.RelativeColumn(2);  // ش بیمه
                        c.RelativeColumn(2);  // شغل
                        c.ConstantColumn(30); // روز
                        c.RelativeColumn(2);  // روزانه
                        c.RelativeColumn(2.5f); // ماهانه
                        c.RelativeColumn(2);  // مزایای مشمول
                        c.RelativeColumn(2);  // حق تاهل (1405)
                        c.RelativeColumn(2);  // سنوات (1405)
                        c.RelativeColumn(2.5f); // جمع مشمول
                        c.RelativeColumn(2.5f); // ناخالص
                        c.RelativeColumn(2);  // سهم کارگر
                    });

                    // هدر جدول
                    table.Header(h =>
                    {
                        static IContainer HeaderStyle(IContainer c) =>
                            c.Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().AlignMiddle();

                        h.Cell().Element(HeaderStyle).Text("ردیف").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("نام و نام‌خانوادگی").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("کد ملی").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("شماره بیمه").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("شغل").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("روز").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("دستمزد روزانه").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("دستمزد ماهانه").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("سایر مزایای مشمول").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("حق تاهل").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("پایه سنوات").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("جمع مشمول").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("جمع ناخالص").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("سهم بیمه شده").SemiBold();
                    });

                    // ردیف‌های پرسنل
                    foreach (var emp in _data.Rows)
                    {
                        static IContainer CellStyle(IContainer c) =>
                            c.Border(1).BorderColor(Colors.Grey.Medium).Padding(2).AlignMiddle();

                        static IContainer Center(IContainer c) => CellStyle(c).AlignCenter();
                        static IContainer Right(IContainer c) => CellStyle(c).AlignRight();

                        table.Cell().Element(Center).Text(emp.RowIndex.ToString());
                        table.Cell().Element(Right).Text(emp.FullName).FontSize(7);
                        table.Cell().Element(Center).Text(emp.NationalCode).FontSize(7);
                        table.Cell().Element(Center).Text(emp.InsuranceCode).FontSize(7);
                        table.Cell().Element(Right).Text(emp.JobTitle).FontSize(7);
                        table.Cell().Element(Center).Text(emp.WorkDays.ToString("0.##", FaCulture));
                        table.Cell().Element(Center).Text(Money(emp.DailyWage));
                        table.Cell().Element(Center).Text(Money(emp.MonthlyWage));
                        table.Cell().Element(Center).Text(Money(emp.OtherSubjectBenefits));
                        table.Cell().Element(Center).Text(Money(emp.MaritalAllowance));
                        table.Cell().Element(Center).Text(Money(emp.SeniorityBase));
                        table.Cell().Element(Center).Text(Money(emp.TotalSubjectToInsurance)).SemiBold();
                        table.Cell().Element(Center).Text(Money(emp.TotalGrossPay));
                        table.Cell().Element(Center).Text(Money(emp.WorkerPremium)).SemiBold();
                    }

                    // ردیف جمع در پایین جدول (تجمعی کل صفحات)
                    table.Footer(f =>
                    {
                        static IContainer FooterStyle(IContainer c) =>
                            c.Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).Padding(2).AlignCenter().AlignMiddle();

                        f.Cell().ColumnSpan(5).Element(FooterStyle).AlignRight().PaddingRight(5).Text("جمع کل (ریال):").SemiBold();
                        f.Cell().Element(FooterStyle).Text(_data.TotalWorkDays.ToString("0.##", FaCulture)).SemiBold();
                        f.Cell().Element(FooterStyle).Text("-"); // روزانه جمع ندارد
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalMonthlyWage)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalOtherBenefits)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalMaritalAllowance)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalSeniorityBase)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalSubjectToInsurance)).Bold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalGrossPay)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalWorkerPremium)).Bold();
                    });
                });

                // ۲. باکس محاسبات سهم کارفرما و امضا (زیر جدول)
                mainCol.Item().PaddingTop(10).Row(row =>
                {
                    // باکس مالیات/بیمه سمت راست
                    row.RelativeItem(2).Border(1).BorderColor(Colors.Black).Padding(5).Column(c =>
                    {
                        c.Item().PaddingBottom(4).Text("محاسبات سهم کارفرما (ریال)").Bold();

                        c.Item().Row(r => { r.RelativeItem().Text("جمع حق بیمه سهم کارگر (۷٪):"); r.RelativeItem().AlignLeft().Text(Money(_data.TotalWorkerPremium)); });
                        c.Item().Row(r => { r.RelativeItem().Text("جمع حق بیمه سهم کارفرما (۲۰٪):"); r.RelativeItem().AlignLeft().Text(Money(_data.TotalEmployerPremium)); });
                        c.Item().Row(r => { r.RelativeItem().Text("جمع بیمه بیکاری (۳٪):"); r.RelativeItem().AlignLeft().Text(Money(_data.TotalUnemploymentPremium)); });

                        c.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                        c.Item().PaddingTop(3).Row(r => { r.RelativeItem().Text("جمع کل قابل پرداخت (۳۰٪):").SemiBold(); r.RelativeItem().AlignLeft().Text(Money(_data.TotalPayablePremium)).Bold(); });
                    });

                    row.ConstantItem(20); // فاصله

                    // باکس مهر و امضا
                    row.RelativeItem(1).Border(1).BorderColor(Colors.Black).Padding(5).AlignCenter().AlignMiddle().Text("مهر و امضای کارفرما").SemiBold();
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