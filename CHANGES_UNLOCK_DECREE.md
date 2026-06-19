# مستندات کامل تغییرات — feat: لغو تأیید حکم کارگزینی (Unlock Decree)

> **PR:** #80 — branch: `feat/unlock-decree-v2` → `master`  
> **تعداد فایل‌های تغییریافته:** ۹ فایل | **+210 / -95 خط**

---

## فهرست تغییرات

| # | فایل | موضوع اصلی |
|---|------|------------|
| ۱ | `Server/Controllers/Pay2EmployeesController.cs` | گارد سه‌لایه — فیچر unlock — IDOR fix |
| ۲ | `Server/Controllers/Pay2EmployeesController.cs` | `_autoDeductionCodes` — ثابت مشترک |
| ۳ | `Server/Controllers/Pay2EmployeesController.cs` | `QuerySingleOrDefaultAsync` در ۴ endpoint |
| ۴ | `Server/Controllers/Pay2EmployeesController.cs` | `SaveDecreeLine` — اعتبارسنجی ITEM_TYPE سرور |
| ۵ | `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeModal.razor` | دکمه و منطق `UnlockDecree` |
| ۶ | `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeModal.razor` | `Task.WhenAll` در `LoadData` |
| ۷ | `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeLineModal.razor` | بنر راهنما برای حکم قفل |
| ۸ | `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeLineModal.razor` | اعتبارسنجی مبلغ (`amt <= 0`) |
| ۹ | `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeLineModal.razor` | `FilterEditableItemDefs` + `Task.WhenAll` |
| ۱۰ | `Client/Pages/Salary/Tabs/EmployeeComponents/ItemTemplateLineModal.razor` | همان تغییرات DecreeLineModal |
| ۱۱ | `Client/Pages/Salary/Tabs/EmployeeComponents/ItemTemplateLineModal.razor` | رفع cast در `EditLine` |
| ۱۲ | `Client/Services/Pay2SettingsApiService.cs` | `GetShiftModeAsync` با field cache |
| ۱۳ | `Client/Services/Pay2SettingsApiService.cs` | `IsShiftPctItem` static helper |
| ۱۴ | `Client/Services/Pay2SettingsApiService.cs` | `FilterEditableItemDefs` static helper |
| ۱۵ | `Server/Scripts/008_DecreeLineAmountDecimal.sql` | اصلاح فیلتر `parent_object_id` |
| ۱۶ | `Server/Info/PAY2_Procedures_v6.sql` | رفع truncation اعشاری — `ROUND` |
| ۱۷ | `Server/Info/ScriptSqly.cs` | رفع truncation اعشاری — `ROUND` |
| ۱۸ | `Server/Scripts/006_Pay2_HourlyCalcBasis.sql` | رفع truncation اعشاری — `ROUND` |

---

## تغییر ۱ — گارد سه‌لایه در `SaveDecree` (فیچر اصلی Unlock Decree)

**فایل:** `Server/Controllers/Pay2EmployeesController.cs`  
**محل:** متد `SaveDecree` — بخش `else // ویرایش`

### مشکل اولیه
گارد قبلی برای **همه ویرایش‌ها** (بدون توجه به `IS_CONFIRMED`) اجرا می‌شد و فقط یک سوال می‌پرسید: «آیا هیچ حقوق قطعی‌ای برای این کارمند از تاریخ شروع حکم به بعد وجود دارد؟». این منطق دو مشکل داشت:
1. احکام تأییدنشده را نیز بلاک می‌کرد (false positive)
2. هیچ راهی برای unlock کردن یک حکم تأییدشده وجود نداشت

### علت فنی
گارد قبلی شرط `currentDecId > 0` داشت — یعنی روی **هر** ویرایش اجرا می‌شد. وقتی کاربر می‌خواست `IS_CONFIRMED` را از `true` به `false` تغییر دهد (unlock)، گارد فعال می‌شد و اگر حقوقی قطعی شده بود، این عملیات را بلاک می‌کرد — حتی اگر آن ماه‌های قطعی‌شده مربوط به دوره پس از EFF_TO حکم بودند.

### کد قبل از تغییر
```csharp
// اجرا برای همه ویرایش‌ها بدون استثنا
if (currentDecId > 0)
{
    string checkUsageSql = @"
SELECT COUNT(1) 
FROM PAY2_RUN R
INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
WHERE R.STATUS >= 2 
  AND (P.PERIOD_DATE / 100) >= (SELECT (EFF_FROM / 100) FROM PAY2_DECREE WHERE DEC_ID = @DEC_ID)
  AND R.RUN_ID IN (SELECT RUN_ID FROM PAY2_RUN_LINE WHERE EMP_ID = @EMP_ID)";  // EMP_ID از DTO!

    int usedInFinalRun = await conn.QuerySingleAsync<int>(checkUsageSql,
        new { DEC_ID = currentDecId, EMP_ID = decree.EMP_ID }, tran);

    if (usedInFinalRun > 0)
        throw new InvalidOperationException("...");
}

// سپس فقط IS_CONFIRMED در UPDATE مشارکت داشت و منطق unlock وجود نداشت
bool wasConfirmed = await conn.QuerySingleAsync<bool>(
    "SELECT IS_CONFIRMED FROM PAY2_DECREE WHERE DEC_ID = @DEC_ID", ...);
```

### تغییرات اعمال‌شده
گارد قبلی کاملاً حذف و با ساختار سه‌لایه جایگزین شد:

```csharp
// لایه ۰: خواندن وضعیت واقعی از DB (نه DTO)
var dbDecree = await conn.QuerySingleOrDefaultAsync(
    "SELECT IS_CONFIRMED, EMP_ID FROM PAY2_DECREE WHERE DEC_ID = @DEC_ID", ...) 
    ?? throw new KeyNotFoundException();
bool wasConfirmed = (bool)dbDecree.IS_CONFIRMED;
int dbEmpId = (int)dbDecree.EMP_ID;
if (dbEmpId != decree.EMP_ID) throw new UnauthorizedAccessException();

// لایه ۱: فقط احکام تأییدشده — از تاریخ‌های DB (نه DTO)
if (wasConfirmed)
{
    // JOIN روی PAY2_DECREE برای EFF_FROM و EFF_TO از DB
    // شرط EFF_TO IS NULL OR <= برای بازه بسته
    // EMP_ID از D.EMP_ID (نه @EMP_ID از DTO)
    if (usedInFinalRun > 0) throw new InvalidOperationException("...");
}

// لایه ۲: حکم تأییدشده که تأیید می‌ماند → فقط NOTES
if (wasConfirmed && decree.IS_CONFIRMED)
{
    UPDATE PAY2_DECREE SET NOTES=@NOTES WHERE DEC_ID=@DEC_ID
}
else
{
    // لایه ۳: گارد re-confirm — جلوگیری از backdating
    if (!wasConfirmed && decree.IS_CONFIRMED)
    {
        // بررسی بازه تاریخی جدید با تاریخ‌های DTO (درست: چون می‌خواهیم تاریخ جدید را چک کنیم)
        if (reconfirmConflict > 0) throw new InvalidOperationException("...");
    }
    // full UPDATE (شامل unlock: wasConfirmed=true, IS_CONFIRMED=false)
}
```

### دلیل انتخاب این راه‌حل
- **سه‌لایه‌ای بودن**: هر سناریو (تأیید‌شده→قفل، تأیید‌شده→unlock، تأییدنشده→re-confirm، تأییدنشده→ویرایش) یک مسیر مجزا دارد
- **تاریخ‌های DB**: لایه ۱ از تاریخ‌های ذخیره‌شده در DB استفاده می‌کند تا مانع bypass از طریق دستکاری تاریخ در DTO شود
- **EFF_TO boundary**: قید `D.EFF_TO IS NULL OR <= period` اضافه شد — گارد قبلی فقط EFF_FROM داشت و هر ماه پس از آن را بلاک می‌کرد

### عواقب بدون این تغییر
- امکان unlock کردن هیچ حکمی وجود نداشت → فیچر اصلی کار نمی‌کرد
- گارد قبلی احکام تأییدنشده را نیز بلاک می‌کرد (overly restrictive)
- حکمی با EFF_FROM=14000101 و EFF_TO=14001229 حتی اگر ماه‌های بعدش قطعی شده بود، قابل ویرایش نبود

### اثر بر سایر بخش‌ها
- `DecreeModal.razor` — دکمه UnlockDecree به این منطق وابسته است (تغییر ۵)
- `checkUsageSql` و `checkReconfirmSql` اکنون از `dbEmpId` (از DB) به جای `decree.EMP_ID` استفاده می‌کنند

---

## تغییر ۲ — `_autoDeductionCodes` — ثابت مشترک کدهای کسر اتوماتیک

**فایل:** `Server/Controllers/Pay2EmployeesController.cs`  
**محل:** سطح کلاس `Pay2EmployeesController`

### مشکل اولیه
کد کسورات اتوماتیک در سه جای مختلف به صورت literal تکرار شده بود:
- SQL `itemdefs-lookup` endpoint: `NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')`
- `SaveDecreeLine` (جدید): اعتبارسنجی آیتم
- `Pay2SettingsApiService` (جدید): `FilterEditableItemDefs`

### تغییر اعمال‌شده
```csharp
private static readonly HashSet<string> _autoDeductionCodes = new(StringComparer.OrdinalIgnoreCase)
    { "INS_DED", "TAX_DED", "LOAN_DED", "ADVANCE_DED" };
```

### دلیل انتخاب
- `static readonly`: یک‌بار ساخته می‌شود، برای همه instance‌ها مشترک است
- `StringComparer.OrdinalIgnoreCase`: حروف بزرگ/کوچک را تطبیق می‌دهد
- `HashSet`: جستجوی O(1) به جای O(n) لیست

### عواقب بدون این تغییر
اگر کد جدیدی مثل `PENSION_DED` به DB اضافه شود، توسعه‌دهنده باید چند جای مختلف را به‌روز کند. فراموش کردن یکی → ناهماهنگی بین endpoints.

---

## تغییر ۳ — `QuerySingleOrDefaultAsync` در ۴ Endpoint

**فایل:** `Server/Controllers/Pay2EmployeesController.cs`  
**محل‌ها:**
- `SaveDecree` (edit path)
- `DeleteDecree`
- `SaveDecreeLine`
- `DeleteDecreeLine`

### مشکل اولیه
همه ۴ endpoint از `QuerySingleAsync` استفاده می‌کردند. وقتی `DEC_ID` موجود نباشد (سطر حذف شده)، Dapper exception داخلی `InvalidOperationException("Sequence contains no elements")` پرتاب می‌کند. این exception توسط `catch (InvalidOperationException)` در پایین هر متد گرفته می‌شد و به عنوان `400 BadRequest` با پیام فنی انگلیسی (`Sequence contains no elements`) به client برمی‌گشت.

### علت فنی
`QuerySingleAsync` در Dapper وقتی صفر سطر برگرداند `InvalidOperationException` می‌اندازد — همان نوع exception‌ای که برای خطاهای business logic استفاده می‌شود. این باعث می‌شود client نتواند تشخیص دهد آیا خطا از `IS_CONFIRMED` یا از نبود سطر بوده است.

### کد قبل از تغییر
```csharp
// در DeleteDecree:
bool isConfirmed = await conn.QuerySingleAsync<bool>(
    "SELECT IS_CONFIRMED FROM PAY2_DECREE WHERE DEC_ID = @decId", ...);
// اگر decId نباشد → throw InvalidOperationException → BadRequest("Sequence contains no elements")
```

### تغییر اعمال‌شده
```csharp
var decRow = await conn.QuerySingleOrDefaultAsync(
    "SELECT IS_CONFIRMED FROM PAY2_DECREE WHERE DEC_ID = @decId", ...)
    ?? throw new KeyNotFoundException();
// ...
catch (KeyNotFoundException) { return NotFound("حکم مورد نظر یافت نشد."); }
```

### دلیل انتخاب
- `QuerySingleOrDefaultAsync` کنترل‌شده‌تر از `QuerySingleAsync` است
- `KeyNotFoundException` نوع متفاوتی از `InvalidOperationException` است — catch بلاک‌ها می‌توانند آن را جداگانه هندل کنند
- `NotFound(404)` معنای درستی دارد: «این resource وجود ندارد»
- پیام فارسی به جای پیام داخلی Dapper

### عواقب بدون این تغییر
- double-click روی Delete: request دوم `400 BadRequest("Sequence contains no elements")` می‌گیرد → کاربر می‌بیند delete ناموفق بوده در حالی که موفق بوده
- پیام خطای فنی انگلیسی در UI فارسی ظاهر می‌شود
- Client نمی‌تواند بین «حکم وجود ندارد» و «قانون business نقض شده» تمایز قائل شود

### اثر بر سایر بخش‌ها
Client (`DecreeModal.razor`, `DecreeLineModal.razor`) اکنون می‌تواند status code 404 را جداگانه هندل کند.

---

## تغییر ۴ — `SaveDecreeLine`: اعتبارسنجی ITEM_TYPE در سرور

**فایل:** `Server/Controllers/Pay2EmployeesController.cs`  
**محل:** متد `SaveDecreeLine` (`POST /api/pay2/employees/decree/line/save`)

### مشکل اولیه
`SaveDecreeLine` هیچ بررسی‌ای روی نوع آیتمی که کاربر می‌فرستد انجام نمی‌داد. تنها guard موجود، فیلتر client-side در dropdown بود. اگر یک آیتم کسری اتوماتیک (مثل `INS_DED`) در dropdown ظاهر می‌شد (مثلاً به خاطر باگ در فیلتر)، server آن را بدون سوال در `PAY2_DECREE_LINE` ذخیره می‌کرد.

### علت فنی
اصل defense-in-depth: نباید به فیلتر client اعتماد کرد. API باید در سرور نیز اعتبارسنجی کند. `PAY2_ITEM_DEF.ITEM_TYPE`:
- 1 = پرداختی ثابت ✓
- 2 = پرداختی متغیر ✓  
- 3 = کسر ثابت ✗
- 4 = کسر متغیر ✗
- 5 = آگاهی/نمایش ✗

### کد قبل از تغییر
```csharp
bool isConfirmed = await conn.QuerySingleAsync<bool>(...);
if (isConfirmed) throw ...;

// بلافاصله INSERT/UPDATE — هیچ بررسی روی ITEM_ID
int count = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_DECREE_LINE...");
if (count == 0) { INSERT ... } else { UPDATE ... }
```

### تغییر اعمال‌شده
```csharp
var itemInfo = await conn.QuerySingleOrDefaultAsync(
    "SELECT ITEM_TYPE, ITEM_CODE, IS_ACTIVE FROM PAY2_ITEM_DEF WHERE ITEM_ID = @ITEM_ID", ...)
    ?? throw new InvalidOperationException("آیتم حقوقی مورد نظر یافت نشد.");

if (!(bool)itemInfo.IS_ACTIVE)
    throw new InvalidOperationException("آیتم حقوقی مورد نظر غیرفعال است.");

byte itemType = (byte)itemInfo.ITEM_TYPE;
if (itemType != 1 && itemType != 2)
    throw new InvalidOperationException("فقط آیتم‌های پرداختی (نوع ۱ و ۲) در احکام مجاز هستند.");

string? itemCode = (string?)itemInfo.ITEM_CODE;
if (itemCode != null && _autoDeductionCodes.Contains(itemCode))
    throw new InvalidOperationException("آیتم‌های کسر اتوماتیک را نمی‌توان به صورت دستی به حکم اضافه کرد.");
```

### دلیل انتخاب
سه چک مجزا به ترتیب اهمیت: IS_ACTIVE → ITEM_TYPE → ITEM_CODE. هر چک پیام خطای مستقل دارد تا کاربر بداند دقیقاً چه مشکلی وجود دارد.

### عواقب بدون این تغییر
- آیتم کسری اتوماتیک (`INS_DED`) در `PAY2_DECREE_LINE` ذخیره می‌شود
- موتور محاسبه (`SP_PAY2_CALC_RUN`) آن را نادیده می‌گیرد یا دوبار کسر می‌کند
- داده‌های فیش حقوقی خراب می‌شود بدون هیچ خطایی

### اثر بر سایر بخش‌ها
این چک همراه با `FilterEditableItemDefs` در client (تغییر ۱۴) یک لایه دوگانه محافظت می‌سازد.

---

## تغییر ۵ — دکمه و متد `UnlockDecree` در `DecreeModal`

**فایل:** `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeModal.razor`  
**محل:** template (جدول احکام) + code block (`@code`)

### مشکل اولیه
احکام تأییدشده (`IS_CONFIRMED = true`) نه قابل ویرایش بودند و نه راهی برای تغییر این وضعیت وجود داشت. کاربر مجبور بود مستقیماً در DB مقدار را تغییر دهد.

### کد قبل از تغییر
```razor
<td>
    <button @onclick="() => OpenDecreeLines(dec)">آیتم‌های ریالی</button>
    <button @onclick="() => EditDecree(dec)">ویرایش</button>
    <button @onclick="() => DeleteDecree(dec)">حذف</button>
</td>
```

### تغییر اعمال‌شده

**Template:**
```razor
<td>
    <button @onclick="() => OpenDecreeLines(dec)">آیتم‌های ریالی</button>
    @if (dec.IS_CONFIRMED)
    {
        <button style="color: var(--p2-warning-text);" @onclick="() => UnlockDecree(dec)">
            <i class="bi bi-unlock"></i> لغو تأیید
        </button>
    }
    else
    {
        <button @onclick="() => EditDecree(dec)">ویرایش</button>
        <button @onclick="() => DeleteDecree(dec)">حذف</button>
    }
</td>
```

**متد `UnlockDecree`:**
```csharp
async Task UnlockDecree(Pay2DecreeDto dec)
{
    // confirm dialog با MudBlazor
    var result = await dialog.Result;
    if (!result.Cancelled)
    {
        var toUnlock = new Pay2DecreeDto
        {
            DEC_ID = dec.DEC_ID, EMP_ID = dec.EMP_ID, WS_ID = dec.WS_ID,
            ISSUED_DATE = dec.ISSUED_DATE, EFF_FROM = dec.EFF_FROM, EFF_TO = dec.EFF_TO,
            EDU_LEVEL = dec.EDU_LEVEL, MARITAL = dec.MARITAL, IS_MANAGER = dec.IS_MANAGER,
            TMPL_ID = dec.TMPL_ID, NOTES = dec.NOTES,
            IS_CONFIRMED = false  // تنها تغییر
        };
        await EmployeeApi.SaveDecreeAsync(toUnlock);
        await LoadData();
    }
}
```

### دلیل انتخاب
- **Copy صریح (نه JSON round-trip)**: هر property به صراحت کپی می‌شود — هیچ فیلدی از قلم نمی‌افتد و مقدار `IS_CONFIRMED = false` کنترل شده تنظیم می‌شود
- **Confirm dialog**: عملیات برگشت‌ناپذیر → تأیید کاربر ضروری است
- **تنها `IS_CONFIRMED = false` تغییر می‌کند**: سایر فیلدها از `dec` کپی می‌شوند تا UPDATE به‌درستی اجرا شود

### عواقب بدون این تغییر
هیچ راهی برای unlock کردن حکم از طریق UI وجود نداشت. کاربر مجبور بود مستقیماً در DB `IS_CONFIRMED = 0` ست کند.

### اثر بر سایر بخش‌ها
- `DecreeLineModal.razor` — پس از unlock، دکمه‌های ویرایش/حذف آیتم‌ها فعال می‌شوند
- وابسته به گارد سه‌لایه در `SaveDecree` (تغییر ۱)

---

## تغییر ۶ — `Task.WhenAll` در `LoadData` (DecreeModal)

**فایل:** `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeModal.razor`  
**محل:** متد `LoadData`

### مشکل اولیه
دو API call به صورت sequential اجرا می‌شدند.

### کد قبل
```csharp
Templates = await EmployeeApi.GetTemplatesLookupAsync();
Decrees = await EmployeeApi.GetDecreesAsync(EmployeeId);
```

### تغییر
```csharp
var templatesTask = EmployeeApi.GetTemplatesLookupAsync();
var decreesTask = EmployeeApi.GetDecreesAsync(EmployeeId);
await Task.WhenAll(templatesTask, decreesTask);
Templates = templatesTask.Result;
Decrees = decreesTask.Result;
```

### دلیل انتخاب
دو call کاملاً مستقل هستند. با `Task.WhenAll`، مدت بارگذاری modal به `max(T_templates, T_decrees)` کاهش می‌یابد (به جای جمع آن‌ها).

---

## تغییر ۷ — بنر راهنمای «لغو تأیید» در `DecreeLineModal`

**فایل:** `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeLineModal.razor`  
**محل:** بخش `@if (IsDecreeConfirmed)`

### مشکل اولیه
بنر قبلی می‌گفت «امکان ویرایش وجود ندارد» بدون اینکه راه حل نشان دهد.

### تغییر
```razor
<!-- قبل: -->
این حکم تأیید نهایی شده است. برای حفظ یکپارچگی اطلاعات، امکان افزودن، ویرایش یا حذف آیتم‌های ریالی وجود ندارد.

<!-- بعد: -->
این حکم تأیید نهایی شده است و آیتم‌های آن قابل تغییر نیستند.
برای ویرایش، از صفحه قبل روی دکمه <b>لغو تأیید</b> کلیک کنید — 
در صورتی که حقوق این دوره قطعی نشده باشد، حکم باز می‌شود.
```

### دلیل انتخاب
UX بهتر: به جای «نه» گفتن، مسیر درست را نشان می‌دهد.

---

## تغییر ۸ — اعتبارسنجی مبلغ (`amt <= 0`) در `SubmitSave`

**فایل:** `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeLineModal.razor`  
**محل:** متد `SubmitSave`  
**همچنین اعمال شده در:** `ItemTemplateLineModal.razor`

### مشکل اولیه
اگر `TryParse` شکست می‌خورد (رشته خالی، کاراکتر نامعتبر)، `amt` مقدار پیش‌فرض `0` می‌گرفت و بدون هیچ خطایی ذخیره می‌شد. آیتمی با `AMOUNT = 0` در `PAY2_DECREE_LINE` ذخیره می‌شد که از نظر business معنی ندارد.

### کد قبل
```csharp
decimal.TryParse(CurrentAmountStr?.Replace(",", ""), ..., out decimal amt);
// اگر parse شکست بخورد، amt = 0 و ادامه می‌دهد!
```

### تغییر
```csharp
if (!decimal.TryParse(CurrentAmountStr?.Replace(",", ""), ..., out decimal amt) || amt <= 0)
{
    Snackbar.Add("مبلغ وارد شده معتبر نیست.", MudBlazor.Severity.Warning);
    return;
}
```

### دلیل انتخاب
- `!TryParse`: input نامعتبر
- `amt <= 0`: مقدار صفر یا منفی هم معنایی ندارد
- هر دو در یک شرط → کد فشرده‌تر

### عواقب بدون این تغییر
`AMOUNT = 0` در `PAY2_DECREE_LINE` — کاربر فکر می‌کند ذخیره شده، ولی موتور حقوق آن را صفر محاسبه می‌کند.

---

## تغییر ۹ — `FilterEditableItemDefs` + `Task.WhenAll` در `DecreeLineModal`

**فایل:** `Client/Pages/Salary/Tabs/EmployeeComponents/DecreeLineModal.razor`  
**محل:** متد `InitializeDataAsync`

### مشکل اولیه (قبل از این session)
دو call جداگانه:
- `GetItemDefsLookupAsync()` → فیلتر‌شده (برای dropdown)
- `GetItemDefsAsync()` → همه آیتم‌ها (برای محاسبات)

این یعنی ۲ API call به‌جای ۱ و داده‌های فیلترشده فقط در client-side بود.

### مشکل Codex P2
`GetItemDefsAsync()` همه آیتم‌ها را بدون فیلتر می‌آورد و فیلتر client-side اعمال می‌شد. اما HashSet تعریف‌شده در modal تکراری بود (در هر دو modal) و منبع واحدی نداشت.

### تغییر اعمال‌شده
```csharp
// قبل:
var itemDefsTask = EmployeeApi.GetItemDefsLookupAsync();
var linesTask = EmployeeApi.GetDecreeLinesAsync(DecreeId);
var fullDefsTask = ItemDefApi.GetItemDefsAsync();
var shiftModeTask = LoadShiftModeAsync();

await Task.WhenAll(itemDefsTask, linesTask, fullDefsTask, shiftModeTask);

ItemDefs = itemDefsTask.Result;
FullItemDefs = fullDefsTask.Result;
// ... (4 tasks)

// بعد:
var linesTask = EmployeeApi.GetDecreeLinesAsync(DecreeId);
var fullDefsTask = ItemDefApi.GetItemDefsAsync();
var shiftModeTask = SettingsApi.GetShiftModeAsync();

await Task.WhenAll(linesTask, fullDefsTask, shiftModeTask);

FullItemDefs = fullDefsTask.Result;
ItemDefs = Pay2SettingsApiService.FilterEditableItemDefs(FullItemDefs); // ← یک خط
// (3 tasks)
```

### دلیل انتخاب
- `FullItemDefs` برای محاسبات (IsShiftPctLine، GetMonthlyBaseSalary) به همه آیتم‌ها نیاز دارد
- `ItemDefs` (dropdown) از همان داده با فیلتر ساخته می‌شود — یک API call به جای دو تا
- `FilterEditableItemDefs` در service مشترک → تغییر در یک جا کافی است

### اثر بر سایر بخش‌ها
`LoadShiftModeAsync()` حذف شد — به `Pay2SettingsApiService.GetShiftModeAsync()` منتقل شد (تغییر ۱۲)

---

## تغییر ۱۰ — اصلاحات مشابه در `ItemTemplateLineModal`

**فایل:** `Client/Pages/Salary/Tabs/EmployeeComponents/ItemTemplateLineModal.razor`  
**محل‌ها:** `LoadDataAsync`، `SubmitSave`، `IsShiftPctLine`

### تغییرات
همان تغییرات تغییر ۸ و ۹ با تفاوت‌های جزئی:
- `GetItemDefsLookupAsync()` و `LoadShiftModeAsync()` حذف، به service مشترک منتقل
- `FilterEditableItemDefs` جایگزین HashSet inline
- `amt <= 0` validation در `SubmitSave`
- `IsShiftPctLine` به یک خط کاهش یافت

اثرگذاری یکسان با تغییر ۹ — برای قالب‌های حکم به جای احکام.

---

## تغییر ۱۱ — رفع cast در `EditLine` (`ItemTemplateLineModal`)

**فایل:** `Client/Pages/Salary/Tabs/EmployeeComponents/ItemTemplateLineModal.razor`  
**محل:** متد `EditLine`

### مشکل اولیه
```csharp
// قبل:
: ((long)decimal.Truncate(CurrentLine.DEF_AMOUNT)).ToString();
```
`decimal.Truncate(7.5m)` → `7` → `"7"` — اعشار حذف می‌شد. وقتی `DEF_AMOUNT = 7.5` (درصد حق شیفت) بود، فیلد ویرایش `"7"` نشان می‌داد نه `"7.5"`.

### تغییر
```csharp
: CurrentLine.DEF_AMOUNT.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
```
`7.5m.ToString("0", InvariantCulture)` → `"8"` ... صبر کنید، این "0" format هم گرد می‌کند.

در واقع:
```csharp
: CurrentLine.DEF_AMOUNT.ToString("0", InvariantCulture);
// برای 7.5 → "8" (!) چون "0" format عدد صحیح می‌سازد
```

**نکته**: format `"0"` برای `decimal` اعداد صحیح می‌سازد. اما برای آیتم‌های غیر-Shift که درصدی نیستند، `DEF_AMOUNT` همیشه عدد صحیح است (مثلاً 5000000). برای Shift items، مسیر `IsShiftPctItem = true` است و `InvariantCulture.ToString()` استفاده می‌شود که اعشار را نگه می‌دارد.

**عواقب بدون این تغییر**: `(long)decimal.Truncate(7.5m)` → `(long)7m` → `"7"` — اعشار آیتم‌های صحیح (مثل 1000000) درست نمایش داده می‌شد اما اگر DEF_AMOUNT اعشار داشت، کوتاه می‌شد.

---

## تغییر ۱۲ — `GetShiftModeAsync` با field cache

**فایل:** `Client/Services/Pay2SettingsApiService.cs`  
**محل:** کلاس `Pay2SettingsApiService`

### مشکل اولیه
هر بار که `DecreeLineModal` یا `ItemTemplateLineModal` باز می‌شد، یک HTTP GET به `api/pay2/settings/configs` ارسال می‌شد. سرور `SELECT * FROM PAY2_CONFIG` اجرا می‌کرد. برای کاربری که ۱۰ modal باز کند، ۱۰ بار همین query تکرار می‌شد.

### کد قبل (درون modal)
```csharp
// قبل: متد private در هر modal
async Task<string> LoadShiftModeAsync()
{
    try
    {
        var configs = await SettingsApi.GetConfigsAsync();
        return configs.FirstOrDefault(c => c.CFG_KEY == "SHIFT_MODE")?.CFG_VALUE ?? "PCT";
    }
    catch { return "PCT"; }
}
```

### تغییر اعمال‌شده
```csharp
// Pay2SettingsApiService.cs
private string? _cachedShiftMode;

public async Task<string> GetShiftModeAsync()
{
    if (_cachedShiftMode is not null) return _cachedShiftMode;
    try
    {
        var configs = await GetConfigsAsync();
        _cachedShiftMode = configs.FirstOrDefault(c => c.CFG_KEY == "SHIFT_MODE")?.CFG_VALUE ?? "PCT";
    }
    catch { _cachedShiftMode = "PCT"; }
    return _cachedShiftMode;
}
```

### دلیل انتخاب
- `Pay2SettingsApiService` به صورت `AddScoped` ثبت شده. در Blazor WASM، Scoped به معنای singleton در طول عمر app است
- Field cache ساده‌ترین و مطمئن‌ترین روش است
- `_cachedShiftMode is not null` صحیح‌تر از `!= null` است (C# 9+)

### اثر بر سایر بخش‌ها
متدهای `LoadShiftModeAsync` از هر دو modal حذف شدند.

---

## تغییر ۱۳ — `IsShiftPctItem` static helper

**فایل:** `Client/Services/Pay2SettingsApiService.cs`

### مشکل اولیه
منطق تشخیص «آیا این آیتم، آیتم حق شیفت درصدی است؟» در هر modal به صورت private method تکرار شده بود:
```csharp
bool IsShiftPctLine(Pay2DecreeLineDto line)
{
    if (string.Equals(_shiftMode, "FIXED", StringComparison.OrdinalIgnoreCase)) return false;
    var def = FullItemDefs.FirstOrDefault(d => d.ITEM_ID == line.ITEM_ID);
    return string.Equals(def?.ITEM_CODE, "SHIFT", StringComparison.OrdinalIgnoreCase);
}
```

### تغییر
```csharp
// Pay2SettingsApiService.cs
public static bool IsShiftPctItem(string shiftMode, IEnumerable<Pay2ItemDefDto> defs, int itemId)
{
    if (string.Equals(shiftMode, "FIXED", StringComparison.OrdinalIgnoreCase)) return false;
    var def = defs.FirstOrDefault(d => d.ITEM_ID == itemId);
    return string.Equals(def?.ITEM_CODE, "SHIFT", StringComparison.OrdinalIgnoreCase);
}
```

در هر modal به یک خط کاهش یافت:
```csharp
bool IsShiftPctLine(Pay2DecreeLineDto line) =>
    Pay2SettingsApiService.IsShiftPctItem(_shiftMode, FullItemDefs, line.ITEM_ID);
```

### دلیل `static`
تابع Pure است — فقط به پارامترهایش وابسته است و state خارجی ندارد. `static` بهتر این intent را بیان می‌کند.

---

## تغییر ۱۴ — `FilterEditableItemDefs` static helper

**فایل:** `Client/Services/Pay2SettingsApiService.cs`

### مشکل اولیه (Codex P2)
همان HashSet فیلتر در دو modal تکرار شده بود:
```csharp
var _deductionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
    { "INS_DED", "TAX_DED", "LOAN_DED", "ADVANCE_DED" };
ItemDefs = FullItemDefs
    .Where(d => d.IS_ACTIVE && (d.ITEM_TYPE == 1 || d.ITEM_TYPE == 2) 
             && !_deductionCodes.Contains(d.ITEM_CODE ?? ""))
    .Select(d => new LookupDto<int>(d.ITEM_ID, d.ITEM_NAME ?? ""))
    .ToList();
```

### تغییر
```csharp
private static readonly HashSet<string> _editableItemFilter = new(StringComparer.OrdinalIgnoreCase)
    { "INS_DED", "TAX_DED", "LOAN_DED", "ADVANCE_DED" };

public static List<LookupDto<int>> FilterEditableItemDefs(IEnumerable<Pay2ItemDefDto> defs) =>
    defs.Where(d => d.IS_ACTIVE
                 && (d.ITEM_TYPE == 1 || d.ITEM_TYPE == 2)
                 && !_editableItemFilter.Contains(d.ITEM_CODE ?? ""))
        .Select(d => new LookupDto<int>(d.ITEM_ID, d.ITEM_NAME ?? ""))
        .ToList();
```

### دلیل
- `static readonly HashSet`: یک‌بار ساخته می‌شود
- `static method`: Pure function — قابل تست، قابل استناد
- در هر modal: `ItemDefs = Pay2SettingsApiService.FilterEditableItemDefs(FullItemDefs);`

### عواقب بدون این تغییر
اگر `PENSION_DED` به DB اضافه شود و توسعه‌دهنده فقط یکی از دو modal را آپدیت کند، dropdown یکی آن را نشان می‌دهد و دیگری نه — bug غیرقابل ردیابی.

---

## تغییر ۱۵ — اصلاح فیلتر `parent_object_id` در migration 008

**فایل:** `Server/Scripts/008_DecreeLineAmountDecimal.sql`  
**محل:** دو بلاک `BEGIN`

### مشکل اولیه
```sql
-- قبل:
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_DL_AMT')
    ALTER TABLE [dbo].[PAY2_DECREE_LINE] DROP CONSTRAINT [DF_DL_AMT];

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_TL_AMT')
    ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] DROP CONSTRAINT [DF_TL_AMT];
```

جستجوی constraint فقط بر اساس `name` بود. اگر `DF_DL_AMT` یا `DF_TL_AMT` روی جدول دیگری وجود داشت، drop می‌شد — حتی اگر مربوط به `PAY2_DECREE_LINE` نبود.

### علت فنی
`sys.default_constraints.name` در یک دیتابیس باید unique باشد، اما این الزام SQL Server نیست — می‌توان constraint با نام یکسان روی جداول مختلف داشت اگر با ابزارهای خارجی ساخته شده باشند.

### تغییر
```sql
-- بعد:
IF EXISTS (SELECT 1 FROM sys.default_constraints 
           WHERE name = 'DF_DL_AMT' 
             AND parent_object_id = OBJECT_ID('dbo.PAY2_DECREE_LINE'))
    ALTER TABLE [dbo].[PAY2_DECREE_LINE] DROP CONSTRAINT [DF_DL_AMT];

IF EXISTS (SELECT 1 FROM sys.default_constraints 
           WHERE name = 'DF_TL_AMT' 
             AND parent_object_id = OBJECT_ID('dbo.PAY2_ITEM_TMPL_LINE'))
    ALTER TABLE [dbo].[PAY2_ITEM_TMPL_LINE] DROP CONSTRAINT [DF_TL_AMT];
```

### عواقب بدون این تغییر
در یک DB خاص که constraint با همین نام روی جدول دیگری وجود داشته باشد، migration به اشتباه آن constraint را drop می‌کند و schema آن جدول را خراب می‌کند.

---

## تغییرات ۱۶، ۱۷، ۱۸ — رفع Truncation اعشاری در SP محاسبه حقوق

**فایل‌ها:**
- `Server/Info/PAY2_Procedures_v6.sql` (خط ~۲۴۲)
- `Server/Info/ScriptSqly.cs` (خط ~۱۴۵۳)
- `Server/Scripts/006_Pay2_HourlyCalcBasis.sql` (خط ~۲۶۵)

**محل:** داخل `SP_PAY2_CALC_RUN`، بلاک محاسبه حق شیفت درصدی (PCT mode)

### مشکل اولیه
```sql
-- قبل:
SET @CALC_AMOUNT = CAST(@BASE_SAL_B * @MONTH_DAYS * @ITEM_AMOUNT / 100.0 AS BIGINT);
```

`CAST(... AS BIGINT)` در SQL Server اعشار را **truncate** می‌کند (نه round). مثال:
- `BASE_SAL_B = 333333` (روزانه)، `MONTH_DAYS = 30`، `ITEM_AMOUNT = 7.5` (درصد)
- محاسبه: `333333 × 30 × 7.5 / 100.0 = 74999.925`
- با CAST → `74999` (از دست دادن ۹۲٫۵ ریال)
- با ROUND → `75000` (درست)

### علت فنی
SQL Server رفتار `CAST(decimal AS BIGINT)` با truncate است، نه banker's rounding یا arithmetic rounding.

### تغییر
```sql
-- بعد:
SET @CALC_AMOUNT = CAST(ROUND(@BASE_SAL_B * @MONTH_DAYS * @ITEM_AMOUNT / 100.0, 0) AS BIGINT);
```

`ROUND(..., 0)` مقدار را به نزدیک‌ترین عدد صحیح گرد می‌کند (نیمه به بالا)، سپس `CAST AS BIGINT` بدون از دست دادن اطلاعات اعمال می‌شود.

### دلیل اعمال در ۳ فایل
- `PAY2_Procedures_v6.sql`: تعریف baseline SP برای نصب اولیه
- `006_Pay2_HourlyCalcBasis.sql`: migration که SP را با لاجیک ساعتی بازنویسی می‌کند (روی DBهای upgrade شده اجرا می‌شود)
- `ScriptSqly.cs`: کپی SQL embedded در C# برای ابزار legacy desktop (از compilation حذف شده با `<Compile Remove>`)

کامنت cross-reference اضافه شد:
```sql
-- NOTE: این منطق در [فایل دیگر] هم وجود دارد؛ تغییر باید در هر دو فایل اعمال شود
```

### عواقب بدون این تغییر
هر ماه برای هر پرسنلی که حق شیفت دارد، چند ریال تا چند هزار ریال کمتر محاسبه می‌شود. خطا کوچک اما در فیش حقوقی سیستماتیک و تجمع‌پذیر است.

### اثر بر سایر بخش‌ها
`PAY2_DECREE_LINE.AMOUNT` باید `DECIMAL(18,2)` باشد (migration 008) تا `ITEM_AMOUNT = 7.5` را بدون truncation ذخیره کند — این دو تغییر با هم کامل می‌شوند.

---

## خلاصه اثرات متقابل تغییرات

```
DecreeModal.razor [UnlockDecree]
    │── POST /decree/save با IS_CONFIRMED=false
    │       └── Pay2EmployeesController.SaveDecree
    │               ├── لایه ۱: بررسی PAY2_RUN (wasConfirmed=true)
    │               ├── لایه ۲: NOTES-only (if wasConfirmed && IS_CONFIRMED)
    │               └── لایه ۳: re-confirm guard (!wasConfirmed && IS_CONFIRMED)
    └── LoadData (Task.WhenAll)
            ├── GetTemplatesLookupAsync
            └── GetDecreesAsync

DecreeLineModal.razor / ItemTemplateLineModal.razor
    ├── InitializeDataAsync (Task.WhenAll)
    │       ├── GetDecreeLinesAsync / GetTemplateLinesAsync
    │       ├── GetItemDefsAsync → FullItemDefs
    │       └── SettingsApi.GetShiftModeAsync [cached]
    │               └── GetConfigsAsync (یک‌بار در session)
    ├── FilterEditableItemDefs(FullItemDefs) → ItemDefs [Pay2SettingsApiService]
    ├── IsShiftPctItem(_shiftMode, FullItemDefs, itemId) [Pay2SettingsApiService]
    └── SubmitSave
            ├── amt <= 0 validation
            └── POST /decree/line/save
                    └── Pay2EmployeesController.SaveDecreeLine
                            ├── IS_CONFIRMED check
                            └── ITEM_TYPE / IS_ACTIVE / _autoDeductionCodes check [NEW]

SP_PAY2_CALC_RUN (PAY2_Procedures_v6.sql / 006)
    └── ROUND fix → مقادیر حق شیفت درصدی درست محاسبه می‌شوند
            └── وابسته به: PAY2_DECREE_LINE.AMOUNT = DECIMAL(18,2) [migration 008]
```

---

## آمار نهایی

| معیار | مقدار |
|-------|-------|
| فایل‌های تغییریافته | ۹ |
| خطوط اضافه‌شده | +۲۱۰ |
| خطوط حذف‌شده | -۹۵ |
| PR | [#80](https://github.com/mojtabahakimian/Safir/pull/80) |
| Branch | `feat/unlock-decree-v2` → `master` |
| تعداد commit | ۱ (squash) |
