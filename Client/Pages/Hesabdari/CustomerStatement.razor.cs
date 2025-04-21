// prg/Safir23/Client/Pages/Hesabdari/CustomerStatement.razor.cs
using Microsoft.AspNetCore.Components;
using Safir.Client.Services; // <--- این خط رو اضافه کنید یا مطمئن شوید وجود دارد
using Safir.Shared.Models.Hesabdari; // برای ThePart1
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MudBlazor;                     // برای ISnackbar و کامپوننت ها
using Microsoft.Extensions.Logging;  // برای ILogger
using Microsoft.JSInterop;

namespace Safir.Client.Pages.Hesabdari // مطمئن شوید namespace درست است
{
    // نام کلاس باید با نام فایل یکی باشد و partial باشد
    public partial class CustomerStatement : ComponentBase
    {
        // این پراپرتی کد حساب را از آدرس URL دریافت می کند
        [Parameter]
        public string? HesabCode { get; set; }

        // تزریق سرویس ها مورد نیاز
        [Inject] private CustomerApi CustomerApi { get; set; } = default!; // <-- به این شکل تغییر دهید
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private ILogger<CustomerStatement> Logger { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!; // <<< تزریق IJSRuntime

        [Inject] private NavigationManager NavManager { get; set; } = default!; // <-- این خط رو اضافه کنید

        // لیستی برای نگهداری آیتم های صورت حساب دریافتی از سرور
        private List<QDAFTARTAFZIL2_H>? statementItems;
        // فلگی برای نمایش وضعیت لودینگ
        private bool isLoading = false;
        private bool isDownloading = false; // <<< فلگ برای نمایش وضعیت دانلود

        // تاریخ های پیش فرض برای درخواست از سرور (می توانید بعداً امکان تغییرشان را اضافه کنید)
        private long? currentStartDate = 14030101; // مثال
        private long? currentEndDate = 14040130;   // مثال

        // این متد زمانی اجرا می شود که پارامترهای ورودی (مثل HesabCode) مقداردهی شوند
        protected override async Task OnParametersSetAsync()
        {
            await LoadStatement();
        }

        // متد اصلی برای دریافت و بارگذاری اطلاعات صورت حساب
        private async Task LoadStatement()
        {
            // بررسی کد حساب ورودی
            if (string.IsNullOrWhiteSpace(HesabCode))
            {
                Snackbar.Add("کد حساب مشتری نامعتبر است.", Severity.Warning);
                statementItems = new List<QDAFTARTAFZIL2_H>(); // تنظیم لیست خالی
                return;
            }

            isLoading = true;      // شروع لودینگ
            statementItems = null; // پاک کردن داده های قبلی
            StateHasChanged();     // بروزرسانی UI برای نمایش لودینگ

            try
            {
                Logger.LogInformation("Loading statement for {HesabCode} between {StartDate} and {EndDate}", HesabCode, currentStartDate, currentEndDate);

                // فراخوانی سرویس کلاینت برای دریافت داده از API سرور
                statementItems = await CustomerApi.GetCustomerStatementAsync(HesabCode, currentStartDate, currentEndDate);

                // بررسی نتیجه بازگشتی از API
                if (statementItems == null)
                {
                    Snackbar.Add("خطا در دریافت اطلاعات صورت حساب از سرور.", Severity.Error);
                    Logger.LogWarning("API returned null statement items for {HesabCode}", HesabCode);
                    statementItems = new List<QDAFTARTAFZIL2_H>(); // تنظیم لیست خالی در صورت خطا
                }
                else
                {
                    Logger.LogInformation("Successfully loaded {Count} items for {HesabCode}.", statementItems.Count, HesabCode);
                }
            }
            catch (Exception ex) // مدیریت خطاهای پیش بینی نشده
            {
                Logger.LogError(ex, "Error loading statement for {HesabCode}", HesabCode);
                Snackbar.Add($"خطای غیرمنتظره: {ex.Message}", Severity.Error);
                statementItems = new List<QDAFTARTAFZIL2_H>(); // تنظیم لیست خالی در صورت خطا
            }
            finally // این بلاک همیشه اجرا می شود
            {
                isLoading = false; // پایان لودینگ
                StateHasChanged(); // بروزرسانی UI برای نمایش جدول یا پیام خطا
            }
        }

        // --- متدهای کمکی برای فرمت نمایش داده ها در جدول ---

        // تبدیل long تاریخ شمسی (YYYYMMDD) به رشته "YYYY/MM/DD"
        private string FormatShamsiDateFromLong(long? dateLong)
        {
            if (!dateLong.HasValue || dateLong.Value <= 0) return string.Empty;
            try
            {
                string dateStr = dateLong.Value.ToString();
                if (dateStr.Length == 8) // YYYYMMDD
                    return $"{dateStr.Substring(0, 4)}/{dateStr.Substring(4, 2)}/{dateStr.Substring(6, 2)}";
                return dateStr; // بازگرداندن خود عدد اگر فرمت 8 رقمی نبود
            }
            catch (Exception ex) { Logger.LogWarning(ex, "Could not format Shamsi date from long: {DateLong}", dateLong); return dateLong.Value.ToString(); }
        }

        // فرمت اعداد (بدهکار، بستانکار، مانده) با جداکننده هزارگان و حذف صفر
        private string FormatNumber(decimal? number) // <--- اینجا double? به decimal? تغییر کرد
        {
            if (!number.HasValue || number.Value == 0) return string.Empty;
            return number.Value.ToString("N0"); // این کد برای decimal? هم کار می کند
        }

        // فرمت شماره سند (که double? بود) به رشته
        private string FormatDocNumber(double? docNumber)
        {
            if (!docNumber.HasValue) return string.Empty;
            // اگر عدد اعشار ندارد، به long تبدیل و سپس به رشته
            if (docNumber.Value == Math.Floor(docNumber.Value))
                return ((long)docNumber.Value).ToString();
            // اگر اعشار دارد، با فرمت عددی نمایش بده (یا هر فرمت دلخواه دیگر)
            return docNumber.Value.ToString("N0");
        }

        private async Task DownloadPdf()
        {
            if (string.IsNullOrWhiteSpace(HesabCode))
            {
                Snackbar.Add("کد حساب مشتری نامعتبر است.", Severity.Warning);
                return;
            }
            if (statementItems == null || !statementItems.Any())
            {
                Snackbar.Add("داده ای برای دانلود وجود ندارد.", Severity.Info);
                return;
            }

            isDownloading = true; // شروع وضعیت دانلود
            StateHasChanged(); // بروزرسانی UI

            try
            {
                // 1. دریافت بایت های PDF از API با استفاده از CustomerApi
                byte[]? pdfBytes = await CustomerApi.GetCustomerStatementPdfBytesAsync(HesabCode, currentStartDate, currentEndDate);

                if (pdfBytes != null && pdfBytes.Length > 0)
                {
                    // 2. ساخت نام فایل
                    string startDateStr = currentStartDate?.ToString() ?? "all";
                    string endDateStr = currentEndDate?.ToString() ?? "all";
                    string fileName = $"Statement_{HesabCode}_{startDateStr}_{endDateStr}.pdf";

                    // 3. فراخوانی تابع JavaScript برای شروع دانلود
                    await JSRuntime.InvokeVoidAsync("downloadFileFromBytes", fileName, pdfBytes);

                    Snackbar.Add("دانلود PDF آغاز شد.", Severity.Success);
                    Logger.LogInformation("PDF download initiated for HesabCode: {HesabCode}", HesabCode);
                }
                else
                {
                    // اگر بایت ها null یا خالی باشند (یعنی خطا در API رخ داده)
                    Snackbar.Add("خطا در دریافت فایل PDF از سرور.", Severity.Error);
                    Logger.LogError("Failed to receive valid PDF bytes for HesabCode: {HesabCode}", HesabCode);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during PDF download process for HesabCode: {HesabCode}", HesabCode);
                Snackbar.Add($"خطای غیرمنتظره هنگام دانلود: {ex.Message}", Severity.Error);
            }
            finally
            {
                isDownloading = false; // پایان وضعیت دانلود
                StateHasChanged(); // بروزرسانی UI
            }
        }
    }
}