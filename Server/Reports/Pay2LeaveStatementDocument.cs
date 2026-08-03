using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary;
using System.Linq;

namespace Safir.Server.Reports
{
    public class Pay2LeaveStatementDocument : IDocument
    {
        private readonly Pay2LeaveStatementDto _model;

        public Pay2LeaveStatementDocument(Pay2LeaveStatementDto model)
        {
            _model = model;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container
                .Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(11));
                    page.ContentFromRightToLeft();

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().Element(ComposeFooter);
                });
        }

        private void ComposeHeader(IContainer container)
        {
            container.PaddingBottom(20).Column(col =>
            {
                col.Item().AlignCenter().Text("صورت‌حساب شفاف مرخصی (Transparent Leave Statement)").FontSize(18).SemiBold().FontColor(Colors.Blue.Darken2);
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                col.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().Text($"نام پرسنل: {_model.EmployeeName}");
                    row.RelativeItem().Text($"کد پرسنل: {_model.EmployeeCode}");
                    row.RelativeItem().Text($"سال: {_model.Year}");
                    row.RelativeItem().Text($"تاریخ گزارش: {_model.PrintDate}");
                });
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Element(ComposeConfigSnapshot);
                col.Item().PaddingVertical(15).Element(ComposeBalanceSummary);
                col.Item().Element(ComposeHistoryTable);
            });
        }

        private void ComposeConfigSnapshot(IContainer container)
        {
            container.Background(Colors.Grey.Lighten4).Padding(10).Column(col =>
            {
                col.Item().Text("قوانین و تنظیمات پایه در زمان استخراج این گزارش:").SemiBold();
                col.Item().Text($"- ارزش زمانی هر روز مرخصی: {_model.LeaveMinsPerDay} دقیقه (معادل {FormatMinutesToTime(_model.LeaveMinsPerDay, _model.LeaveMinsPerDay)}).");
                col.Item().Text($"- حداکثر مرخصی قابل انتقال به سال بعد: طبق قانون ماده ۶۶ قانون کار، سقف انتقال {_model.LeaveCarryoverMax} روز است.");
            });
        }

        private void ComposeBalanceSummary(IContainer container)
        {
            container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
            {
                col.Item().Text("خلاصه وضعیت مانده مرخصی (در سال جاری)").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken2);
                col.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("استحقاق سال جاری:").SemiBold();
                        c.Item().Text(FormatMinutesToTime(_model.EntitlementMin, _model.LeaveMinsPerDay));
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("انتقالی از سال قبل:").SemiBold();
                        c.Item().Text(FormatMinutesToTime(_model.CarriedInMin, _model.LeaveMinsPerDay));
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("کل مرخصی مصرفی:").SemiBold();
                        c.Item().Text(FormatMinutesToTime(_model.UsedMin, _model.LeaveMinsPerDay)).FontColor(Colors.Red.Medium);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("مانده فعلی:").SemiBold();
                        c.Item().Text(FormatMinutesToTime(_model.BalanceMin, _model.LeaveMinsPerDay)).FontColor(Colors.Green.Darken2);
                    });
                });
            });
        }

        private void ComposeHistoryTable(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().PaddingBottom(5).Text("جزئیات مرخصی‌های استفاده شده (کسر شده از مانده)").FontSize(14).SemiBold();
                col.Item().PaddingBottom(10).Text("توجه: روزهای تعطیل رسمی و جمعه‌ها که بین روزهای مرخصی شما بوده‌اند، از مانده شما کسر نشده‌اند.").FontSize(10).FontColor(Colors.Grey.Medium);

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1); // تاریخ شروع
                        columns.RelativeColumn(1); // تاریخ پایان
                        columns.RelativeColumn(2); // مدت کسر شده (روز/ساعت/دقیقه)
                        columns.RelativeColumn(1); // معادل دقیقه‌ای
                        columns.RelativeColumn(3); // توضیحات
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(CellStyle).Text("از تاریخ");
                        header.Cell().Element(CellStyle).Text("تا تاریخ");
                        header.Cell().Element(CellStyle).Text("میزان کسر شده");
                        header.Cell().Element(CellStyle).Text("معادل دقیقه");
                        header.Cell().Element(CellStyle).Text("توضیحات");

                        static IContainer CellStyle(IContainer container) => container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Black);
                    });

                    if (!_model.History.Any())
                    {
                        table.Cell().ColumnSpan(5).PaddingVertical(10).AlignCenter().Text("هیچ مرخصی در این سال ثبت نشده است.");
                    }
                    else
                    {
                        foreach (var item in _model.History)
                        {
                            table.Cell().Element(CellStyle).Text(item.START_DATE.ToString());
                            table.Cell().Element(CellStyle).Text(item.END_DATE.ToString());

                            var durationText = "";
                            if (item.REQ_DAYS > 0) durationText += $"{item.REQ_DAYS} روز ";
                            if (item.REQ_HOURS > 0) durationText += $"{item.REQ_HOURS} ساعت ";
                            if (item.REQ_MINUTES > 0) durationText += $"{item.REQ_MINUTES} دقیقه";
                            if (string.IsNullOrEmpty(durationText)) durationText = "0";

                            table.Cell().Element(CellStyle).Text(durationText);
                            table.Cell().Element(CellStyle).Text(item.TotalDeductedMinutes.ToString());
                            table.Cell().Element(CellStyle).Text(item.DESCRIPTION ?? "-");

                            static IContainer CellStyle(IContainer container) => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
                        }
                    }
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(x =>
            {
                x.Span("صفحه ");
                x.CurrentPageNumber();
                x.Span(" از ");
                x.TotalPages();
            });
        }

        private string FormatMinutesToTime(int totalMinutes, int minsPerDay)
        {
            if (totalMinutes == 0) return "0 روز";

            bool isNegative = totalMinutes < 0;
            totalMinutes = System.Math.Abs(totalMinutes);

            int days = totalMinutes / minsPerDay;
            int remainingMins = totalMinutes % minsPerDay;
            int hours = remainingMins / 60;
            int minutes = remainingMins % 60;

            var parts = new System.Collections.Generic.List<string>();
            if (days > 0) parts.Add($"{days} روز");
            if (hours > 0) parts.Add($"{hours} ساعت");
            if (minutes > 0) parts.Add($"{minutes} دقیقه");

            var result = string.Join(" و ", parts);
            return isNegative ? $"- ({result})" : result;
        }
    }
}
