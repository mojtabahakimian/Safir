# Safir / Jules PAY2 E2E Seed Package

این بسته، بخش نیمه‌تمام تبدیل داده حقوق و دستمزد به Seed قابل اجرای SQL Server را کامل می‌کند.

## فایل‌ها

- `Server/Database/pay2_seed.sql` — Seed آماده SQL Server با داده‌های حقوقی واقعی از نظر ساختار و مبالغ، اما با اطلاعات هویتی پرسنل ناشناس‌سازی‌شده.
- `Server/Database/tools/generate_pay2_seed.py` — مبدل قابل تکرار `Data.txt` به Seed SQL.
- `jules_setup_complete.sh` — Setup Script کامل Jules: نصب SQL Server 2025، بازسازی دیتابیس تست، اجرای Schema، اجرای Seed، اعتبارسنجی، Build و نصب Chromium.

## تعداد رکوردهای Seed

| جدول | تعداد |
|---|---:|
| PAY2_CONFIG | 38 |
| PAY2_TAX_BRACKET | 9 |
| PAY2_PERIOD | 3 |
| PAY2_RUN | 36 |
| PAY2_RUN_LINE | 79 |
| PAY2_RUN_DETAIL | 552 |
| PAY2_EMPLOYEE | 16 |
| PAY2_ITEM_DEF | 21 |
| PAY2_ATTENDANCE | 32 |
| PAY2_DECREE | 18 |
| PAY2_DECREE_LINE | 124 |

به‌دلیل نبودن سه جدول والد در فایل JSON، Seed به‌صورت خودکار داده حداقلی و مصنوعی برای این موارد اضافه می‌کند:

- `PAY2_WORKSHOP`: یک رکورد
- `PAY2_JOB`: ده رکورد مطابق `JOB_ID`های ارجاع‌شده
- `PAY2_ITEM_TEMPLATE`: دو رکورد مطابق `TMPL_ID`های ارجاع‌شده

## ناشناس‌سازی

موارد زیر با مقادیر مصنوعی جایگزین شده‌اند:

- نام و نام خانوادگی
- نام پدر
- کد ملی و شماره شناسنامه
- شماره بیمه
- موبایل
- شماره کارت پرسنلی
- شبا و حساب بانکی، در صورت وجود
- کد پرسنلی و `ACC_T`
- تاریخ و محل تولد

شناسه‌های اصلی (`EMP_ID`, `RUN_ID`, `DEC_ID`, `ITEM_ID` و غیره)، مبالغ، کارکرد، مالیات، بیمه، احکام و روابط بین جداول حفظ شده‌اند.

**فایل اصلی `Data.txt` حاوی اطلاعات شخصی واقعی است و نباید داخل Repository عمومی Commit شود.**

## مسیر قرارگیری در Repository

```text
Safir/
├── Server/
│   └── Database/
│       ├── schema.sql
│       ├── pay2_seed.sql
│       └── tools/
│           └── generate_pay2_seed.py
└── ...
```

فایل `jules_setup_complete.sh` را لازم نیست حتماً Commit کنید؛ محتوای آن را می‌توان مستقیماً در باکس **Setup script** در Jules قرار داد.

## Environment Variables در Jules

```text
ConnectionStrings__DefaultConnection
Server=localhost,1433;Database=SafirTestDb;User Id=sa;Password=<TEST_PASSWORD>;TrustServerCertificate=True;
```

```text
ASPNETCORE_ENVIRONMENT
Development
```

Setup Script رمز را از Connection String می‌خواند و آن را در Log چاپ نمی‌کند. نام دیتابیس باید با `SafirTest` شروع شود و Server باید `localhost` یا `127.0.0.1` باشد؛ در غیر این صورت اسکریپت برای جلوگیری از اتصال تصادفی به Production متوقف می‌شود.

## ترتیب اجرا

1. `pay2_seed.sql` را در `Server/Database/` قرار دهید.
2. مبدل Python را در `Server/Database/tools/` قرار دهید.
3. محتوای `jules_setup_complete.sh` را در Setup Script جولز Paste کنید.
4. Environment Variableها را مطابق بالا تنظیم کنید.
5. روی **Run and snapshot** کلیک کنید.

Setup Script در هر اجرا دیتابیس تست را Drop و دوباره Create می‌کند؛ بنابراین اجرای `schema.sql` و Seed تکرارپذیر است و با Objectهای موجود برخورد نمی‌کند.

## تولید مجدد Seed

فقط روی سیستم امنی که `Data.txt` در آن قرار دارد اجرا کنید:

```bash
python3 Server/Database/tools/generate_pay2_seed.py \
  /path/to/Data.txt \
  Server/Database/pay2_seed.sql
```

## محدوده این Seed

این بسته برای تست گزارش‌های PAY2، محاسبات ثبت‌شده، Excel و فیش حقوقی بر اساس `PAY2_RUN*` مناسب است. اما داده ارسالی شامل این بخش‌ها نبود:

- کاربر ورود و دسترسی‌ها، مانند داده‌های `SALA_DTL`
- تنظیمات حسابداری کارگاه در `PAY2_WORKSHOP_ACC`
- سرفصل‌ها و داده لازم در `DEED_HED` و `DEED_DTL`

بنابراین برای تست کامل «محاسبه جدید حقوق و صدور سند حسابداری واقعی»، باید یک Seed جداگانه و ناشناس‌شده برای احراز هویت و حسابداری نیز اضافه شود. این فایل عمداً داده جعلی حسابداری اختراع نمی‌کند.

## اعتبارسنجی انجام‌شده

- JSON خراب‌شده بر اثر Line Breakهای داخل متن ترمیم و Parse شد.
- تمام روابط داخلی داده‌های ارسالی بررسی شد.
- هیچ Parent مفقودی میان Run/RunLine/RunDetail، Period/Attendance و Decree/DecreeLine وجود ندارد.
- اطلاعات هویتی مستقیم کارکنان در فایل SQL نهایی باقی نمانده است.
- اسکریپت Python با `py_compile` و اسکریپت Bash با `bash -n` بررسی شده‌اند.

اجرای واقعی Seed روی SQL Server در این محیط انجام نشده است؛ تست نهایی باید با **Run and snapshot** در Jules انجام شود و هر خطای Schema/Constraint همان‌جا با `sqlcmd -b` باعث Fail شدن Setup خواهد شد.
