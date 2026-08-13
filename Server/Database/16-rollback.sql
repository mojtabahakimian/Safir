/* ═══════════════════════════════════════════════════════════════════
   بازگردانی از اسنپ‌شات

   هر گام نویسنده پیش از اجرا اسنپ‌شات می‌گیرد. این رویه آن را
   برمی‌گرداند و اجرا را به وضعیت «بازگردانی‌شده» می‌برد.

   بدون این، اجرای موتور روی داده واقعی ریسک دارد.

   نکته: عمداً هیچ «USE <database>» اینجا نیست — نام پایگاه در هر
   نصب فرق می‌کند. اسکریپت را روی پایگاه هدف اجرا کنید.
   ═══════════════════════════════════════════════════════════════════ */

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE dbo.CC_sp_Rollback
    @RunId    INT,
    @StepCode VARCHAR(10) = NULL,   -- خالي = آخرين اسنپ‌شات هر جدول
    @UserName NVARCHAR(50) = N'system',
    @WhatIf   BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    ---- اسنپ‌شات‌هاي قابل استفاده
    IF OBJECT_ID('tempdb..#Snap') IS NOT NULL DROP TABLE #Snap;

    SELECT  s.SnapshotId, s.TableName, s.BackupTable, s.RowsCopied, s.StepCode
    INTO    #Snap
    FROM    dbo.CC_Snapshot s
    JOIN   (SELECT TableName, MAX(SnapshotId) AS Id
            FROM   dbo.CC_Snapshot
            WHERE  RunId = @RunId
              AND (@StepCode IS NULL OR StepCode = @StepCode)
              AND  RestoredAtUtc IS NULL
            GROUP BY TableName) x ON x.Id = s.SnapshotId;

    IF NOT EXISTS (SELECT 1 FROM #Snap)
    BEGIN
        SELECT N'اسنپ‌شات قابل بازگرداني يافت نشد' AS پيام;
        RETURN;
    END

    ---- بررسي وجود واقعي جداول پشتيبان
    DECLARE @missing NVARCHAR(MAX) = NULL;

    SELECT  @missing = STRING_AGG(BackupTable, ', ')
    FROM    #Snap
    WHERE   OBJECT_ID('dbo.' + BackupTable, 'U') IS NULL;

    IF @missing IS NOT NULL
    BEGIN
        RAISERROR(N'جدول پشتيبان يافت نشد: %s', 16, 1, @missing);
        RETURN;
    END

    IF @WhatIf = 1
    BEGIN
        SELECT  TableName   AS جدول,
                BackupTable AS جدول_پشتيبان,
                RowsCopied  AS تعداد_سطر,
                StepCode    AS گام
        FROM    #Snap ORDER BY TableName;

        SELECT N'حالت گزارش — چيزي بازگردانده نشد' AS وضعيت;
        RETURN;
    END

    BEGIN TRAN;

    DECLARE @tbl SYSNAME, @bak SYSNAME, @sql NVARCHAR(MAX), @n INT = 0;

    DECLARE cSnap CURSOR LOCAL FAST_FORWARD FOR
        SELECT TableName, BackupTable FROM #Snap;

    OPEN cSnap;
    FETCH NEXT FROM cSnap INTO @tbl, @bak;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @tbl = 'DTL_MANF'
        BEGIN
            SET @sql = N'
                UPDATE  d
                   SET  d.SMABL = b.SMABL,
                        d.MABLK = b.MABLK,
                        d.MEGHk = b.MEGHk,
                        d.PERT  = b.PERT
                FROM    dbo.DTL_MANF d
                JOIN    dbo.' + QUOTENAME(@bak) + N' b
                        ON b.FNUMB = d.FNUMB AND b.CODE = d.CODE';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;
        END
        ELSE IF @tbl = 'HEAD_MANF'
        BEGIN
            SET @sql = N'
                UPDATE  h
                   SET  h.IMBIBE_MANF = b.IMBIBE_MANF,
                        h.IMBIBE_SAR  = b.IMBIBE_SAR
                FROM    dbo.HEAD_MANF h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.FNUMB = h.FNUMB';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;
        END
        ELSE IF @tbl = 'DEED_HED'
        BEGIN
            -- بازگرداني شماره اسناد و اسناد حذف‌شده؛ ۹ جدول فرزند خودکار دنبال
            -- مي‌آيند. سه مرحله، به همان دليلي که CC_sp_S04_SortDeeds دو-مرحله‌اي
            -- است: اگر شمارهٔ اصليِ يک سند برابر شمارهٔ فعليِ سند ديگري باشد که
            -- هنوز به حالت اصلي‌اش برنگشته، UPDATE يا INSERT مستقيم به
            -- PRIMARY KEY تکراري مي‌خورد.
            EXEC sp_set_session_context @key = N'cc_bulk', @value = 1;

            -- ۱) هر سندي که شماره‌اش فرق کرده را به يک بازهٔ منفيِ ناهم‌پوشان
            --    مي‌بريم تا شمارهٔ اصلي‌اش براي درج سندهاي حذف‌شده (مرحلهٔ ۲) و
            --    بازگرداني خودش (مرحلهٔ ۳) آزاد و بدون برخورد باشد.
            SET @sql = N'
                UPDATE  h
                   SET  h.N_S = -3000000.0 - h.N_S
                FROM    dbo.DEED_HED h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.base = h.base
                WHERE   h.N_S <> b.N_S';
            EXEC sp_executesql @sql;

            -- ۲) سندهايي که CC_sp_S03_DeleteEmptyDeeds کامل حذف کرده بود را با
            --    همان base و همان مقادير همهٔ ستون‌ها دوباره درج مي‌کنيم. امن
            --    است چون مرحلهٔ ۱ هر شمارهٔ زندهٔ همپوشان را قبلاً کنار زده.
            SET @sql = N'
                SET IDENTITY_INSERT dbo.DEED_HED ON;
                INSERT INTO dbo.DEED_HED
                    (N_S, DATE_S, SHARH_S, NO_S, ANBAR, N_FACTOR, GHATEI, USER_NAME,
                     base, SGN1, SGN2, SGN3, SGN4, OKF, sgn1usid, sgn2usid, sgn3usid,
                     CRT, UID, BAYEG)
                SELECT b.N_S, b.DATE_S, b.SHARH_S, b.NO_S, b.ANBAR, b.N_FACTOR, b.GHATEI,
                       b.USER_NAME, b.base, b.SGN1, b.SGN2, b.SGN3, b.SGN4, b.OKF,
                       b.sgn1usid, b.sgn2usid, b.sgn3usid, b.CRT, b.UID, b.BAYEG
                FROM   dbo.' + QUOTENAME(@bak) + N' b
                WHERE  NOT EXISTS (SELECT 1 FROM dbo.DEED_HED h WHERE h.base = b.base);
                SET IDENTITY_INSERT dbo.DEED_HED OFF;';
            EXEC sp_executesql @sql;

            -- ۳) سندهاي مرحلهٔ ۱ را از بازهٔ منفي به شمارهٔ اصلي‌شان برمي‌گردانيم.
            SET @sql = N'
                UPDATE  h
                   SET  h.N_S = b.N_S
                FROM    dbo.DEED_HED h
                JOIN    dbo.' + QUOTENAME(@bak) + N' b ON b.base = h.base
                WHERE   h.N_S < 0';
            EXEC sp_executesql @sql;
            SET @n += @@ROWCOUNT;

            EXEC sp_set_session_context @key = N'cc_bulk', @value = 0;
        END

        FETCH NEXT FROM cSnap INTO @tbl, @bak;
    END

    CLOSE cSnap;
    DEALLOCATE cSnap;

    ---- علامت‌گذاري اسنپ‌شات‌ها
    UPDATE  s
       SET  s.RestoredAtUtc = SYSUTCDATETIME()
    FROM    dbo.CC_Snapshot s
    JOIN    #Snap t ON t.SnapshotId = s.SnapshotId;

    ---- تغييرات ثبت‌شده باطل مي‌شوند
    DELETE dbo.CC_FormulaChange
    WHERE  RunId = @RunId
      AND (@StepCode IS NULL OR StepCode = @StepCode);

    ---- وضعيت اجرا
    UPDATE dbo.CC_Run
       SET Status = 5,                       -- بازگردانی‌شده
           FormulasDirty = 0,
           FinishedAtUtc = SYSUTCDATETIME()
     WHERE RunId = @RunId;

    INSERT dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    VALUES (@RunId, @StepCode, 2,
            CONCAT(N'بازگرداني توسط ', @UserName, N': ', @n, N' سطر'));

    COMMIT;

    SELECT @n AS تعداد_سطر_بازگردانده_شده;
END
GO


/* ═══════════════════════════════════════════════════════════════════
   پاکسازی اسنپ‌شات‌های قدیمی
   جداول CC_BAK_* بعد از ۹۰ روز حذف می‌شوند.
   ═══════════════════════════════════════════════════════════════════ */
CREATE OR ALTER PROCEDURE dbo.CC_sp_PurgeSnapshots
    @OlderThanDays INT = 90,
    @WhatIf        BIT = 1
AS
BEGIN
    SET NOCOUNT ON;

    IF OBJECT_ID('tempdb..#Old') IS NOT NULL DROP TABLE #Old;

    SELECT  SnapshotId, BackupTable, TakenAtUtc
    INTO    #Old
    FROM    dbo.CC_Snapshot
    WHERE   TakenAtUtc < DATEADD(DAY, -@OlderThanDays, SYSUTCDATETIME());

    IF @WhatIf = 1
    BEGIN
        SELECT BackupTable AS جدول, TakenAtUtc AS تاريخ FROM #Old;
        SELECT COUNT(*) AS تعداد_قابل_حذف FROM #Old;
        RETURN;
    END

    DECLARE @bak SYSNAME;
    DECLARE cOld CURSOR LOCAL FAST_FORWARD FOR SELECT BackupTable FROM #Old;

    OPEN cOld;
    FETCH NEXT FROM cOld INTO @bak;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF OBJECT_ID('dbo.' + @bak, 'U') IS NOT NULL
            EXEC('DROP TABLE dbo.' + @bak);

        FETCH NEXT FROM cOld INTO @bak;
    END

    CLOSE cOld;
    DEALLOCATE cOld;

    DELETE s FROM dbo.CC_Snapshot s JOIN #Old o ON o.SnapshotId = s.SnapshotId;

    SELECT COUNT(*) AS تعداد_حذف_شده FROM #Old;
END
GO

PRINT N'رويه‌هاي بازگرداني و پاکسازي ايجاد شدند.';
GO
