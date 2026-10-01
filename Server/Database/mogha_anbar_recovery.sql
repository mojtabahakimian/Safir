-- Final MOGHA_ANBAR definition, identical to ScriptSqly.Main.cs. No DROP.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER FUNCTION [dbo].[MOGHA_ANBAR] (@dt2 INT, @ANBAR INT, @KOL INT)
RETURNS TABLE
AS
RETURN (
    WITH
    avl_sub AS (
        -- موجودی اولیه
        SELECT CODE, SUM(MOGODI_A) AS MEG, SUM(MABL_A) AS SumOfMABL_A, ANBAR
        FROM dbo.STUF_FSK
        GROUP BY CODE, ANBAR
        HAVING ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- خرید، برگشت فروش، تولید، سایر ورودی (TAG 1,7,9,24)
        SELECT i.CODE, SUM(i.MEGHk), SUM(i.MABL_K), i.ANBAR
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG IN (1, 7, 9, 24) AND h.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- ایجاد موجودی (TAG 22)
        SELECT i.CODE, SUM(i.MEGH_MAR), SUM(i.MABL * i.MEGH_MAR), i.ANBAR
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG = 22 AND h.DATE_N <= @dt2 AND i.MEGH_MAR <> 0
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- ورودی از انتقال (TAG 5 - انبار مقصد)
        SELECT i.CODE, SUM(i.MEGHk), SUM(i.MABL_K), i.ANBARF
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG = 5 AND h.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBARF
        HAVING i.ANBARF LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- انبارگردانی (ورودی)
        SELECT l.CODE, SUM((l.MOG - l.NUM3) * -1), SUM(ABS(l.MOG - l.NUM3) * l.MABL), a.GRD_ANBAR
        FROM dbo.ANBGRD_LST l INNER JOIN dbo.ANBGRD_HEAD a ON l.GRD_NUM = a.GRD_NUM
        WHERE a.GRD_DATE <= @dt2 AND a.N_S IS NOT NULL
              AND a.GRD_ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))
        GROUP BY l.CODE, a.GRD_ANBAR
        HAVING SUM((l.MOG - l.NUM3) * -1) >= 0

        UNION ALL

        -- برگشت فروش (TAG مجازی 4): کالا از مشتری به انبار برمی‌گردد (ورودی)
        SELECT i.CODE, SUM(i.MEGH_MAR), SUM(i.MABL * i.MEGH_MAR), i.ANBAR
        FROM dbo.BACK_HEAD bh
             INNER JOIN dbo.INVO_LST i ON bh.ta = i.TAG AND bh.NUMBER1 = i.NUMBER
        WHERE bh.ta + 2 = 4 AND i.MEGH_MAR <> 0 AND bh.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))
    ),
    avl AS (
        SELECT CODE, SUM(NULLIF(MEG, 0)) AS SMEGH, SUM(SumOfMABL_A) AS SMABLA, ANBAR
        FROM avl_sub
        GROUP BY CODE, ANBAR
    ),
    fr_sub AS (
        -- فروش، انتقال، برگشت خرید، سایر خروجی (TAG 2,5,8,10,11,26)
        SELECT i.CODE, SUM(i.MEGHk) AS MEG, i.ANBAR
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG IN (2, 5, 8, 10, 11, 26) AND h.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- انبارگردانی (خروجی)
        SELECT l.CODE, SUM(l.MOG - l.NUM3), a.GRD_ANBAR
        FROM dbo.ANBGRD_LST l INNER JOIN dbo.ANBGRD_HEAD a ON l.GRD_NUM = a.GRD_NUM
        WHERE a.GRD_DATE <= @dt2 AND a.N_S IS NOT NULL
              AND a.GRD_ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))
        GROUP BY l.CODE, a.GRD_ANBAR
        HAVING SUM(l.MOG - l.NUM3) > 0

        UNION ALL

        -- تعمیر (TAG 20)
        SELECT i.CODE, SUM(i.MEGHK), i.ANBAR
        FROM dbo.HEAD_LST h INNER JOIN dbo.INVO_LST i ON h.TAG = i.TAG AND h.NUMBER = i.NUMBER
        WHERE i.TAG = 20 AND h.DATE_N <= @dt2 AND (h.TAMIR = 1 OR h.TAMIR = 4)
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))

        UNION ALL

        -- برگشت خرید (TAG مجازی 3): کالا به تأمین‌کننده برمی‌گردد (خروجی)
        SELECT i.CODE, SUM(i.MEGH_MAR) AS MEG, i.ANBAR
        FROM dbo.BACK_HEAD bh
             INNER JOIN dbo.INVO_LST i ON bh.ta = i.TAG AND bh.NUMBER1 = i.NUMBER
        WHERE bh.ta + 2 = 3 AND i.MEGH_MAR <> 0 AND bh.DATE_N <= @dt2
        GROUP BY i.CODE, i.ANBAR
        HAVING i.ANBAR LIKE CAST(@ANBAR AS NVARCHAR(10))
    ),
    fr AS (
        SELECT CODE, SUM(MEG) AS MEG, ANBAR
        FROM fr_sub
        GROUP BY CODE, ANBAR
    ),
    -- مرتب‌سازی مطابق کارت انبار: DATE_N، BARGAH (از TAGCOD)، NUMBER
    lastav_base AS (
        SELECT i.CODE, i.ANBAR, i.AVRAGE AS AVRAGE, h.DATE_N, t.BARGAH, i.NUMBER, i.ID
        FROM dbo.INVO_LST i
             INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
             INNER JOIN dbo.TAGCOD t ON i.TAG = t.CODE
        WHERE h.DATE_N <= @dt2 AND i.TAG IN (1, 7, 9, 24)

        UNION ALL

        -- وارده از انتقال (ANBARF = انبار مقصد)
        SELECT i.CODE, i.ANBARF, i.AVRAGE2, h.DATE_N, t.BARGAH, i.NUMBER, i.ID
        FROM dbo.INVO_LST i
             INNER JOIN dbo.HEAD_LST h ON i.NUMBER = h.NUMBER AND i.TAG = h.TAG
             INNER JOIN dbo.TAGCOD t ON i.TAG = t.CODE
        WHERE h.DATE_N <= @dt2 AND i.TAG = 5
    ),
    -- وقتی یک سند یک کالا را در چند ردیف با نرخ‌های متفاوت ثبت کرده
    -- (مثلاً دو محموله‌ی هم‌روز با نرخ فرق)، ردیف‌ها در
    -- (DATE_N,BARGAH,NUMBER) کاملاً هم‌تراز می‌شوند و بدون تای‌برک نهایی،
    -- ROW_NUMBER بین اجراهای مختلف می‌تواند هرکدام را انتخاب کند —
    -- نتیجه‌ی MABLK بدون هیچ تغییری در داده، بین دو اجرای پشت‌سرهم عوض
    -- می‌شد. id DESC (آخرین ردیفی که نوشته شده) درست است: نرخِ روی id
    -- بزرگ‌تر همان نرخی است که اسناد *بعدی* (AVRAGE2شان) واقعاً استفاده
    -- کرده‌اند — یعنی موتور نرخ میانگین بعد از پردازش هر دو ردیف همین
    -- سند، روی همین عدد نهایی نشسته. تأیید شد: با این تای‌برک، MABLK
    -- کد ۳۰۹۲/انبار۳ دقیقاً با مانده‌ی حسابداری برابر شد (تا ریال).
    lastav AS (
        SELECT CODE, ANBAR, AVRAGE,
               ROW_NUMBER() OVER (PARTITION BY CODE, ANBAR ORDER BY DATE_N DESC, BARGAH DESC, NUMBER DESC, ID DESC) AS rn
        FROM lastav_base
    ),
    kart_anbar AS (
        SELECT
            sf.CODE,
            sf.ANBAR,
            ROUND(ISNULL(ISNULL(avl.SMEGH, 0) - ISNULL(fr.MEG, 0), 0), 2) AS MAND,
            ISNULL(
                COALESCE(la.AVRAGE, sf.FI_A, 0) *
                ROUND(ISNULL(ISNULL(avl.SMEGH, 0) - ISNULL(fr.MEG, 0), 0), 2),
                0
            ) AS MABLK
        FROM dbo.STUF_FSK sf
        INNER JOIN avl ON sf.CODE = avl.CODE AND sf.ANBAR = avl.ANBAR
        LEFT  JOIN fr  ON sf.CODE = fr.CODE  AND sf.ANBAR = fr.ANBAR
        LEFT  JOIN (SELECT CODE, ANBAR, AVRAGE FROM lastav WHERE rn = 1) la
               ON sf.CODE = la.CODE AND sf.ANBAR = la.ANBAR
        WHERE sf.ANBAR = @ANBAR
    ),
    hesab AS (
        SELECT d.HES_K, d.HES_M, SUM(d.BED - d.BES) AS mand, d.HES_T, d.HES
        FROM dbo.DEED_DTL d INNER JOIN dbo.DEED_HED h ON d.N_S = h.N_S
        WHERE h.DATE_S <= @dt2 AND d.HES_K = @KOL AND d.HES_M = @ANBAR
        GROUP BY d.HES_K, d.HES_M, d.HES_T, d.HES
    )
    SELECT
        ka.CODE,
        ROUND(ka.MABLK, 0)                                                             AS MABLK,
        ka.MAND,
        ISNULL(he.mand, 0)                                                             AS mab,
        CASE WHEN (ka.MABLK - ISNULL(he.mand, 0)) > 0
             THEN ROUND(ka.MABLK - ISNULL(he.mand, 0), 0)
             ELSE 0 END                                                                AS tafBED,
        CASE WHEN (ka.MABLK - ISNULL(he.mand, 0)) <= 0
             THEN ROUND(ka.MABLK - ISNULL(he.mand, 0), 0) * -1
             ELSE 0 END                                                                AS TAFBES,
        he.HES_T,
        he.HES_K,
        he.HES_M,
        he.HES
    FROM kart_anbar ka
    LEFT JOIN hesab he ON ka.CODE = he.HES_T
);
GO
