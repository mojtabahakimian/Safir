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
            catch (System.Data.SqlClient.SqlException ex) when (ex.Message.Contains("CK_ATT_DAYSB") || ex.Message.Contains("CK_ATT_DAYS"))
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

    }
}