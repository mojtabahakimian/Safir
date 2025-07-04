using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Hesabdari;
using Safir.Shared.Utility;

namespace Safir.Server.Controllers
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
        private readonly IWebHostEnvironment _env; // <--- اضافه کردن WebHostEnvironment
        private byte[]? _logoBytes = null;       // <--- متغیر برای نگهداری بایت های لوگو

        private const string PersianFontName = "IRANYekanFN";
        private const string LogoFileName = "2.png"; // <--- نام فایل لوگو
        private const string FontsFolderName = "Fonts"; // <--- نام پوشه فونت/لوگو


        // --- سازنده (Constructor) را آپدیت کنید ---
        public CustomerStatementDocument(IEnumerable<QDAFTARTAFZIL2_H> items,
                                         string hesabCode,
                                         string customerName,
                                         long? start,
                                         long? end,
                                         ILogger logger,
                                         IWebHostEnvironment env) // <--- پارامتر جدید
        {
            _statementItems = items ?? new List<QDAFTARTAFZIL2_H>();
            _hesabCode = hesabCode;
            _customerName = customerName ?? string.Empty;
            _startDate = start;
            _endDate = end;
            _logger = logger;
            _env = env; // <--- ذخیره کردن env

            // --- تلاش برای خواندن فایل لوگو ---
            try
            {
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
            // بقیه کد ثبت فونت بدون تغییر باقی می ماند...
            try
            {
                _logger.LogInformation("Attempted to register Persian font '{FontName}' for QuestPDF.", PersianFontName);
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
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30, Unit.Point);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(PersianFontName));
                page.ContentFromRightToLeft(); // فعال کردن راست به چپ

                page.Header().Element(ComposeHeader); // <--- فراخوانی هدر
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

        private void ComposeHeader(IContainer container)
        {
            container.Row(row =>
            {
                // ستون سمت چپ برای لوگو
                if (_logoBytes != null)
                {
                    row.ConstantItem(80) // عرض ثابت برای لوگو (مثلا 80 پوینت)
                       .AlignLeft()      // تراز به چپ
                       .AlignTop()       // تراز به بالا
                       .PaddingRight(10) // کمی فاصله از متن
                       .Image(_logoBytes)
                       .FitArea(); // یا FitWidth(), FitHeight() بسته به نیاز
                }
                else
                {
                    // اگر لوگو نیست، یک فضای خالی بگذارید تا تراز متن بهم نخورد
                    row.ConstantItem(80);
                }


                // ستون سمت راست برای اطلاعات متنی (بقیه فضای موجود را بگیرد)
                row.RelativeItem().Column(column =>
                {
                    // خط عنوان اصلی – راست چین
                    column.Item().AlignRight()
                          .Text($"صورت حساب مشتری: {_customerName} ({_hesabCode})")
                          .SemiBold().FontSize(14);

                    // تاریخ تهیه گزارش - راست چین
                    column.Item().AlignRight().Text(text =>
                    {
                        text.Span("تاریخ تهیه گزارش: ").SemiBold();
                        text.Span($"{FormatShamsiDateFromLong(CL_Tarikh.GetCurrentPersianDateAsLong())} {DateTime.Now:HH:mm}");
                    });

                    // سایر اطلاعات هدر در صورت نیاز
                });


            });
        }


        // متد ComposeTable بدون تغییر باقی می ماند...
        private void ComposeTable(IContainer container)
        {
            container.PaddingTop(10).Table(table =>
            {
                // تعریف ستون ها مثل قبل
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1f);   // ردیف
                    columns.RelativeColumn(1.2f); // تاریخ
                    columns.RelativeColumn(1f);   // شماره سند
                    columns.RelativeColumn(4f);   // شرح
                    columns.RelativeColumn(2.2f); // بدهکار
                    columns.RelativeColumn(2.2f); // بستانکار
                    columns.ConstantColumn(30f);  // تش (عرض کم)
                    columns.RelativeColumn(2.5f); // مانده
                });

                // ----- تنظیم دقیق پدینگ هدر -----
                table.Header(header =>
                {
                    // استایل پایه هدر (بدون پدینگ افقی عمومی)
                    static IContainer BaseHeaderStyle(IContainer c) =>
                        c.DefaultTextStyle(x => x.SemiBold().FontFamily(PersianFontName))
                         .PaddingVertical(5) // فقط پدینگ عمودی
                         .BorderBottom(1)
                         .BorderColor(Colors.Grey.Lighten2);

                    // تعریف استایل‌های تراز (مثل قبل)
                    static IContainer HeaderCenter(IContainer c) => BaseHeaderStyle(c).AlignCenter();
                    static IContainer HeaderRight(IContainer c) => BaseHeaderStyle(c).AlignRight();

                    // --- اعمال تراز و پدینگ افقی *مجزا* به هر سلول هدر ---
                    // میتونی مقادیر PaddingHorizontal رو برای تنظیم دقیق‌تر تغییر بدی
                    header.Cell().Element(HeaderCenter).PaddingHorizontal(5).Text("ردیف");      // پدینگ 5
                    header.Cell().Element(HeaderCenter).PaddingHorizontal(5).Text("تاریخ");     // پدینگ 5
                    header.Cell().Element(HeaderCenter).PaddingHorizontal(5).Text("ش سند");    // پدینگ 5
                    header.Cell().Element(HeaderRight).PaddingHorizontal(5).Text("شرح");       // پدینگ 5 (برای ستون عریض)
                    header.Cell().Element(HeaderRight).PaddingHorizontal(8).Text("بدهکار");    // پدینگ بیشتر برای اعداد
                    header.Cell().Element(HeaderRight).PaddingHorizontal(8).Text("بستانکار");  // پدینگ بیشتر برای اعداد
                    header.Cell().Element(HeaderCenter).PaddingHorizontal(3).Text("تش");       // پدینگ کمتر برای ستون باریک
                    header.Cell().Element(HeaderRight).PaddingHorizontal(8).Text("مانده");     // پدینگ بیشتر برای اعداد
                });

                // ----- بدنه جدول (بدون تغییر) -----
                int rowIndex = 0;
                decimal tolerance = 0.01m;

                foreach (var item in _statementItems)
                {
                    rowIndex++;
                    static IContainer BaseCellStyle(IContainer c) =>
                       c.BorderBottom(1)
                        .BorderColor(Colors.Grey.Lighten1)
                        .PaddingVertical(3)
                        .PaddingHorizontal(5); // پدینگ افقی سلول های بدنه هم 5 هست

                    static IContainer CellCenter(IContainer c) => BaseCellStyle(c).AlignCenter();
                    static IContainer CellRight(IContainer c) => BaseCellStyle(c).AlignRight();

                    string tashkhisText = string.Empty;
                    if (item.MAND.HasValue)
                    {
                        if (item.MAND.Value > tolerance) tashkhisText = "بده";
                        else if (item.MAND.Value < -tolerance) tashkhisText = "بس";
                    }
                    decimal? absMand = item.MAND.HasValue ? Math.Abs(item.MAND.Value) : item.MAND;

                    table.Cell().Element(CellCenter).Text(rowIndex.ToString());
                    table.Cell().Element(CellCenter).Text(FormatShamsiDateFromLong(item.DATE_S));
                    table.Cell().Element(CellCenter).Text(FormatDocNumber(item.N_S));
                    table.Cell().Element(CellRight).Text(item.SHARH ?? string.Empty);
                    table.Cell().Element(CellRight).Text(FormatNumber(item.BED));
                    table.Cell().Element(CellRight).Text(FormatNumber(item.BES));
                    table.Cell().Element(CellCenter).Text(tashkhisText);
                    table.Cell().Element(CellRight).Text(FormatNumber(absMand));
                }
            });
        }
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

            if (docNumber.Value == Math.Floor(docNumber.Value))
                return ((long)docNumber.Value).ToString();

            return docNumber.Value.ToString("N0");
        }
        #endregion
    }
}