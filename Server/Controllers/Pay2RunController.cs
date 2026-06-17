using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Security.Claims;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/run")]
    [Authorize]
    public class Pay2RunController : ControllerBase
    {
        private readonly IDatabaseService _db;
        public Pay2RunController(IDatabaseService db) => _db = db;

        [HttpGet("period-info")]
        public async Task<ActionResult<Pay2PeriodDto>> GetPeriodInfo([FromQuery] int wsId, [FromQuery] long periodDate)
        {
            var sql = "SELECT * FROM PAY2_PERIOD WHERE WS_ID = @wsId AND PERIOD_DATE = @periodDate";
            var period = await _db.DoGetDataSQLAsyncSingle<Pay2PeriodDto>(sql, new { wsId, periodDate });
            return Ok(period);
        }

        [HttpGet("latest")]
        public async Task<ActionResult<Pay2RunDto>> GetLatestRun([FromQuery] int perId)
        {
            var sql = "SELECT TOP 1 * FROM PAY2_RUN WHERE PER_ID = @perId AND IS_LATEST = 1 ORDER BY RUN_NO DESC";
            var run = await _db.DoGetDataSQLAsyncSingle<Pay2RunDto>(sql, new { perId });
            return Ok(run);
        }

        [HttpGet("{runId:int}/lines")]
        public async Task<ActionResult<Pay2RunResultDto>> GetRunLines(int runId)
        {
            var result = new Pay2RunResultDto();

            // 1. استخراج ستون‌های پویا (فقط آیتم‌هایی که در این ماه برای حداقل یک نفر محاسبه شده‌اند)
            string colSql = @"
                SELECT DISTINCT D.ITEM_ID, I.ITEM_CODE, I.ITEM_NAME, I.SORT_ORDER
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId
                ORDER BY I.SORT_ORDER";

            result.Columns = (await _db.DoGetDataSQLAsync<Pay2RunColumnDto>(colSql, new { runId })).ToList();

            // 2. استخراج ردیف‌های اصلی فیش حقوقی
            string lineSql = @"
                SELECT L.*, E.EMP_CODE, E.LAST_NAME + ' ' + E.FIRST_NAME AS FULL_NAME
                FROM PAY2_RUN_LINE L WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON L.EMP_ID = E.EMP_ID
                WHERE L.RUN_ID = @runId
                ORDER BY E.LAST_NAME, E.FIRST_NAME";

            result.Lines = (await _db.DoGetDataSQLAsync<Pay2RunLineDto>(lineSql, new { runId })).ToList();

            // 3. استخراج مبالغ ریز (Details) و اتصال آن‌ها به ردیف‌ها
            string detSql = @"
                SELECT D.EMP_ID, I.ITEM_CODE, D.AMOUNT
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId";

            // استفاده از یک کلاس داخلی موقت برای خواندن سریع داده‌ها از Dapper
            var details = await _db.DoGetDataSQLAsync<RunDetailFlat>(detSql, new { runId });

            // گروه‌بندی داده‌ها بر اساس شناسه پرسنل برای پردازش سریع در RAM
            var groupedDetails = details
                .GroupBy(x => x.EMP_ID)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(x => x.ITEM_CODE, x => x.AMOUNT)
                );

            foreach (var line in result.Lines)
            {
                if (groupedDetails.TryGetValue(line.EMP_ID, out var empDetails))
                {
                    line.Details = empDetails;
                }
            }

            return Ok(result);
        }

        // کلاس کمکی برای Dapper
        private class RunDetailFlat
        {
            public int EMP_ID { get; set; }
            public string ITEM_CODE { get; set; } = "";
            public long AMOUNT { get; set; }
        }

        [HttpPost("calculate")]
        public async Task<ActionResult<int>> CalculateRun([FromBody] Pay2RunCalcRequest request)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                int newRunId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // ۱. بررسی وضعیت دوره
                    var periodStatus = await conn.QuerySingleOrDefaultAsync<byte?>("SELECT STATUS FROM PAY2_PERIOD WHERE PER_ID = @PER_ID", new { request.PER_ID }, tran);
                    if (periodStatus == null || periodStatus == 1)
                        throw new InvalidOperationException("دوره کارکرد هنوز باز است. لطفاً ابتدا در تب کارکرد، دکمه 'بستن کارکرد' را بزنید.");

                    // ۲. 🚀 مهار باگ امنیتی: بررسی وضعیت آخرین فیش (نباید تایید نهایی شده باشد)
                    var latestRunStatus = await conn.QuerySingleOrDefaultAsync<byte?>("SELECT STATUS FROM PAY2_RUN WHERE PER_ID = @PER_ID AND IS_LATEST = 1", new { request.PER_ID }, tran);
                    if (latestRunStatus >= 2)
                        throw new InvalidOperationException("برای این دوره فیش حقوقیِ تأیید شده وجود دارد. امکان بازمحاسبه نیست.");

                    // ۳. فراخوانی SP محاسبات
                    var sql = @"
                        DECLARE @NewId INT;
                        EXEC SP_PAY2_CALC_RUN 
                            @WS_ID = @WS_ID, 
                            @PER_ID = @PER_ID, 
                            @PAYROLL_N_S = @PAYROLL_N_S, 
                            @CALC_BY = @UserCod, 
                            @IS_RERUN = @IsReRun, 
                            @NEW_RUN_ID = @NewId OUTPUT;
                        SELECT @NewId;";

                    var p = new DynamicParameters(request);
                    p.Add("UserCod", userCod);

                    return await conn.QuerySingleAsync<int>(sql, p, tran);
                });

                return Ok(newRunId);
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطای موتور محاسبه: " + ex.Message); }
        }

        [HttpPut("{runId:int}/revert")]
        public async Task<IActionResult> RevertRun(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await conn.ExecuteAsync("EXEC SP_PAY2_REVERT_RUN @RUN_ID = @runId, @REVERT_BY = @userCod", new { runId, userCod }, tran);
                });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpPut("{runId:int}/finalize")]
        public async Task<IActionResult> FinalizeRun(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await conn.ExecuteAsync("EXEC SP_PAY2_FINALIZE_RUN @RUN_ID = @runId, @FINAL_BY = @userCod", new { runId, userCod }, tran);
                });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("{runId:int}/generate-deed")]
        public async Task<IActionResult> GenerateDeed(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var status = await conn.QuerySingleAsync<byte>("SELECT STATUS FROM PAY2_RUN WHERE RUN_ID = @runId", new { runId }, tran);
                    if (status != 2) throw new InvalidOperationException("اجرا باید در وضعیت 'تأیید نهایی' باشد تا سند صادر شود.");

                    // 🚀 مهار باگ State Desync: همگام‌سازی وضعیت در هر دو جدول
                    var sql = @"
                        UPDATE PAY2_RUN SET STATUS = 3 WHERE RUN_ID = @runId;
                        UPDATE PAY2_PERIOD SET STATUS = 4 WHERE PER_ID = (SELECT PER_ID FROM PAY2_RUN WHERE RUN_ID = @runId);";

                    await conn.ExecuteAsync(sql, new { runId }, tran);
                });
                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }
    }
}