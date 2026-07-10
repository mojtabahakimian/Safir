using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using Safir.Server.Reports;
using Safir.Server.Services;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using Safir.Shared.Models.Salary.Reports;
using Safir.Shared.Utility;
using System.Security.Claims;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/run")]
    [Authorize]
    public class Pay2RunController : ControllerBase
    {
        private readonly IDatabaseService _db;
        public Pay2RunController(IDatabaseService db) => _db = db;

        // ===================================================================
        // Helper: Mode Resolver
        // ===================================================================
        private async Task<byte> ResolveDeedModeAsync(System.Data.IDbConnection conn, int runId, System.Data.IDbTransaction? tran = null)
        {
            var runInfo = await conn.QuerySingleOrDefaultAsync(
                "SELECT R.DEED_MODE, P.WS_ID FROM PAY2_RUN R INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID WHERE R.RUN_ID = @runId",
                new { runId }, tran);

            if (runInfo == null)
                throw new InvalidOperationException("اجرای حقوق مورد نظر یافت نشد.");

            if (runInfo.DEED_MODE != null)
            {
                byte mode = (byte)runInfo.DEED_MODE;
                if (mode == 1 || mode == 2) return mode;
            }

            int wsId = (int)runInfo.WS_ID;
            var wsMode = await conn.QuerySingleOrDefaultAsync<byte?>(
                "SELECT DEFAULT_DEED_MODE FROM PAY2_WORKSHOP WHERE WS_ID = @wsId",
                new { wsId }, tran);

            if (wsMode.HasValue && (wsMode.Value == 1 || wsMode.Value == 2))
                return wsMode.Value;

            throw new InvalidOperationException("الگوی صدور سند نامعتبر است. لطفاً تنظیمات کارگاه را بررسی کنید.");
        }

        // ===================================================================
        // Helper: IsAccountingDeedEditable
        // ===================================================================
        private async Task<bool> IsAccountingDeedEditable(System.Data.IDbConnection conn, double n_s, System.Data.IDbTransaction tran)
        {
            var okfStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                "SELECT OKF FROM DEED_HED WITH (UPDLOCK) WHERE N_S = @n_s",
                new { n_s }, tran);

            return okfStatus.HasValue && okfStatus.Value == 1;
        }

        // ===================================================================
        // Helper: Shared Validation
        // ===================================================================
        private async Task<List<string>> ValidateDeedArticlesAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tran, List<Pay2DeedArticleDto> articles)
        {
            var errors = new List<string>();

            if (articles == null || articles.Count == 0)
            {
                errors.Add("هیچ آرتیکلی برای صدور سند تولید نشد (احتمالاً خالص پرداختی همه پرسنل صفر است).");
                return errors;
            }

            long totalBed = 0;
            long totalBes = 0;

            foreach (var art in articles)
            {
                if (string.IsNullOrWhiteSpace(art.HES_CODE))
                    errors.Add($"کد حساب برای آرتیکل «{art.SHARH}» خالی است.");

                if (string.IsNullOrWhiteSpace(art.SHARH))
                    errors.Add($"شرح آرتیکل برای حساب «{art.HES_CODE}» خالی است.");

                if (art.BED < 0 || art.BES < 0)
                    errors.Add($"مبالغ بدهکار و بستانکار نمی‌توانند منفی باشند. حساب: {art.HES_CODE}");

                if (art.BED > 0 && art.BES > 0)
                    errors.Add($"یک آرتیکل نمی‌تواند همزمان بدهکار و بستانکار باشد. حساب: {art.HES_CODE}");

                if (art.BED == 0 && art.BES == 0)
                    errors.Add($"مبلغ آرتیکل نمی‌تواند صفر باشد. حساب: {art.HES_CODE}");

                totalBed += art.BED;
                totalBes += art.BES;

                var parsed = Pay2AccountCodeParser.Parse(art.HES_CODE, art.SHARH);
                if (!parsed.IsValid)
                {
                    errors.Add(parsed.ErrorMessage);
                }
                else
                {
                    art.HesK = parsed.HesK;
                    art.HesM = parsed.HesM;
                    art.HesT = parsed.HesT;
                    art.HesT2 = parsed.HesT2;
                    art.HesT3 = parsed.HesT3;
                    art.HesT4 = parsed.HesT4;

                    // DB Existence Check
                    var isExisting = await conn.QuerySingleOrDefaultAsync<bool?>(
                        "SELECT IsValid FROM dbo.FN_PAY2_VALIDATE_ACC_EXISTS(@HES_CODE)",
                        new { HES_CODE = art.HES_CODE }, tran);

                    if (isExisting == null || isExisting == false)
                    {
                        errors.Add($"کد حساب «{art.HES_CODE}» نامعتبر است و در سرفصل‌های حسابداری (در سطح مورد نظر) تعریف نشده است.");
                    }
                }
            }

            if (totalBed != totalBes)
            {
                errors.Add($"سند تراز نیست. جمع بدهکار: {totalBed} | جمع بستانکار: {totalBes} | اختلاف: {totalBed - totalBes}");
            }

            return errors;
        }

        [HttpGet("{runId:int}/deed-preview")]
        public async Task<ActionResult<Pay2DeedPreviewDto>> GetDeedPreview(int runId)
        {
            try
            {
                var preview = new Pay2DeedPreviewDto { RUN_ID = runId };

                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var runInfo = await conn.QuerySingleOrDefaultAsync(
                        "SELECT STATUS FROM PAY2_RUN WHERE RUN_ID = @runId", new { runId }, tran);

                    if (runInfo == null)
                        throw new InvalidOperationException("محاسبه مورد نظر یافت نشد.");

                    if ((byte)runInfo.STATUS < 2)
                        throw new InvalidOperationException("برای مشاهده پیش‌نمایش سند، لیست حقوق باید در وضعیت تایید نهایی باشد.");

                    byte mode = await ResolveDeedModeAsync(conn, runId, tran);
                    preview.DEED_MODE = mode;
                    preview.DEED_MODE_TITLE = mode == 1 ? "سند کلی (تجمیعی)" : "سند نیمه‌تفصیلی اشخاص";

                    // اجرا به صورت فقط-خواندنی
                    var rawArticles = await conn.QueryAsync<Pay2DeedArticleDto>(
                        "EXEC SP_PAY2_GEN_DEED @RUN_ID = @runId, @DEED_MODE = @mode",
                        new { runId, mode }, tran);

                    preview.Articles = rawArticles.ToList();

                    // Validation
                    preview.ValidationErrors = await ValidateDeedArticlesAsync(conn, tran, preview.Articles);

                    preview.TotalBed = preview.Articles.Sum(x => x.BED);
                    preview.TotalBes = preview.Articles.Sum(x => x.BES);
                    preview.IsBalanced = preview.TotalBed > 0 && preview.TotalBed == preview.TotalBes && preview.ValidationErrors.Count == 0;
                });

                return Ok(preview);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
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
                    // 2. Lock PAY2_RUN
                    var runInfo = await conn.QuerySingleAsync(
                        "SELECT STATUS, PER_ID, DEED_ID_SAL FROM PAY2_RUN WITH (UPDLOCK) WHERE RUN_ID = @runId",
                        new { runId }, tran);

                    int perId = (int)runInfo.PER_ID;

                    // 3. Lock PAY2_PERIOD
                    var periodInfo = await conn.QuerySingleAsync(
                        "SELECT PERIOD_DATE, DEED_N_S_PAY FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @perId",
                        new { perId }, tran);

                    // 4. خواندن وضعیت Run
                    byte status = (byte)runInfo.STATUS;
                    if (status != 2 && status != 3)
                        throw new InvalidOperationException("اجرا باید در وضعیت 'تأیید نهایی' یا 'سند صادر شده' باشد.");

                    // 5. Resolve Mode
                    byte mode = await ResolveDeedModeAsync(conn, runId, tran);

                    // 6. اجرای Generator و ساخت Preview در حافظه
                    var rawArticles = await conn.QueryAsync<Pay2DeedArticleDto>(
                        "EXEC SP_PAY2_GEN_DEED @RUN_ID = @runId, @DEED_MODE = @mode, @CALC_BY = @userCod",
                        new { runId, mode, userCod }, tran);

                    var articles = rawArticles.ToList();

                    // 7. Parse و Validation تمام آرتیکل‌ها
                    var validationErrors = await ValidateDeedArticlesAsync(conn, tran, articles);
                    if (validationErrors.Any())
                        throw new InvalidOperationException(string.Join(" \n ", validationErrors));

                    // 8. کنترل تراز
                    long previewTotalBed = articles.Sum(x => x.BED);
                    long previewTotalBes = articles.Sum(x => x.BES);
                    if (previewTotalBed != previewTotalBes || previewTotalBed == 0)
                        throw new InvalidOperationException("سند تراز نیست یا خالی است.");

                    long periodDate = (long)periodInfo.PERIOD_DATE;
                    double? existingNs = (double?)periodInfo.DEED_N_S_PAY;
                    long deedDate = Safir.Shared.Utility.CL_Tarikh.GetPersianMonthEndAsLong(periodDate);
                    string hedSharh = $"سند حقوق و دستمزد دوره {periodDate}";
                    double targetNs;

                    // 9. خواندن سند قبلی با Lock
                    if (status == 3 && existingNs.HasValue && existingNs.Value > 0)
                    {
                        targetNs = existingNs.Value;

                        // 10. کنترل قابل‌ویرایش بودن سند
                        if (!await IsAccountingDeedEditable(conn, targetNs, tran))
                            throw new InvalidOperationException("این سند در سیستم حسابداری بررسی و قطعی شده است. امکان بازصدور و تغییر ارقام آن از طریق ماژول حقوق وجود ندارد.");

                        // 12. Update DEED_HED
                        await conn.ExecuteAsync(@"
                            UPDATE DEED_HED
                            SET DATE_S = @DATE_S, SHARH_S = @SHARH, NO_S = 11, USER_NAME = @USER, UID = @UID
                            WHERE N_S = @N_S",
                            new { N_S = targetNs, DATE_S = deedDate, SHARH = hedSharh, USER = userName, UID = userCod }, tran);

                        // 13. فقط اکنون DELETE DEED_DTL قبلی
                        await conn.ExecuteAsync("DELETE FROM DEED_DTL WHERE N_S = @N_S", new { N_S = targetNs }, tran);
                    }
                    else
                    {
                        // 11. تعیین شماره سند
                        targetNs = (await conn.QuerySingleOrDefaultAsync<double?>("SELECT MAX(N_S) FROM DEED_HED WITH (UPDLOCK)", null, tran) ?? 0) + 1;

                        // 12. Insert DEED_HED
                        await conn.ExecuteAsync(@"
                            INSERT INTO DEED_HED (N_S, DATE_S, SHARH_S, NO_S, USER_NAME, OKF, CRT, UID)
                            VALUES (@N_S, @DATE_S, @SHARH, 11, @USER, 1, GETDATE(), @UID)",
                            new { N_S = targetNs, DATE_S = deedDate, SHARH = hedSharh, USER = userName, UID = userCod }, tran);
                    }

                    // 14. Insert تمام ردیف‌ها
                    // 15. درج HES کامل
                    // 16. درج HES_K تا HES_T4
                    int radif = 1;
                    foreach (var art in articles)
                    {
                        var p = new DynamicParameters();
                        p.Add("N_S", targetNs);
                        p.Add("RADIF", radif++);
                        p.Add("HES_K", art.HesK);
                        p.Add("HES_M", art.HesM);
                        p.Add("HES_T", art.HesT);
                        p.Add("HES", art.HES_CODE);
                        p.Add("SHARH", art.SHARH);
                        p.Add("BED", (double)art.BED);
                        p.Add("BES", (double)art.BES);
                        p.Add("UID", userCod);

                        string cols = "N_S, RADIF, HES_K, HES_M, HES_T, HES, SHARH, BED, BES, CRT, UID";
                        string vals = "@N_S, @RADIF, @HES_K, @HES_M, @HES_T, @HES, @SHARH, @BED, @BES, GETDATE(), @UID";

                        if (art.HesT2.HasValue) { cols += ", HES_T2"; vals += ", @HES_T2"; p.Add("HES_T2", art.HesT2.Value); }
                        if (art.HesT3.HasValue) { cols += ", HES_T3"; vals += ", @HES_T3"; p.Add("HES_T3", art.HesT3.Value); }
                        if (art.HesT4.HasValue) { cols += ", HES_T4"; vals += ", @HES_T4"; p.Add("HES_T4", art.HesT4.Value); }

                        await conn.ExecuteAsync($@"INSERT INTO DEED_DTL ({cols}) VALUES ({vals})", p, tran);
                    }

                    // 17. Query جمع واقعی DEED_DTL
                    var actualSums = await conn.QuerySingleAsync(
                        "SELECT ISNULL(SUM(BED), 0) AS ActualBed, ISNULL(SUM(BES), 0) AS ActualBes FROM DEED_DTL WHERE N_S = @N_S",
                        new { N_S = targetNs }, tran);

                    // 18. مقایسه با Preview
                    if ((long)actualSums.ActualBed != previewTotalBed || (long)actualSums.ActualBes != previewTotalBes || actualSums.ActualBed != actualSums.ActualBes)
                    {
                        throw new InvalidOperationException("خطای بحرانی سیستمی: مبالغ درج شده در دیتابیس با مبالغ پیش‌نمایش همخوانی ندارد. عملیات متوقف شد.");
                    }

                    // 19. ذخیره PAY2_RUN.DEED_MODE
                    // 20. ذخیره DEED_GENERATOR_VERSION = 1
                    // 21. Update وضعیت Run و Period
                    await conn.ExecuteAsync(@"
                        UPDATE PAY2_RUN SET STATUS = 3, DEED_ID_SAL = @deedId, DEED_MODE = @mode, DEED_GENERATOR_VERSION = 1 WHERE RUN_ID = @runId;
                        UPDATE PAY2_PERIOD SET STATUS = 4, DEED_N_S_PAY = @targetNs WHERE PER_ID = @perId;",
                        new { runId, deedId = (int)targetNs, mode, targetNs, perId }, tran);
                });

                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطا در صدور سند: " + ex.Message); }
        }

        // ===================================================================
        // بقیه متدها بدون تغییر باقی می‌مانند (کپی دقیق از قبل)
        // ===================================================================

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

            string colSql = @"
                SELECT DISTINCT D.ITEM_ID, I.ITEM_CODE, I.ITEM_NAME, I.SORT_ORDER
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId
                ORDER BY I.SORT_ORDER";

            result.Columns = (await _db.DoGetDataSQLAsync<Pay2RunColumnDto>(colSql, new { runId })).ToList();

            string lineSql = @"
                SELECT L.*, E.EMP_CODE, E.LAST_NAME + ' ' + E.FIRST_NAME AS FULL_NAME
                FROM PAY2_RUN_LINE L WITH (NOLOCK)
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON L.EMP_ID = E.EMP_ID
                WHERE L.RUN_ID = @runId
                ORDER BY E.LAST_NAME, E.FIRST_NAME";

            result.Lines = (await _db.DoGetDataSQLAsync<Pay2RunLineDto>(lineSql, new { runId })).ToList();

            string detSql = @"
                SELECT D.EMP_ID, I.ITEM_CODE, D.AMOUNT
                FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                WHERE D.RUN_ID = @runId";

            var details = await _db.DoGetDataSQLAsync<RunDetailFlat>(detSql, new { runId });

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

        private class RunDetailFlat
        {
            public int EMP_ID { get; set; }
            public string ITEM_CODE { get; set; } = "";
            public long AMOUNT { get; set; }
        }

        [HttpGet("{runId:int}/excel-audit")]
        public async Task<IActionResult> GetExcelAudit(int runId)
        {
            var service = new Pay2ExcelAuditService(_db);
            var result = await service.BuildAsync(runId);
            if (result == null)
                return NotFound("داده‌ای برای خروجی اکسل تحلیلی یافت نشد.");

            return File(result.Value.Bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.Value.FileName);
        }

        [HttpGet("{runId:int}/employee/{empId:int}/payslip")]
        public async Task<IActionResult> GetPayslip(int runId, int empId, [FromQuery] bool isOfficial = false)
        {
            const string headSql = @"
                SELECT
                    RL.RUN_ID, RL.EMP_ID, RL.WORK_DAYS,
                    RL.GROSS_PAY, RL.INS_BASE, RL.INS_WORKER, RL.TAX_AMOUNT,
                    RL.LOAN_DED, RL.ADVANCE_DED, RL.OTHER_DED, RL.TOTAL_DED, RL.NET_PAY,
                    RL.LEAVE_BAL_DAYS, RL.LOAN_BALANCE,
                    E.EMP_CODE, (E.LAST_NAME + N' ' + E.FIRST_NAME) AS FULL_NAME, E.NATIONAL_CODE,
                    J.JOB_NAME,
                    P.PERIOD_DATE,
                    W.WS_NAME
                FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                INNER JOIN PAY2_RUN      R WITH (NOLOCK) ON RL.RUN_ID = R.RUN_ID
                INNER JOIN PAY2_PERIOD   P WITH (NOLOCK) ON R.PER_ID  = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID   = W.WS_ID
                INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON RL.EMP_ID = E.EMP_ID
                LEFT  JOIN PAY2_JOB      J WITH (NOLOCK) ON E.JOB_ID  = J.JOB_ID
                WHERE RL.RUN_ID = @runId AND RL.EMP_ID = @empId";

            var head = await _db.DoGetDataSQLAsyncSingle<PayslipHeadRow>(headSql, new { runId, empId });
            if (head == null)
                return NotFound("فیش حقوقی برای این پرسنل در این اجرا یافت نشد.");

            string earnSql;
            if (isOfficial)
            {
                earnSql = @"
                    SELECT I.ITEM_NAME AS Title, D.AMOUNT AS Amount
                    FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                    INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                    WHERE D.RUN_ID = @runId AND D.EMP_ID = @empId
                      AND I.ITEM_TYPE IN (1, 2)
                      AND D.AMOUNT <> 0
                      AND I.ITEM_CODE <> 'BASE_SAL'
                    ORDER BY I.SORT_ORDER";
            }
            else
            {
                earnSql = @"
                    SELECT I.ITEM_NAME AS Title, D.AMOUNT AS Amount
                    FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                    INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                    WHERE D.RUN_ID = @runId AND D.EMP_ID = @empId
                      AND I.ITEM_TYPE IN (1, 2)
                      AND D.AMOUNT <> 0
                      AND I.ITEM_CODE <> 'BASE_SAL_B'
                    ORDER BY I.SORT_ORDER";
            }

            var earnings = (await _db.DoGetDataSQLAsync<PayslipLineDto>(earnSql, new { runId, empId })).ToList();

            var dto = new PayslipReportDto
            {
                EmployeeName = head.FULL_NAME ?? "",
                EmployeeCode = head.EMP_CODE ?? "",
                NationalCode = head.NATIONAL_CODE,
                JobTitle = head.JOB_NAME,
                PeriodDate = head.PERIOD_DATE,
                PeriodTitle = BuildPeriodTitle(head.PERIOD_DATE),
                WorkshopName = head.WS_NAME ?? "",
                WorkDays = head.WORK_DAYS,
                Earnings = earnings,
                InsBase = head.INS_BASE,
                LeaveBalanceDays = head.LEAVE_BAL_DAYS,
                LoanBalance = head.LOAN_BALANCE,
                NetPay = head.NET_PAY,
                PrintDate = FormatShamsi(CL_Tarikh.GetCurrentPersianDateAsLong())
            };

            void AddDed(string title, long amount)
            {
                if (amount != 0)
                    dto.Deductions.Add(new PayslipLineDto { Title = title, Amount = amount });
            }
            AddDed("کسر بیمه کارگر", head.INS_WORKER);
            AddDed("کسر مالیات", head.TAX_AMOUNT);
            AddDed("قسط وام", head.LOAN_DED);
            AddDed("مساعده", head.ADVANCE_DED);
            AddDed("سایر کسورات", head.OTHER_DED);

            if (!isOfficial)
            {
                long rounding = head.NET_PAY - (head.GROSS_PAY - head.TOTAL_DED);
                if (rounding > 0)
                    dto.Earnings.Add(new PayslipLineDto { Title = "تعدیل (گرد کردن)", Amount = rounding });
                else if (rounding < 0)
                    dto.Deductions.Add(new PayslipLineDto { Title = "تعدیل (گرد کردن)", Amount = -rounding });
            }
            else
            {
                long officialTotalEarn = dto.Earnings.Sum(x => x.Amount);
                long officialTotalDed = dto.Deductions.Sum(x => x.Amount);
                dto.NetPay = officialTotalEarn - officialTotalDed;
            }

            dto.NetPayInWords = CL_HESABDARI.ALPHANUM(dto.NetPay) + " ریال";

            var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
            byte[] pdfBytes = new PayslipDocument(dto, env).GeneratePdf();

            return File(pdfBytes, "application/pdf", $"Payslip_{dto.EmployeeCode}.pdf");
        }

        private class PayslipHeadRow
        {
            public int RUN_ID { get; set; }
            public int EMP_ID { get; set; }
            public decimal WORK_DAYS { get; set; }
            public long GROSS_PAY { get; set; }
            public long INS_BASE { get; set; }
            public long INS_WORKER { get; set; }
            public long TAX_AMOUNT { get; set; }
            public long LOAN_DED { get; set; }
            public long ADVANCE_DED { get; set; }
            public long OTHER_DED { get; set; }
            public long TOTAL_DED { get; set; }
            public long NET_PAY { get; set; }
            public decimal? LEAVE_BAL_DAYS { get; set; }
            public long? LOAN_BALANCE { get; set; }
            public string? EMP_CODE { get; set; }
            public string? FULL_NAME { get; set; }
            public string? NATIONAL_CODE { get; set; }
            public string? JOB_NAME { get; set; }
            public long PERIOD_DATE { get; set; }
            public string? WS_NAME { get; set; }
        }

        private static readonly string[] PersianMonthNames =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };

        private static string BuildPeriodTitle(long periodDate)
        {
            long year = periodDate / 10000;
            int month = (int)((periodDate / 100) % 100);
            string monthName = (month >= 1 && month <= 12) ? PersianMonthNames[month - 1] : "";
            return string.IsNullOrEmpty(monthName) ? year.ToString() : $"{monthName} {year}";
        }

        private static string FormatShamsi(long dateLong)
        {
            if (dateLong <= 0) return string.Empty;
            string d = dateLong.ToString();
            return d.Length == 8 ? $"{d[..4]}/{d.Substring(4, 2)}/{d.Substring(6, 2)}" : d;
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
                    var periodStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_PERIOD WITH (UPDLOCK) WHERE PER_ID = @PER_ID",
                        new { request.PER_ID }, tran);

                    if (periodStatus == null || periodStatus == 1)
                        throw new InvalidOperationException("دوره کارکرد هنوز باز است. لطفاً ابتدا در تب کارکرد، دکمه 'بستن کارکرد' را بزنید.");

                    var latestRunStatus = await conn.QuerySingleOrDefaultAsync<byte?>(
                        "SELECT STATUS FROM PAY2_RUN WITH (UPDLOCK) WHERE PER_ID = @PER_ID AND IS_LATEST = 1",
                        new { request.PER_ID }, tran);

                    if (latestRunStatus >= 2)
                        throw new InvalidOperationException("برای این دوره فیش حقوقیِ تأیید شده وجود دارد. امکان بازمحاسبه نیست.");

                    bool isReRun = latestRunStatus.HasValue;

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
                    p.Add("IsReRun", isReRun);

                    return await conn.QuerySingleAsync<int>(sql, p, tran, commandTimeout: 180);
                });

                return Ok(newRunId);
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطای موتور محاسبه: " + ex.Message); }
        }

        [HttpPut("{runId:int}/unfinalize-deed")]
        public async Task<IActionResult> UnfinalizeDeed(int runId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var runInfo = await conn.QuerySingleOrDefaultAsync(
                        @"SELECT R.STATUS, R.PER_ID, R.DEED_ID_SAL, P.DEED_N_S_PAY
                          FROM PAY2_RUN R
                          INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                          WHERE R.RUN_ID = @runId",
                        new { runId }, tran);

                    if (runInfo == null)
                        throw new InvalidOperationException("محاسبه‌ای با این شناسه یافت نشد.");

                    byte status = (byte)runInfo.STATUS;
                    if (status != 3)
                        throw new InvalidOperationException("این عملیات فقط برای اجراهایی که سند صادر کرده‌اند (وضعیت ۳) مجاز است.");

                    double? deedNs = (double?)runInfo.DEED_N_S_PAY;

                    if (deedNs.HasValue && deedNs.Value > 0)
                    {
                        if (!await IsAccountingDeedEditable(conn, deedNs.Value, tran))
                            throw new InvalidOperationException(
                                "این سند در سیستم حسابداری بررسی و قطعی شده است. امکان لغو صدور سند از طریق ماژول حقوق وجود ندارد.");

                        await conn.ExecuteAsync("DELETE FROM DEED_DTL WHERE N_S = @N_S", new { N_S = deedNs.Value }, tran);
                        await conn.ExecuteAsync("DELETE FROM DEED_HED WHERE N_S = @N_S", new { N_S = deedNs.Value }, tran);
                    }

                    int perId = (int)runInfo.PER_ID;

                    await conn.ExecuteAsync(@"
                        UPDATE PAY2_RUN
                        SET STATUS = 2, DEED_ID_SAL = NULL, DEED_MODE = NULL, DEED_GENERATOR_VERSION = NULL,
                            NOTES = SUBSTRING(ISNULL(NOTES,'') + N' | DeedUnfinalized by ' + CAST(@userCod AS NVARCHAR), 1, 300)
                        WHERE RUN_ID = @runId;

                        UPDATE PAY2_PERIOD
                        SET STATUS = 3, DEED_N_S_PAY = NULL
                        WHERE PER_ID = @perId;",
                        new { runId, perId, userCod }, tran);
                });

                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطا در لغو صدور سند: " + ex.Message); }
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
    }
}
