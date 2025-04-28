// File: Server/Controllers/ProformaDocument.cs (Or Server/Reports/ProformaDocument.cs)
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Kharid; // For ProformaPrintDto
using Safir.Shared.Utility;       // For CL_Tarikh & Format helpers
using System;
using System.Globalization;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using System.Linq; // Needed for .Any()

namespace Safir.Server.Controllers // Or Safir.Server.Reports
{
    public class ProformaDocument : IDocument
    {
        private readonly ProformaPrintDto _data;
        private readonly ILogger _logger;
        private readonly IWebHostEnvironment _env;
        private byte[]? _logoBytes = null;

        private const string PersianFontName = "IRANYekanFN"; // Make sure this font is registered in Program.cs
        private const string LogoFileName = "2.png";
        private const string FontsFolderName = "Fonts";
        private static readonly CultureInfo FaCulture = new CultureInfo("fa-IR");

        public ProformaDocument(ProformaPrintDto data, ILogger logger, IWebHostEnvironment env)
        {
            _data = data;
            _logger = logger;
            _env = env;

            // Load Logo (similar to CustomerStatementDocument)
            try
            {
                // Corrected path combination for ContentRootPath
                string logoPath = Path.Combine(_env.ContentRootPath, FontsFolderName, LogoFileName);
                if (File.Exists(logoPath))
                {
                    _logoBytes = File.ReadAllBytes(logoPath);
                    _logger.LogInformation("Logo file '{LogoFileName}' loaded successfully from path: {LogoPath}", LogoFileName, logoPath);
                }
                else
                {
                    _logger.LogWarning("Logo file '{LogoFileName}' not found at path: {LogoPath}", LogoFileName, logoPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading logo file '{LogoFileName}'", LogoFileName);
                _logoBytes = null;
            }
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container
              .Page(page =>
              {
                  page.Size(PageSizes.A4);
                  page.Margin(1.5f, Unit.Centimetre);
                  page.PageColor(Colors.White);
                  page.DefaultTextStyle(x => x.FontSize(9).FontFamily(PersianFontName));
                  page.ContentFromRightToLeft();

                  page.Header().Element(ComposeHeader);
                  page.Content().Element(ComposeContent);
                  page.Footer().Element(ComposeFooter);
              });
        }

        void ComposeHeader(IContainer container)
        {
            container.Column(headerColumn =>
            {
                // Row for Info, Title, Logo (Order Changed)
                headerColumn.Item().Row(row =>
                {
                    // 1. Info Column (Number, Date) - Moved to the right
                    row.RelativeItem().Column(column =>
                    {
                        column.Spacing(2);
                        column.Item().AlignRight().Text(text =>
                        {
                            text.Span("شماره : ").SemiBold();
                            text.Span(_data.Header.NUMBER.ToString("F0"));
                        });
                        column.Item().AlignRight().Text(text =>
                        {
                            text.Span("تاریخ : ").SemiBold();
                            text.Span(FormatShamsiDateFromLong(_data.Header.DATE_N));
                        });
                    });

                    // 2. Title Column - Remains centered
                    row.RelativeItem().AlignCenter().Text("پیش فاکتور فروش") // Changed title to match image
                      .SemiBold().FontSize(14);

                    // 3. Optional Logo Column - Moved to the left
                    if (_logoBytes != null)
                    {
                        // Use AlignLeft() for the logo container
                        row.ConstantItem(80).AlignLeft().AlignTop().Image(_logoBytes).FitArea();
                    }
                    else
                    {
                        row.ConstantItem(80); // Placeholder if no logo
                    }
                });

                // Customer Info Section (remains below the main header row)
                headerColumn.Item().PaddingTop(10).BorderBottom(1).BorderColor(Colors.Grey.Lighten1).Column(customerInfoColumn =>
                {
                    // ... (Customer info rows remain the same as before) ...
                    customerInfoColumn.Item().Row(row => {
                        row.RelativeItem().AlignRight().Text(text => {
                            text.Span("خریدار: ").SemiBold();
                            text.Span($"{_data.Header.CustomerName ?? ""}");
                        });
                        row.ConstantItem(150).AlignRight().Text(text => {
                            text.Span("کد: ").SemiBold();
                            text.Span($"{_data.Header.CUST_NO ?? ""}");
                        });
                    });
                    customerInfoColumn.Item().PaddingTop(2).Row(row => {
                        row.RelativeItem().AlignRight().Text(text => {
                            text.Span("آدرس: ").SemiBold();
                            text.Span($"{_data.Header.CustomerAddress ?? ""}");
                        });
                        row.ConstantItem(150).AlignRight().Text(text => {
                            text.Span("تلفن: ").SemiBold();
                            text.Span($"{_data.Header.CustomerTel ?? ""}");
                        });
                    });
                    if (!string.IsNullOrWhiteSpace(_data.Header.MOLAH))
                    {
                        customerInfoColumn.Item().PaddingTop(2).AlignRight().Text(text => {
                            text.Span("ملاحظات: ").SemiBold();
                            text.Span($"{_data.Header.MOLAH}");
                        });
                    }
                    customerInfoColumn.Item().PaddingVertical(5); // Space before table
                });
            });
        }



        void ComposeContent(IContainer container)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(30);   // ردیف
                    columns.RelativeColumn(5);    // شرح کالا
                    columns.RelativeColumn(1.5f); // واحد کالا
                    columns.RelativeColumn(1.5f); // مقدار
                    columns.RelativeColumn(2f);   // فی
                    columns.RelativeColumn(1.5f); // تخفیف %
                    columns.RelativeColumn(2.5f); // مبلغ کل
                });

                table.Header(header =>
                {
                    static IContainer HeaderCellStyle(IContainer c) => c.DefaultTextStyle(x => x.SemiBold()).BorderBottom(1).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter();

                    header.Cell().Element(HeaderCellStyle).Text("ردیف");
                    header.Cell().Element(HeaderCellStyle).Text("شرح کالا");
                    header.Cell().Element(HeaderCellStyle).Text("واحد کالا");
                    header.Cell().Element(HeaderCellStyle).Text("مقدار");
                    header.Cell().Element(HeaderCellStyle).Text("فی");
                    header.Cell().Element(HeaderCellStyle).Text("تخفیف %");
                    header.Cell().Element(HeaderCellStyle).Text("مبلغ کل");
                });

                short index = 0;
                if (_data.Lines != null && _data.Lines.Any()) // Check if Lines is not null and has items
                {
                    foreach (var item in _data.Lines)
                    {
                        index++;
                        static IContainer BodyCellStyle(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).AlignCenter();
                        static IContainer BodyCellRight(IContainer c) => BodyCellStyle(c).AlignRight();
                        static IContainer BodyCellLeft(IContainer c) => BodyCellStyle(c).AlignLeft();

                        table.Cell().Element(BodyCellStyle).Text(index.ToString());
                        table.Cell().Element(BodyCellRight).Text(item.ItemName ?? "");
                        table.Cell().Element(BodyCellStyle).Text(item.UnitName ?? "");
                        table.Cell().Element(BodyCellStyle).Text(item.MEGH.ToString("N0", FaCulture));
                        table.Cell().Element(BodyCellLeft).Text(item.MABL.ToString("N0", FaCulture));
                        table.Cell().Element(BodyCellLeft).Text(item.N_KOL.ToString("N1", FaCulture));
                        table.Cell().Element(BodyCellLeft).Text(item.NetAmount.ToString("N0", FaCulture));
                    }
                }
                else
                {
                    // Optional: Add a row indicating no items if the list is empty
                    table.Cell().ColumnSpan(7).Padding(10).AlignCenter().Text("موردی برای نمایش وجود ندارد.");
                }
            });
        }


        void ComposeFooter(IContainer container)
        {
            container.BorderTop(1).BorderColor(Colors.Grey.Medium).PaddingTop(5).Row(row =>
            {
                // Left Side
                row.RelativeItem().AlignRight().PaddingRight(10).Column(col => {
                    col.Item().Text(text => {
                        text.Span("مبلغ به حروف: ").SemiBold();
                        // Consider using a library like Humanizer.Core.Persian or Num2Str.Persian for converting number to words
                        // Example placeholder:
                        text.Span($"{_data.AmountInWords ?? (_data.TotalAmountPayable > 0 ? "محاسبه نشده" : "صفر")}");
                    });
                    if (!string.IsNullOrWhiteSpace(_data.Header.SHARAYET))
                    {
                        col.Item().PaddingTop(5).Text(text => {
                            text.Span("شرایط: ").SemiBold();
                            text.Span($"{_data.Header.SHARAYET}");
                        });
                    }
                    // Add signature placeholders
                    col.Item().PaddingTop(20).Row(sigRow => // Add more padding if needed
                    {
                        sigRow.RelativeItem().AlignCenter().Text("مهر و امضاء فروشنده").FontSize(8);
                        sigRow.RelativeItem().AlignCenter().Text("مهر و امضاء خریدار").FontSize(8);
                    });
                });


                // Right Side (Totals)
                row.ConstantItem(180).Column(col =>
                {
                    col.Spacing(2);

                    static void TotalLine(IContainer c, string title, decimal value)
                    {
                        c.Row(row =>
                        {
                            row.RelativeItem().AlignRight().Text(title).SemiBold();
                            row.ConstantItem(100).AlignLeft().Text(value.ToString("N0", FaCulture));
                        });
                    }

                    TotalLine(col.Item(), "جمع کل:", _data.TotalAmountBeforeDiscount);
                    TotalLine(col.Item(), "تخفیف:", _data.TotalDiscountAmount);
                    if ((_data.Header.MABL_HAZ ?? 0) > 0)
                    {
                        TotalLine(col.Item(), "خدمات:", _data.Header.MABL_HAZ ?? 0);
                    }
                    if (_data.TotalVatAmount > 0) // Show VAT only if it's calculated
                    {
                        TotalLine(col.Item(), "م.ارزش افزوده:", _data.TotalVatAmount);
                    }
                    // Add a separator line before the final total
                    col.Item().PaddingVertical(2).BorderBottom(1).BorderColor(Colors.Grey.Lighten1);
                    TotalLine(col.Item(), "قابل پرداخت:", _data.TotalAmountPayable);
                });
            });
        }


        // Helper Format Methods
        private string FormatShamsiDateFromLong(long? dateLong)
        {
            if (!dateLong.HasValue || dateLong.Value <= 0) return string.Empty;
            try
            {
                string d = dateLong.Value.ToString();
                return d.Length == 8 ? $"{d[..4]}/{d.Substring(4, 2)}/{d.Substring(6, 2)}" : d;
            }
            catch { return dateLong.Value.ToString(); }
        }

        // Optional: Add helper for converting number to words if you implement a library
        // private string ConvertAmountToWords(decimal amount) { ... }
    }
}