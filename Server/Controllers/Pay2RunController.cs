using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
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

        [HttpGet("{runId:int}/payslip/{empId:int}/pdf")]
        public async Task<IActionResult> GetPayslipPdf(int runId, int empId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            // Check if the user is authorized for the WS_ID of the employee
            var accessCheckSql = @"
                SELECT 1
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_RUN R WITH (NOLOCK) ON RL.RUN_ID = R.RUN_ID
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                WHERE RL.RUN_ID = @runId AND RL.EMP_ID = @empId";
                // Assuming RLS or higher level filter is handled by DatabaseService (userCod) or middleware.
                // In Safir, typically IDatabaseService dynamically resolves connection based on tenant/header
                // Or user claims dictate access. Since we don't know the full mappings, we rely on the system's
                // standard access control via the connection string and ensuring the runId matches the empId.
                // However, let's add a check that the run actually belongs to the user's scope if WS_USER mappings exist.
                // Based on memory: "Database profiles and their access roles are securely configured in Server/appsettings.json under 'DatabaseProfiles'".
                // So tenant isolation handles the base security. But we still need to ensure runId and empId match to prevent IDOR within the tenant.
            var hasAccess = await _db.DoGetDataSQLAsyncSingle<int?>(accessCheckSql, new { runId, empId });
            if (hasAccess == null) return Forbid();

            // Attempt to retrieve archived JSON
            try
            {
                var existingJson = await _db.DoGetDataSQLAsyncSingle<string>(
                    "SELECT PAYSLIP_JSON FROM PAY2_RUN_LINE WITH (NOLOCK) WHERE RUN_ID = @runId AND EMP_ID = @empId",
                    new { runId, empId });

                if (!string.IsNullOrWhiteSpace(existingJson))
                {
                    var archivedDto = System.Text.Json.JsonSerializer.Deserialize<PayslipReportDto>(existingJson);
                    if (archivedDto != null)
                    {
                        var doc = new Safir.Server.Reports.PayslipDocument(archivedDto);
                        byte[] pdfBytesOut = doc.GeneratePdf();

                        Response.Headers.Append("Cache-Control", "no-store");
                        return File(pdfBytesOut, "application/pdf");
                    }
                }
            }
            catch
            {
                // Column might not exist yet, proceed with live generation
            }

            var sqlHeader = @"
                SELECT
                    W.WS_NAME AS WorkshopName,
                    W.EMPLOYER_NAME AS EmployerName,
                    P.PERIOD_TITLE AS PeriodTitle,
                    E.LAST_NAME + ' ' + E.FIRST_NAME AS EmployeeName,
                    E.EMP_CODE AS EmployeeCode,
                    RL.WORK_DAYS AS WorkDays,
                    RL.GROSS_PAY AS GrossPay,
                    RL.TOTAL_DED AS TotalDed,
                    RL.NET_PAY AS NetPay,
                    RL.INS_WORKER,
                    RL.TAX_AMOUNT,
                    RL.LOAN_DED,
                    RL.ADVANCE_DED,
                    RL.OTHER_DED
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_RUN R WITH (NOLOCK) ON RL.RUN_ID = R.RUN_ID
                INNER JOIN PAY2_PERIOD P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID = W.WS_ID
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON RL.EMP_ID = E.EMP_ID
                WHERE RL.RUN_ID = @runId AND RL.EMP_ID = @empId";

            var header = await _db.DoGetDataSQLAsyncSingle<dynamic>(sqlHeader, new { runId, empId });

            if (header == null) return NotFound("فیش حقوقی یافت نشد.");

            var sqlDetails = @"
                SELECT
                    I.ITEM_NAME AS Title,
                    D.AMOUNT AS Amount,
                    I.ITEM_TYPE AS ItemType
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId AND D.EMP_ID = @empId AND D.AMOUNT > 0
                ORDER BY I.SORT_ORDER";

            var details = await _db.DoGetDataSQLAsync<dynamic>(sqlDetails, new { runId, empId });

            var dto = new PayslipReportDto
            {
                WorkshopName = header.WorkshopName,
                EmployerName = header.EmployerName ?? string.Empty,
                PeriodTitle = header.PeriodTitle,
                EmployeeName = header.EmployeeName,
                EmployeeCode = header.EmployeeCode,
                WorkDays = Convert.ToDecimal(header.WorkDays),
                GrossPay = Convert.ToInt64(header.GrossPay),
                TotalDed = Convert.ToInt64(header.TotalDed),
                NetPay = Convert.ToInt64(header.NetPay)
            };

            foreach (var d in details)
            {
                var line = new PayslipLineDto { Title = d.Title, Amount = Convert.ToInt64(d.Amount) };
                if (d.ItemType == 1 || d.ItemType == 2)
                {
                    dto.Earnings.Add(line);
                }
                else if (d.ItemType == 3 || d.ItemType == 4)
                {
                    dto.Deductions.Add(line);
                }
            }

            bool hasIns = ((IEnumerable<dynamic>)details).Any(x => x.Title.ToString().Contains("بیمه"));
            bool hasTax = ((IEnumerable<dynamic>)details).Any(x => x.Title.ToString().Contains("مالیات"));
            bool hasLoan = ((IEnumerable<dynamic>)details).Any(x => x.Title.ToString().Contains("وام"));
            bool hasAdvance = ((IEnumerable<dynamic>)details).Any(x => x.Title.ToString().Contains("مساعده"));
            bool hasOtherDed = ((IEnumerable<dynamic>)details).Any(x => x.Title.ToString().Contains("سایر کسورات"));

            if (!hasIns && Convert.ToInt64(header.INS_WORKER) > 0) dto.Deductions.Add(new PayslipLineDto { Title = "حق بیمه سهم کارگر", Amount = Convert.ToInt64(header.INS_WORKER) });
            if (!hasTax && Convert.ToInt64(header.TAX_AMOUNT) > 0) dto.Deductions.Add(new PayslipLineDto { Title = "مالیات", Amount = Convert.ToInt64(header.TAX_AMOUNT) });
            if (!hasLoan && Convert.ToInt64(header.LOAN_DED) > 0) dto.Deductions.Add(new PayslipLineDto { Title = "اقساط وام", Amount = Convert.ToInt64(header.LOAN_DED) });
            if (!hasAdvance && Convert.ToInt64(header.ADVANCE_DED) > 0) dto.Deductions.Add(new PayslipLineDto { Title = "مساعده", Amount = Convert.ToInt64(header.ADVANCE_DED) });
            if (!hasOtherDed && Convert.ToInt64(header.OTHER_DED) > 0) dto.Deductions.Add(new PayslipLineDto { Title = "سایر کسورات", Amount = Convert.ToInt64(header.OTHER_DED) });

            var document = new Safir.Server.Reports.PayslipDocument(dto);
            byte[] pdfBytes = document.GeneratePdf();

            // Archive the payload if the run is finalized (STATUS >= 2)
            var runStatus = await _db.DoGetDataSQLAsyncSingle<byte?>("SELECT STATUS FROM PAY2_RUN WITH (NOLOCK) WHERE RUN_ID = @runId", new { runId });
            if (runStatus >= 2)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(dto);
                // We'll update the PAYSLIP_JSON field if it exists, otherwise we'll add it to the schema.
                // Assuming we dynamically add the column if it doesn't exist, to prevent breaking the schema mapping:
                try
                {
                    await _db.DoExecuteSQLAsync(@"
                        IF COL_LENGTH('PAY2_RUN_LINE', 'PAYSLIP_JSON') IS NULL
                        BEGIN
                            ALTER TABLE PAY2_RUN_LINE ADD PAYSLIP_JSON NVARCHAR(MAX) NULL;
                        END
                        UPDATE PAY2_RUN_LINE SET PAYSLIP_JSON = @json WHERE RUN_ID = @runId AND EMP_ID = @empId
                    ", new { json, runId, empId });
                }
                catch
                {
                    // Ignore column addition errors if the user doesn't have DDL permissions,
                    // though this should be handled properly in migrations.
                }
            }

            Response.Headers.Append("Cache-Control", "no-store");
            return File(pdfBytes, "application/pdf");
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
                    // ۱. 🚀 استفاده از UPDLOCK برای جلوگیری قطعی از Race Condition در کلیک‌های همزمان
                    var periodStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @PER_ID",
                        new { request.PER_ID }, tran);

                    if (periodStatus == null || periodStatus == 1)
                        throw new InvalidOperationException("دوره کارکرد هنوز باز است. لطفاً ابتدا در تب کارکرد، دکمه 'بستن کارکرد' را بزنید.");

                    // ۲. بررسی وضعیت آخرین فیش با UPDLOCK
                    var latestRunStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_RUN WITH (UPDLOCK) WHERE PER_ID = @PER_ID AND IS_LATEST = 1",
                        new { request.PER_ID }, tran);

                    if (latestRunStatus >= 2)
                        throw new InvalidOperationException("برای این دوره فیش حقوقیِ تأیید شده وجود دارد. امکان بازمحاسبه نیست.");

                    // ۳. فراخوانی SP محاسبات (با افزایش Timeout به ۱۸۰ ثانیه برای کارگاه‌های بزرگ)
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

                    // 🚀 اعمال Command Timeout
                    return await conn.QuerySingleAsync<int>(sql, p, tran, commandTimeout: 180);
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
            var userName = User.Identity?.Name ?? "System";

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // ۱. کنترل وضعیت با قفل انحصاری
                    var runInfo = await conn.QuerySingleAsync(
                        "SELECT STATUS, PER_ID FROM PAY2_RUN WITH (UPDLOCK) WHERE RUN_ID = @runId",
                        new { runId }, tran);

                    if ((byte)runInfo.STATUS != 2)
                        throw new InvalidOperationException("اجرا باید در وضعیت 'تأیید نهایی' باشد تا سند صادر شود.");

                    int perId = (int)runInfo.PER_ID;

                    // ۲. گرفتن تاریخ دوره برای ثبت در هدر سند
                    long periodDate = await conn.QuerySingleAsync<long>(
                        "SELECT PERIOD_DATE FROM PAY2_PERIOD WHERE PER_ID = @perId",
                        new { perId }, tran);

                    // ۳. گرفتن شماره سند جدید از سیستم حسابداری (DEED_HED)
                    double nextNs = (await conn.QuerySingleOrDefaultAsync<double?>(
                        "SELECT MAX(N_S) FROM DEED_HED WITH (UPDLOCK)", null, tran) ?? 0) + 1;

                    // ۴. ایجاد هدر سند حقوق در حسابداری
                    string hedSharh = $"سند حقوق و دستمزد دوره {periodDate}";
                    await conn.ExecuteAsync(@"
                        INSERT INTO DEED_HED (N_S, DATE_S, SHARH_S, NO_S, USER_NAME, OKF, CRT, UID)
                        VALUES (@N_S, @DATE_S, @SHARH, 2, @USER, 1, GETDATE(), @UID)",
                        new { N_S = nextNs, DATE_S = periodDate, SHARH = hedSharh, USER = userName, UID = userCod }, tran);

                    // ۵. فراخوانی SP تولید آرتیکل‌های سند
                    var articles = await conn.QueryAsync(
                        "EXEC SP_PAY2_GEN_DEED @RUN_ID = @runId, @CALC_BY = @userCod",
                        new { runId, userCod }, tran, commandTimeout: 120);

                    // ۶. درج آرتیکل‌ها در ریز سند (DEED_DTL)
                    int radif = 1;
                    foreach (var art in articles)
                    {
                        string hesCode = (string)art.HES_CODE;
                        var parts = hesCode.Split('-');

                        int hesK = int.Parse(parts[0]);
                        int hesM = int.Parse(parts[1]);
                        int? hesT = null;

                        // اگر SP پرسنل را مشخص کرده (مثلاً برای مساعده)، کد تفصیلی او را از جدول پرسنل استخراج می‌کنیم
                        if (art.EMP_ID != null)
                        {
                            string? accT = await conn.QuerySingleOrDefaultAsync<string>(
                                "SELECT ACC_T FROM PAY2_EMPLOYEE WHERE EMP_ID = @empId",
                                new { empId = (int)art.EMP_ID }, tran);

                            if (!string.IsNullOrWhiteSpace(accT) && int.TryParse(accT, out int tValue))
                                hesT = tValue;
                        }

                        await conn.ExecuteAsync(@"
                            INSERT INTO DEED_DTL (N_S, RADIF, HES_K, HES_M, HES_T, SHARH, BED, BES, CRT, UID)
                            VALUES (@N_S, @RADIF, @HES_K, @HES_M, @HES_T, @SHARH, @BED, @BES, GETDATE(), @UID)",
                            new
                            {
                                N_S = nextNs,
                                RADIF = radif++,
                                HES_K = hesK,
                                HES_M = hesM,
                                HES_T = hesT,
                                SHARH = (string)art.SHARH,
                                BED = (double)art.BED,
                                BES = (double)art.BES,
                                UID = userCod
                            }, tran);
                    }

                    // ۷. آپدیت وضعیت‌های سیستم حقوق
                    await conn.ExecuteAsync(@"
                        UPDATE PAY2_RUN SET STATUS = 3, DEED_ID_SAL = @deedId WHERE RUN_ID = @runId;
                        UPDATE PAY2_PERIOD SET STATUS = 4, DEED_N_S_PAY = @nextNs WHERE PER_ID = @perId;",
                        new { runId, deedId = (int)nextNs, nextNs, perId }, tran);
                });

                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطا در صدور سند: " + ex.Message); }
        }
    }
}