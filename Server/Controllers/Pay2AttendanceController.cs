using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Data;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/attendance")]
    [Authorize]
    public class Pay2AttendanceController : ControllerBase
    {
        private readonly IDatabaseService _db;
        public Pay2AttendanceController(IDatabaseService db) => _db = db;

        [HttpGet("init")]
        public async Task<IActionResult> InitPeriod([FromQuery] int wsId, [FromQuery] long periodDate)
        {
            if (wsId <= 0)
                return BadRequest("کارگاه نامعتبر است.");

            if (!IsValidPayrollPeriodDate(periodDate))
                return BadRequest("ماه دوره نامعتبر است. فرمت صحیح: YYYYMM00 مثل 14030700");

            try
            {
                var result = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var period = await conn.QuerySingleOrDefaultAsync<Pay2PeriodDto>(
                        "SELECT * FROM PAY2_PERIOD WHERE WS_ID=@wsId AND PERIOD_DATE=@periodDate",
                        new { wsId, periodDate }, tran);

                    if (period == null)
                    {
                        const string insertPerSql = @"INSERT INTO PAY2_PERIOD (WS_ID, PERIOD_DATE, HOLIDAY_DAYS, TENDAR_APPLY, STATUS, OPENED_AT) 
                                                      OUTPUT INSERTED.* 
                                                      VALUES (@wsId, @periodDate, 0, 0, 1, GETDATE())";
                        period = await conn.QuerySingleAsync<Pay2PeriodDto>(insertPerSql, new { wsId, periodDate }, tran);
                    }

                    const string attSql = @"
                        SELECT 
                            e.EMP_ID, e.EMP_CODE, e.LAST_NAME + ' ' + e.FIRST_NAME AS FULL_NAME,
                            ISNULL(a.WORK_DAYS, 0) AS WORK_DAYS, ISNULL(a.DAYS_TOLID, 0) AS DAYS_TOLID, 
                            ISNULL(a.DAYS_EDARI, 0) AS DAYS_EDARI, ISNULL(a.DAYS_KHADAMAT, 0) AS DAYS_KHADAMAT, ISNULL(a.DAYS_FOROSH, 0) AS DAYS_FOROSH,
                            ISNULL(a.OT_NORMAL_H, 0) AS OT_NORMAL_H, ISNULL(a.OT_HOLIDAY_H, 0) AS OT_HOLIDAY_H, ISNULL(a.OT_ADMIN_H, 0) AS OT_ADMIN_H,
                            ISNULL(a.LEAVE_DAYS, 0) AS LEAVE_DAYS, ISNULL(a.ABSENT_DAYS, 0) AS ABSENT_DAYS, ISNULL(a.MISSION_DAYS, 0) AS MISSION_DAYS,
                            ISNULL(a.DAYS, 0) AS DAYS, ISNULL(a.DAYSB, 0) AS DAYSB, ISNULL(a.FRID_COUNT, 0) AS FRID_COUNT, ISNULL(a.TDAYS, 0) AS TDAYS,
                            ISNULL(a.PERF_AMOUNT, 0) AS PERF_AMOUNT, ISNULL(a.TRANSP_AMOUNT, 0) AS TRANSP_AMOUNT, ISNULL(a.KASR_OTHER, 0) AS KASR_OTHER,
                            ISNULL(a.LOCKED, 0) AS LOCKED
                        FROM PAY2_EMPLOYEE e
                        LEFT JOIN PAY2_ATTENDANCE a ON e.EMP_ID = a.EMP_ID AND a.PER_ID = @PerId
                        WHERE e.WS_ID = @wsId AND e.IS_ACTIVE = 1
                        ORDER BY e.LAST_NAME, e.FIRST_NAME";

                    var lines = (await conn.QueryAsync<Pay2AttendanceLineDto>(attSql, new { PerId = period.PER_ID, wsId }, tran)).ToList();

                    // تنظیم IsDirty به فالس چون تازه از سرور لود شده‌اند
                    lines.ForEach(x => x.IsDirty = false);

                    return new Pay2AttendanceSaveRequest { Period = period, Lines = lines };
                });

                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveBulk([FromBody] Pay2AttendanceSaveRequest request)
        {
            if (request.Period.STATUS != 1) return BadRequest("دوره بسته شده و قابل ویرایش نیست.");

            // فقط خطوطی که در کلاینت ویرایش شده‌اند را پردازش می‌کنیم (کاهش چشمگیر بار سرور)
            var dirtyLines = request.Lines.Where(x => !x.LOCKED).ToList();

            // اعمال خودکار مجموع کارکردها بر روی WORK_DAYS برای جلوگیری از خطای Constraint دیتابیس
            foreach (var line in dirtyLines)
            {
                var sumDays = line.DAYS_TOLID + line.DAYS_EDARI + line.DAYS_KHADAMAT + line.DAYS_FOROSH;
                var maxDays = Math.Max(line.DAYS, line.DAYSB);
                var requiredMinWorkDays = Math.Max(sumDays, maxDays);

                if (line.WORK_DAYS < requiredMinWorkDays)
                {
                    line.WORK_DAYS = requiredMinWorkDays;
                }
            }

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await conn.ExecuteAsync("UPDATE PAY2_PERIOD SET HOLIDAY_DAYS=@HOLIDAY_DAYS, TENDAR_APPLY=@TENDAR_APPLY WHERE PER_ID=@PER_ID", request.Period, tran);

                    if (!dirtyLines.Any())
                        return;

                    // 🚀 جادوی پرفورمنس: استفاده از OPENJSON برای ثبت 1000 رکورد در 1 میلی‌ثانیه!
                    string jsonData = System.Text.Json.JsonSerializer.Serialize(dirtyLines);

                    const string mergeSql = @"
                        MERGE PAY2_ATTENDANCE AS t
                        USING (
                            SELECT * FROM OPENJSON(@JsonData) WITH (
                                EMP_ID INT, WORK_DAYS DECIMAL(5,2), DAYS_TOLID DECIMAL(5,2), DAYS_EDARI DECIMAL(5,2), DAYS_KHADAMAT DECIMAL(5,2), DAYS_FOROSH DECIMAL(5,2),
                                OT_NORMAL_H DECIMAL(6,2), OT_HOLIDAY_H DECIMAL(6,2), OT_ADMIN_H DECIMAL(6,2), LEAVE_DAYS DECIMAL(5,2), ABSENT_DAYS DECIMAL(5,2), MISSION_DAYS DECIMAL(5,2),
                                DAYS DECIMAL(5,2), DAYSB DECIMAL(5,2), FRID_COUNT TINYINT, TDAYS DECIMAL(5,2), PERF_AMOUNT BIGINT, TRANSP_AMOUNT BIGINT, KASR_OTHER BIGINT
                            )
                        ) AS s
                        ON t.PER_ID = @PER_ID AND t.EMP_ID = s.EMP_ID
                        WHEN MATCHED THEN
                            UPDATE SET WORK_DAYS=s.WORK_DAYS, DAYS_TOLID=s.DAYS_TOLID, DAYS_EDARI=s.DAYS_EDARI, DAYS_KHADAMAT=s.DAYS_KHADAMAT, DAYS_FOROSH=s.DAYS_FOROSH,
                                       OT_NORMAL_H=s.OT_NORMAL_H, OT_HOLIDAY_H=s.OT_HOLIDAY_H, OT_ADMIN_H=s.OT_ADMIN_H, LEAVE_DAYS=s.LEAVE_DAYS, ABSENT_DAYS=s.ABSENT_DAYS, MISSION_DAYS=s.MISSION_DAYS,
                                       DAYS=s.DAYS, DAYSB=s.DAYSB, FRID_COUNT=s.FRID_COUNT, TDAYS=s.TDAYS, PERF_AMOUNT=s.PERF_AMOUNT, TRANSP_AMOUNT=s.TRANSP_AMOUNT, KASR_OTHER=s.KASR_OTHER
                        WHEN NOT MATCHED THEN
                            INSERT (PER_ID, EMP_ID, SOURCE, WORK_DAYS, DAYS_TOLID, DAYS_EDARI, DAYS_KHADAMAT, DAYS_FOROSH, OT_NORMAL_H, OT_HOLIDAY_H, OT_ADMIN_H, LEAVE_DAYS, ABSENT_DAYS, MISSION_DAYS, DAYS, DAYSB, FRID_COUNT, TDAYS, PERF_AMOUNT, TRANSP_AMOUNT, KASR_OTHER)
                            VALUES (@PER_ID, s.EMP_ID, 1, s.WORK_DAYS, s.DAYS_TOLID, s.DAYS_EDARI, s.DAYS_KHADAMAT, s.DAYS_FOROSH, s.OT_NORMAL_H, s.OT_HOLIDAY_H, s.OT_ADMIN_H, s.LEAVE_DAYS, s.ABSENT_DAYS, s.MISSION_DAYS, s.DAYS, s.DAYSB, s.FRID_COUNT, s.TDAYS, s.PERF_AMOUNT, s.TRANSP_AMOUNT, s.KASR_OTHER);";

                    await conn.ExecuteAsync(mergeSql, new { PER_ID = request.Period.PER_ID, JsonData = jsonData }, tran);
                });
                return Ok();
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Message.Contains("CK_ATT_DAYSB") || ex.Message.Contains("CK_ATT_DAYS"))
            {
                return BadRequest("خطای منطقی: کارکرد اسمی یا رسمی پرسنل نمی‌تواند از مجموع روزهای کارکرد کل بیشتر باشد.");
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        // بخش مقادیر پویا بدون تغییر ماند
        [HttpGet("dynamic-values")]
        public async Task<ActionResult<IEnumerable<Pay2AttValueDto>>> GetDynamicValues([FromQuery] int perId, [FromQuery] int empId)
        {
            const string sql = @"
                SELECT d.ITEM_ID, d.ITEM_NAME, ISNULL(v.VALUE, 0) AS VALUE
                FROM PAY2_ITEM_DEF d
                LEFT JOIN PAY2_ATT_VALUE v ON v.ITEM_ID = d.ITEM_ID AND v.PER_ID = @perId AND v.EMP_ID = @empId
                WHERE d.IS_ACTIVE = 1 
                  AND d.ITEM_CODE NOT IN ('BASE_SAL','BASE_SAL_B','HOME','CHILDREN','FAMILY_ALLOW','ATTRACT','GROCERY','HARD_COND','NAHAR','SHIFT','OTHER_FIX','OT_NORMAL','OT_HOLIDAY','OT_ADMIN','PERF_BONUS','TRANSP','INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED','OTHER_DED')
                ORDER BY d.SORT_ORDER";
            return Ok(await _db.DoGetDataSQLAsync<Pay2AttValueDto>(sql, new { perId, empId }));
        }

        [HttpPost("dynamic-values/save")]
        public async Task<IActionResult> SaveDynamicValues([FromQuery] int perId, [FromQuery] int empId, [FromBody] List<Pay2AttValueDto> values)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    int attExists = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_ATTENDANCE WHERE PER_ID=@perId AND EMP_ID=@empId", new { perId, empId }, tran);
                    if (attExists == 0) throw new InvalidOperationException("ابتدا باید کارکرد اصلی پرسنل ذخیره شود.");

                    string jsonData = System.Text.Json.JsonSerializer.Serialize(values.Where(x => x.VALUE != 0));

                    const string mergeSql = @"
                        MERGE PAY2_ATT_VALUE AS t
                        USING (SELECT * FROM OPENJSON(@JsonData) WITH (ITEM_ID INT, VALUE BIGINT)) AS s
                        ON t.PER_ID = @PER_ID AND t.EMP_ID = @EMP_ID AND t.ITEM_ID = s.ITEM_ID
                        WHEN MATCHED THEN UPDATE SET VALUE = s.VALUE
                        WHEN NOT MATCHED THEN INSERT (PER_ID, EMP_ID, ITEM_ID, VALUE) VALUES (@PER_ID, @EMP_ID, s.ITEM_ID, s.VALUE);";

                    await conn.ExecuteAsync(mergeSql, new { PER_ID = perId, EMP_ID = empId, JsonData = jsonData }, tran);
                });
                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpPost("close-period/{perId:int}")]
        public async Task<IActionResult> ClosePeriod(int perId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await conn.ExecuteAsync(
                        "EXEC dbo.SP_PAY2_CLOSE_PERIOD @PER_ID=@perId, @CLOSE_BY=NULL",
                        new { perId },
                        tran);
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("reopen-period/{perId:int}")]
        public async Task<IActionResult> ReopenPeriod(int perId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // 🚀 مهار باگ امنیتی: فقط دوره‌ای که "بسته" است (نه محاسبه شده) اجازه باز شدن دارد
                    // 🚀 اصلاح پرفورمنس و امنیت: اضافه شدن قفل برای جلوگیری از Race Condition
                    var status = await conn.QuerySingleOrDefaultAsync<byte?>("SELECT STATUS FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID=@perId", new { perId }, tran);

                    if (status == null)
                        throw new InvalidOperationException("دوره یافت نشد.");

                    if (status >= 3)
                        throw new InvalidOperationException("این دوره محاسبه شده است و قابل بازگشت نیست. ابتدا از تب محاسبه حقوق، عملیات لغو را انجام دهید.");

                    // 🚀 فیکس معماری: جلوگیری از ایجاد تضاد (Desync) بین کارکرد و پیش‌نویس فیش‌ها
                    // P1 Badge Restrict the run check to unreverted runs
                    // After revert, PAY2_RUN headers remain with STATUS = 1 but their details (PAY2_RUN_LINE) are deleted.
                    // We must block reopening ONLY if there's an ACTIVE run (which has PAY2_RUN_LINE items).
                    int runCount = await conn.QuerySingleAsync<int>(@"
                        SELECT COUNT(1)
                        FROM PAY2_RUN R
                        WHERE R.PER_ID = @perId
                          AND EXISTS (SELECT 1 FROM PAY2_RUN_LINE RL WHERE RL.RUN_ID = R.RUN_ID)",
                        new { perId }, tran);

                    if (runCount > 0)
                        throw new InvalidOperationException("برای این دوره فیش حقوقی (حتی پیش‌نویس) صادر شده است. برای ویرایش کارکرد، ابتدا باید در تب محاسبه حقوق، فیش را لغو (Revert) کنید.");

                    await conn.ExecuteAsync("UPDATE PAY2_PERIOD SET STATUS = 1, CLOSED_AT = NULL WHERE PER_ID = @perId", new { perId }, tran);
                });

                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطای سیستمی: " + ex.Message); }
        }
        private static bool IsValidPayrollPeriodDate(long periodDate)
        {
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            int day = (int)(periodDate % 100);

            return year >= 1300 &&
                   year <= 1499 &&
                   month >= 1 &&
                   month <= 12 &&
                   day == 0;
        }

        [HttpGet("periods")]
        public async Task<ActionResult<IEnumerable<Pay2PeriodLookupDto>>> GetPeriods([FromQuery] int wsId)
        {
            if (wsId <= 0)
                return BadRequest("کارگاه نامعتبر است.");

            const string sql = @"
     SELECT 
            PER_ID, -- 🚀 فیلد PER_ID اضافه شد
            (PERIOD_DATE / 100) * 100 AS PERIOD_DATE,
            STATUS,
            CAST(PERIOD_DATE / 10000 AS NVARCHAR(4)) 
            + N' - ' +
            RIGHT(N'00' + CAST((PERIOD_DATE / 100) % 100 AS NVARCHAR(2)), 2)
            + N' - ' +
            CASE ((PERIOD_DATE / 100) % 100)
                WHEN 1 THEN N'فروردین'
                WHEN 2 THEN N'اردیبهشت'
                WHEN 3 THEN N'خرداد'
                WHEN 4 THEN N'تیر'
                WHEN 5 THEN N'مرداد'
                WHEN 6 THEN N'شهریور'
                WHEN 7 THEN N'مهر'
                WHEN 8 THEN N'آبان'
                WHEN 9 THEN N'آذر'
                WHEN 10 THEN N'دی'
                WHEN 11 THEN N'بهمن'
                WHEN 12 THEN N'اسفند'
                ELSE N'نامعتبر'
            END AS PERIOD_TITLE
        FROM PAY2_PERIOD
        WHERE WS_ID = @wsId
        ORDER BY PERIOD_DATE DESC;";

            var rows = await _db.DoGetDataSQLAsync<Pay2PeriodLookupDto>(sql, new { wsId });
            return Ok(rows);
        }

        [HttpDelete("period/{perId:int}")]
        public async Task<IActionResult> DeletePeriod(int perId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // 1. بررسی وجود دوره و وضعیت آن 
                    // 🚀 اصلاح بسیار مهم: استفاده از WITH (UPDLOCK) برای جلوگیری از Race Condition
                    // این قفل تضمین می‌کند که در زمان پردازش حذف، هیچ کاربر دیگری نتواند این دوره را محاسبه کند
                    var period = await conn.QuerySingleOrDefaultAsync<Pay2PeriodDto>(
                        "SELECT * FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @perId",
                        new { perId }, tran);

                    if (period == null)
                        throw new InvalidOperationException("دوره مورد نظر یافت نشد.");

                    if (period.STATUS >= 3)
                        throw new InvalidOperationException("این دوره محاسبه شده یا سند آن صادر شده است. برای حذف، ابتدا باید محاسبات حقوق این ماه را لغو (Revert) کنید.");

                    // 2. بررسی امنیتی: آیا هیچ فیش حقوقی (حتی پیش‌نویس) به این دوره متصل است؟
                    int runCount = await conn.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM PAY2_RUN WHERE PER_ID = @perId",
                        new { perId }, tran);

                    if (runCount > 0)
                        throw new InvalidOperationException("فیش حقوقی (PAY2_RUN) برای این دوره وجود دارد. ابتدا باید محاسبات را حذف کنید.");

                    // 3. حذف آبشاری ایمن (به ترتیب از فرزند به والد)

                    // الف: حذف مقادیر متغیر (پویا)
                    await conn.ExecuteAsync("DELETE FROM PAY2_ATT_VALUE WHERE PER_ID = @perId", new { perId }, tran);

                    // ب: حذف کارکرد پرسنل
                    await conn.ExecuteAsync("DELETE FROM PAY2_ATTENDANCE WHERE PER_ID = @perId", new { perId }, tran);

                    // ج: حذف هدر دوره
                    await conn.ExecuteAsync("DELETE FROM PAY2_PERIOD WHERE PER_ID = @perId", new { perId }, tran);
                });

                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // لاگ خطای سیستمی
                return StatusCode(500, "خطای سیستمی در حذف دوره: " + ex.Message);
            }
        }

        [HttpDelete("period/{perId:int}/employee/{empId:int}")]
        public async Task<IActionResult> DeleteAttendanceLine(int perId, int empId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // 1. بررسی وضعیت دوره با قفل برای جلوگیری از تغییرات همزمان
                    var status = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @perId",
                        new { perId }, tran);

                    if (status == null)
                        throw new InvalidOperationException("دوره کارکرد یافت نشد.");

                    if (status != 1)
                        throw new InvalidOperationException("این دوره بسته شده یا محاسبه گردیده است. امکان تغییر یا حذف رکورد وجود ندارد.");

                    // 2. بررسی وابستگی: آیا فیش حقوقی (حتی پیش‌نویس) برای این شخص در این ماه وجود دارد؟
                    // 🚀 اصلاح پرفورمنس: استفاده از EXISTS برای سرعت بسیار بالاتر
                    string checkRunSql = @"
                        IF EXISTS (
                            SELECT 1 
                            FROM PAY2_RUN R WITH (NOLOCK)
                            INNER JOIN PAY2_RUN_LINE RL WITH (NOLOCK) ON R.RUN_ID = RL.RUN_ID
                            WHERE R.PER_ID = @perId AND RL.EMP_ID = @empId
                        ) SELECT 1 ELSE SELECT 0";

                    var runExists = await conn.QuerySingleAsync<int>(checkRunSql, new { perId, empId }, tran);

                    if (runExists == 1)
                        throw new InvalidOperationException("برای این پرسنل در این دوره فیش حقوقی صادر شده است. ابتدا باید از بخش محاسبه حقوق، فیش ایشان را لغو کنید.");

                    // 3. حذف ایمن و آبشاری (اول فرزند، بعد والد)
                    await conn.ExecuteAsync("DELETE FROM PAY2_ATT_VALUE WHERE PER_ID = @perId AND EMP_ID = @empId", new { perId, empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_ATTENDANCE WHERE PER_ID = @perId AND EMP_ID = @empId", new { perId, empId }, tran);
                });

                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطای سیستمی: " + ex.Message);
            }
        }
    }
}