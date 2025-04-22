// PdfGenerator.cs
// -------------------------------
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

namespace Safir.Server.Controllers // یا namespace صحیح شما
{
    /// <summary>
    /// کلاس تولید PDF صورت حساب مشتری
    /// </summary>
    public class CustomerStatementDocument : IDocument
    {
        private readonly IEnumerable<QDAFTARTAFZIL2_H> _statementItems;
        private readonly string _hesabCode;
        private readonly long? _startDate;
        private readonly long? _endDate;
        private readonly ILogger _logger;
        private readonly string _customerName;
        private const string PersianFontName = "IRANYekanFN";

        public CustomerStatementDocument(IEnumerable<QDAFTARTAFZIL2_H> items,
                                         string hesabCode,
                                         string customerName,   // <‑‑ دریافت نام مشتری
                                         long? start,
                                         long? end,
                                         ILogger logger)
        {
            _statementItems = items ?? new List<QDAFTARTAFZIL2_H>();
            _hesabCode = hesabCode;
            _customerName = customerName ?? string.Empty;       // ذخیره
            _startDate = start;
            _endDate = end;
            _logger = logger;

            // --- ثبت فونت فارسی (در صورت نیاز) ---
            try
            {
                // اگر قبلاً ثبت شده باشد مشکلی ایجاد نمی‌شود
                // FontManager.RegisterFont(File.OpenRead("irsans.ttf"));
                _logger.LogInformation("Attempted to register Persian font '{FontName}' for QuestPDF.", PersianFontName);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Persian font file not found. PDF may not render Persian text correctly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering Persian font for QuestPDF.");
            }
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                // 1) حالت Landscape
                page.Size(PageSizes.A4.Landscape());

                page.Margin(30, Unit.Point);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(PersianFontName));
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

        #region Header
        private void ComposeHeader(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    // خط عنوان اصلی – حالا شامل نام و کد
                    column.Item().AlignRight()
                          .Text($"صورت حساب مشتری: {_customerName} ({_hesabCode})")
                          .SemiBold().FontSize(14);

                    column.Item().AlignRight().Text(text =>
                    {
                        text.Span("تاریخ تهیه گزارش: ").SemiBold();
                        text.Span($"{FormatShamsiDateFromLong(CL_Tarikh.PersianCalendarHelper.GetCurrentPersianDateAsLong())} {DateTime.Now:HH:mm}");
                    });
                });

                // در صورت نیاز لوگو:
                // row.ConstantItem(100).Height(50).Image(...);
            });
        }
        #endregion

        #region Table
        private void ComposeTable(IContainer container)
        {
            container.PaddingTop(10).Table(table =>
            {
                // 2) ترتیب ستون‌ها (ردیف، تاریخ، ش سند، شرح، بدهکار، بستانکار، تش، مانده)
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1f);   // ردیف
                    columns.RelativeColumn(1.2f); // تاریخ
                    columns.RelativeColumn(1f);   // شماره سند
                    columns.RelativeColumn(4f);   // شرح
                    columns.RelativeColumn(2.2f); // بدهکار
                    columns.RelativeColumn(2.2f); // بستانکار
                    columns.ConstantColumn(30f);  // تش
                    columns.RelativeColumn(2.5f); // مانده
                });

                // هدر
                table.Header(header =>
                {
                    static IContainer HeaderCell(IContainer c) =>
                        c.DefaultTextStyle(x => x.SemiBold().FontFamily(PersianFontName))
                         .PaddingVertical(5)
                         .BorderBottom(1)
                         .BorderColor(Colors.Grey.Lighten2)
                         .AlignCenter();

                    header.Cell().Element(HeaderCell).Text("ردیف");
                    header.Cell().Element(HeaderCell).Text("تاریخ");
                    header.Cell().Element(HeaderCell).Text("ش سند");
                    header.Cell().Element(HeaderCell).Text("شرح");
                    header.Cell().Element(HeaderCell).Text("بدهکار");
                    header.Cell().Element(HeaderCell).Text("بستانکار");
                    header.Cell().Element(HeaderCell).Text("تش");
                    header.Cell().Element(HeaderCell).Text("مانده");
                });

                int     rowIndex  = 0;
                decimal tolerance = 0.01m; // برای تشخیص بده/بس

                foreach (var item in _statementItems)
                {
                    rowIndex++;

                    static IContainer BodyCell(IContainer c) =>
                        c.BorderBottom(1)
                         .BorderColor(Colors.Grey.Lighten1)
                         .PaddingVertical(3)
                         .PaddingHorizontal(5);

                    static IContainer CellCenter(IContainer c) => BodyCell(c).AlignCenter();
                    static IContainer CellRight(IContainer c)  => BodyCell(c).AlignRight();
                    static IContainer CellLeft(IContainer c)   => BodyCell(c).AlignLeft();

                    // محاسبه ستون "تش"
                    string tashkhisText = string.Empty;
                    if (item.MAND.HasValue)
                    {
                        if (item.MAND.Value > tolerance)        tashkhisText = "بده";
                        else if (item.MAND.Value < -tolerance) tashkhisText = "بس";
                    }

                    // قدر مطلق مانده برای نمایش
                    decimal? absMand = item.MAND.HasValue ? Math.Abs(item.MAND.Value) : item.MAND;

                    // --- افزودن سلول‌ها به ترتیب جدید ---
                    table.Cell().Element(CellCenter).Text(rowIndex.ToString());
                    table.Cell().Element(CellCenter).Text(FormatShamsiDateFromLong(item.DATE_S));
                    table.Cell().Element(CellCenter).Text(FormatDocNumber(item.N_S));
                    table.Cell().Element(c => CellRight(c).PaddingRight(10)).Text(item.SHARH ?? string.Empty);
                    table.Cell().Element(c => CellLeft(c).PaddingLeft(10)).Text(FormatNumber(item.BED));
                    table.Cell().Element(c => CellLeft(c).PaddingLeft(10)).Text(FormatNumber(item.BES));
                    table.Cell().Element(CellCenter).Text(tashkhisText);                    // تش
                    table.Cell().Element(c => CellLeft(c).PaddingLeft(10)).Text(FormatNumber(absMand)); // مانده (قدر مطلق)
                }
            });
        }
        #endregion

        #region Helper Format Methods
        private string FormatShamsiDateFromLong(long? dateLong)
        {
            if (!dateLong.HasValue || dateLong.Value <= 0) return string.Empty;

            try
            {
                string d = dateLong.Value.ToString();
                return d.Length == 8
                    ? $"{d[..4]}/{d.Substring(4, 2)}/{d.Substring(6, 2)}"
                    : d;
            }
            catch
            {
                return dateLong.Value.ToString();
            }
        }

        private string FormatNumber(decimal? number)
        {
            if (!number.HasValue || number.Value == 0) return string.Empty;
            return number.Value.ToString("N0");
        }

        private string FormatDocNumber(double? docNumber)
        {
            if (!docNumber.HasValue) return string.Empty;

            // اگر عدد صحیح باشد، بدون جداکننده اعشار چاپ می‌کنیم
            if (docNumber.Value == Math.Floor(docNumber.Value))
                return ((long)docNumber.Value).ToString();

            return docNumber.Value.ToString("N0");
        }
        #endregion
    }
}
