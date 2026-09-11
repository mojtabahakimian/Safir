/* ═══════════════════════════════════════════════════════════════════
   ضربانِ اجرا — تشخیصِ «در حال اجرا» از «یخ‌زده»

   ── مسئله ──
   صفِ کارها در حافظه‌ی پروسه است. اگر سرور وسط یک اجرا ری‌استارت شود
   (دیپلوی، ری‌سایکل app pool، یا کرش)، صف از بین می‌رود ولی
   CC_Run.Status روی ۱ («در حال اجرا») می‌ماند — برای همیشه.

   کاربر ساعت‌ها بعد صفحه را باز می‌کند و «در حال اجرا» می‌بیند، در
   حالی که هیچ‌چیز اجرا نمی‌شود. تنها راهِ فهمیدنش این بود که تاریخِ
   آخرین خطِ CC_RunLog را نگاه کند و خودش نتیجه بگیرد — کاری که نباید
   از کاربر خواست.

   ── چرا StartedAtUtc کافی نیست ──
   می‌گوید کِی شروع شد، نه اینکه هنوز زنده است. یک اجرای سالمِ
   چهل‌دقیقه‌ای و یک اجرای مرده‌ی چهل‌دقیقه‌پیش، از دیدِ آن ستون یکی‌اند.

   ── راه‌حل ──
   ارکستریتور در هر مرزِ گام این ستون را تازه می‌کند. آن‌وقت:
     • صفحه می‌تواند بگوید «ده دقیقه است خبری نیست»
     • و موقع بالا آمدن سرور، هر اجرای Runningی که ضربانش کهنه است
       «متوقف‌شده» علامت می‌خورد تا کاربر بتواند ادامه‌اش دهد
   ═══════════════════════════════════════════════════════════════════ */

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.CC_Run', 'LastHeartbeatUtc') IS NULL
BEGIN
    ALTER TABLE dbo.CC_Run ADD LastHeartbeatUtc DATETIME2 NULL;
    PRINT N'ستون LastHeartbeatUtc به CC_Run اضافه شد.';
END
GO

/* ───────────────────────────────────────────────────────────────────
   آزادسازیِ اجراهای یخ‌زده.

   @StaleMinutes: چند دقیقه سکوت یعنی مرده. پیش‌فرض ۱۵ — بلندترین
   گامِ واقعی (بازسازی نرخ میانگین روی کل تاریخچه) حدود دو تا سه دقیقه
   است و در طول همان هم ضربان می‌زند، پس ۱۵ دقیقه سکوت یعنی پروسه رفته.

   به Paused می‌رود نه Failed: داده‌ای که تا آن لحظه نوشته شده معتبر
   است و کاربر می‌تواند «ادامه اجرا» را بزند. Failed یعنی محاسبه غلط
   بوده، که اینجا صادق نیست.

   ⚠️ ضربانِ NULL هم کهنه شمرده می‌شود، ولی فقط وقتی خودِ اجرا از
   @StaleMinutes قدیمی‌تر باشد — وگرنه اجرایی که همین الان شروع شده و
   هنوز اولین ضربانش را نزده، اشتباهی متوقف می‌شد.
   ─────────────────────────────────────────────────────────────────── */
CREATE OR ALTER PROCEDURE dbo.CC_sp_ReleaseStaleRuns
    @StaleMinutes INT = 15
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @freed TABLE (RunId INT, Silent INT);

    UPDATE  r
       SET  r.Status = 2,                    -- Paused
            r.Note   = LEFT(ISNULL(r.Note + N' | ', N'')
                       + N'به‌صورت خودکار متوقف شد: '
                       + CAST(DATEDIFF(MINUTE,
                              ISNULL(r.LastHeartbeatUtc, r.StartedAtUtc),
                              SYSUTCDATETIME()) AS NVARCHAR(10))
                       + N' دقیقه بی‌ضربان (سرور احتمالاً ری‌استارت شده).', 500)
    OUTPUT  inserted.RunId,
            DATEDIFF(MINUTE, ISNULL(deleted.LastHeartbeatUtc, deleted.StartedAtUtc),
                     SYSUTCDATETIME())
    INTO    @freed
    FROM    dbo.CC_Run r
    WHERE   r.Status = 1                     -- Running
      AND   DATEDIFF(MINUTE,
                     ISNULL(r.LastHeartbeatUtc, r.StartedAtUtc),
                     SYSUTCDATETIME()) >= @StaleMinutes;

    INSERT  dbo.CC_RunLog (RunId, StepCode, Severity, Message)
    SELECT  f.RunId, NULL, 2,
            CONCAT(N'اجرا ', f.Silent, N' دقیقه هیچ نشانه‌ی حیاتی نداشت و ',
                   N'به «متوقف‌شده» تغییر کرد. سرور احتمالاً وسط کار ری‌استارت ',
                   N'شده است؛ با «ادامه اجرا» می‌توانید از همین‌جا ادامه دهید.')
    FROM    @freed f;

    SELECT RunId, Silent AS SilentMinutes FROM @freed;
END
GO

PRINT N'ضربانِ اجرا و رویه‌ی CC_sp_ReleaseStaleRuns آماده شد.';
GO
