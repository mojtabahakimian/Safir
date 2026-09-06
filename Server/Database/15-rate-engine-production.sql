/* ═══════════════════════════════════════════════════════════════════
   مرحله ۴ — موتور نرخ، نسخه تولیدی

   تفاوت با نسخه آزمون بازگشتی (فایل 03):
     ۱) در DTL_MANF و HEAD_MANF می‌نویسد، نه فقط در CC_ItemCost
     ۲) هر تغییر در CC_FormulaChange ثبت می‌شود
     ۳) @RunId می‌گیرد و به سابقه اجرا وصل است
     ۴) S10 (تراز هزینه تبدیل) هم اینجاست

   ترتیب اجرا: S10 سپس S11
   چون ضریب تعدیل مستقل از نرخ مواد است، یک بار محاسبه کافی است
   و دیگر نیازی به قرار گرفتن داخل حلقه ندارد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

-- بدون این دو، S11 که در CC_ItemCost (ستون محاسباتی PERSISTED) DELETE/INSERT
-- می‌کند با خطای 1934 شکست می‌خورد.
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

/* ═══════════════════════════════════════════════════════════════════
   S07B — تخصیص دستمزد و سربار به تفکیک کالا، بر اساس ضریب جذب

   کاربر برای هر کالا یک «ضریب جذب دستمزد» دستی وارد می‌کند
   (dbo.CC_LaborAbsorptionRate — مثلاً بر مبنای وزن: کالای ۱ کیلوگرمی
   ضریب ۱، کالای ۲ کیلوگرمی ضریب ۲ و...، ولی مبنا هرچه کاربر بخواهد
   می‌تواند باشد) و اختیاراً یک «ضریب جذب سربار» مستقل (چون معیارِ
   درستِ سربار می‌تواند با دستمزد فرق کند؛ تأیید کاربر: «فعلاً از
   دستمزد براش مقدار بده» — یعنی وقتی ضریب سربار خالی است، همان ضریب
   دستمزد جایگزینش می‌شود). دستمزد/سربارِ واقعیِ هر واحد تولیدی
   (مثلاً یزد) — مستقیم از حساب ۷۵۱، فیلترشده با HES_M (کدِ کالای
   تولیدشده) به کدهایی که همان واحد تولید کرده (نگاه کنید توضیح پایین‌تر).

   ⚠️ فرمولِ نرخِ واحدِ هر کالا (تأیید صریحِ کاربر، منطقِ حسابداریِ
   صنعتی): نرخِ دستمزدِ هر واحدِ کالا باید با افزایشِ حجمِ تولیدِ
   همان کالا کاهش یابد — یعنی هرچه یک کالا بیشتر تولید شود، هزینه‌ی
   دستمزدِ همان مقدارِ ثابت روی تعدادِ بیشتری واحد جذب می‌شود، پس نرخِ
   هر واحدش کمتر می‌شود؛ نه این‌که نرخِ واحد ثابت بماند و فقط جمعِ کل
   با حجم بالا برود. فرمول دقیق:

       نرخِ واحدِ کالا = ضریبِ کالا ÷ (مجموعِ سادهٔ ضرایبِ همه‌ی
                          کالاهای تولیدشده‌ی همین واحد این ماه ×
                          مقدارِ تولیدِ خودِ همین کالا این ماه)
                          × دستمزدِ واقعیِ واحد

   نکته‌ی مهم: «مجموعِ ضرایب» اینجا سادهٔ (بدون وزن‌دهی به مقدار) است —
   هر کد فقط یک‌بار با ضریبِ خودش جمع می‌شود، نه ضریب×مقدارش (که نسخه‌ی
   قبلی این فایل بود و باعث می‌شد نرخِ واحد اصلاً به مقدارِ تولیدِ خودِ
   همان کالا وابسته نباشد — با تستِ واقعی روی کد ۱۷۸۶/آب‌پنیر کشف و
   تصحیح شد).

   ⚠️ رفتار ضریب خالی/صفر (تأیید کاربر، کد ۳۷۳ خرداد ۱۴۰۵ کشف شد):
   خالی (NULL) یعنی «هنوز بررسی نشده» — این کالا از تقسیم و از مخرج
   کسر کنار می‌ماند و مقدار فعلیِ IMBIBE_MANF/IMBIBE_SAR دست‌نخورده
   می‌ماند. صفرِ صریح (Coefficient=0) اما یعنی «کاربر عمداً این کالا
   را از جذب دستمزد کنار گذاشته» — IMBIBE_MANF/IMBIBE_SAR همین کالا
   صراحتاً صفر می‌شود (نه این‌که دست‌نخورده بماند)، وگرنه یک نرخِ
   قدیمیِ باقی‌مانده از زمانی که ضریب هنوز صفر نشده بود، برای همیشه
   به‌جا می‌ماند و کسی متوجه نمی‌شود.

   عمداً قبل از S07A اجرا می‌شود (SeqNo=72، بین S07=70 و S07A=75) تا
   محاسبه‌ی نرخ میانگین/تولید همان ماه از همین مقدار استفاده کند.

   ⚠️ عمداً کنار پلاگ اصلاحی S10 (تأیید کاربر): چون همین دستمزد/سربارِ
   واقعیِ ۷۵۱ مبنای تقسیم است، انتظار می‌رود ضریب k در S10 نزدیک ۱ در
   بیاید — S10 همچنان به‌عنوان یک لایه‌ی تطبیق نهایی (گرد کردن/موارد
   خاص) دست‌نخورده باقی می‌ماند، نه این‌که حذف شود.

   ⚠️ اصلاح (کد ۳۶۸/۲۰/... واحد یزد، خرداد ۱۴۰۵ کشف شد): اگر یک واحد
   اصلاً نگاشت حساب دستمزد/سربار (CC_UnitAcc.CostKind) نداشته باشد،
   @actWage/@actOh همیشه صفر می‌ماند — بدون گارد، فرمول کالاهای
   ضریب‌دار همین واحد صفر می‌شدند (نابودیِ واقعیِ داده، نه خطای
   بی‌ضرر). حالا وقتی واقعی صفر است ولی کالایی با ضریب هست، هیچ‌کاری
   نمی‌کنیم و فقط هشدار می‌دهیم.

   ⚠️ کالای هم‌زمان چندواحدی (تأیید کاربر، کد ۳۷۳ خرداد ۱۴۰۵ کشف شد):
   HEAD_MANF فقط یک ردیف به‌ازای (CODE, GHEYMAT) دارد — نمی‌تواند
   هم‌زمان نرخ دو واحد را نگه دارد. اگر یک کد در چند واحد تولید شود
   (مثلاً کد ۳۷۳: عمدتاً انبار ۳/واحد اصلی، ولی کمی هم انبار ۸۰۸/یزد)
   و دو واحد برای همان فرمول نرخ‌های متفاوت پیشنهاد بدهند، دیگر
   به‌صورت کورسر (که هرکدام آخر اجرا شود بی‌سروصدا آن یکی را رونویسی
   می‌کرد) پیش نمی‌رویم؛ به‌جایش همه‌ی واحدها یک‌جا (Set-based) پردازش
   می‌شوند، پیشنهادِ هر واحد برای هر فرمول جمع‌آوری می‌شود، و فقط اگر
   پیشنهادها برابر باشند (یا فقط یک واحد پیشنهاد داده باشد) اعمال
   می‌شود؛ در صورت تعارض، هیچ‌کدام اعمال نمی‌شود و فقط هشدار ثبت
   می‌شود.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S07B_SyncLaborRate
    @RunId INT, @Month INT, @DT1 BIGINT, @DT2 BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Total INT = 0;

    -- ۱) دستمزد/سربارِ واقعیِ هر واحد.
    --
    -- ⚠️ اصلاح (تأیید کاربر: «تو داری دستمزد کارخونه را زیاد میزنی که
    -- در ۷۵۱ خودشو نشون میده»): قبلاً این عدد از خودِ حساب ۷۵۱ (تفصیلی
    -- ۹۹۹۹۹۹۹۹) می‌آمد — ولی ۷۵۱ خودش نتیجه‌ی همین فرمول‌هاست (هر برگه‌ی
    -- ورود کالا به انبار، با نرخِ IMBIBE_MANFِ همان لحظه، به ۷۵۱ می‌زند)؛
    -- یعنی اگر نرخِ قبلی زیاد بوده، ۷۵۱ هم زیاد شده و دوباره از رویِ آن
    -- نرخِ بعدی ساختن یعنی تکرارِ همان خطا (حلقه‌ی خودتغذیه). منبعِ
    -- مستقل و واقعی همان چیزی است که S10 هم زیرِ عنوانِ «واقعی» استفاده
    -- می‌کند: CC_UnitAcc (حساب‌های ۷۱۱-۷۴۵ و مشابه، طبقِ تنظیماتِ کاربر)،
    -- نه ۷۵۱.
    SELECT  m.UnitId,
            ISNULL(SUM(CASE WHEN m.CostKind = 1 THEN t.Amount * m.Ratio ELSE 0 END), 0) AS ActWage,
            ISNULL(SUM(CASE WHEN m.CostKind = 2 THEN t.Amount * m.Ratio ELSE 0 END), 0) AS ActOh
    INTO    #UnitActual
    FROM    dbo.CC_UnitAcc m
    CROSS   APPLY (
                SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                FROM   dbo.DEED_DTL d
                JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                  AND  d.HES_K = m.HesKol
                  AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                  AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
            ) t
    WHERE   m.IsActive = 1
    GROUP BY m.UnitId;

    -- ۲) مقدارِ کلِ تولیدِ همین ماه به‌تفکیکِ (واحد، کد)، به‌همراه ضریبِ
    --    وزنِ هر واحدِ MEGHk (کاربر: «مقدار ممکنه وزن نباشه مثلا ۱۸۰
    --    گرم باشه و تعداد زیاد» — یعنی MEGHk خام برای کالاهایی که با
    --    «عدد/بسته» شمرده می‌شوند با کالاهایی که با «کیلوگرم» شمرده
    --    می‌شوند قابلِ‌جمع نیست). WeightFactor از dbo.stuf_def_nfani.
    --    COLN6 می‌آید — همان ستونی که در گزارشِ KALAS خودِ کاربر با
    --    `SUM(CAST(COLN6 AS FLOAT)*MEGHk)` به‌عنوان «وزن» جمع می‌زند
    --    (مثلاً پنیر ۱۸۰گرمی → COLN6=۰.۱۸). وقتی این ستون خالی/غیرِعددی
    --    است یا صفر/منفی، ۱ فرض می‌شود (یعنی MEGHk خودش از قبل واحدِ
    --    وزنی است، مثلِ کالاهای کیلوگرمی که COLN6=۱ دارند).
    SELECT  cua.UnitId, hm.CODE, SUM(pl.MEGHK) AS Qty,
            MAX(ISNULL(NULLIF(TRY_CAST(nf.COLN6 AS FLOAT), 0), 1)) AS WeightFactor
    INTO    #CodeQty
    FROM    dbo.HEAD_LST  h
    JOIN    dbo.INVO_LST  pl  ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
    JOIN    dbo.HEAD_MANF hm  ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT) AND hm.GHEYMAT = @Month
    JOIN    dbo.CC_UnitAnbar cua ON cua.Anbar = pl.ANBAR AND cua.AnbarRole = 3
    JOIN    dbo.CC_Unit   u   ON u.UnitId  = cua.UnitId AND u.IsActive = 1
    LEFT    JOIN dbo.stuf_def_nfani nf ON nf.CODE = hm.CODE
    WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
    GROUP BY cua.UnitId, hm.CODE;

    -- مجموعِ وزنیِ ضرایب هر واحد در همین ماه — Σ(ضریب × مقدار ×
    -- ضریبِ‌وزن)، فقط کالاهایی که کاربر برایشان ضریب صریحِ غیرصفر ثبت
    -- کرده. این «مخرجِ مشترک» است: باعث می‌شود نرخِ هر کالا فقط تابعِ
    -- ضریبِ خودش باشد (نه مقدارِ تولیدش)، ولی جمعِ دستمزدِ همه‌ی
    -- کالاها با دستمزدِ واقعی برابر بماند (نگاه کنید توضیحِ فرمولِ
    -- پایین‌تر). ضریب سربارِ مؤثر = ISNULL(OverheadCoefficient, Coefficient).
    SELECT  cq.UnitId,
            ISNULL(SUM(r.Coefficient * cq.Qty * cq.WeightFactor), 0) AS TotalWeight,
            ISNULL(SUM(CASE WHEN ISNULL(r.OverheadCoefficient, r.Coefficient) <> 0
                             THEN ISNULL(r.OverheadCoefficient, r.Coefficient) * cq.Qty * cq.WeightFactor
                             ELSE 0 END), 0) AS TotalWeightOh
    INTO    #UnitWeight
    FROM    #CodeQty cq
    JOIN    dbo.CC_LaborAbsorptionRate r ON r.CODE = cq.CODE AND r.UnitId = cq.UnitId AND r.IsFixed = 0
    WHERE   r.Coefficient IS NOT NULL AND r.Coefficient <> 0
    GROUP BY cq.UnitId;

    -- ⚠️ کالاهای «کارمزدی» (IsFixed=1) از استخرِ تقسیم‌شونده کسر می‌شوند
    -- (تأیید صریحِ کاربر): نرخِ این کالاها ثابت می‌ماند و دست نمی‌خورد،
    -- ولی خودشان هم بخشی از دستمزدِ واقعیِ حسابِ ۷۵۱ را مصرف کرده‌اند —
    -- اگر همین سهم از دستمزدِ واقعی کسر نشود، بقیه‌ی کالاها (که ضریب
    -- دارند) کلِ دستمزدِ واقعی را بینِ خودشان تقسیم می‌کنند، درحالی‌که
    -- کالاهای کارمزدی هم جداگانه دستمزدِ ثابتِ خودشان را نگه داشته‌اند —
    -- یعنی جمعِ کلِ دستمزدِ همه‌ی کالاها بیشتر از دستمزدِ واقعی می‌شود.
    -- پس دستمزد/سربارِ باقی‌مانده برای تقسیم = دستمزدِ واقعی − Σ(مقدار
    -- تولیدِ هر کالای کارمزدی × نرخِ ثابتِ فعلی‌اش).
    SELECT  r.UnitId,
            ISNULL(SUM(q.Qty * hm.IMBIBE_MANF), 0) AS FixedWage,
            ISNULL(SUM(q.Qty * hm.IMBIBE_SAR),  0) AS FixedOh
    INTO    #FixedTotals
    FROM    dbo.CC_LaborAbsorptionRate r
    JOIN    #CodeQty q  ON q.UnitId = r.UnitId AND q.CODE = TRY_CAST(r.CODE AS BIGINT)
    JOIN    dbo.HEAD_MANF hm ON hm.CODE = r.CODE AND hm.GHEYMAT = @Month
    WHERE   r.IsFixed = 1
    GROUP BY r.UnitId;

    -- هشدار صفر بودن واقعی وقتی وزنی هست — ماجرای یزد/خرداد که باعث
    -- شد این گارد اضافه شود.
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'واحد ', w.UnitId, N': دستمزد واقعی این واحد (طبق CC_UnitAcc) صفر آمد (احتمالاً نگاشتِ حساب‌ها برای این واحد ناقص است یا هیچ کدی توسط این واحد در این ماه تولید نشده) — تقسیم دستمزد رد شد تا IMBIBE_MANF صفر نشود.')
    FROM   #UnitWeight w
    LEFT   JOIN #UnitActual a ON a.UnitId = w.UnitId
    WHERE  w.TotalWeight <> 0 AND ISNULL(a.ActWage, 0) = 0;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'واحد ', w.UnitId, N': سربار واقعی این واحد (طبق CC_UnitAcc، CostKind=2) صفر آمد (احتمالاً برای این واحد نگاشتِ سربار تعریف نشده یا هیچ کدی توسط این واحد در این ماه تولید نشده) — تقسیم سربار رد شد تا IMBIBE_SAR صفر نشود.')
    FROM   #UnitWeight w
    LEFT   JOIN #UnitActual a ON a.UnitId = w.UnitId
    WHERE  w.TotalWeightOh <> 0 AND ISNULL(a.ActOh, 0) = 0;

    -- هشدار وقتی سهمِ کالاهای کارمزدی از دستمزدِ واقعی بیشتر است —
    -- یعنی چیزی در نرخِ ثابتِ آن‌ها یا در خودِ دستمزدِ واقعی مشکوک
    -- است؛ باقی‌مانده منفی می‌شد، پس تقسیم برای بقیه‌ی کالاها هم رد
    -- شد (نه فقط صفر شدنِ کارمزدی‌ها، که اصلاً دست‌نخورده‌اند).
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'واحد ', a.UnitId, N': مجموعِ دستمزدِ کالاهای کارمزدی (', FORMAT(ft.FixedWage, 'N0'),
                  N') از دستمزدِ واقعیِ کلِ واحد (', FORMAT(a.ActWage, 'N0'),
                  N') بیشتر است — تقسیمِ دستمزد برای بقیه‌ی کالاها رد شد.')
    FROM   #UnitActual a
    JOIN   #FixedTotals ft ON ft.UnitId = a.UnitId
    WHERE  a.ActWage - ft.FixedWage <= 0 AND ft.FixedWage <> 0;

    -- هشدار: کالایی که این ماه تولید شده ولی هیچ سطری در
    -- CC_LaborAbsorptionRate ندارد.
    --
    -- ⚠️ چرا لازم است: JOIN به CC_LaborAbsorptionRate در ساخت #Proposals
    -- (پایین) از نوع INNER است، پس چنین کالایی اصلاً وارد محاسبه نمی‌شود و
    -- IMBIBE_MANF/IMBIBE_SAR فرمولش روی مقدار قبلی — معمولاً صفر — می‌ماند.
    -- تا امروز این حالت هیچ خطا و هیچ هشداری نمی‌داد، پس فقط وقتی کشف
    -- می‌شد که کسی دستی سراغ خودِ فرمول برود.
    --
    -- نمونه‌ی واقعی: کد ۳۱۷۰ «پنیر پیتزا موزارلا ۱ کیلویی نازلی»، فرمول
    -- ۸۲۶۰۳۱۶۸۸ مرداد ۱۴۰۵ — هر دو نرخ صفر، بدون هیچ نشانه‌ای.
    --
    -- عمداً فقط هشدار است نه خطا: نبودِ ضریب برای کالای تازه طبیعی است و
    -- نباید جلوی بستنِ ماه را بگیرد؛ فقط باید دیده شود.
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'کالای ', cq.CODE, N' «', ISNULL(sd.NAME, N'؟'), N'» در واحد ', cq.UnitId,
                  N' این ماه تولید شده ولی ضریب جذب دستمزد/سربار برایش تعریف نشده — ',
                  N'دستمزد و سربار فرمولش دست‌نخورده ماند. ',
                  N'از بخش «نرخ جذب دستمزد» ضریبش را ثبت کنید.')
    FROM   #CodeQty cq
    LEFT   JOIN dbo.STUF_DEF sd ON sd.CODE = cq.CODE
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.CC_LaborAbsorptionRate r
                       WHERE r.CODE = cq.CODE AND r.UnitId = cq.UnitId);

    -- ۳) پیشنهادِ نرخ هر (واحد، فرمول) این ماه (تأیید کاربر: نرخِ پایه
    --    = دستمزدِ واقعی ÷ مجموعِ وزنی، بعد در ضریب و وزنِ خودِ کالا
    --    ضرب می‌شود — نه تقسیم بر مقدارِ تولیدِ خودِ کالا. نتیجه: دو
    --    کالای هم‌ضریب/هم‌وزن نرخِ واحدِ یکسان می‌گیرند، ولی چون نرخ در
    --    مقدار ضرب می‌شود، جمعِ دستمزدشان به‌نسبتِ تولید فرق می‌کند):
    --      IsFixed=1        → NULL (کارمزدی، هرگز دست نمی‌خورد)
    --      Coefficient=0    → 0    (کاربر عمداً کنار گذاشته)
    --      Coefficient=NULL → NULL (هنوز بررسی نشده، دست نمی‌خورد)
    --      باقی‌ماندهٔ واقعی (پس از کسرِ سهمِ کارمزدی‌ها) صفر/منفی، یا
    --      مقدارِ خودِ کالا صفر → NULL (نرخ ناقص می‌شد، دست نمی‌خورد —
    --                                  هشدار بالا)
    --      وگرنه            → باقی‌ماندهٔ واقعی × ضریب × ضریبِ‌وزنِ خودِ
    --                          کالا ÷ مجموعِ وزنی
    ;WITH ThisMonthFormula AS (
        SELECT DISTINCT hm.FNUMB, hm.CODE, cua.UnitId
        FROM   dbo.HEAD_LST h
        JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = TRY_CAST(pl.N_KOL AS INT) AND hm.GHEYMAT = @Month
        JOIN   dbo.CC_UnitAnbar cua ON cua.Anbar = pl.ANBAR AND cua.AnbarRole = 3
        JOIN   dbo.CC_Unit u ON u.UnitId = cua.UnitId AND u.IsActive = 1
        WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
    )
    SELECT  f.FNUMB, f.CODE, f.UnitId,
            CASE WHEN r.IsFixed = 1                                      THEN NULL
                 WHEN r.Coefficient = 0                                  THEN 0
                 WHEN r.Coefficient IS NULL                              THEN NULL
                 WHEN ISNULL(a.ActWage, 0) - ISNULL(ft.FixedWage, 0) <= 0
                      OR ISNULL(w.TotalWeight, 0) = 0 OR ISNULL(q.Qty, 0) = 0 THEN NULL
                 ELSE (a.ActWage - ISNULL(ft.FixedWage, 0)) * r.Coefficient * q.WeightFactor / w.TotalWeight
            END AS ProposedWage,
            CASE WHEN r.IsFixed = 1                                      THEN NULL
                 WHEN ISNULL(r.OverheadCoefficient, r.Coefficient) = 0    THEN 0
                 WHEN ISNULL(r.OverheadCoefficient, r.Coefficient) IS NULL THEN NULL
                 WHEN ISNULL(a.ActOh, 0) - ISNULL(ft.FixedOh, 0) <= 0
                      OR ISNULL(w.TotalWeightOh, 0) = 0 OR ISNULL(q.Qty, 0) = 0 THEN NULL
                 ELSE (a.ActOh - ISNULL(ft.FixedOh, 0)) * ISNULL(r.OverheadCoefficient, r.Coefficient) * q.WeightFactor / w.TotalWeightOh
            END AS ProposedOh
    INTO    #Proposals
    FROM    ThisMonthFormula f
    JOIN    dbo.CC_LaborAbsorptionRate r ON r.CODE = f.CODE AND r.UnitId = f.UnitId
    LEFT    JOIN #UnitActual a ON a.UnitId = f.UnitId
    LEFT    JOIN #UnitWeight w ON w.UnitId = f.UnitId
    LEFT    JOIN #CodeQty   q ON q.UnitId = f.UnitId AND q.CODE = f.CODE
    LEFT    JOIN #FixedTotals ft ON ft.UnitId = f.UnitId;

    DELETE FROM #Proposals WHERE ProposedWage IS NULL AND ProposedOh IS NULL;

    -- ۴) تعارض بین واحدها روی یک فرمولِ مشترک (کد چندواحدی): اگر
    --    پیشنهادهای واحدهای مختلف برای همین فرمول فرق کنند، هیچ‌کدام
    --    اعمال نمی‌شود و فقط هشدار ثبت می‌شود — دستمزد/سربار جداگانه
    --    بررسی می‌شوند چون ممکن است فقط یکی از این دو تعارض داشته باشد.
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'کالای ', CODE, N' هم‌زمان در چند واحد تولید می‌شود و ضریب دستمزدشان به نرخ‌های متفاوت می‌رسد (',
                  FORMAT(MinW, 'N2'), N' در برابر ', FORMAT(MaxW, 'N2'),
                  N') — چون فرمول این کالا فقط یک نرخ می‌تواند داشته باشد، دستمزدش دست‌نخورده ماند.')
    FROM   (SELECT FNUMB, CODE, MIN(ProposedWage) AS MinW, MAX(ProposedWage) AS MaxW, COUNT(*) AS N
            FROM   #Proposals WHERE ProposedWage IS NOT NULL GROUP BY FNUMB, CODE) wa
    WHERE  wa.N > 1 AND ABS(wa.MaxW - wa.MinW) > 0.01;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT @RunId, 'S07B', 2,
           CONCAT(N'کالای ', CODE, N' هم‌زمان در چند واحد تولید می‌شود و ضریب سربارشان به نرخ‌های متفاوت می‌رسد (',
                  FORMAT(MinO, 'N2'), N' در برابر ', FORMAT(MaxO, 'N2'),
                  N') — چون فرمول این کالا فقط یک نرخ می‌تواند داشته باشد، سربارش دست‌نخورده ماند.')
    FROM   (SELECT FNUMB, CODE, MIN(ProposedOh) AS MinO, MAX(ProposedOh) AS MaxO, COUNT(*) AS N
            FROM   #Proposals WHERE ProposedOh IS NOT NULL GROUP BY FNUMB, CODE) oa
    WHERE  oa.N > 1 AND ABS(oa.MaxO - oa.MinO) > 0.01;

    -- ۵) اعمال — فقط جایی که تعارضی نیست (تک‌واحدی، یا همه‌ی واحدها
    --    یک نرخ پیشنهاد داده‌اند).
    DECLARE @WageRows INT = 0, @OhRows INT = 0;

    UPDATE hm
       SET hm.IMBIBE_MANF = wa.MinW
    FROM   dbo.HEAD_MANF hm
    JOIN   (SELECT FNUMB, CODE, MIN(ProposedWage) AS MinW, MAX(ProposedWage) AS MaxW
            FROM   #Proposals WHERE ProposedWage IS NOT NULL GROUP BY FNUMB, CODE) wa
           ON wa.FNUMB = hm.FNUMB AND wa.CODE = hm.CODE
    WHERE  hm.GHEYMAT = @Month
      AND  ABS(wa.MaxW - wa.MinW) <= 0.01;

    SET @WageRows = @@ROWCOUNT;
    SET @Total   += @WageRows;

    UPDATE hm
       SET hm.IMBIBE_SAR = oa.MinO
    FROM   dbo.HEAD_MANF hm
    JOIN   (SELECT FNUMB, CODE, MIN(ProposedOh) AS MinO, MAX(ProposedOh) AS MaxO
            FROM   #Proposals WHERE ProposedOh IS NOT NULL GROUP BY FNUMB, CODE) oa
           ON oa.FNUMB = hm.FNUMB AND oa.CODE = hm.CODE
    WHERE  hm.GHEYMAT = @Month
      AND  ABS(oa.MaxO - oa.MinO) <= 0.01;

    SET @OhRows = @@ROWCOUNT;

    -- ⚠️ قبلاً @Total = @WageRows + @OhRows بود — چون تقریباً هر فرمول
    -- هم‌زمان هم دستمزدش هم سربارش آپدیت می‌شود، این عدد هر فرمول را
    -- دوبار می‌شمرد (مثلاً ۹۴+۹۴=۱۸۸ برای ۹۴ فرمولِ واقعی) و در پیام
    -- «X فرمول به‌روزرسانی شد» (RateEngineSteps.cs) گمراه‌کننده بود.
    -- حالا @Total = تعدادِ فرمولِ یکتایی که حداقل یکی از دو مقدارش
    -- عوض شده، مطابق همان (FNUMB, CODE)ای که در دو UPDATE بالا هدف
    -- قرار گرفت.
    SELECT @Total = COUNT(*)
    FROM (
        SELECT FNUMB, CODE
        FROM   (SELECT FNUMB, CODE, MIN(ProposedWage) AS MinW, MAX(ProposedWage) AS MaxW
                FROM   #Proposals WHERE ProposedWage IS NOT NULL GROUP BY FNUMB, CODE) wa
        WHERE  ABS(wa.MaxW - wa.MinW) <= 0.01
        UNION
        SELECT FNUMB, CODE
        FROM   (SELECT FNUMB, CODE, MIN(ProposedOh) AS MinO, MAX(ProposedOh) AS MaxO
                FROM   #Proposals WHERE ProposedOh IS NOT NULL GROUP BY FNUMB, CODE) oa
        WHERE  ABS(oa.MaxO - oa.MinO) <= 0.01
    ) touched;

    -- ⚠️ قبلاً فقط در حالت هشدار/تعارض چیزی در CC_RunLog ثبت می‌شد —
    -- یک اجرای موفقِ بی‌مشکل هیچ ردی در لاگ اجرا نمی‌گذاشت (تأیید
    -- کاربر: «لاگ نمی‌زنه»). حالا همیشه یک خلاصه ثبت می‌شود، عیناً
    -- سبکِ لاگِ S10.
    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, 'S07B', 1,
            CONCAT(N'دستمزد ', @WageRows, N' فرمول و سربار ', @OhRows, N' فرمول به‌روزرسانی شد (', @Total, N' فرمولِ یکتا).'));

    DROP TABLE #UnitActual;
    DROP TABLE #CodeQty;
    DROP TABLE #UnitWeight;
    DROP TABLE #FixedTotals;
    DROP TABLE #Proposals;

    SELECT @Total AS Value;
END
GO

/* ═══════════════════════════════════════════════════════════════════
   S10 — تراز هزینه تبدیل به تفکیک واحد تولیدی

   جذب‌شده = Σ (مقدار تولید × نرخ جذب فرمول)
   واقعی   = Σ (مانده سرفصل × ضریب سهم)   طبق CC_UnitAcc
   ضریب    = واقعی ÷ جذب‌شده

   کنترل متقابل: به شرط صفر بودن کار در جریان، جذب باید با
   گردش بستانکار حساب ۷۵۱ با تفصیلی 99999999 برابر باشد.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S10_BalanceConversion
    @RunId INT,
    @Month TINYINT,
    @DT1   BIGINT,
    @DT2   BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TafDastmozd BIGINT = 99999999;   -- دستمزد (و روي اين پايگاه‌داده: سربار هم همين‌جا)
    DECLARE @TafSarbar   BIGINT = 99999998;   -- سربار، فقط وقتي نصب از دستمزد جدايش کرده باشد
    DECLARE @UnitId INT, @SplitMode TINYINT;

    -- تشخيص واحد از روي دپارتمان کنار گذاشته شد: دپارتمان را اپراتور دستي
    -- روي برگه مي‌زند و اشتباه تايپي رايج است. ملاک مطمئن، انباري است که
    -- محصول توليدشده وارد آن مي‌شود (CC_UnitAnbar.AnbarRole = 3، «محصول»)
    -- — همان چيزي که در تنظيمات واحدها از قبل تعريف شده و کاربر تأييد
    -- کرد بايد ملاک باشد (نه Depatman). CHK-16 (S00) از قبل هر انباري که
    -- برگه توليد دارد ولي به هيچ واحدي وصل نيست را هشدار مي‌دهد.
    --
    -- ريسک مشابهِ حالت قبلي (Depatman=NULL تکراري) اينجا اين است: اگر يک
    -- انبارِ «محصول» به بيش از يک واحد فعال وصل باشد، هر دو دقيقاً همان
    -- برگه‌ها را پردازش مي‌کنند و چون اين حلقه IMBIBE_MANF/IMBIBE_SAR را
    -- مستقيماً در HEAD_MANF ويرايش مي‌کند، واحد دوم رويِ مقدارِ از‌قبل‌
    -- تعديل‌شده‌ي واحد اول دوباره ضريب مي‌زند — فرمول‌ها خراب مي‌شوند.
    IF EXISTS (
        SELECT ua.Anbar
        FROM   dbo.CC_UnitAnbar ua
        JOIN   dbo.CC_Unit      u  ON u.UnitId = ua.UnitId AND u.IsActive = 1
        WHERE  ua.AnbarRole = 3
        GROUP  BY ua.Anbar
        HAVING COUNT(DISTINCT ua.UnitId) > 1
    )
    BEGIN
        RAISERROR(N'يک انبار محصول (نقش «محصول») به بيش از يک واحد توليدي فعال وصل است؛ اين باعث پردازش دوباره‌ي همان برگه‌ها و خراب شدن فرمول‌ها مي‌شود. نگاشت انبار⇄واحد را در تنظیمات اصلاح کنيد.', 16, 1);
        RETURN;
    END

    DELETE dbo.CC_ConversionCost WHERE RunId = @RunId;
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND StepCode = 'S10' AND RuleCode = 'CHK-08';

    DECLARE cUnit CURSOR LOCAL FAST_FORWARD FOR
        SELECT UnitId, SplitMode
        FROM   dbo.CC_Unit WHERE IsActive = 1 ORDER BY SeqNo;

    OPEN cUnit;
    FETCH NEXT FROM cUnit INTO @UnitId, @SplitMode;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        ---- ۱) جذب‌شده از برگه‌هاي توليد اين واحد (بر اساس انبار محصول)
        DECLARE @absWage FLOAT, @absOh FLOAT;

        -- کالاهای کارمزدی از این جمع کنار می‌مانند (تأیید کاربر): نرخِ
        -- S07B هم دقیقاً همین کار را می‌کند — دستمزدِ واقعیِ CC_UnitAcc
        -- را منهایِ سهمِ کارمزدی‌ها بین بقیه تقسیم می‌کند؛ پس «جذب‌شده»
        -- باید همان محدوده (فقط غیرکارمزدی) را داشته باشد تا با «واقعی»
        -- (که حالا S07B هم از همین CC_UnitAcc می‌گیرد، نه از ۷۵۱) در یک
        -- پاس دقیقاً جفت شود، بدون نیاز به همگراییِ چندمرحله‌ای.
        SELECT  @absWage = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_MANF,0)), 0),
                @absOh   = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_SAR ,0)), 0)
        FROM    dbo.HEAD_LST  h
        JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                AND hm.GHEYMAT = @Month
        WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
          AND   pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                              WHERE UnitId = @UnitId AND AnbarRole = 3)
          AND   NOT EXISTS (
                    SELECT 1 FROM dbo.CC_LaborAbsorptionRate fx
                    WHERE fx.UnitId = @UnitId AND fx.CODE = hm.CODE AND fx.IsFixed = 1
                );

        DECLARE @absTotal FLOAT = @absWage + @absOh;

        ---- ۲) کنترل متقابل با حساب ۷۵۱، به تفکيک واحد و به تفکيک دستمزد/سربار
        -- HES_M روي اين رديف‌ها کدِ خودِ کالاي توليدشده است (نه يک معينِ
        -- عمومي) — کاربر تأييد کرد و مستقيماً تست شد: تمام HES_M هاي اين
        -- تفصيلي دقيقاً با STUF_DEF.CODE مطابقت دارند. پس مي‌شود دقيقاً
        -- همان مجموعه کدهايي را که اين واحد در همين بازه توليد کرده
        -- (زيرکوئري پايين، عيناً منطق جذب‌شده در بالا) فيلتر کرد و مانده
        -- ۷۵۱ را per-واحد گرفت، نه فقط جمع کل شرکت.
        --
        -- ⚠️ @TafSarbar (سربار، ۹۹۹۹۹۹۹۸) روي اين پايگاه‌داده استفاده
        -- نمي‌شود — همه‌ي دستمزد و سربار زير همان @TafDastmozd (۹۹۹۹۹۹۹۹)
        -- ثبت مي‌شوند (کاربر تأييد کرد). ولي روي نصب‌هاي ديگر ممکن است
        -- اين دو را جدا کنند؛ اگر اينجا فقط @TafDastmozd را چک مي‌کرديم،
        -- روي چنان پايگاه‌داده‌اي سهمِ سربار از مانده ۷۵۱ اصلاً ديده
        -- نمي‌شد و کنترل CHK-08 دقيقاً به‌اندازه‌ي سربار غلط مي‌شد. پس هر
        -- دو تفصيلي را جدا جمع مي‌زنيم؛ هر کدام که در اين پايگاه‌داده
        -- خالي باشد صفر مي‌ماند و به کنترل کل آسيبي نمي‌زند.
        --
        -- ⚠️ اصلاح (تأیید کاربر، کد ۳۷۳ خرداد ۱۴۰۵ کشف شد، عیناً همان
        -- تصحیح در S07B بالاتر): وقتی یک کد هم‌زمان در چند واحد تولید
        -- می‌شود، IN ساده مبلغِ کامل ۷۵۱ آن کد را به هر واحدی که کد را
        -- تولید کرده کامل می‌افزود (دوبارشماری در CHK-08). حالا مبلغ هر
        -- کد به نسبتِ سهمِ مقداریِ (MEGHK) این واحد از کل تولید همان کد
        -- در همین ماه تقسیم می‌شود.
        DECLARE @absWipWage FLOAT, @absWipOh FLOAT, @absWip FLOAT;

        ;WITH CodeAmt AS (
            SELECT  TRY_CAST(d.HES_M AS BIGINT) AS CODE,
                    SUM(CASE WHEN d.HES_T = @TafDastmozd THEN d.BES - d.BED ELSE 0 END) AS WageAmt,
                    SUM(CASE WHEN d.HES_T = @TafSarbar   THEN d.BES - d.BED ELSE 0 END) AS OhAmt
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
            WHERE   d.HES_K = 751 AND d.HES_T IN (@TafDastmozd, @TafSarbar)
              AND   hd.DATE_S BETWEEN @DT1 AND @DT2
            GROUP BY TRY_CAST(d.HES_M AS BIGINT)
        ),
        CodeQty AS (
            SELECT  TRY_CAST(pl.CODE AS BIGINT) AS CODE, cua.UnitId, SUM(pl.MEGHK) AS Qty
            FROM    dbo.HEAD_LST h
            JOIN    dbo.INVO_LST pl      ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
            JOIN    dbo.CC_UnitAnbar cua ON cua.Anbar  = pl.ANBAR AND cua.AnbarRole = 3
            JOIN    dbo.CC_Unit u        ON u.UnitId   = cua.UnitId AND u.IsActive = 1
            WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
            GROUP BY TRY_CAST(pl.CODE AS BIGINT), cua.UnitId
        ),
        CodeTotalQty AS (
            SELECT CODE, SUM(Qty) AS TotalQty FROM CodeQty GROUP BY CODE
        )
        -- ⚠️ اصلاح (تأیید کاربر: «کنترلِ از ۷۵۱ باید کاملِ تفصیلی ۹۹۹۹۹۹۹۹
        -- را جمع بزند، کارمزدی‌ها هم توش باشد»): یک نسخه‌ی قبلی این‌جا
        -- سهمِ کالاهای کارمزدی را هم کنار می‌گذاشت تا با «جذب‌شده» (که
        -- عمداً کارمزدی‌ها را کنار می‌گذارد) هم‌محدوده شود — ولی این خودِ
        -- «کنترل از ۷۵۱» را اشتباه می‌کرد: این ستون قرار است یک کنترلِ
        -- مستقل و کاملِ گردشِ واقعیِ حساب باشد، نه مقیدشده به همان
        -- محدوده‌ای که روشِ جذب فعلاً استفاده می‌کند. پس هیچ کدی
        -- (کارمزدی یا نه) از این جمع کنار گذاشته نمی‌شود.
        SELECT  @absWipWage = ISNULL(SUM(ca.WageAmt * cq.Qty / ctq.TotalQty), 0),
                @absWipOh   = ISNULL(SUM(ca.OhAmt   * cq.Qty / ctq.TotalQty), 0)
        FROM    CodeAmt ca
        JOIN    CodeQty cq       ON cq.CODE  = ca.CODE AND cq.UnitId = @UnitId
        JOIN    CodeTotalQty ctq ON ctq.CODE = ca.CODE
        WHERE   ctq.TotalQty <> 0;

        SET @absWip = @absWipWage + @absWipOh;

        ---- ۳) واقعي از تراز، طبق نگاشت قابل ويرايش کاربر
        DECLARE @actWage FLOAT, @actOh FLOAT;

        -- CROSS APPLY نه JOIN روي جمعِ از‌قبل‌گروه‌بندی‌شده، چون هر سطر
        -- CC_UnitAcc ممکن است سطح معین/تفصیلی متفاوتی مشخص کرده باشد؛
        -- خالی‌بودن هرکدام یعنی «همهٔ آن سطح» (نگاشت گسترده‌تر، مثل قبل).
        SELECT  @actWage = ISNULL(SUM(CASE WHEN m.CostKind = 1
                                           THEN t.Amount * m.Ratio ELSE 0 END), 0),
                @actOh   = ISNULL(SUM(CASE WHEN m.CostKind = 2
                                           THEN t.Amount * m.Ratio ELSE 0 END), 0)
        FROM    dbo.CC_UnitAcc m
        CROSS   APPLY (
                    SELECT SUM(d.BED) - SUM(d.BES) AS Amount
                    FROM   dbo.DEED_DTL d
                    JOIN   dbo.DEED_HED hd ON hd.N_S = d.N_S
                    WHERE  hd.DATE_S BETWEEN @DT1 AND @DT2
                      AND  d.HES_K = m.HesKol
                      AND  (m.HesMoin    IS NULL OR d.HES_M = m.HesMoin)
                      AND  (m.HesTafsili IS NULL OR d.HES_T = m.HesTafsili)
                ) t
        WHERE   m.IsActive = 1 AND m.UnitId = @UnitId;

        -- ⚠️ اصلاح (تأیید کاربر: نرخِ S07B هم دستمزدِ واقعی را منهایِ
        -- سهمِ کارمزدی‌ها می‌کند قبل از تقسیم): «جذب‌شده» بالا عمداً
        -- کارمزدی‌ها را ندارد، پس «واقعی» هم باید همین سهم کم شود، وگرنه
        -- ضریب k یک تفاوتِ کاذب (دقیقاً به‌اندازه‌ی دستمزدِ کارمزدی‌ها)
        -- نشان می‌دهد و همه‌ی نرخ‌های غیرکارمزدی را غلط تعدیل می‌کند.
        DECLARE @fixedWage FLOAT, @fixedOh FLOAT;

        SELECT  @fixedWage = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_MANF,0)), 0),
                @fixedOh   = ISNULL(SUM(pl.MEGHK * ISNULL(hm.IMBIBE_SAR ,0)), 0)
        FROM    dbo.HEAD_LST  h
        JOIN    dbo.INVO_LST  pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
        JOIN    dbo.HEAD_MANF hm ON hm.FNUMB  = TRY_CAST(pl.N_KOL AS INT)
                                AND hm.GHEYMAT = @Month
        JOIN    dbo.CC_LaborAbsorptionRate fx ON fx.UnitId = @UnitId AND fx.CODE = hm.CODE AND fx.IsFixed = 1
        WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
          AND   pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                              WHERE UnitId = @UnitId AND AnbarRole = 3);

        SET @actWage = @actWage - @fixedWage;
        SET @actOh   = @actOh   - @fixedOh;

        DECLARE @actTotal FLOAT = @actWage + @actOh;

        ---- ۴) ضريب تعديل
        DECLARE @kWage FLOAT = 1, @kOh FLOAT = 1;

        IF @absTotal <> 0
        BEGIN
            IF @SplitMode = 1                    -- يک ضريب براي کل هزينه تبديل
            BEGIN
                DECLARE @k FLOAT = @actTotal / @absTotal;
                SET @kWage = @k;
                SET @kOh   = @k;
            END
            ELSE                                 -- دو ضريب مجزا
            BEGIN
                SET @kWage = CASE WHEN @absWage <> 0 THEN @actWage / @absWage ELSE 1 END;
                SET @kOh   = CASE WHEN @absOh   <> 0 THEN @actOh   / @absOh   ELSE 1 END;
            END
        END

        ---- ۵) ثبت نتيجه
        INSERT dbo.CC_ConversionCost
            (RunId, UnitId, CostKind, AbsorbedAmount, AbsorbedFromWip,
             ActualAmount, AdjustFactor, ActualDetailJson)
        VALUES
            (@RunId, @UnitId, 0, @absTotal, @absWip, @actTotal,
             CASE WHEN @absTotal <> 0 THEN @actTotal / @absTotal ELSE 1 END,
             (SELECT m.HesKol, m.HesMoin, m.HesTafsili, m.CostKind, m.Ratio
              FROM   dbo.CC_UnitAcc m
              WHERE  m.UnitId = @UnitId AND m.IsActive = 1
              FOR JSON PATH)),
            (@RunId, @UnitId, 1, @absWage, @absWipWage, @actWage, @kWage, NULL),
            (@RunId, @UnitId, 2, @absOh,   @absWipOh,   @actOh,   @kOh,   NULL);

        ---- ۶) هشدار اختلاف کنترلي
        --
        -- ⚠️ اصلاح (همان باگِ CHK-09، اینجا هم پیدا شد): @absWip بالا عمداً
        -- کاملِ ۷۵۱ است (تأیید کاربر، برای ستونِ نمایشیِ «کنترل از ۷۵۱»)،
        -- ولی @absTotal («جذب‌شده») عمداً کارمزدی‌ها را ندارد — این دو
        -- همیشه به‌اندازه‌ی دستمزدِ کارمزدی‌ها فرق دارند و مقایسه‌ی مستقیم‌شان
        -- همیشه یک هشدارِ کاذب می‌سازد. اینجا برای خودِ این چک یک نسخه‌ی
        -- کارمزدی‌نتّشده از ۷۵۱ می‌سازیم — دقیقاً همان استثنایی که @absWage
        -- بالا هم دارد.
        DECLARE @absWipFixed FLOAT;

        ;WITH CodeAmt2 AS (
            SELECT  TRY_CAST(d.HES_M AS BIGINT) AS CODE,
                    SUM(CASE WHEN d.HES_T = @TafDastmozd THEN d.BES - d.BED ELSE 0 END) AS WageAmt,
                    SUM(CASE WHEN d.HES_T = @TafSarbar   THEN d.BES - d.BED ELSE 0 END) AS OhAmt
            FROM    dbo.DEED_DTL d
            JOIN    dbo.DEED_HED hd ON hd.N_S = d.N_S
            WHERE   d.HES_K = 751 AND d.HES_T IN (@TafDastmozd, @TafSarbar)
              AND   hd.DATE_S BETWEEN @DT1 AND @DT2
            GROUP BY TRY_CAST(d.HES_M AS BIGINT)
        ),
        CodeQty2 AS (
            SELECT  TRY_CAST(pl.CODE AS BIGINT) AS CODE, cua.UnitId, SUM(pl.MEGHK) AS Qty
            FROM    dbo.HEAD_LST h
            JOIN    dbo.INVO_LST pl      ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
            JOIN    dbo.CC_UnitAnbar cua ON cua.Anbar  = pl.ANBAR AND cua.AnbarRole = 3
            JOIN    dbo.CC_Unit u        ON u.UnitId   = cua.UnitId AND u.IsActive = 1
            WHERE   h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
            GROUP BY TRY_CAST(pl.CODE AS BIGINT), cua.UnitId
        ),
        CodeTotalQty2 AS (
            SELECT CODE, SUM(Qty) AS TotalQty FROM CodeQty2 GROUP BY CODE
        )
        SELECT  @absWipFixed = ISNULL(SUM(ca.WageAmt * cq.Qty / ctq.TotalQty), 0)
                              + ISNULL(SUM(ca.OhAmt   * cq.Qty / ctq.TotalQty), 0)
        FROM    CodeAmt2 ca
        JOIN    CodeQty2 cq       ON cq.CODE  = ca.CODE AND cq.UnitId = @UnitId
        JOIN    CodeTotalQty2 ctq ON ctq.CODE = ca.CODE
        WHERE   ctq.TotalQty <> 0
          AND   NOT EXISTS (
                    SELECT 1 FROM dbo.CC_LaborAbsorptionRate fx
                    WHERE fx.UnitId = @UnitId AND TRY_CAST(fx.CODE AS BIGINT) = ca.CODE AND fx.IsFixed = 1
                );

        IF ABS(@absWipFixed - @absTotal) > 10000000
            INSERT dbo.CC_Exception
                (RunId, StepCode, RuleCode, ExType, Severity, Amount, Description)
            VALUES (@RunId, 'S10', 'CHK-08', 10, 1, @absWipFixed - @absTotal,
                    CONCAT(N'اختلاف جذب: برگه‌هاي توليد ', FORMAT(@absTotal, 'N0'),
                           N' در برابر حساب ۷۵۱ (بدونِ کارمزدی‌ها) ', FORMAT(@absWipFixed, 'N0')));

        ---- ۷) اعمال ضريب روي فرمول‌هاي کالاهاي توليدشده در اين واحد
        IF @WhatIf = 0 AND (@kWage <> 1 OR @kOh <> 1)
        BEGIN
            BEGIN TRAN;

            UPDATE  hm
               SET  hm.IMBIBE_MANF = hm.IMBIBE_MANF * @kWage,
                    hm.IMBIBE_SAR  = hm.IMBIBE_SAR  * @kOh
            OUTPUT  @RunId, 'S10', inserted.FNUMB,
                    TRY_CAST(inserted.CODE AS BIGINT), NULL, 'IMBIBE_MANF',
                    deleted.IMBIBE_MANF, inserted.IMBIBE_MANF,
                    CONCAT(N'ضريب تعديل هزينه تبديل ', FORMAT(@kWage, 'N5'))
              INTO  dbo.CC_FormulaChange
                    (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                     FieldName, OldValue, NewValue, Reason)
            FROM    dbo.HEAD_MANF hm
            WHERE   hm.GHEYMAT = @Month
              AND   EXISTS (
                        SELECT 1
                        FROM   dbo.HEAD_LST h
                        JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                        WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                          AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
                          AND  pl.ANBAR IN (SELECT Anbar FROM dbo.CC_UnitAnbar
                                            WHERE UnitId = @UnitId AND AnbarRole = 3))
              AND   NOT EXISTS (
                        SELECT 1 FROM dbo.CC_LaborAbsorptionRate fx
                        WHERE fx.UnitId = @UnitId AND fx.CODE = hm.CODE AND fx.IsFixed = 1
                   );

            INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
            VALUES (@RunId, 'S10', 1,
                    CONCAT(N'واحد ', @UnitId, N': ضريب تعديل ',
                           FORMAT(@kWage, 'N5'), N' روي ', @@ROWCOUNT, N' فرمول'));

            COMMIT;
        END

        FETCH NEXT FROM cUnit INTO @UnitId, @SplitMode;
    END

    CLOSE cUnit;
    DEALLOCATE cUnit;

    ---- خلاصه
    SELECT  u.UnitName                          AS واحد,
            CASE c.CostKind WHEN 0 THEN N'کل هزينه تبديل'
                            WHEN 1 THEN N'دستمزد'
                            ELSE N'سربار' END   AS نوع,
            c.AbsorbedAmount                    AS جذب_شده,
            c.AbsorbedFromWip                   AS کنترل_از_751,
            c.ActualAmount                      AS واقعي,
            c.AdjustFactor                      AS ضريب
    FROM    dbo.CC_ConversionCost c
    JOIN    dbo.CC_Unit u ON u.UnitId = c.UnitId
    WHERE   c.RunId = @RunId
    ORDER BY u.SeqNo, c.CostKind;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   S11 — انتشار نرخ، نسخه تولیدی

   سطح‌بندی درخت فرمول، سپس محاسبه از عمیق‌ترین سطح به سطح صفر.
   نتیجه در DTL_MANF نوشته و در CC_FormulaChange ثبت می‌شود.

   یک پاس، قطعی، بدون تکرار.
   ⚠️ يک کالا مي‌تواند در همان ماه بيش از يک فرمول فعال داشته باشد (مثلاً
   روزهاي مختلف با ترکيب مواد متفاوت توليد شده باشد) — طبق تأييد صاحب
   پروژه اين طبيعي است، نه خطاي داده. نسخه‌ي قبلي فقط يک فرمول را با
   TOP 1 (آخرين DATE_ACTIV/FNUMB) براي محاسبه و انتشار انتخاب مي‌کرد؛
   بهاي «خودِ» کالا حالا ميانگين موزونِ بهاي همه‌ي فرمول‌هاي فعالش است،
   وزن‌دهي‌شده با مقدار واقعيِ توليدشده زيرِ هرکدام در بازه‌ي @DT1..@DT2
   (از HEAD_LST/INVO_LST TAG=9، N_KOL=FNUMB). اگر هيچ‌کدام توليد واقعي
   نداشتند (فرمول تعريف شده ولي هنوز مصرف نشده)، ميانگين ساده جايگزين
   وزن مي‌شود — دقيقاً همان قاعده‌اي که CHK-09 در S00 هم استفاده مي‌کند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_S11_PropagateRates
    @RunId  INT,
    @Month  TINYINT,
    @DT1    BIGINT,
    @DT2    BIGINT,
    @WhatIf BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    /* ─── ۱) يال‌هاي درخت ─── */
    IF OBJECT_ID('tempdb..#Edge') IS NOT NULL DROP TABLE #Edge;

    SELECT  DISTINCT
            CAST(h.CODE AS BIGINT) AS Parent,
            CAST(d.CODE AS BIGINT) AS Child
    INTO    #Edge
    FROM    dbo.HEAD_MANF h
    JOIN    dbo.DTL_MANF  d ON d.FNUMB = h.FNUMB
    WHERE   h.GHEYMAT = @Month
      AND   h.CODE IS NOT NULL AND d.CODE IS NOT NULL
      AND   CAST(h.CODE AS BIGINT) <> CAST(d.CODE AS BIGINT);

    CREATE CLUSTERED INDEX IX_Edge ON #Edge(Parent, Child);

    /* ─── ۲) تشخيص حلقه؛ بدون اين، محاسبه بي‌نهايت مي‌شود ─── */
    IF OBJECT_ID('tempdb..#Cycle') IS NOT NULL DROP TABLE #Cycle;
    CREATE TABLE #Cycle (Code BIGINT PRIMARY KEY);

    ;WITH Walk AS (
        SELECT  Parent AS Root, Child, 1 AS Lvl,
                CAST('/' + CAST(Parent AS VARCHAR(20)) + '/' AS VARCHAR(4000)) AS Pt
        FROM    #Edge
        UNION ALL
        SELECT  w.Root, e.Child, w.Lvl + 1,
                CAST(w.Pt + CAST(e.Parent AS VARCHAR(20)) + '/' AS VARCHAR(4000))
        FROM    Walk w JOIN #Edge e ON e.Parent = w.Child
        WHERE   w.Lvl < 20
          AND   w.Pt NOT LIKE '%/' + CAST(e.Child AS VARCHAR(20)) + '/%'
    )
    INSERT #Cycle(Code)
    SELECT DISTINCT Root FROM Walk WHERE Child = Root
    OPTION (MAXRECURSION 0);

    IF EXISTS (SELECT 1 FROM #Cycle)
    BEGIN
        INSERT dbo.CC_Exception
            (RunId, StepCode, RuleCode, ExType, Severity, Code, Description)
        SELECT @RunId, 'S11', 'CHK-06', 5, 2, Code,
               N'حلقه در ساختار فرمول — محاسبه نرخ ممکن نيست'
        FROM   #Cycle;

        RAISERROR(N'حلقه در ساختار فرمول يافت شد؛ محاسبه متوقف شد.', 16, 1);
        RETURN;
    END

    /* ─── ۳) سطح‌بندي ─── */
    IF OBJECT_ID('tempdb..#C') IS NOT NULL DROP TABLE #C;

    CREATE TABLE #C (
        Code       BIGINT PRIMARY KEY,
        Llc        SMALLINT NOT NULL DEFAULT 0,
        HasFormula BIT      NOT NULL DEFAULT 0,
        Src        TINYINT  NOT NULL DEFAULT 1,
        Mat        FLOAT    NOT NULL DEFAULT 0,
        Wage       FLOAT    NOT NULL DEFAULT 0,
        Oh         FLOAT    NOT NULL DEFAULT 0
    );

    INSERT #C (Code)
    SELECT Parent FROM #Edge UNION SELECT Child FROM #Edge;

    DECLARE @changed INT = 1, @guard INT = 0;

    WHILE @changed > 0 AND @guard < 30
    BEGIN
        UPDATE  c
           SET  c.Llc = x.NewLlc
        FROM    #C c
        JOIN   (SELECT e.Child, MAX(p.Llc) + 1 AS NewLlc
                FROM   #Edge e JOIN #C p ON p.Code = e.Parent
                GROUP BY e.Child) x ON x.Child = c.Code
        WHERE   x.NewLlc > c.Llc;

        SET @changed = @@ROWCOUNT;
        SET @guard  += 1;
    END

    CREATE INDEX IX_C_Llc ON #C(Llc);

    /* ─── ۳ب) فرمول‌هاي هر کالا در اين ماه — ممکن است بيش از يکي باشد ───
       #F جايگزينِ ستون تکيِ #C.FNUMB قبلي است: هر رديف يک فرمول فعال است،
       با مقدار واقعيِ توليدشده زيرش (Qty) که وزنِ ميانگين‌گيري مي‌شود. */
    IF OBJECT_ID('tempdb..#F') IS NOT NULL DROP TABLE #F;

    CREATE TABLE #F (
        FNUMB INT    PRIMARY KEY,
        Code  BIGINT NOT NULL,
        Qty   FLOAT  NOT NULL DEFAULT 0,
        Wage  FLOAT  NOT NULL DEFAULT 0,
        Oh    FLOAT  NOT NULL DEFAULT 0,
        Mat   FLOAT  NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_F_Code ON #F(Code);

    INSERT #F (FNUMB, Code, Qty, Wage, Oh)
    SELECT  hm.FNUMB, CAST(hm.CODE AS BIGINT),
            ISNULL(p.Qty, 0), ISNULL(hm.IMBIBE_MANF, 0), ISNULL(hm.IMBIBE_SAR, 0)
    FROM    dbo.HEAD_MANF hm
    CROSS   APPLY (
                SELECT SUM(pl.MEGHk) AS Qty
                FROM   dbo.HEAD_LST h
                JOIN   dbo.INVO_LST pl ON pl.NUMBER = h.NUMBER AND pl.TAG = 9
                WHERE  h.TAG = 9 AND h.DATE_N BETWEEN @DT1 AND @DT2
                  AND  TRY_CAST(pl.N_KOL AS INT) = hm.FNUMB
            ) p
    WHERE   hm.GHEYMAT = @Month AND hm.CODE IS NOT NULL
      AND   EXISTS (SELECT 1 FROM #C c WHERE c.Code = CAST(hm.CODE AS BIGINT));

    UPDATE  c SET c.HasFormula = 1, c.Src = 2
    FROM    #C c
    WHERE   EXISTS (SELECT 1 FROM #F f WHERE f.Code = c.Code);

    /* ─── ۴) نرخ مواد خريدني: ميانگين وزني خروج از انبار ───
       عمداً روي کالاهاي بدون فرمول محدود نيست: نيمه‌ساخته‌اي که خودش هم
       اين ماه از انبار حواله خورده (مثل هر ماده‌ي اوليه‌ي ديگر) بايد
       دقيقاً همان ميانگين واقعيِ انبارش را به‌عنوان نرخِ «خودش وقتي در
       فرمولِ کالاي ديگري مصرف مي‌شود» بگيرد — نه نرخِ تازه‌محاسبه‌شده‌ي
       زنجيره‌ي BOM. کاربر تأييد کرد اين دقيقاً همان چيزي است که مغايرت
       حساب ۷۷۱ را ايجاد مي‌کرد: MaterialIssueRebuildService مبلغ واقعيِ
       حواله (بر مبناي AVRAGE واقعيِ انبار در لحظه‌ي هر تراکنش) را با
       SMABL مقايسه مي‌کند؛ اگر SMABL از ميانگين همان انبار بيايد، دو طرف
       از يک منبع مشتق مي‌شوند و طبيعتاً هم‌خوان مي‌مانند — برخلاف نرخِ
       لحظه‌ايِ بازسازي‌شده‌ي BOM که فقط آخرين قيمتِ اجزا را منعکس مي‌کند،
       نه ميانگينِ واقعيِ کل ماه. نتيجه در ۵-ج پايين‌تر override نمي‌شود
       (شرط Src<>1 آنجا).

       ⚠️ ميرايي (damping) — فيکسِ ناپايداريِ کدهاي چندسطحيِ خودمصرف:
       براي کالايي که هم فرمول دارد هم اين ماه به‌عنوان ماده‌ي اوليه‌ي
       کالاي ديگري حواله خروج مي‌شود (مثل ۳۷۳→۱۷۳۲→۳۳۶۵)، رسيدِ توليدِ
       همين کالا (Case ۹ در AverageRateRebuildService) از SMABL همين
       دورِ S11 قيمت مي‌گيرد؛ آن رسيد وارد ميانگين انبارش مي‌شود؛ همين جا
       آن ميانگين به‌عنوان نرخ رسمي‌اش برمي‌گردد. با S07A تنها (بدون S11
       ميانِ هر دور) اين خودش پايدار و سريع همگراست (تست عملي روي کد
       ۳۳۶۵: ۷۰۷ ریال → ۳۰ → ۳). اما وقتي S11 دوباره‌محاسبه‌شده را به
       DTL_MANF مي‌نويسد و S07A دوباره از همان مي‌خواند، هر سطح از زنجيره
       (۳۷۳، سپس ۱۷۳۲، سپس ۳۳۶۵) کمي تقويتش مي‌کند و رويِ‌هم زنجيره‌ي
       سه‌سطحي واگرا مي‌شود (روي ران ۱۷: ۶۸۶→۱۴۰۹→۲۶۳۴، تقريباً دو برابرِ
       هر دور — سقفِ ۵ دورِ حلقه‌ي همگراييِ CloseOrchestrator با هشدار
       متوقفش مي‌کرد، بدون رسيدن به جواب واقعي).
       راه‌حل: هر دور فقط کسري از تغيير را قبول مي‌کنيم (successive
       under-relaxation، تکنيک استاندارد براي رام‌کردن محاسبه‌ي تکراريِ
       خودارجاع)، نه کل آن را — مقدار قبلي از خودِ CC_ItemCost همين
       RunId مي‌آيد (هنوز پاک نشده؛ DELETE در پايين همين رويه است). دور
       اول (که هنوز رکورد قبلي نيست) کامل پذيرفته مي‌شود؛ از دور دوم به
       بعد فقط ۳۵٪ از تغيير اعمال مي‌شود. */
    DECLARE @Damping FLOAT = 0.35;

    UPDATE  c
       SET  c.Mat = CASE WHEN prev.MaterialCost IS NULL THEN z.fi
                          ELSE prev.MaterialCost + @Damping * (z.fi - prev.MaterialCost) END,
            c.Src = 1
    FROM    #C c
    JOIN   (SELECT k.code, SUM(k.MABL_K) / NULLIF(SUM(k.MEGHk), 0) AS fi
            FROM   dbo.KALAS k
            WHERE  k.TAG = 10 AND k.MM = @Month AND k.MEGHk <> 0
            GROUP BY k.code) z ON z.code = c.Code
    LEFT    JOIN dbo.CC_ItemCost prev
            ON  prev.RunId = @RunId AND prev.Code = c.Code
    WHERE   z.fi IS NOT NULL;

    ---- بدون گردش در ماه: آخرين نرخ ميانگين ثبت‌شده
    UPDATE  c
       SET  c.Mat = lp.AVRAGE
    FROM    #C c
    CROSS   APPLY (SELECT TOP 1 i.AVRAGE
                   FROM   dbo.INVO_LST i
                   JOIN   dbo.HEAD_LST h ON h.NUMBER = i.NUMBER AND h.TAG = i.TAG
                   WHERE  CAST(i.CODE AS BIGINT) = c.Code AND i.AVRAGE > 0
                   ORDER BY h.DATE_N DESC, i.NUMBER DESC) lp
    WHERE   c.HasFormula = 0 AND c.Mat = 0;

    UPDATE #C SET Src = 3 WHERE HasFormula = 0 AND Mat = 0;

    /* ─── ۵) محاسبه از عميق‌ترين سطح به سطح صفر ───
       چون فرزندها هميشه سطح عميق‌تري از والد دارند، وقتي به والد
       مي‌رسيم بهاي همه اجزايش قبلاً محاسبه شده است. */

    DECLARE @lvl SMALLINT = (SELECT MAX(Llc) FROM #C);
    DECLARE @totalChanges INT = 0;

    WHILE @lvl >= 0
    BEGIN
        IF @WhatIf = 0
        BEGIN
            BEGIN TRAN;

            ---- ۵-الف) نرخ اجزا در فرمول والدهاي اين سطح
            UPDATE  d
               SET  d.SMABL = ch.Mat + ch.Wage + ch.Oh,
                    d.MABLK = ROUND((ch.Mat + ch.Wage + ch.Oh) * d.MEGHk, 0)
            OUTPUT  @RunId, 'S11', inserted.FNUMB,
                    NULL, TRY_CAST(inserted.CODE AS BIGINT), 'SMABL',
                    deleted.SMABL, inserted.SMABL,
                    N'انتشار نرخ — سطح‌بندي BOM'
              INTO  dbo.CC_FormulaChange
                    (RunId, StepCode, FNUMB, ParentCode, ChildCode,
                     FieldName, OldValue, NewValue, Reason)
            FROM    dbo.DTL_MANF  d
            JOIN    dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
            JOIN    #C p  ON p.Code  = CAST(hm.CODE AS BIGINT) AND p.Llc = @lvl
            JOIN    #C ch ON ch.Code = CAST(d.CODE  AS BIGINT)
            WHERE   ABS(ISNULL(d.SMABL, 0) - (ch.Mat + ch.Wage + ch.Oh)) > 0.5;

            SET @totalChanges += @@ROWCOUNT;

            COMMIT;
        END

        ---- ۵-ب) بهاي هر فرمولِ اين سطح = مجموع اجزاي همان فرمول
        UPDATE  f
           SET  f.Mat = ISNULL(a.MatCost, 0)
        FROM    #F f
        JOIN    #C p ON p.Code = f.Code AND p.Llc = @lvl
        CROSS   APPLY (SELECT SUM(d.MEGHk * (ch.Mat + ch.Wage + ch.Oh)) AS MatCost
                       FROM   dbo.DTL_MANF d
                       JOIN   #C ch ON ch.Code = CAST(d.CODE AS BIGINT)
                       WHERE  d.FNUMB = f.FNUMB) a;

        ---- ۵-ج) بهاي «خودِ» کالا = ميانگين موزونِ همه‌ي فرمول‌هايش با
        ---- مقدار واقعيِ توليدشده (Qty)؛ بدون هيچ توليدي، ميانگين ساده.
        ---- وقتي Mat از گام ۴ (ميانگين واقعيِ انبار) تعيين شده، Wage/Oh
        ---- را هم از BOM نمي‌گيرد و صفر مي‌ماند — نه فقط Mat را دست
        ---- نمي‌زند: نرخ انباري از MABL_K واقعيِ ثبت‌شده مي‌آيد که همان
        ---- لحظه‌ي توليد (TAG=9) از قبل دستمزد/سربار را داخلش دارد (نگاه
        ---- کنید AverageRateRebuildService, case 9: produced = IMBIBE_MANF
        ---- + IMBIBE_SAR + SumOfMABLK). اگر اينجا دوباره w.Wage/w.Oh را
        ---- روي همان کد جمع بزنيم، دستمزد/سربار دوبار حساب مي‌شود — دقيقاً
        ---- همان چيزي که مغايرت ۷۷۱ را نصفه رفع کرده بود (Mat درست شد ولي
        ---- Wage هنوز از BOM اضافه مي‌آمد).
        UPDATE  c
           SET  c.Wage = CASE WHEN c.Mat <> 0 THEN 0 ELSE w.Wage END,
                c.Oh   = CASE WHEN c.Mat <> 0 THEN 0 ELSE w.Oh   END,
                c.Mat  = CASE WHEN c.Mat <> 0 THEN c.Mat ELSE w.Mat END
        FROM    #C c
        CROSS   APPLY (
                    SELECT
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Mat  * f.Qty) / SUM(f.Qty) ELSE AVG(f.Mat)  END AS Mat,
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Wage * f.Qty) / SUM(f.Qty) ELSE AVG(f.Wage) END AS Wage,
                        CASE WHEN SUM(f.Qty) > 0 THEN SUM(f.Oh   * f.Qty) / SUM(f.Qty) ELSE AVG(f.Oh)   END AS Oh
                    FROM #F f WHERE f.Code = c.Code
                ) w
        WHERE   c.Llc = @lvl AND c.HasFormula = 1;

        SET @lvl -= 1;
    END

    /* ─── ۶) ثبت نتيجه در CC_ItemCost ───
       FNUMB اينجا فقط براي نمايش در گزارش است؛ وقتي کالا چند فرمول همان
       ماه دارد، فرمولي که بيشترين مقدار واقعي زيرش توليد شده به‌عنوان
       نماينده انتخاب مي‌شود (بهاي واقعي همچنان ميانگين موزونِ همه است،
       نه فقط همين يکي). */
    DELETE dbo.CC_ItemCost WHERE RunId = @RunId;

    INSERT dbo.CC_ItemCost
        (RunId, PeriodMonth, Code, LowLevelCode, SourceKind, FNUMB,
         MaterialCost, WageCost, OverheadCost)
    SELECT  @RunId, @Month, c.Code, c.Llc, c.Src, rep.FNUMB, c.Mat, c.Wage, c.Oh
    FROM    #C c
    OUTER   APPLY (SELECT TOP 1 f.FNUMB FROM #F f WHERE f.Code = c.Code
                    ORDER BY f.Qty DESC, f.FNUMB DESC) rep;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message, ContextJson)
    VALUES (@RunId, 'S11', 1,
            CONCAT(N'انتشار نرخ: ', @totalChanges, N' نرخ به‌روز شد'),
            (SELECT MAX(Llc) AS maxLevel, COUNT(*) AS items,
                    SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END) AS noSource
             FROM #C FOR JSON PATH));

    /* ─── ۷) آزمون سلامت: CHK-09 بايد صفر شود ───
       Khod اينجا از خودِ #C خوانده مي‌شود (يعني همان بهاي موزوني که تازه
       محاسبه و منتشر شد)، نه دوباره از HEAD_MANF/DTL_MANF به تفکيک FNUMB —
       وگرنه هر فرمولِ «غيرمنتخب» يک کالاي چندفرمولي هميشه کاذب فلگ مي‌شد. */
    DELETE dbo.CC_Exception WHERE RunId = @RunId AND RuleCode = 'CHK-09';

    ;WITH DarValed AS (
        SELECT CAST(d.CODE AS BIGINT) AS Code, AVG(d.SMABL) AS Nerkh
        FROM   dbo.DTL_MANF d
        JOIN   dbo.HEAD_MANF hm ON hm.FNUMB = d.FNUMB AND hm.GHEYMAT = @Month
        GROUP BY CAST(d.CODE AS BIGINT)
    )
    INSERT dbo.CC_Exception
        (RunId, StepCode, RuleCode, ExType, Severity, Code, Amount, Description)
    SELECT  @RunId, 'S11', 'CHK-09', 14, 2, c.Code, (c.Mat + c.Wage + c.Oh) - v.Nerkh,
            N'نرخ پس از اجراي موتور هنوز منتشر نشده — نياز به بررسي'
    FROM    #C c
    JOIN    DarValed v ON v.Code = c.Code
    WHERE   c.HasFormula = 1
      AND   ABS((c.Mat + c.Wage + c.Oh) - v.Nerkh) / NULLIF((c.Mat + c.Wage + c.Oh), 0) > 0.001;

    /* ─── خلاصه ─── */
    SELECT  Llc                                          AS سطح,
            COUNT(*)                                     AS تعداد_کالا,
            SUM(CASE WHEN Src = 3 THEN 1 ELSE 0 END)     AS بدون_منبع_نرخ
    FROM    #C
    GROUP BY Llc ORDER BY Llc;

    SELECT  @totalChanges AS تعداد_نرخ_به‌روز_شده,
            (SELECT COUNT(*) FROM dbo.CC_Exception
             WHERE RunId = @RunId AND RuleCode = 'CHK-09' AND IsResolved = 0)
                          AS نرخ_منتشر_نشده_باقيمانده;
END
GO


PRINT N'موتور نرخ توليدي (S10 و S11) ايجاد شد.';

/* نمونه:
   EXEC dbo.CC_sp_S10_BalanceConversion @RunId=1, @Month=5,
                                        @DT1=14050501, @DT2=14050531, @WhatIf=1;
   EXEC dbo.CC_sp_S11_PropagateRates    @RunId=1, @Month=5,
                                        @DT1=14050501, @DT2=14050531, @WhatIf=1;
*/
GO
