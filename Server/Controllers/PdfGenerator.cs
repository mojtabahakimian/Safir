// using ها را بررسی کنید و موارد لازم را اضافه کنید
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Hesabdari;
using Safir.Shared.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // برای Any()

namespace Safir.Server.Controllers // یا Namespace صحیح فایل شما
{
    // کلاس تعریف سند PDF برای صورت حساب
    public class CustomerStatementDocument : IDocument
    {
        private readonly IEnumerable<QDAFTARTAFZIL2_H> _statementItems;
        private readonly string _hesabCode;
        private readonly long? _startDate;
        private readonly long? _endDate;
        private readonly ILogger _logger;
        private const string PersianFontName = "IRANYekanFN";
        public CustomerStatementDocument(IEnumerable<QDAFTARTAFZIL2_H> items, string hesabCode, long? start, long? end, ILogger logger)
        {
            _statementItems = items ?? new List<QDAFTARTAFZIL2_H>();
            _hesabCode = hesabCode;
            _startDate = start;
            _endDate = end;
            _logger = logger;

            // --- تنظیمات فونت فارسی ---
            try
            {
                // <<< --- اصلاح شده: حذف شرط بررسی FontFamilies --- >>>
                // فقط تلاش می‌کنیم فونت را ثبت کنیم. اگر قبلا ثبت شده باشد، معمولا مشکلی پیش نمی‌آید.
                //FontManager.RegisterFont(File.OpenRead("irsans.ttf")); // مسیر فونت را بررسی کنید
                _logger.LogInformation("Attempted to register Persian font '{FontName}' for QuestPDF.", PersianFontName);

            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Persian font file 'irsans.ttf' not found. PDF might not render Persian text correctly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering Persian font for QuestPDF.");
            }
            // ---------------------------
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container
                .Page(page =>
                {
                    page.Margin(30, Unit.Point);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(PersianFontName)); // استفاده از نام فونت
                    page.ContentFromRightToLeft();

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeTable);
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("صفحه ").FontSize(8);
                        text.CurrentPageNumber().FontSize(8);
                        text.Span(" از ").FontSize(8);
                        text.TotalPages().FontSize(8);
                    });
                });
        }

        void ComposeHeader(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().AlignRight().Text($"صورت حساب مشتری: {_hesabCode}")
                        .SemiBold().FontSize(14);

                    column.Item().AlignRight().Text(text =>
                    {
                        text.Span("از تاریخ: ").SemiBold();
                        text.Span(FormatShamsiDateFromLong(_startDate));
                    });

                    column.Item().AlignRight().Text(text =>
                    {
                        text.Span("تا تاریخ: ").SemiBold();
                        text.Span(FormatShamsiDateFromLong(_endDate));
                    });
                    column.Item().AlignRight().Text(text =>
                    {
                        text.Span("تاریخ تهیه گزارش: ").SemiBold();
                        text.Span(FormatShamsiDateFromLong(CL_Tarikh.PersianCalendarHelper.GetCurrentPersianDateAsLong()) + " " + DateTime.Now.ToString("HH:mm"));
                    });
                });
                // لوگو یا ...
                // row.ConstantItem(100).Height(50).Placeholder();
            });
        }

        void ComposeTable(IContainer container)
        {
            container.PaddingTop(10).Table(table =>
            {
                // تعریف ستون‌ها با اضافه شدن ستون "تش"
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(0.5f); // ردیف
                    columns.RelativeColumn(1.2f); // تاریخ
                    columns.RelativeColumn(1f);   // ش سند
                    columns.RelativeColumn(4f);   // شرح
                    columns.RelativeColumn(2.2f); // بدهکار
                    columns.RelativeColumn(2.2f); // بستانکار
                    columns.RelativeColumn(2.5f); // مانده
                    columns.ConstantColumn(30f);  // <<< ستون جدید "تش" با عرض ثابت 30 پوینت >>>
                });

                // هدر جدول با اضافه شدن ستون "تش"
                table.Header(header =>
                {
                    static IContainer HeaderCellStyle(IContainer c) => c.DefaultTextStyle(x => x.SemiBold().FontFamily(PersianFontName)).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).AlignCenter();

                    // ترتیب جدید هدرها
                    header.Cell().Element(HeaderCellStyle).Text("ردیف");
                    header.Cell().Element(HeaderCellStyle).Text("تاریخ");
                    header.Cell().Element(HeaderCellStyle).Text("ش سند");
                    header.Cell().Element(HeaderCellStyle).Text("شرح");
                    header.Cell().Element(HeaderCellStyle).Text("بدهکار");
                    header.Cell().Element(HeaderCellStyle).Text("بستانکار");
                    header.Cell().Element(HeaderCellStyle).Text("مانده");
                    header.Cell().Element(HeaderCellStyle).Text("تش"); // <<< هدر ستون جدید >>>
                });

                int rowIndex = 0;
                // تلورانس برای مقایسه مانده با صفر (برای جلوگیری از خطاهای اعشاری)
                decimal tolerance = 0.01m;

                foreach (var item in _statementItems)
                {
                    rowIndex++;
                    static IContainer BodyCellStyleBase(IContainer c) =>
                        c.BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingVertical(3).PaddingHorizontal(5);
                    static IContainer BodyAlignCenter(IContainer c) => BodyCellStyleBase(c).AlignCenter();
                    static IContainer BodyAlignRight(IContainer c) => BodyCellStyleBase(c).AlignRight();
                    static IContainer BodyAlignLeft(IContainer c) => BodyCellStyleBase(c).AlignLeft();

                    // محاسبه متن "تش" بر اساس مانده
                    string tashkhisText = "";
                    if (item.MAND.HasValue)
                    {
                        if (item.MAND.Value > tolerance)
                        {
                            tashkhisText = "بده";
                        }
                        else if (item.MAND.Value < -tolerance)
                        {
                            tashkhisText = "بس";
                        }
                        // اگر مانده صفر یا خیلی نزدیک به صفر بود، tashkhisText خالی می‌ماند
                    }

                    // نمایش داده‌ها
                    table.Cell().Element(BodyAlignCenter).Text(rowIndex.ToString());
                    table.Cell().Element(BodyAlignCenter).Text(FormatShamsiDateFromLong(item.DATE_S));
                    table.Cell().Element(BodyAlignCenter).Text(FormatDocNumber(item.N_S));
                    table.Cell().Element(c => BodyAlignRight(c).PaddingRight(10)).Text(item.SHARH ?? string.Empty);
                    table.Cell().Element(c => BodyAlignLeft(c).PaddingLeft(10)).Text(FormatNumber(item.BED));
                    table.Cell().Element(c => BodyAlignLeft(c).PaddingLeft(10)).Text(FormatNumber(item.BES));
                    table.Cell().Element(c => BodyAlignLeft(c).PaddingLeft(10)).Text(FormatNumber(item.MAND));
                    // <<< نمایش مقدار محاسبه شده "تش" در ستون جدید (وسط‌چین) >>>
                    table.Cell().Element(BodyAlignCenter).Text(tashkhisText);
                }
            });
        }

        // --- توابع کمکی فرمت‌دهی ---
        private string FormatShamsiDateFromLong(long? dateLong)
        {
            if (!dateLong.HasValue || dateLong.Value <= 0) return string.Empty;
            try
            {
                string dateStr = dateLong.Value.ToString();
                if (dateStr.Length == 8)
                    return $"{dateStr.Substring(0, 4)}/{dateStr.Substring(4, 2)}/{dateStr.Substring(6, 2)}";
                return dateStr;
            }
            catch { return dateLong.Value.ToString(); }
        }

        private string FormatNumber(decimal? number)
        {
            if (!number.HasValue || number.Value == 0) return string.Empty;
            return number.Value.ToString("N0");
        }
        private string FormatDocNumber(double? docNumber)
        {
            if (!docNumber.HasValue) return string.Empty;
            if (docNumber.Value == Math.Floor(docNumber.Value))
                return ((long)docNumber.Value).ToString();
            return docNumber.Value.ToString("N0");
        }
    }
}