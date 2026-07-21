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
                        c.ConstantColumn(20); // ردیف
                        c.RelativeColumn(2.6f);  // نام
                        c.RelativeColumn(1.8f);  // کد ملی
                        c.RelativeColumn(1.6f);  // ش بیمه
                        c.RelativeColumn(1.6f);  // شغل
                        c.RelativeColumn(1.4f);  // تاریخ شروع به کار
                        c.RelativeColumn(1.4f);  // تاریخ ترک کار
                        c.ConstantColumn(24);    // روز
                        c.RelativeColumn(1.7f);  // پایه مزد روزانه
                        c.RelativeColumn(1.7f);  // پایه سنوات روزانه
                        c.RelativeColumn(1.7f);  // دستمزد روزانه کل
                        c.RelativeColumn(2.0f);  // دستمزد ماهانه
                        c.RelativeColumn(1.9f);  // سایر مزایای مشمول
                        c.RelativeColumn(2.1f);  // جمع دستمزد و مزایای مشمول
                        c.RelativeColumn(2.0f);  // جمع ناخالص
                        c.RelativeColumn(1.8f);  // حق بیمه سهم کارگر
                        c.RelativeColumn(1.8f);  // مالیات حقوق
                        c.RelativeColumn(2.0f);  // مانده قابل پرداخت
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
                        h.Cell().Element(HeaderStyle).Text("تاریخ شروع به کار").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("تاریخ ترک کار").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("روز").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("پایه مزد روزانه").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("پایه سنوات روزانه").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("دستمزد روزانه کل").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("دستمزد ماهانه").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("سایر مزایای مشمول").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("جمع دستمزد و مزایای مشمول").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("جمع ناخالص").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("حق بیمه سهم کارگر").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("مالیات حقوق").SemiBold();
                        h.Cell().Element(HeaderStyle).Text("مانده قابل پرداخت").SemiBold();
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
                        table.Cell().Element(Center).Text(emp.HireDate).FontSize(7);
                        table.Cell().Element(Center).Text(emp.FireDate).FontSize(7);
                        table.Cell().Element(Center).Text(emp.WorkDays.ToString("0.##", FaCulture));
                        table.Cell().Element(Center).Text(Money(emp.DailyBaseWage));
                        table.Cell().Element(Center).Text(Money(emp.DailySeniority));
                        table.Cell().Element(Center).Text(Money(emp.TotalDailyWage));
                        table.Cell().Element(Center).Text(Money(emp.MonthlyWage));
                        table.Cell().Element(Center).Text(Money(emp.OtherSubjectBenefits));
                        table.Cell().Element(Center).Text(Money(emp.TotalSubjectToInsurance)).SemiBold();
                        table.Cell().Element(Center).Text(Money(emp.TotalGrossPay));
                        table.Cell().Element(Center).Text(Money(emp.WorkerPremium)).SemiBold();
                        table.Cell().Element(Center).Text(Money(emp.TaxAmount));
                        table.Cell().Element(Center).Text(Money(emp.PayableBalance)).SemiBold();
                    }

                    // ردیف جمع در پایین جدول (تجمعی کل صفحات)
                    table.Footer(f =>
                    {
                        static IContainer FooterStyle(IContainer c) =>
                            c.Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).Padding(2).AlignCenter().AlignMiddle();

                        f.Cell().ColumnSpan(7).Element(FooterStyle).AlignRight().PaddingRight(5).Text("جمع کل (ریال):").SemiBold();
                        f.Cell().Element(FooterStyle).Text(_data.TotalWorkDays.ToString("0.##", FaCulture)).SemiBold();
                        f.Cell().Element(FooterStyle).Text("-"); // پایه مزد روزانه جمع ندارد
                        f.Cell().Element(FooterStyle).Text("-"); // پایه سنوات روزانه جمع ندارد
                        f.Cell().Element(FooterStyle).Text("-"); // دستمزد روزانه کل جمع ندارد
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalMonthlyWage)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalOtherBenefits)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalSubjectToInsurance)).Bold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalGrossPay)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalWorkerPremium)).Bold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalTaxAmount)).SemiBold();
                        f.Cell().Element(FooterStyle).Text(Money(_data.TotalPayableBalance)).Bold();
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