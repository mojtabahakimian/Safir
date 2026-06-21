using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary;
using System.Globalization;

namespace Safir.Server.Reports
{
    public class PayslipDocument : IDocument
    {
        private readonly PayslipReportDto _data;
        private readonly CultureInfo _faCulture;

        public PayslipDocument(PayslipReportDto data)
        {
            _data = data;
            _faCulture = new CultureInfo("fa-IR");
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container
                .Page(page =>
                {
                    page.Size(PageSizes.A5.Landscape());
                    page.Margin(1, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("IRANYekanFN").FontSize(10).DirectionFromRightToLeft());
                    page.ContentFromRightToLeft();

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);
                });
        }

        void ComposeHeader(IContainer container)
        {
            container.BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(5).Column(column =>
            {
                column.Item().AlignCenter().Text("فیش حقوقی پرسنل").SemiBold().FontSize(14);

                column.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(text => { text.Span("نام کارگاه: ").SemiBold(); text.Span(_data.WorkshopName); });
                        col.Item().Text(text => { text.Span("نام کارفرما: ").SemiBold(); text.Span(_data.EmployerName); });
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(text => { text.Span("دوره: ").SemiBold(); text.Span(_data.PeriodTitle); });
                        col.Item().Text(text => { text.Span("کارکرد: ").SemiBold(); text.Span(_data.WorkDays.ToString("0.##", _faCulture) + " روز"); });
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(text => { text.Span("نام و نام خانوادگی: ").SemiBold(); text.Span(_data.EmployeeName); });
                        col.Item().Text(text => { text.Span("کد پرسنلی: ").SemiBold(); text.Span(_data.EmployeeCode); });
                    });
                });
            });
        }

        void ComposeContent(IContainer container)
        {
            container.PaddingVertical(10).Row(row =>
            {
                // 1. مزایا (Earnings)
                row.RelativeItem().PaddingRight(5).Column(col =>
                {
                    col.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(2).AlignCenter().Text("مزایا").SemiBold().FontColor(Colors.Blue.Darken2);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // عنوان
                            columns.RelativeColumn(2); // مبلغ
                        });

                        foreach (var item in _data.Earnings)
                        {
                            table.Cell().Padding(2).AlignRight().Text(item.Title);
                            table.Cell().Padding(2).AlignLeft().Text(item.Amount.ToString("N0", _faCulture));
                        }
                    });
                });

                // 2. خط جداکننده (Vertical separator)
                row.ConstantItem(1).Background(Colors.Grey.Lighten2);

                // 3. کسورات (Deductions)
                row.RelativeItem().PaddingLeft(5).Column(col =>
                {
                    col.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(2).AlignCenter().Text("کسورات").SemiBold().FontColor(Colors.Red.Darken2);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // عنوان
                            columns.RelativeColumn(2); // مبلغ
                        });

                        foreach (var item in _data.Deductions)
                        {
                            table.Cell().Padding(2).AlignRight().Text(item.Title);
                            table.Cell().Padding(2).AlignLeft().Text(item.Amount.ToString("N0", _faCulture));
                        }
                    });
                });
            });
        }

        void ComposeFooter(IContainer container)
        {
            container.BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(5).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().AlignRight().Text(text => { text.Span("جمع مزایا: ").SemiBold(); text.Span(_data.GrossPay.ToString("N0", _faCulture)); });
                    row.RelativeItem().AlignCenter().Text(text => { text.Span("جمع کسورات: ").SemiBold(); text.Span(_data.TotalDed.ToString("N0", _faCulture)); });
                    row.RelativeItem().AlignLeft().Text(text => { text.Span("خالص پرداختی: ").SemiBold(); text.Span(_data.NetPay.ToString("N0", _faCulture)).FontColor(Colors.Green.Darken2).FontSize(12).SemiBold(); });
                });

                column.Item().PaddingTop(20).Row(row =>
                {
                    row.RelativeItem().AlignCenter().Text("مهر و امضای کارفرما").FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.RelativeItem().AlignCenter().Text("امضای کارگر/کارمند").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }
    }
}
