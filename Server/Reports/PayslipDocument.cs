// File: Server/Reports/PayslipDocument.cs
// سند QuestPDF فیش حقوقی ماژول PAY2 — راست‌به‌چپ، فونت IRANYekanFN.
// سبک کلی (RTL، فونت، لوگو، ساختار Header/Content/Footer) از ProformaDocument پیروی می‌کند.
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Salary.Reports;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Safir.Server.Reports
{
    public class PayslipDocument : IDocument
    {
        private readonly PayslipReportDto _data;
        private readonly IWebHostEnvironment _env;
        private byte[]? _logoBytes = null;

        private const string PersianFontName = "IRANYekanFN"; // در Program.cs رجیستر شده است
        private const string LogoFileName = "2.png";
        private const string FontsFolderName = "Fonts";
        private static readonly CultureInfo FaCulture = new CultureInfo("fa-IR");

        public PayslipDocument(PayslipReportDto data, IWebHostEnvironment env)
        {
            _data = data;
            _env = env;

            // بارگذاری لوگو (اختیاری — در صورت نبود، فیش بدون لوگو تولید می‌شود)
            try
            {
                string logoPath = Path.Combine(_env.ContentRootPath, FontsFolderName, LogoFileName);
                if (File.Exists(logoPath))
                    _logoBytes = File.ReadAllBytes(logoPath);
            }
            catch
            {
                _logoBytes = null;
            }
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.2f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(PersianFontName));
                page.ContentFromRightToLeft();

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        // ───────────────────────────── Header ─────────────────────────────
        void ComposeHeader(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    // راست: مشخصات کارگاه و دوره
                    row.RelativeItem().AlignRight().Column(c =>
                    {
                        c.Item().Text(_data.WorkshopName).SemiBold().FontSize(12);
                        c.Item().Text($"دوره: {_data.PeriodTitle}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });

                    // وسط: عنوان
                    row.RelativeItem().AlignCenter().AlignMiddle().Text("فیش حقوقی").SemiBold().FontSize(16);

                    // چپ: لوگو یا تاریخ چاپ
                    if (_logoBytes != null)
                    {
                        row.ConstantItem(70).AlignLeft().AlignTop().Image(_logoBytes).FitArea();
                    }
                    else
                    {
                        row.RelativeItem().AlignLeft().AlignMiddle().Text(t =>
                        {
                            t.Span("تاریخ چاپ: ").FontSize(8).FontColor(Colors.Grey.Darken1);
                            t.Span(_data.PrintDate ?? "").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    }
                });

                col.Item().PaddingTop(6).BorderBottom(1).BorderColor(Colors.Grey.Medium);

                // نوار مشخصات پرسنل
                col.Item().PaddingTop(6).Background(Colors.Grey.Lighten4).Padding(6).Column(info =>
                {
                    info.Item().Row(row =>
                    {
                        row.RelativeItem(2).Text(t => { t.Span("نام و نام خانوادگی: ").SemiBold(); t.Span(_data.EmployeeName); });
                        row.RelativeItem().Text(t => { t.Span("کد پرسنلی: ").SemiBold(); t.Span(_data.EmployeeCode); });
                        row.RelativeItem().Text(t => { t.Span("کد ملی: ").SemiBold(); t.Span(string.IsNullOrWhiteSpace(_data.NationalCode) ? "—" : _data.NationalCode); });
                    });
                    info.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem(2).Text(t => { t.Span("شغل: ").SemiBold(); t.Span(string.IsNullOrWhiteSpace(_data.JobTitle) ? "—" : _data.JobTitle); });
                        row.RelativeItem().Text(t => { t.Span("کارکرد: ").SemiBold(); t.Span($"{_data.WorkDays.ToString("0.##", FaCulture)} روز"); });
                        row.RelativeItem().Text(t => { t.Span("مبنای بیمه: ").SemiBold(); t.Span(Money(_data.InsBase)); });
                    });
                });
            });
        }

        // ───────────────────────────── Content ─────────────────────────────
        void ComposeContent(IContainer container)
        {
            container.PaddingVertical(8).Row(row =>
            {
                // در حالت RTL، اولین آیتم سمت راست قرار می‌گیرد ⇒ راست=مزایا، چپ=کسورات
                row.RelativeItem().Element(c =>
                    ComposeItemsBlock(c, "مزایا و دریافتی‌ها", _data.Earnings, _data.TotalEarnings, "جمع کل مزایا", "#2E7D32"));

                row.ConstantItem(12); // فاصلهٔ بین دو ستون

                row.RelativeItem().Element(c =>
                    ComposeItemsBlock(c, "کسورات", _data.Deductions, _data.TotalDeductions, "جمع کل کسورات", "#C62828"));
            });
        }

        void ComposeItemsBlock(IContainer container, string heading, System.Collections.Generic.List<PayslipLineDto> lines,
                               long total, string totalLabel, string accentColor)
        {
            container.Border(1).BorderColor(Colors.Grey.Lighten1).Column(col =>
            {
                // سرتیتر رنگی
                col.Item().Background(accentColor).Padding(5).Text(heading).FontColor(Colors.White).SemiBold().FontSize(11);

                // جدول اقلام
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3); // شرح
                        c.RelativeColumn(2); // مبلغ
                    });

                    table.Header(h =>
                    {
                        static IContainer HeaderCell(IContainer c) =>
                            c.Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(5);

                        h.Cell().Element(HeaderCell).Text("شرح").SemiBold();
                        h.Cell().Element(HeaderCell).AlignLeft().Text("مبلغ (ریال)").SemiBold();
                    });

                    if (lines != null && lines.Any())
                    {
                        foreach (var ln in lines)
                        {
                            static IContainer BodyCell(IContainer c) =>
                                c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(5);

                            table.Cell().Element(BodyCell).Text(ln.Title ?? "");
                            table.Cell().Element(BodyCell).AlignLeft().Text(Money(ln.Amount));
                        }
                    }
                    else
                    {
                        table.Cell().ColumnSpan(2).Padding(8).AlignCenter().Text("—").FontColor(Colors.Grey.Medium);
                    }
                });

                // جمع ستون
                col.Item().Background(Colors.Grey.Lighten4).Padding(5).Row(r =>
                {
                    r.RelativeItem().Text(totalLabel).SemiBold();
                    r.RelativeItem().AlignLeft().Text(Money(total)).SemiBold();
                });
            });
        }

        // ───────────────────────────── Footer ─────────────────────────────
        void ComposeFooter(IContainer container)
        {
            container.PaddingTop(8).Column(col =>
            {
                // خالص پرداختی برجسته
                col.Item().Border(1.5f).BorderColor(Colors.Blue.Darken2).Background(Colors.Blue.Lighten5).Padding(8).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text(t =>
                    {
                        t.Span("خالص قابل پرداخت: ").SemiBold().FontSize(12);
                        t.Span(Money(_data.NetPay)).Bold().FontSize(15).FontColor(Colors.Blue.Darken2);
                        t.Span(" ریال").FontSize(10);
                    });
                });

                if (!string.IsNullOrWhiteSpace(_data.NetPayInWords))
                {
                    col.Item().PaddingTop(3).Text(t =>
                    {
                        t.Span("به حروف: ").SemiBold().FontSize(8);
                        t.Span(_data.NetPayInWords).FontSize(8);
                    });
                }

                // اطلاعات تکمیلی (مانده‌ها) — نمایش همیشگی حتی در صورت صفر بودن
                col.Item().PaddingTop(4).Text(t =>
                {
                    decimal leave = _data.LeaveBalanceDays ?? 0;
                    t.Span($"مانده مرخصی: {leave.ToString("0.##", FaCulture)} روز    ")
                     .FontSize(8).FontColor(Colors.Grey.Darken1);

                    long loan = _data.LoanBalance ?? 0;
                    t.Span($"مانده وام: {Money(loan)} ریال")
                     .FontSize(8).FontColor(Colors.Grey.Darken1);
                });

                // محل امضا
                col.Item().PaddingTop(20).Row(row =>
                {
                    row.RelativeItem().AlignCenter().Text("مهر و امضاء کارفرما").FontSize(8);
                    row.RelativeItem().AlignCenter().Text("امضاء دریافت‌کننده").FontSize(8);
                });
            });
        }

        // ───────────────────────────── Helpers ─────────────────────────────
        private static string Money(long value) => value.ToString("N0", FaCulture);
    }
}
