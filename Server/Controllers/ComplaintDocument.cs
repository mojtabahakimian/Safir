// Safir.Server/Controllers/ComplaintDocument.cs (یا Safir.Server/Reports/)
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Safir.Shared.Models.Complaints;
using Safir.Shared.Utility; // برای CL_Tarikh
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Safir.Server.Reports // یا Safir.Server.Controllers
{
    public class ComplaintDocument : IDocument
    {
        private readonly ComplaintFormDto _data;
        private readonly ILogger _logger;
        private readonly IWebHostEnvironment _env;
        private byte[]? _logoBytes = null;

        private const string PersianFontName = "IRANYekanFN"; // Ensure this font is registered
        private const string LogoFileName = "2.png"; // نام فایل لوگوی شما
        private const string FontsFolderName = "Fonts";

        public ComplaintDocument(ComplaintFormDto data, ILogger logger, IWebHostEnvironment env)
        {
            _data = data;
            _logger = logger;
            _env = env;

            try
            {
                string logoPath = Path.Combine(_env.ContentRootPath, FontsFolderName, LogoFileName);
                if (File.Exists(logoPath))
                {
                    _logoBytes = File.ReadAllBytes(logoPath);
                }
                else
                {
                    _logger.LogWarning("Logo file '{LogoFileName}' not found at path: {LogoPath}", LogoFileName, logoPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading logo file '{LogoFileName}' for complaint PDF.", LogoFileName);
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
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(PersianFontName).LineHeight(1.5f));
                    page.ContentFromRightToLeft();

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("صفحه ").FontSize(8);
                        text.CurrentPageNumber().FontSize(8);
                        text.Span(" از ").FontSize(8);
                        text.TotalPages().FontSize(8);
                        text.Span($" - تاریخ چاپ: {CL_Tarikh.FormatShamsiDateFromLong(CL_Tarikh.GetCurrentPersianDateAsLong())} {DateTime.Now:HH:mm}").FontSize(8);
                    });
                });
        }

        void ComposeHeader(IContainer container)
        {
            container.Row(row =>
            {
                if (_logoBytes != null)
                {
                    row.ConstantItem(80).AlignLeft().AlignTop().Image(_logoBytes).FitArea();
                }
                else
                {
                    row.ConstantItem(80); // Placeholder
                }

                row.RelativeItem().Column(column =>
                {
                    column.Item().AlignCenter().Text("فرم رسیدگی به شکایت مشتری").Bold().FontSize(16);
                    column.Item().AlignCenter().Text("مجتمع تولیدی یزد سپار").FontSize(12);
                    if (_data.ComplaintRegisteredDate.HasValue)
                    {
                        column.Item().AlignRight().PaddingTop(5).Text(txt =>
                        {
                            txt.Span("تاریخ ثبت شکایت: ").SemiBold();
                            txt.Span(CL_Tarikh.FormatShamsiDateFromLong(CL_Tarikh.ConvertToPersianDateLong(_data.ComplaintRegisteredDate)));
                        });
                    }
                });
                // ستون خالی برای حفظ تقارن اگر لوگو وجود دارد
                row.ConstantItem(80);
            });
            container.PaddingVertical(10); // فاصله بعد از هدر
        }

        void ComposeContent(IContainer container)
        {
            container.Column(column =>
            {
                column.Spacing(10); // فاصله بین بخش‌ها

                // Section 1: Customer Info
                Section(column, "1 - اطلاعات مشتری:", new[]
                {
                    ($"نام: {_data.CustomerFirstName}", $"نام خانوادگی: {_data.CustomerLastName}"),
                    ($"تلفن همراه: {_data.CustomerMobile}", $"ایمیل: {_data.CustomerEmail ?? "-"}"),
                    ($"آدرس: {_data.CustomerAddress ?? "-"}", "")
                });

                // Section 2: Complaint Info
                Section(column, "2 - اطلاعات مربوط به شکایت:", new[]
                {
                    ($"نوع محصول: {_data.ProductTypeComplaint ?? "-"}", $"نوع پنیر پیتزا: {_data.PizzaType ?? "-"}"),
                    ($"وزن محصول: {_data.ProductWeight ?? "-"}", $"تاریخ تولید: {FormatDate(_data.ProductionDate)}"),
                    ($"تاریخ انقضاء: {FormatDate(_data.ExpiryDate)}", $"کد محصول: {_data.ProductCode ?? "-"}"),
                    ($"سایر لبنیات (نام): {_data.OtherDairyProductName ?? "-"}", ""), // Add weight/code for other dairy if needed
                    ($"محل خرید: {_data.PurchaseLocation ?? "-"}", $"تاریخ خرید: {FormatDate(_data.PurchaseDate)}"),
                    ($"شماره سری ساخت: {_data.BatchNumber ?? "-"}", "")
                });

                // Section 3: Complaint Type
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(colType =>
                {
                    colType.Item().Text("3 - نوع شکایت:").SemiBold().FontSize(11);
                    colType.Item().PaddingLeft(10).Column(checkboxCol => {
                        if (_data.IsComplaintType_TasteSmell) checkboxCol.Item().Text("☑ طعم یا بوی نامطبوع");
                        if (_data.IsComplaintType_Packaging) checkboxCol.Item().Text("☑ خرابی بسته‌بندی");
                        if (_data.IsComplaintType_WrongExpiryDate) checkboxCol.Item().Text("☑ تاریخ انقضاء اشتباه");
                        if (_data.IsComplaintType_NonConformity) checkboxCol.Item().Text("☑ عدم تطبیق محصول با مشخصات درج شده");
                        if (_data.IsComplaintType_ForeignObject) checkboxCol.Item().Text("☑ وجود اجسام خارجی");
                        if (_data.IsComplaintType_AbnormalTexture) checkboxCol.Item().Text("☑ بافت غیر عادی");
                        if (_data.IsComplaintType_Mold) checkboxCol.Item().Text("☑ کپک زدگی");
                        if (_data.IsComplaintType_Other)
                        {
                            checkboxCol.Item().Text("☑ سایر موارد:");
                            checkboxCol.Item().PaddingRight(15).Text(_data.ComplaintType_OtherDescription ?? "-").Italic();
                        }
                    });
                });


                // Section 4: Complaint Description
                Section(column, "4 - توضیحات شکایت:", _data.ComplaintDescription);

                // Section 5: Customer Actions
                Section(column, "5 - اقدامات صورت گرفته از سوی مشتری:",
                    _data.CustomerActionTaken ? $"بله - توضیحات: {_data.CustomerActionDescription ?? "توضیحی ثبت نشده"}" : "خیر");

                // Section 6: Customer Request
                column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(colReq => {
                    colReq.Item().Text("6 - درخواست شما:").SemiBold().FontSize(11);
                    colReq.Item().PaddingLeft(10).Column(reqCheckboxes => {
                        if (_data.RequestedResolution_Refund) reqCheckboxes.Item().Text("☑ باز پرداخت وجه");
                        if (_data.RequestedResolution_Replacement) reqCheckboxes.Item().Text("☑ تعویض محصول");
                        if (_data.RequestedResolution_FurtherInvestigation) reqCheckboxes.Item().Text("☑ بررسی بیشتر");
                        if (!string.IsNullOrWhiteSpace(_data.RequestedResolution_Explanation))
                        {
                            reqCheckboxes.Item().Text("توضیحات درخواست:").SemiBold();
                            reqCheckboxes.Item().PaddingRight(15).Text(_data.RequestedResolution_Explanation).Italic();
                        }
                    });
                });


                // Section 7: Confirmation
                Section(column, "7 - تأیید اطلاعات:", _data.InformationConfirmed ? "تأیید می‌کنم تمام اطلاعات وارد شده صحیح است." : "تأیید نشده است.");
            });
        }

        // Helper to create sections with a title and content
        private void Section(ColumnDescriptor column, string title, string content)
        {
            column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(col =>
            {
                col.Item().Text(title).SemiBold().FontSize(11);
                col.Item().PaddingRight(10).Text(content ?? "-");
            });
        }

        // Helper to create sections with a title and key-value pairs
        private void Section(ColumnDescriptor column, string title, (string Key, string Value)[] items)
        {
            column.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(col =>
            {
                col.Item().Text(title).SemiBold().FontSize(11);
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.Key)) // فقط اگر کلید وجود داشت، ردیف را نمایش بده
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignRight().Text(item.Key);
                            if (!string.IsNullOrEmpty(item.Value)) // اگر مقدار هم وجود داشت، آن را نمایش بده
                            {
                                row.ConstantItem(15); // فاصله
                                row.RelativeItem(2).AlignRight().Text(item.Value); // مقدار را با نسبت بیشتر نمایش بده
                            }
                            else // اگر فقط کلید بود و مقدار خالی
                            {
                                row.RelativeItem(2); // فضای خالی برای تقارن
                            }
                        });
                    }
                }
            });
        }
        private string FormatDate(DateTime? date) => date.HasValue ? CL_Tarikh.FormatShamsiDateFromLong(CL_Tarikh.ConvertToPersianDateLong(date)) : "-";
    }
}