using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary;
using System.Globalization;

namespace Safir.Server.Reports
{
    public class LeaveReportDocument : IDocument
    {
        private readonly List<Pay2LeaveReportRowDto> _data;
        private readonly string _workshopName;
        private readonly int _year;
        private readonly string _reportDateStr;
        private const string PersianFontName = "IRANYekanFN";
        private static readonly CultureInfo FaCulture = new CultureInfo("fa-IR");

        public LeaveReportDocument(List<Pay2LeaveReportRowDto> data, string workshopName, int year, string reportDateStr)
        {
            _data = data;
            _workshopName = workshopName;
            _year = year;
            _reportDateStr = reportDateStr;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
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
                    row.RelativeItem().AlignRight().Text($"کارگاه: {_workshopName}").FontSize(11).SemiBold();
                    row.RelativeItem().AlignCenter().Text($"گزارش وضعیت مرخصی پرسنل - سال {_year}").FontSize(14).Bold();
                    row.RelativeItem().AlignLeft().Text($"تاریخ گزارش: {_reportDateStr}").FontSize(10).SemiBold();
                });
                col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(30); // ردیف
                    c.ConstantColumn(50); // کد
                    c.RelativeColumn(3);  // نام
                    c.RelativeColumn(2);  // استحقاق کل
                    c.RelativeColumn(2);  // انتقالی
                    c.RelativeColumn(2);  // استحقاق تا امروز
                    c.RelativeColumn(2);  // استفاده شده
                    c.RelativeColumn(2);  // مانده کل (روز)
                    c.RelativeColumn(2);  // مانده تا امروز (روز)
                });

                table.Header(h =>
                {
                    static IContainer HeaderStyle(IContainer c) => c.Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().AlignMiddle();

                    h.Cell().Element(HeaderStyle).Text("ردیف").SemiBold();
                    h.Cell().Element(HeaderStyle).Text("کد").SemiBold();
                    h.Cell().Element(HeaderStyle).Text("نام و نام‌خانوادگی").SemiBold();
                    h.Cell().Element(HeaderStyle).Text("استحقاق سالانه").SemiBold();
                    h.Cell().Element(HeaderStyle).Text("انتقالی از قبل").SemiBold();
                    h.Cell().Element(HeaderStyle).Text("استحقاق تا امروز").SemiBold();
                    h.Cell().Element(HeaderStyle).Text("استفاده شده").SemiBold();
                    h.Cell().Element(HeaderStyle).Text("مانده پایان سال (روز)").SemiBold();
                    h.Cell().Element(HeaderStyle).Text("مانده تا امروز (روز)").SemiBold();
                });

                int index = 1;
                foreach (var item in _data)
                {
                    static IContainer CellStyle(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Medium).Padding(4).AlignMiddle();
                    static IContainer Center(IContainer c) => CellStyle(c).AlignCenter();
                    static IContainer Right(IContainer c) => CellStyle(c).AlignRight();

                    table.Cell().Element(Center).Text(index++.ToString());
                    table.Cell().Element(Center).Text(item.EMP_CODE);
                    table.Cell().Element(Right).Text(item.FULL_NAME).SemiBold();
                    table.Cell().Element(Center).Text(Mins(item.ENTITLEMENT_MIN));
                    table.Cell().Element(Center).Text(Mins(item.CARRIED_IN_MIN));
                    table.Cell().Element(Center).Text(Mins(item.PRORATA_ENTITLEMENT_MIN)).FontColor(Colors.Blue.Darken2).SemiBold();
                    table.Cell().Element(Center).Text(Mins(item.USED_MIN)).FontColor(Colors.Red.Darken2);

                    table.Cell().Element(Center).Text(item.BALANCE_DAYS.ToString("0.00", FaCulture));
                    table.Cell().Element(Center).Text(item.PRORATA_BALANCE_DAYS.ToString("0.00", FaCulture)).FontColor(item.PRORATA_BALANCE_DAYS < 0 ? Colors.Red.Medium : Colors.Green.Darken2).Bold();
                }
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(text =>
            {
                text.Span("تمام مقادیر (به جز ستون‌های روز) بر حسب دقیقه می‌باشند.").FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span("  |  صفحه ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" از ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        }

        private static string Mins(int value) => value.ToString("N0", FaCulture);
    }
}