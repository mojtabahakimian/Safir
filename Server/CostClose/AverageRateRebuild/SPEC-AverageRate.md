# مشخصات بازسازی نرخ میانگین کاردکس

این سند، الگوریتمِ بازسازی نرخ میانگین در سفیر (گام `S07A`) را دقیق توصیف
می‌کند تا برنامهٔ شبانهٔ `AUTO_BAZ` بتواند خروجیِ یکسان تولید کند.

مبدأ هر دو، متد `C0_TASK` در `MainWindow.xaml.cs` است. سفیر یک پورت دستی از
همان است، با چند اصلاح مشخص. **بخش ۹ فهرست همان اصلاح‌هاست** — اگر وقت کم
است، از آنجا شروع کنید؛ بخش‌های ۱ تا ۸ مرجعِ کاملند برای مقایسه.

مرجعِ کد: `Server/CostClose/AverageRateRebuild/AverageRateRebuildService.cs`

---

## ۱. دامنه

- بازسازی از `@SinceDate` تا امروز، **بدون سقف تاریخ**. پیش‌فرض `10101`
  یعنی «از ابتدای تاریخ سیستم».
- محدود کردن به یک دوره غلط است: میانگین متحرکِ هر تراکنش به همهٔ
  تراکنش‌های قبلیِ همان (کالا، انبار) وابسته است.
- فقط شاخهٔ `Mid(OPTIONSS, 66, 1) = "5"` پیاده شده. شاخهٔ دیگر (نرخ استاندارد
  به‌جای میانگین وزنی برای بعضی گروه‌های کالا) منسوخ است و عمداً پورت نشده.
  در این پایگاه `OPTIONSS` طولش ۶۸ است، کاراکتر ۶۶ برابر `5` و کاراکتر ۵۶
  هم `5`.

## ۲. چه چیزی نوشته می‌شود

| جدول | ستون‌ها |
|---|---|
| `INVO_LST` | `AVRAGE`، `AVRAGE2`، `MABL`، `MABL_K` |
| `ANBGRD_LST` | `MABL` |

هیچ‌چیز در `DEED_HED`/`DEED_DTL` نوشته نمی‌شود. بازسازی سند حسابداری کارِ
جداگانه‌ای است و **باید بعد از این اجرا شود**، چون مبلغ حواله‌ها را از همین
`MABL_K` می‌خواند.

## ۳. انتخاب کالا و مقدار اول دوره

```sql
SELECT S.CODE, F.ANBAR, F.MOGODI_A, F.FI_A, F.MABL_A
FROM   dbo.STUF_DEF S INNER JOIN dbo.STUF_FSK F ON S.CODE = F.CODE
```

یک ردیف فقط وقتی پردازش می‌شود که:

- جفتِ `(CODE, ANBAR)` آن در `INVO_LST` وجود داشته باشد، **یا**
- `CODE` آن در `ANBGRD_LST` باشد (اینجا فقط کد، بدون انبار).

مانده‌های شروع، به‌ازای هر (کالا، انبار):

```
MBKM   = MABL_A     ← ارزش ریالی
MIAN   = FI_A       ← نرخ میانگین
MOGUDI = MOGODI_A   ← مقدار
```

اگر `MIAN = 0` باشد، به ترتیب:

1. `GETSTANDARDPRICE(code)`
2. اگر باز صفر بود: `GETFIRSTPRICE(code)`

```sql
-- GETSTANDARDPRICE_KOL  (و GETSTANDARDPRICE همان است)
SELECT TOP 1 SUM(dm.MABLK) AS SumOfMABLK, hm.IMBIBE_MANF, hm.IMBIBE_SAR
FROM   dbo.HEAD_MANF hm INNER JOIN dbo.DTL_MANF dm ON hm.FNUMB = dm.FNUMB
WHERE  hm.CODE = @Code
GROUP BY hm.IMBIBE_MANF, hm.IMBIBE_SAR, hm.FNUMB
ORDER BY hm.FNUMB;
-- نتیجه = SumOfMABLK + IMBIBE_MANF + IMBIBE_SAR

-- GETFIRSTPRICE
SELECT TOP 1 FI_A FROM dbo.STUF_FSK WHERE CODE = @Code ORDER BY FI_A DESC;
```

## ۴. ساخت کاردکس

یک `UNION` از شش شاخه، و بعد **`ORDER BY DATE_N, tartib, id`**.

هر شاخه ستون‌های یکسانی برمی‌گرداند: `DATE_N, TAG, NUMBER, ANBAR, CODE,
MEGH, MEGHk, MEGH_MAR, MABL, MABL_K, N_KOL, id, tartib`.

`@Anbar IS NULL` یعنی «همهٔ انبارهای این کالا یک‌جا» (فقط برای کالاهای
چرخه‌دار — بخش ۷).

**شاخهٔ ۱ — تراکنش‌های عادی**

```sql
FROM dbo.INVO_LST i
INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
INNER JOIN dbo.TAGCOD   t ON h.TAG = t.CODE
WHERE i.CODE = @Code AND (@Anbar IS NULL OR i.ANBAR = @Anbar)
  AND h.DATE_N > @SinceDate
```

**شاخهٔ ۲ — انتقالی ورود** (همان سطرِ `TAG=5`، این بار به نام انبار مقصد)

```sql
SELECT h.DATE_N, 6 AS TAG, ..., i.ANBARF AS ANBAR, ..., t.tartib
FROM dbo.INVO_LST i
INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
INNER JOIN dbo.TAGCOD   t ON h.TAG + 1 = t.CODE
WHERE i.CODE = @Code AND (@Anbar IS NULL OR i.ANBARF = @Anbar)
  AND h.DATE_N > @SinceDate AND i.TAG = 5
```

**شاخهٔ ۳ — انبارگردانی**

```sql
SELECT ah.GRD_DATE AS DATE_N,
       CASE WHEN (al.MOG - al.NUM3) > 0 THEN 18 ELSE 17 END AS TAG,
       al.GRD_NUM AS NUMBER, ah.GRD_ANBAR AS ANBAR, al.CODE,
       (al.MOG - al.NUM3) AS MEGH, ABS(al.MOG - al.NUM3) AS MEGHk, 0 AS MEGH_MAR,
       al.MABL, ABS(al.MOG - al.NUM3) * al.MABL AS MABL_K,
       CAST(NULL AS FLOAT) AS N_KOL, 0 AS id,
       CASE WHEN (al.MOG - al.NUM3) > 0 THEN 13 ELSE 5 END AS tartib
FROM dbo.ANBGRD_LST al
INNER JOIN dbo.ANBGRD_HEAD ah ON al.GRD_NUM = ah.GRD_NUM
INNER JOIN dbo.STUF_DEF    s  ON al.CODE = s.CODE
WHERE al.CODE = @Code AND (@Anbar IS NULL OR ah.GRD_ANBAR = @Anbar)
  AND (al.MOG - al.NUM3) * -1 <> 0 AND ah.GRD_DATE > @SinceDate
  AND ah.N_S IS NOT NULL
```

`tartib` اینجا هاردکد است چون `TAG` ساختگی است؛ اعداد ۱۳ و ۵ همان مقادیر
`TAGCOD` برای کدهای ۱۸ و ۱۷ هستند. شرط `ah.N_S IS NOT NULL` یعنی فقط
انبارگردانیِ سندخورده.

**شاخهٔ ۴ — برگشت فروش**

```sql
SELECT bh.DATE_N, 4 AS TAG, il.NUMBER, il.ANBAR, il.CODE, il.MEGH, il.MEGHk,
       il.MEGH_MAR, il.MABL, il.MABL_K, il.N_KOL, il.ID AS id,
       CASE WHEN bh.DATE_N = hs.DATE_N THEN 9999 ELSE 6 END AS tartib
FROM dbo.BACK_HEAD bh
INNER JOIN dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
INNER JOIN dbo.HEAD_LST hs ON hs.NUMBER = il.NUMBER AND hs.TAG = il.TAG
WHERE bh.ta = 2 AND il.MEGH_MAR <> 0
  AND il.CODE = @Code AND (@Anbar IS NULL OR il.ANBAR = @Anbar)
  AND bh.DATE_N > @SinceDate
```

سه نکتهٔ مهم:

1. تاریخ از `bh.DATE_N` (تاریخ برگشت) می‌آید، نه تاریخ فروش اصلی.
2. `id` همان `id` سطرِ فروش اصلی است — به‌همین‌دلیل `line.AVRAGE` در
   Case 4 نرخِ منجمدِ لحظهٔ فروش است (بخش ۵).
3. `tartib = 9999` فقط وقتی برگشت هم‌روزِ فروش باشد. دلیلش در بخش ۹ـ۳.

**شاخهٔ ۵ — برگشت خرید**

```sql
SELECT bh.DATE_N, 3 AS TAG, ..., il.ID AS id, 15 AS tartib
FROM dbo.BACK_HEAD bh
INNER JOIN dbo.INVO_LST il ON il.TAG = bh.ta AND il.NUMBER = bh.NUMBER1
WHERE bh.ta = 1 AND il.MEGH_MAR <> 0
  AND il.CODE = @Code AND (@Anbar IS NULL OR il.ANBAR = @Anbar)
  AND bh.DATE_N > @SinceDate
```

اینجا سنتینل لازم نیست: Case 3 از نرخ منجمد استفاده نمی‌کند، فقط از
`MIAN` جاری. `15` همان `tartib` کد ۳ در `TAGCOD` است.

> **`HEAD_LST_FBK` و `HEAD_LST_KBK` را استفاده نکنید.** هر دو ویو هستند و
> دقیقاً همان سطرهای `BACK_HEAD` را می‌دهند:
> `FBK = HEAD_LST WHERE TAG=4`، `KBK = HEAD_LST WHERE TAG=3`،
> `BACK_HEAD = HEAD_LST با ta = TAG−2`.
> اگر هم شاخهٔ `BACK_HEAD` را داشته باشید و هم این دو را، هر برگشت فروش
> **دو بار** در کاردکس می‌آید. جزئیات در بخش ۹ـ۶.

## ۵. جدول ترتیب (`TAGCOD.tartib`)

مقادیر واقعیِ این پایگاه:

| tartib | TAG | شرح |
|---:|---:|---|
| ۱ | ۰ | ابتدای دوره |
| ۲ | ۱۲ | خرید |
| ۳ | ۷ | تولید-ورود |
| ۴ | ۱ | رسید انبار خرید |
| ۵ | ۱۷ | کسری انبار |
| ۶ | ۴ | برگشت فروش |
| ۷ | ۲۲ | برگشت فروش |
| ۸ | ۲۴ | برگشت فروش. |
| ۹ | ۹ | حواله ورود |
| ۱۰ | ۶ | انتقالی - ورود |
| ۱۱ | ۲۶ | برگشت خرید آزاد |
| ۱۲ | ۲۷ | فاکتور برگشت خرید آزاد |
| ۱۳ | ۱۸ | اضافه انبار |
| ۱۴ | ۵ | انتقالی - خروج |
| ۱۵ | ۳ | برگشت خرید |
| ۱۶ | ۲۰ | پیش فاکتور |
| ۱۷ | ۸ | تولید- خروج |
| ۱۸ | ۲ | حواله انبار فروش |
| ۱۹ | ۱۰ | حواله خروج |
| ۲۰ | ۱۱ | حواله خروج سایر |
| ۲۱ | ۱۴ | خدمات |
| ۲۲ | ۱۵ | رسید مستقیم |
| ۲۳ | ۱۳ | فروش |

## ۶. فرمول هر TAG

سه متغیر حالت به‌ازای هر (کالا، انبار): `MBKM` ارزش، `MOGUDI` مقدار،
`MIAN` نرخ.

**قاعدهٔ مشترکِ بازمحاسبه** — هرجا گفته شده «بازمحاسبه»، عیناً این است:

```csharp
if (MBKM == 0)        { /* هیچ — MIAN دست‌نخورده می‌ماند */ }
else if (MOGUDI == 0) { MBKM = 0;  /* MIAN دست‌نخورده می‌ماند */ }
else                  { MIAN = MBKM / MOGUDI; }
```

مقایسه‌ها با صفرِ دقیقِ `double` است، نه با تلورانس. این دقیقاً رفتار کد
اصلی است و باید همان بماند.

| TAG | شرح | MBKM | MOGUDI | بازمحاسبه؟ | می‌نویسد |
|---:|---|---|---|:--:|---|
| ۱ | خرید | `+= MABL_K` | `+= MEGHk` | ✔ | `AVRAGE` |
| ۲ | فروش | `-= MEGHk × MIAN` | `-= MEGHk` | ✘ | `AVRAGE` |
| ۳ | برگشت خرید | `-= MEGH_MAR × MIAN` | `-= MEGH_MAR` | ✔ | `AVRAGE2` |
| ۴ | برگشت فروش | `+= MEGH_MAR × line.AVRAGE` | `+= MEGH_MAR` | ✔ | `AVRAGE2` |
| ۵ | انتقالی خروج | `-= MEGHk × MIAN` | `-= MEGHk` | ✘ | `AVRAGE`, `MABL`, `MABL_K` |
| ۶ | انتقالی ورود | `+= MABL_K` (زنده) | `+= MEGHk` | ✔ | `AVRAGE2` |
| ۹ | تولید | `+= MABL_K` (محاسبه‌شده) | `+= MEGHk` | ✔ | `MABL`, `MABL_K`, `AVRAGE` |
| ۱۰ | حواله خروج مواد | `-= MEGHk × MIAN` | `-= MEGHk` | ✘ | `AVRAGE`, `MABL`, `MABL_K` |
| ۱۱ | حواله خروج سایر | `-= MEGHk × MIAN` | `-= MEGHk` | ✘ | `AVRAGE`, `MABL`, `MABL_K` |
| ۱۷ | انبارگردانی | `+= MIAN × MEGHk` | `+= MEGHk` | ✔ | `ANBGRD_LST.MABL` |
| ۱۸ | انبارگردانی | `-= MEGHk × MIAN` | `-= MEGHk` | ✘ | `ANBGRD_LST.MABL` |
| ۲۲ | برگشت فروش سال قبل | شرطی، پایین | `+= MEGH_MAR` | ✔ | `AVRAGE` |
| ۲۴ | برگشت فروش سال قبل | شرطی، پایین | `+= MEGHk` | ✔ | `AVRAGE` |
| ۲۶ | برگشت خرید آزاد | `-= MEGHk × MIAN` | `-= MEGHk` | ✘ | `AVRAGE` |

هر TAG دیگری نادیده گرفته می‌شود (نه خطا، نه اثر).

**TAG=22 و TAG=24** شرطی‌اند:

```csharp
// 22
if (MBKM <= 0) MBKM  = MABL × MEGH_MAR;
else           MBKM += MIAN × MEGH_MAR;

// 24
if (MBKM <= 0) MBKM  = MABL_K;
else           MBKM += MEGHk × MIAN;
```

**TAG=5** مبلغ را هم می‌نویسد و ردیف را «لمس‌شده» علامت می‌زند:

```csharp
line.MABL   = MIAN;
line.MABL_K = Math.Round(MIAN × MEGHk);
line.Touched = true;
```

**TAG=6** عمداً از مقدارِ زندهٔ همین اجرا می‌خواند، نه از عکسِ لحظهٔ `fetch`:

```csharp
var mablK = line.Touched ? line.MABL_K : t.MABL_K;
MBKM += mablK;
```

**TAG=9 (تولید)**:

```csharp
if (Mid(OPTIONSS,56,1) == "5" && N_KOL > 0)
    // بهای همان فرمولِ مشخصِ این رسید
    produced = SUM(DTL_MANF.MABLK) + IMBIBE_MANF + IMBIBE_SAR
               WHERE HEAD_MANF.FNUMB = N_KOL
               GROUP BY IMBIBE_MANF, IMBIBE_SAR;
else {
    produced = GETSTANDARDPRICE_KOL(code);
    if (produced == 0) produced = GETFIRSTPRICE(code);
}

line.MABL   = produced;
line.MABL_K = Math.Round(produced × MEGHk);
MBKM   += line.MABL_K;
MOGUDI += MEGHk;
// بازمحاسبه
line.AVRAGE = MIAN;
```

**TAG=17 و ۱۸** روی `ANBGRD_LST` می‌نویسند، نه `INVO_LST`:

```sql
UPDATE dbo.ANBGRD_LST SET MABL = @MIAN
WHERE CODE = @CODE AND GRD_NUM = @NUMBER
```

### گِرد کردن

`MABL_K` تنها جایی است که گِرد می‌شود، با `Math.Round(double)` — یعنی
**بانکداری/`ToEven`**، نه `AwayFromZero`. اگر برنامهٔ شبانه `AwayFromZero`
بزند، روی نیم‌ریال‌ها ±۱ ریال اختلاف می‌گیرید. `MABL` و `AVRAGE` و
`AVRAGE2` هرگز گِرد نمی‌شوند.

### مقدار اولیهٔ `line`

از دیتابیس فقط `CODE, ANBAR, ID` خوانده می‌شود. `AVRAGE`، `AVRAGE2`،
`MABL`، `MABL_K` در حافظه از **صفر** شروع می‌کنند و فقط اگر یک TAG در همین
پیمایش همان `id` را لمس کند مقدار می‌گیرند.

نتیجهٔ عملی: اگر Case 4 به ردیفی برسد که هیچ TAG دیگری لمسش نکرده،
`line.AVRAGE` هنوز صفر است و `MBKM` با صفر جمع می‌شود. **این رفتار عمدی
است و باید حفظ شود** — تغییرش خروجی را از کد اصلی جدا می‌کند.

## ۷. ترتیب انبارهای یک کالا

انبارهای یک کالا **سریال** پردازش می‌شوند، ولی نه به ترتیب دلخواه: حوالهٔ
انتقالی وابستگی می‌سازد. `TAG=5` مقداری در `MABL_K` می‌نویسد که `TAG=6`
همان کالا در انبار دیگر بعداً می‌خواند.

مرتب‌سازی صرف بر اساس `(DATE_N, tartib)` این را حل **نمی‌کند**: انتقالی-خروج
`tartib=14` و انتقالی-ورود `tartib=10` است، یعنی مقصد قبل از مبدأ می‌آید —
برعکسِ چیزی که لازم است.

راه‌حل: یال‌های مبدأ→مقصد را بگیرید و مرتب‌سازی توپولوژیک (Kahn) کنید.

```sql
SELECT DISTINCT i.CODE, i.ANBAR AS Src, CAST(i.ANBARF AS INT) AS Dst
FROM   dbo.INVO_LST i
WHERE  i.TAG = 5 AND i.ANBARF IS NOT NULL
   AND i.ANBAR <> CAST(i.ANBARF AS INT)
```

اگر کالا هیچ حوالهٔ انتقالی بین انبارهایش نداشته باشد (اکثریت)، همان ترتیب
ورودی کافی است.

**اگر چرخه بود** (هم A→B و هم، در تاریخی دیگر، B→A) هیچ ترتیب خطی جواب
نمی‌دهد. آن‌وقت کاردکسِ *همهٔ* انبارهای آن کالا یک‌جا خوانده و در یک جریان
زمانی واحد `ORDER BY DATE_N, tartib, id` ادغام می‌شود. هر انبار
`MBKM/MIAN/MOGUDI` مستقل خودش را نگه می‌دارد. چون رویدادها به ترتیب زمانی
واقعی پردازش می‌شوند، `TAG=5` هر حواله همیشه قبل از `TAG=6` متناظرش می‌آید.

در این حالت `TAG=6` **باید** از `line.MABL_K` بخواند نه `t.MABL_K` — چون
همه چیز یک‌بار و از قبل `fetch` شده و `t.MABL_K` یک عکس قدیمی است.

## ۸. موازی‌سازی

موازی‌سازی **فقط در سطح کالا** مجاز است. میانگین متحرک هر کالا مستقل از
کالاهای دیگر و فقط از تراکنش‌های خودش ساخته می‌شود.

- بین انبارهای یک کالا: هرگز موازی (بخش ۷).
- سفیر `Clamp(ProcessorCount × 2, 4, 16)` را همزمان اجرا می‌کند.
- `UPDATE`ها در دسته‌های ۲۰۰ تایی، بدون تراکنش (هر دستور idempotent است).
- عددها با `InvariantCulture` به رشته تبدیل می‌شوند. با Culture فارسی،
  جداکنندهٔ اعشار می‌شود `٫` و SQL خطا می‌دهد یا بدتر، عدد را غلط می‌خواند.

---

## ۹. تفاوت‌های سفیر با `C0_TASK` اصلی

این بخش عملیاتی است. هر مورد یک تغییر مشخص در برنامهٔ شبانه می‌خواهد.

### ۹ـ۱. ترتیب داخل یک روز: `tartib` عددی، نه `BARGAH` متنی — ‏**مهم‌ترین**

کد اصلی برای مرتب‌سازیِ رویدادهای هم‌روز از `TAGCOD.BARGAH` استفاده می‌کرد
که یک ستون **متنی** است. مقایسهٔ متنی به ترتیب الفبای فارسی وابسته است، نه
ترتیب واقعی کسب‌وکار — «حواله انبار فروش» و «خرید» را الفبایی می‌چیند.

باید `TAGCOD.tartib` (عددی) باشد.

### ۹ـ۲. تای‌برک نهایی `id` — ‏**مهم‌ترین**

بدون `id` در انتهای `ORDER BY`، ترتیب ردیف‌هایی که هم `DATE_N` و هم
`tartib` یکسان دارند (چند فاکتور فروش در یک روز، چند رسید خرید در یک روز)
از نظر SQL Server **نامعین** است.

چون پیمایش حالت‌مند است — هر ردیف روی میانگینِ ردیف قبلی بنا می‌شود — یک
تای نامعین در ابتدای سال، کل مسیر میانگین را تا آخر سال منحرف می‌کند. روی
کد ۳۳۶۵ اثرش **۹,۷۰۴,۲۴۲,۹۵۳ ریال نوسان در سود** بین دو اجرای پشت‌سرهم با
دادهٔ یکسان بود.

`id` ستون `IDENTITY` است، پس صعودی = ترتیب واقعی درج.

```sql
ORDER BY DATE_N, tartib, id      -- هر سه، به همین ترتیب
```

اگر فقط یک تغییر می‌توانید بدهید، همین است. **علامتِ اینکه این مشکل را
دارید: دو اجرای پشت‌سرهم روی دادهٔ دست‌نخورده، نتیجهٔ متفاوت بدهد.**

### ۹ـ۳. برگشت فروش: شاخهٔ `BACK_HEAD` و سنتینل هم‌روز

کد اصلی هیچ‌جا `BACK_HEAD` را نمی‌خواند. سفیر شاخهٔ ۴ (بخش ۴) را اضافه
کرده تا Case 4 در تاریخ واقعی برگشت اجرا شود.

ماجرای `tartib`:

- `tartib` واقعیِ کد ۴ برابر ۶ است و کد ۲ برابر ۱۸. یعنی برگشت **قبل از**
  فروشِ هم‌روزِ خودش پردازش می‌شود. آن لحظه `line.AVRAGE` هنوز صفر است، پس
  `MBKM` با صفر جمع می‌شود ولی `MOGUDI` بدون معادلِ ارزشی رشد می‌کند و نرخ
  را مصنوعاً پایین می‌کشد. روی کد ۳۳۶۰/انبار ۸۰۷: ‏۸۸۱K به ۵۷۷K سقوط کرد.
- راه‌حل: `tartib = 9999` تا برود انتهای روز، **ولی فقط وقتی برگشت هم‌روزِ
  فروش اصلی باشد.**
- سنتینلِ همیشگی خودش باگ می‌سازد: روی برگشت فروش شمارهٔ ۵ / کد ۳۶۸ انبار ۲
  (برگشت ۱۶/۰۲، فروش ۳۱/۰۱) هل‌دادن به انتهای روز باعث می‌شد این ردیف بعد
  از یک جفت ورود/فروشِ نامرتبطِ همان روز پردازش شود، `MOGUDI` روی صفر
  می‌نشست، و طبق قاعدهٔ مشترک `MIAN` دست‌نخورده می‌ماند — `AVRAGE2` به‌جای
  بازتاب برگشت، میانگینِ نامرتبطِ همان جفت را می‌گرفت.

پس شرط `CASE WHEN bh.DATE_N = hs.DATE_N THEN 9999 ELSE 6 END` دقیقاً همین
است: نه کمتر، نه بیشتر.

### ۹ـ۴. برگشت خرید هرگز اثر نمی‌گذاشت

Case 3 در کد اصلی وجود داشت ولی تنها منبعش شاخهٔ `HEAD_LST_KBK` بود که
اجرا نمی‌شد. یعنی کدی نوشته شده بود که هیچ ردیفی به آن نمی‌رسید و **هیچ
برگشت خریدی تا امروز روی نرخ میانگین اثر نگذاشته**.

در کل این پایگاه فقط یک سند است (۱۳۳ واحد) — نادر، ولی واقعی. شاخهٔ ۵ در
بخش ۴ اضافه‌اش می‌کند.

### ۹ـ۵. ترتیب انبارها و انتقالی

بخش ۷ کامل. کد اصلی ترتیبِ برگشتیِ کوئری را می‌گرفت (بدون `ORDER BY`) که
وابستگی مبدأ→مقصد را تضمین نمی‌کند. و برای کالاهای چرخه‌دار هیچ مسیری
نداشت.

مورد واقعی: کد ۳۶۸/انبار ۲ — `TAG=6` عکسِ قدیمیِ `MABL_K` را می‌خواند.

### ۹ـ۶. `HEAD_LST_FBK` / `HEAD_LST_KBK` را حذف کنید

کد اصلی این دو شاخه را داشت، مشروط به «آیا این جدول وجود دارد».

هر دو در این پایگاه **ویو** هستند، نه جدول. تعریفشان:

```sql
HEAD_LST_FBK = SELECT ..., TAG AS htag, TAG-2 AS dtag, ... FROM HEAD_LST WHERE TAG = 4
HEAD_LST_KBK = SELECT ..., TAG AS htag, TAG-2 AS dtag, ... FROM HEAD_LST WHERE TAG = 3
BACK_HEAD    = SELECT ..., TAG-2 AS ta,  ...              FROM HEAD_LST
```

یعنی `FBK` همان `ta=2` است و `KBK` همان `ta=1` — همان سطرها، با نام ستون
متفاوت. شمارش تأیید می‌کند: `FBK` سیزده سطر و `ta=2` سیزده سطر؛ `KBK` یک
سطر و `ta=1` یک سطر.

**اگر هر دو دسته شاخه فعال باشند، هر برگشت فروش دو بار در کاردکس می‌آید.**
شاخه‌های `BACK_HEAD` را نگه دارید (سنتینل هم‌روز را دارند، `FBK` ندارد) و
این دو را حذف کنید.

### ۹ـ۷. شاخهٔ نرخ استاندارد پورت نشده

فقط `Mid(OPTIONSS,66,1) = "5"` پیاده است. اگر برنامهٔ شبانه شاخهٔ دیگر را
هم دارد و روی این نصب فعال است، اختلاف از همان‌جاست.

---

## ۱۰. هشدار: کالاهای تولیدی به‌تنهایی همگرا نمی‌شوند

این مهم‌ترین چیزی است که **بعد از** یکسان‌سازی الگوریتم باقی می‌ماند.

`TAG=9` بهای رسید تولید را از `DTL_MANF.MABLK` می‌گیرد. آن ستون ورودی نیست
— خروجیِ موتور نرخ (`S11`) است. و `S11` خودش نرخ مواد را از میانگین انبار
می‌گیرد، یعنی از خروجی همین بازسازی.

پس این دو به هم وابسته‌اند و **یک پاس جواب نهایی نیست**. سفیر آن‌ها را
متناوب اجرا می‌کند تا نرخ‌ها بین دو دور پیاپی از آستانه کمتر تکان بخورند
(دور کامل، نه یک‌بار).

برنامهٔ شبانه معادلی برای `S11` ندارد. نتیجه: برای کالاهای فرمول‌دار، هر
چقدر هم الگوریتم میانگین یکسان شود، خروجی یکی نمی‌شود — چون ورودی‌اش
(`DTL_MANF.MABLK`) در نیمهٔ راه مانده.

سه گزینه:

1. برنامهٔ شبانه فقط بازسازی میانگین را انجام دهد و بستن ماه در سفیر بماند.
   (ساده‌ترین و پیشنهاد ما.)
2. برنامهٔ شبانه بعد از هر پاس، `dbo.CC_sp_S11_PropagateRates` را صدا بزند و
   حلقه را تکرار کند تا همگرا شود.
3. برنامهٔ شبانه کالاهای فرمول‌دار را از دامنهٔ بازسازی کنار بگذارد.

هرکدام را انتخاب کردید، **نه هم‌زمان با بستن ماه اجرا نشود.** هر دو روی
`INVO_LST` می‌نویسند.

---

## ۱۱. آزمون پذیرش

قبل از هر چیز، دو اجرای پشت‌سرهم روی دادهٔ دست‌نخورده باید **دقیقاً** یکی
شود. اگر نشد، مشکل ۹ـ۲ (تای‌برک `id`) هنوز هست و مقایسه با سفیر بی‌معناست.

```sql
-- ۱) عکس گرفتن پیش از اجرا
SELECT ID, AVRAGE, AVRAGE2, MABL, MABL_K
INTO   dbo.ZZ_avg_before
FROM   dbo.INVO_LST;

-- ۲) برنامهٔ شبانه را اجرا کنید، بعد:
SELECT ID, AVRAGE, AVRAGE2, MABL, MABL_K
INTO   dbo.ZZ_avg_night
FROM   dbo.INVO_LST;

-- ۳) عکس را برگردانید، S07A سفیر را اجرا کنید، بعد:
SELECT ID, AVRAGE, AVRAGE2, MABL, MABL_K
INTO   dbo.ZZ_avg_safir
FROM   dbo.INVO_LST;

-- ۴) اختلاف‌ها، از بزرگ‌ترین
SELECT TOP 50 n.ID, i.CODE, i.ANBAR, i.TAG,
       n.AVRAGE  AS Night_AVRAGE,  s.AVRAGE  AS Safir_AVRAGE,
       n.AVRAGE2 AS Night_AVRAGE2, s.AVRAGE2 AS Safir_AVRAGE2,
       n.MABL_K  AS Night_MABLK,   s.MABL_K  AS Safir_MABLK
FROM   dbo.ZZ_avg_night n
JOIN   dbo.ZZ_avg_safir s ON s.ID = n.ID
JOIN   dbo.INVO_LST     i ON i.ID = n.ID
WHERE  ABS(n.AVRAGE  - s.AVRAGE)  > 0.5
    OR ABS(n.AVRAGE2 - s.AVRAGE2) > 0.5
    OR ABS(n.MABL_K  - s.MABL_K)  > 0.5
ORDER BY ABS(n.MABL_K - s.MABL_K) DESC;
```

آستانهٔ ۰٫۵ ریال است: کوچک‌ترین واحد پول یک ریال است، پس کسرِ آن معنا
ندارد.

### راهنمای تشخیص از الگوی اختلاف

| نشانه | به احتمال زیاد |
|---|---|
| هر اجرا نتیجهٔ متفاوت | ۹ـ۲ — تای‌برک `id` |
| اختلاف فقط روی کالاهای پرگردشِ هم‌روز | ۹ـ۱ یا ۹ـ۲ |
| `AVRAGE2` روی برگشت فروش‌ها فرق دارد | ۹ـ۳ — سنتینل هم‌روز |
| برگشت فروش دو برابر اثر گذاشته | ۹ـ۶ — شاخهٔ `FBK` تکراری |
| `AVRAGE2` روی برگشت خرید صفر مانده | ۹ـ۴ |
| اختلاف فقط روی کالاهای چنددانباره با انتقالی | ۹ـ۵ |
| ±۱ ریال روی `MABL_K` | گِرد کردن — `ToEven` در برابر `AwayFromZero` |
| اختلاف فقط روی کالاهای فرمول‌دار | بخش ۱۰ — و با یکسان‌سازی الگوریتم حل نمی‌شود |
