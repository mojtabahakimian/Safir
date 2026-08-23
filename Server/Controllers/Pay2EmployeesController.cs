using Dapper;
using FuzzySharp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using QuestPDF.Fluent;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Salary;
using System.Data;
using System.Security.Claims;
using static Safir.Shared.Models.Salary.Pay2LeaveDto;

using Safir.Server.Security;
using Safir.Server.Services;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/employees")]
    [Authorize]
    public class Pay2EmployeesController : ControllerBase
    {
        private readonly IDatabaseService _db;
        private readonly IMemoryCache _cache;
        public Pay2EmployeesController(IDatabaseService db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        private static readonly HashSet<string> _autoDeductionCodes = new(StringComparer.OrdinalIgnoreCase)
            { "INS_DED", "TAX_DED", "LOAN_DED", "ADVANCE_DED" };

        private static bool IsPersianLeapYear(int year) => ((25 * year + 11) % 33) < 8;

        private static bool IsValidShamsiDate(long value)
        {
            int year = (int)(value / 10000);
            int month = (int)((value / 100) % 100);
            int day = (int)(value % 100);
            if (year < 1200 || year > 1600 || month is < 1 or > 12 || day < 1) return false;
            int maxDay = month <= 6 ? 31 : month <= 11 ? 30 : IsPersianLeapYear(year) ? 30 : 29;
            return day <= maxDay;
        }

        private static long DecrementShamsiDate(long shamsiDate)
        {
            int y = (int)(shamsiDate / 10000);
            int m = (int)((shamsiDate % 10000) / 100);
            int d = (int)(shamsiDate % 100);

            d--;
            if (d < 1)
            {
                m--;
                if (m < 1) { m = 12; y--; }
                d = m <= 6 ? 31 : m <= 11 ? 30 : IsPersianLeapYear(y) ? 30 : 29;
            }
            return y * 10000L + m * 100 + d;
        }

        // این متد را داخل کلاس Pay2EmployeesController اضافه کنید:
        [HttpGet("jobs-lookup")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<LookupDto<int>>>> GetJobsLookup([FromQuery] string? searchTerm)
        {
            try
            {
                // ⚠️ بدون TOP: محدودیت قبلی (TOP 50) باعث می‌شد بخشی از مشاغل (مثل «کارمند اداری») هرگز در لیست ظاهر نشوند
                string sql = @"
            SELECT JOB_ID AS Id,
                   JOB_NAME AS Name
            FROM   PAY2_JOB
            WHERE  IS_ACTIVE = 1";

                object? parameters = null;

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    sql += " AND (JOB_NAME LIKE @Search OR JOB_CODE LIKE @Search)";
                    parameters = new { Search = $"%{searchTerm.Trim()}%" };
                }

                sql += " ORDER BY JOB_NAME;";

                // ✅ اصلاح شد: فراخوانی دقیق با ۲ پارامتر مطابق ساختار اصلی پروژه شما
                var data = await _db.DoGetDataSQLAsync<LookupDto<int>>(sql, parameters);

                return Ok(data ?? Enumerable.Empty<LookupDto<int>>());
            }
            catch (Exception ex)
            {
                // لاگ کردن خطا در کنسول سرور جهت مانیتورینگ
                Console.WriteLine($"[PAY2] Error in jobs-lookup query: {ex.Message}");

                // بازگرداندن یک لیست خالی به جای کرش دادن با خطای ۵۰۰ سیستمی
                return Ok(Enumerable.Empty<LookupDto<int>>());
            }
        }

        [HttpGet]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2EmployeeDto>>> GetAll()
        {
            var (noScope, allowedWsIds) = await GetWorkshopScopeAsync();

            const string sql = @"
                SELECT e.*, w.WS_NAME AS WorkshopName, j.JOB_NAME AS JobName
                FROM PAY2_EMPLOYEE e
                LEFT JOIN PAY2_WORKSHOP w ON e.WS_ID = w.WS_ID
                LEFT JOIN PAY2_JOB j ON e.JOB_ID = j.JOB_ID
                WHERE (@noScope = 1 OR e.WS_ID IN @allowedWsIds)
                ORDER BY e.IS_ACTIVE DESC, e.EMP_ID DESC";

            var data = await _db.DoGetDataSQLAsync<Pay2EmployeeDto>(sql, new { noScope, allowedWsIds });
            return Ok(data);
        }

        /// <summary>محدوده کارگاه‌های مجاز کاربر جاری برای فیلترکردن لیست‌ها</summary>
        private async Task<(bool noScope, List<int> allowedWsIds)> GetWorkshopScopeAsync()
        {
            int userCoScope = int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0");
            var access = await HttpContext.RequestServices
                .GetRequiredService<IPay2AccessService>().GetAccessAsync(userCoScope);
            bool noScope = !access.AclEnforced || !access.WsScopeEnforced;
            // لیست خالی در Dapper به IN () تبدیل می‌شود؛ مقدار ناموجود -1 از خطا جلوگیری می‌کند
            return (noScope, access.AllowedWorkshopIds.DefaultIfEmpty(-1).ToList());
        }

        [HttpPost("save")]
        public async Task<ActionResult<int>> SaveEmployee([FromBody] Pay2EmployeeDto emp)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            var __accessEmp = HttpContext.RequestServices.GetRequiredService<IPay2AccessService>();
            var __needEmp = emp.EMP_ID == 0 ? Pay2Perm.Inp : Pay2Perm.Upd;
            if (!await __accessEmp.HasAndAuditAsync(HttpContext, userCod, Pay2Forms.Employee, __needEmp))
                return StatusCode(403, emp.EMP_ID == 0
                    ? "برای ثبت پرسنل جدید دسترسی لازم را ندارید."
                    : "برای ویرایش اطلاعات پرسنل دسترسی لازم را ندارید.");

            await HttpContext.RequestServices.GetRequiredService<Pay2ScopeResolver>()
                .EnsureWorkshopAsync(userCod, emp.WS_ID);

            try
            {
                var empId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var duplicate = await FindDuplicateEmployeeMessageAsync(conn, tran, emp);
                    if (duplicate is not null)
                        throw new InvalidOperationException(duplicate);

                    int currentId = emp.EMP_ID;
                    if (emp.EMP_ID == 0)
                    {
                        const string insertSql = @"
                            INSERT INTO PAY2_EMPLOYEE 
                            (EMP_CODE, WS_ID, FIRST_NAME, LAST_NAME, FATHER_NAME, NATIONAL_CODE, ID_NUMBER, BIRTH_PLACE, BIRTH_DATE, GENDER, NATIONALITY, IS_JANBAZ, MARITAL, HIRE_DATE, FIRE_DATE, JOB_ID, UNIT, EDU_LEVEL, INS_CODE, INS_TYPE, TAX_EXEMPT, REGION_DEPRIVATION, ACC_T, CARD_NO, MOBILE, BANK_ACC, IBAN, IS_ACTIVE, NOTES, CREATED_BY)
                            OUTPUT INSERTED.EMP_ID
                            VALUES (@EMP_CODE, @WS_ID, @FIRST_NAME, @LAST_NAME, @FATHER_NAME, @NATIONAL_CODE, @ID_NUMBER, @BIRTH_PLACE, @BIRTH_DATE, @GENDER, @NATIONALITY, @IS_JANBAZ, @MARITAL, @HIRE_DATE, @FIRE_DATE, @JOB_ID, @UNIT, @EDU_LEVEL, @INS_CODE, @INS_TYPE, @TAX_EXEMPT, @REGION_DEPRIVATION, @ACC_T, @CARD_NO, @MOBILE, @BANK_ACC, @IBAN, @IS_ACTIVE, @NOTES, @User)";

                        var p = new DynamicParameters(emp);
                        p.Add("User", userCod);
                        currentId = await conn.QueryFirstAsync<int>(insertSql, p, tran);
                    }
                    else
                    {
                        const string updateSql = @"
                            UPDATE PAY2_EMPLOYEE SET 
                            EMP_CODE=@EMP_CODE, WS_ID=@WS_ID, FIRST_NAME=@FIRST_NAME, LAST_NAME=@LAST_NAME, FATHER_NAME=@FATHER_NAME, NATIONAL_CODE=@NATIONAL_CODE, ID_NUMBER=@ID_NUMBER, BIRTH_PLACE=@BIRTH_PLACE, BIRTH_DATE=@BIRTH_DATE, GENDER=@GENDER, NATIONALITY=@NATIONALITY, IS_JANBAZ=@IS_JANBAZ, MARITAL=@MARITAL, HIRE_DATE=@HIRE_DATE, FIRE_DATE=@FIRE_DATE, JOB_ID=@JOB_ID, UNIT=@UNIT, EDU_LEVEL=@EDU_LEVEL, INS_CODE=@INS_CODE, INS_TYPE=@INS_TYPE, TAX_EXEMPT=@TAX_EXEMPT, REGION_DEPRIVATION=@REGION_DEPRIVATION, ACC_T=@ACC_T, CARD_NO=@CARD_NO, MOBILE=@MOBILE, BANK_ACC=@BANK_ACC, IBAN=@IBAN, IS_ACTIVE=@IS_ACTIVE, NOTES=@NOTES
                            WHERE EMP_ID=@EMP_ID";
                        await conn.ExecuteAsync(updateSql, emp, tran);
                    }
                    return currentId;
                });
                return Ok(empId);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return BadRequest(DescribeEmployeeDuplicate(ex.Message));
            }
            catch (System.Data.SqlClient.SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return BadRequest(DescribeEmployeeDuplicate(ex.Message));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // پیش‌بررسی تکراری بودن کد پرسنلی و کد ملی (هم در ثبت جدید و هم در ویرایش)
        // تا خطای خام SqlException مربوط به UX_EMP_NATCODE / UQ_EMP_CODE به کاربر نمایش داده نشود
        private static async Task<string?> FindDuplicateEmployeeMessageAsync(IDbConnection conn, IDbTransaction tran, Pay2EmployeeDto emp)
        {
            const string sql = @"
                SELECT TOP 1 LAST_NAME + N' ' + FIRST_NAME AS FULL_NAME, EMP_CODE, NATIONAL_CODE
                FROM PAY2_EMPLOYEE
                WHERE EMP_ID <> @EMP_ID
                  AND (
                        (@NATIONAL_CODE IS NOT NULL AND NATIONAL_CODE = @NATIONAL_CODE)
                     OR (@EMP_CODE IS NOT NULL AND EMP_CODE = @EMP_CODE)
                  )";

            var nationalCode = string.IsNullOrWhiteSpace(emp.NATIONAL_CODE) ? null : emp.NATIONAL_CODE.Trim();
            var empCode = string.IsNullOrWhiteSpace(emp.EMP_CODE) ? null : emp.EMP_CODE.Trim();

            if (nationalCode is null && empCode is null)
                return null;

            var hit = await conn.QueryFirstOrDefaultAsync<DuplicateEmployeeRow>(
                sql, new { EMP_ID = emp.EMP_ID, NATIONAL_CODE = nationalCode, EMP_CODE = empCode }, tran);

            if (hit is null)
                return null;

            var owner = string.IsNullOrWhiteSpace(hit.FULL_NAME) ? "پرسنل دیگری" : hit.FULL_NAME!.Trim();

            if (nationalCode is not null && string.Equals(hit.NATIONAL_CODE?.Trim(), nationalCode, StringComparison.Ordinal))
                return $"کد ملی وارد شده متعلق به پرسنل دیگری است ({owner}). لطفاً کد ملی صحیح را وارد کنید.";

            return $"کد پرسنلی وارد شده قبلاً برای {owner} ثبت شده است. لطفاً کد پرسنلی دیگری انتخاب کنید.";
        }

        private sealed class DuplicateEmployeeRow
        {
            public string? FULL_NAME { get; set; }
            public string? EMP_CODE { get; set; }
            public string? NATIONAL_CODE { get; set; }
        }

        private static string DescribeEmployeeDuplicate(string sqlMessage)
            => sqlMessage.Contains("NATCODE", StringComparison.OrdinalIgnoreCase)
                ? "کد ملی وارد شده متعلق به پرسنل دیگری است. لطفاً کد ملی صحیح را وارد کنید."
                : "کد پرسنلی یا کد ملی وارد شده در سیستم تکراری است.";

        // --- بخش احکام پرسنل ---
        [HttpGet("{empId:int}/decrees")]
        [Pay2Authorize(Pay2Forms.Decree, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2DecreeDto>>> GetDecrees(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = @"
                SELECT D.*, T.TMPL_NAME AS TemplateName
                FROM PAY2_DECREE D
                LEFT JOIN PAY2_ITEM_TEMPLATE T ON D.TMPL_ID = T.TMPL_ID
                WHERE D.EMP_ID = @empId ORDER BY D.EFF_FROM DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2DecreeDto>(sql, new { empId }));
        }

        [HttpPost("decree/save")]
        [Pay2Authorize(Pay2Forms.Decree, Pay2Perm.Inp)]
        public async Task<ActionResult<int>> SaveDecree([FromBody] Pay2DecreeDto decree)
        {
            if (decree.IS_CONFIRMED == true)
            {
                int __uCodDecree = int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0");
                var __accessServiceDecree = HttpContext.RequestServices.GetRequiredService<IPay2AccessService>();
                if (!await __accessServiceDecree.HasAndAuditAsync(HttpContext, __uCodDecree, Pay2Forms.ActDecreeConfirm, Pay2Perm.Run))
                {
                    return StatusCode(403, "برای تأیید نهایی حکم کارگزینی دسترسی لازم را ندارید.");
                }
            }

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            if (!IsValidShamsiDate(decree.EFF_FROM) ||
                (decree.EFF_TO.HasValue && (!IsValidShamsiDate(decree.EFF_TO.Value) || decree.EFF_TO.Value < decree.EFF_FROM)) ||
                !IsValidShamsiDate(decree.ISSUED_DATE))
                return BadRequest("تاریخ صدور یا بازه اثر حکم شمسی معتبر نیست.");

            try
            {
                var decId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    int currentDecId = decree.DEC_ID;
                    decree.SHIFT_MODE = string.IsNullOrWhiteSpace(decree.SHIFT_MODE) ? null : decree.SHIFT_MODE;

                    if (currentDecId == 0) // درج جدید
                    {
                        if (decree.IS_CONFIRMED)
                        {
                            const string finalRunConflictSql = @"
                                SELECT COUNT(1) FROM PAY2_RUN R
                                INNER JOIN PAY2_PERIOD P ON P.PER_ID=R.PER_ID
                                INNER JOIN PAY2_RUN_LINE RL ON RL.RUN_ID=R.RUN_ID AND RL.EMP_ID=@EMP_ID
                                WHERE R.STATUS>=2 AND P.PERIOD_DATE/100>=@EFF_FROM/100
                                  AND (@EFF_TO IS NULL OR P.PERIOD_DATE/100<=@EFF_TO/100)";
                            if (await conn.QuerySingleAsync<int>(finalRunConflictSql, new { decree.EMP_ID, decree.EFF_FROM, decree.EFF_TO }, tran) > 0)
                                throw new InvalidOperationException("بازه حکم با Run قطعی گذشته تداخل دارد؛ برای آن بازه امکان صدور حکم تأییدشده وجود ندارد.");
                        }

                        // فقط حکم تأییدشده جدید، حکم باز قبلی را در مرز صحیح شمسی می‌بندد.
                        string closePrevSql = @"
                    UPDATE PAY2_DECREE 
                    SET EFF_TO = @PrevTo 
                    WHERE DEC_ID = (SELECT TOP 1 DEC_ID FROM PAY2_DECREE WHERE EMP_ID = @EmpId AND IS_CONFIRMED = 1 AND EFF_TO IS NULL AND EFF_FROM < @NewFrom ORDER BY EFF_FROM DESC)";

                        long prevTo = DecrementShamsiDate(decree.EFF_FROM);
                        if (decree.IS_CONFIRMED)
                            await conn.ExecuteAsync(closePrevSql, new { PrevTo = prevTo, EmpId = decree.EMP_ID, NewFrom = decree.EFF_FROM }, tran);

                        if (decree.IS_CONFIRMED)
                        {
                            const string overlapSql = @"
                                SELECT COUNT(1) FROM PAY2_DECREE WITH (UPDLOCK,HOLDLOCK)
                                WHERE EMP_ID=@EMP_ID AND IS_CONFIRMED=1
                                  AND EFF_FROM<=ISNULL(@EFF_TO,99991231)
                                  AND ISNULL(EFF_TO,99991231)>=@EFF_FROM";
                            if (await conn.QuerySingleAsync<int>(overlapSql, new { decree.EMP_ID, decree.EFF_FROM, decree.EFF_TO }, tran) > 0)
                                throw new InvalidOperationException("بازه این حکم با یک حکم تأییدشده دیگر هم‌پوشانی دارد.");
                        }

                        const string insertSql = @"
                    INSERT INTO PAY2_DECREE (EMP_ID, WS_ID, ISSUED_DATE, EFF_FROM, EFF_TO, EDU_LEVEL, MARITAL, IS_MANAGER, TMPL_ID, IS_CONFIRMED, CREATED_AT, CREATED_BY, NOTES, SHIFT_MODE)
                    OUTPUT INSERTED.DEC_ID
                    VALUES (@EMP_ID, @WS_ID, @ISSUED_DATE, @EFF_FROM, @EFF_TO, @EDU_LEVEL, @MARITAL, @IS_MANAGER, @TMPL_ID, @IS_CONFIRMED, GETDATE(), @User, @NOTES, @SHIFT_MODE)";

                        var p = new DynamicParameters(decree);
                        p.Add("User", userCod);
                        currentDecId = await conn.QueryFirstAsync<int>(insertSql, p, tran);

                        // درج اتوماتیک اقلام ریالی از روی قالب
                        if (decree.TMPL_ID.HasValue && decree.TMPL_ID > 0)
                        {
                            string sqlLines = @"
                        INSERT INTO PAY2_DECREE_LINE (DEC_ID, ITEM_ID, AMOUNT, INS_OV, TAX_OV, BASIS_OV, SHIFT_MODE_OV)
                        SELECT @NewDecId, ITEM_ID, DEF_AMOUNT, INS_OV, TAX_OV, BASIS_OV, SHIFT_MODE_OV
                        FROM PAY2_ITEM_TMPL_LINE WHERE TMPL_ID = @TmplId";
                            await conn.ExecuteAsync(sqlLines, new { NewDecId = currentDecId, TmplId = decree.TMPL_ID.Value }, tran);
                        }
                    }
                    else // ویرایش
                    {
                        var dbDecree = await conn.QuerySingleOrDefaultAsync(
                            "SELECT IS_CONFIRMED, EMP_ID FROM PAY2_DECREE WHERE DEC_ID = @DEC_ID",
                            new { decree.DEC_ID }, tran)
                            ?? throw new KeyNotFoundException();
                        bool wasConfirmed = (bool)dbDecree.IS_CONFIRMED;
                        int dbEmpId = (int)dbDecree.EMP_ID;
                        if (dbEmpId != decree.EMP_ID)
                            throw new UnauthorizedAccessException();

                        // فقط احکام تأییدشده مسدود می‌شوند — احکام تأییدنشده هرگز در حقوق استفاده نشده‌اند
                        // از تاریخ‌های ذخیره‌شده در DB استفاده می‌شود تا bypass از طریق دستکاری تاریخ ممکن نباشد
                        if (wasConfirmed)
                        {
                            const string checkUsageSql = @"
                        SELECT COUNT(1)
                        FROM PAY2_RUN R
                        INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                        INNER JOIN PAY2_DECREE D ON D.DEC_ID = @DEC_ID
                        WHERE R.STATUS >= 2
                          AND (P.PERIOD_DATE / 100) >= (D.EFF_FROM / 100)
                          AND (D.EFF_TO IS NULL OR (P.PERIOD_DATE / 100) <= (D.EFF_TO / 100))
                          AND R.RUN_ID IN (SELECT RUN_ID FROM PAY2_RUN_LINE WHERE EMP_ID = D.EMP_ID)";

                            int usedInFinalRun = await conn.QuerySingleAsync<int>(
                                checkUsageSql, new { DEC_ID = currentDecId }, tran);

                            if (usedInFinalRun > 0)
                                throw new InvalidOperationException("این حکم در ماه‌های گذشته جهت صدور حقوق قطعی استفاده شده است. امکان ویرایش یا لغو تایید آن به هیچ وجه وجود ندارد. برای تغییر حقوق، باید یک حکم جدید صادر کنید.");
                        }

                        // 🛠 رفع بن‌بست منطقی: فقط زمانی آپدیت را به NOTES محدود کن که کاربر هنوز می‌خواهد حکم تایید شده بماند.
                        // اگر کاربر تیک تایید را در UI برداشته باشد (decree.IS_CONFIRMED == false)، باید اجازه دهیم کل حکم از قفل خارج شود.
                        if (wasConfirmed && decree.IS_CONFIRMED)
                        {
                            const string updateNotesOnlySql = "UPDATE PAY2_DECREE SET NOTES=@NOTES WHERE DEC_ID=@DEC_ID";
                            await conn.ExecuteAsync(updateNotesOnlySql, new { decree.NOTES, decree.DEC_ID }, tran);
                        }
                        else
                        {
                            // گارد تأیید مجدد: احکامی که پس از unlock دوباره تأیید می‌شوند
                            // باید تاریخ‌های ورودی بررسی شود تا مانع backdating به ماه‌های قطعی‌شده شویم
                            if (!wasConfirmed && decree.IS_CONFIRMED)
                            {
                                const string checkReconfirmSql = @"
                        SELECT COUNT(1)
                        FROM PAY2_RUN R
                        INNER JOIN PAY2_PERIOD P ON R.PER_ID = P.PER_ID
                        WHERE R.STATUS >= 2
                          AND (P.PERIOD_DATE / 100) >= (@EFF_FROM / 100)
                          AND (@EFF_TO IS NULL OR (P.PERIOD_DATE / 100) <= (@EFF_TO / 100))
                          AND R.RUN_ID IN (SELECT RUN_ID FROM PAY2_RUN_LINE WHERE EMP_ID = @EMP_ID)";

                                int reconfirmConflict = await conn.QuerySingleAsync<int>(
                                    checkReconfirmSql,
                                    new { EFF_FROM = decree.EFF_FROM, EFF_TO = (long?)decree.EFF_TO, EMP_ID = dbEmpId },
                                    tran);

                                if (reconfirmConflict > 0)
                                    throw new InvalidOperationException("بازه زمانی این حکم با ماه‌هایی که حقوق آنها قطعی شده تداخل دارد. لطفاً بازه تاریخی را اصلاح کنید یا با دوره‌های تأیید نشده کار کنید.");
                            }

                            if (decree.IS_CONFIRMED)
                            {
                                const string overlapSql = @"
                                    SELECT COUNT(1) FROM PAY2_DECREE WITH (UPDLOCK,HOLDLOCK)
                                    WHERE EMP_ID=@EMP_ID AND DEC_ID<>@DEC_ID AND IS_CONFIRMED=1
                                      AND EFF_FROM<=ISNULL(@EFF_TO,99991231)
                                      AND ISNULL(EFF_TO,99991231)>=@EFF_FROM";
                                if (await conn.QuerySingleAsync<int>(overlapSql, new { decree.EMP_ID, decree.DEC_ID, decree.EFF_FROM, decree.EFF_TO }, tran) > 0)
                                    throw new InvalidOperationException("بازه این حکم با یک حکم تأییدشده دیگر هم‌پوشانی دارد.");
                            }

                            const string updateSql = @"
                        UPDATE PAY2_DECREE
                        SET ISSUED_DATE=@ISSUED_DATE, EFF_FROM=@EFF_FROM, EFF_TO=@EFF_TO,
                            EDU_LEVEL=@EDU_LEVEL, MARITAL=@MARITAL, IS_MANAGER=@IS_MANAGER,
                            IS_CONFIRMED=@IS_CONFIRMED, NOTES=@NOTES, SHIFT_MODE=@SHIFT_MODE
                        WHERE DEC_ID=@DEC_ID";
                            await conn.ExecuteAsync(updateSql, decree, tran);
                        }
                    }

                    return currentDecId;
                });

                return Ok(decId);
            }
            catch (KeyNotFoundException) { return NotFound("حکم مورد نظر یافت نشد."); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطای سیستمی: " + ex.Message); }
        }


        // اضافه کردن به فایل Pay2EmployeesController.cs

        [HttpGet("templates-lookup")]
        [Pay2Authorize(Pay2Forms.ItemDef, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<LookupDto<int>>>> GetTemplatesLookup()
        {
            // خواندن قالب‌های فعال برای پر کردن Dropdown — WS_ID خالی یعنی قالب عمومی
            var (noScope, allowedWsIds) = await GetWorkshopScopeAsync();
            const string sql = @"
                SELECT TMPL_ID AS Id, TMPL_NAME AS Name
                FROM PAY2_ITEM_TEMPLATE
                WHERE IS_ACTIVE = 1
                  AND (@noScope = 1 OR WS_ID IS NULL OR WS_ID IN @allowedWsIds)
                ORDER BY TMPL_NAME";
            return Ok(await _db.DoGetDataSQLAsync<LookupDto<int>>(sql, new { noScope, allowedWsIds }));
        }

        [HttpDelete("decree/{decId:int}")]
        [Pay2Authorize(Pay2Forms.Decree, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteDecree(int decId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Decree, decId);
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var decRow = await conn.QuerySingleOrDefaultAsync(
                        "SELECT IS_CONFIRMED FROM PAY2_DECREE WHERE DEC_ID = @decId", new { decId }, tran)
                        ?? throw new KeyNotFoundException();
                    if ((bool)decRow.IS_CONFIRMED)
                        throw new InvalidOperationException("این حکم تأیید نهایی شده است. برای حذف آن، ابتدا باید آن را از حالت تایید خارج کنید.");

                    await conn.ExecuteAsync("DELETE FROM PAY2_DECREE_LINE WHERE DEC_ID = @decId", new { decId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_DECREE WHERE DEC_ID = @decId", new { decId }, tran);
                });
                return Ok();
            }
            catch (KeyNotFoundException) { return NotFound("حکم مورد نظر یافت نشد."); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }


        [HttpGet("itemdefs-lookup")]
        [Pay2Authorize(Pay2Forms.ItemDef, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<LookupDto<int>>>> GetItemDefsLookup()
        {
            // فقط آیتم‌های پرداختی (نوع 1 و 2) را می‌آوریم و کسورات اتوماتیک را فیلتر می‌کنیم
            const string sql = @"
        SELECT ITEM_ID AS Id, ITEM_NAME AS Name 
        FROM PAY2_ITEM_DEF 
        WHERE IS_ACTIVE = 1 
          AND ITEM_TYPE IN (1, 2) 
          AND ITEM_CODE NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')
        ORDER BY SORT_ORDER, ITEM_NAME";

            return Ok(await _db.DoGetDataSQLAsync<LookupDto<int>>(sql));
        }

        [HttpGet("decree/{decId:int}/lines")]
        [Pay2Authorize(Pay2Forms.Decree, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2DecreeLineDto>>> GetDecreeLines(int decId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Decree, decId);
            const string sql = @"
                SELECT L.DEC_ID, L.ITEM_ID, I.ITEM_NAME, L.AMOUNT, L.NOMINAL_AMOUNT_OV, L.OFFICIAL_AMOUNT_OV, L.INS_OV, L.TAX_OV, L.BASIS_OV, L.SHIFT_MODE_OV
                FROM PAY2_DECREE_LINE L
                INNER JOIN PAY2_ITEM_DEF I ON L.ITEM_ID = I.ITEM_ID
                WHERE L.DEC_ID = @decId
                ORDER BY I.SORT_ORDER";

            return Ok(await _db.DoGetDataSQLAsync<Pay2DecreeLineDto>(sql, new { decId }));
        }

        [HttpPost("decree/line/save")]
        [Pay2Authorize(Pay2Forms.Decree, Pay2Perm.Upd)]
        public async Task<IActionResult> SaveDecreeLine([FromBody] Pay2DecreeLineDto line)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    line.SHIFT_MODE_OV = string.IsNullOrWhiteSpace(line.SHIFT_MODE_OV) ? null : line.SHIFT_MODE_OV;
                    var decHeader = await conn.QuerySingleOrDefaultAsync(
                        "SELECT IS_CONFIRMED, EFF_FROM, EFF_TO FROM PAY2_DECREE WHERE DEC_ID = @DEC_ID", new { line.DEC_ID }, tran)
                        ?? throw new KeyNotFoundException();
                    if ((bool)decHeader.IS_CONFIRMED)
                        throw new InvalidOperationException("این حکم قفل (تأیید نهایی) شده است! برای افزودن یا تغییر مبالغ، باید ابتدا در صفحه قبل، تیک تایید این حکم را بردارید.");

                    // اعتبارسنجی آیتم: فعال بودن، نوع پرداختی، عدم کسر اتوماتیک
                    var itemInfo = await conn.QuerySingleOrDefaultAsync(
                        "SELECT ITEM_TYPE, ITEM_CODE, IS_ACTIVE FROM PAY2_ITEM_DEF WHERE ITEM_ID = @ITEM_ID",
                        new { line.ITEM_ID }, tran)
                        ?? throw new InvalidOperationException("آیتم حقوقی مورد نظر یافت نشد.");
                    if (!(bool)itemInfo.IS_ACTIVE)
                        throw new InvalidOperationException("آیتم حقوقی مورد نظر غیرفعال است.");
                    byte itemType = (byte)itemInfo.ITEM_TYPE;
                    if (itemType != 1 && itemType != 2)
                        throw new InvalidOperationException("فقط آیتم‌های پرداختی (نوع ۱ و ۲) در احکام مجاز هستند.");
                    string? itemCode = (string?)itemInfo.ITEM_CODE;
                    if ((line.INS_OV == true || line.TAX_OV == true) && Pay2NonSubjectRule.IsRuleItem(itemCode))
                    {
                        long? decreeTo = (long?)decHeader.EFF_TO;
                        var rule = await Pay2NonSubjectRule.LoadAsync(conn, tran);

                        if (line.INS_OV == true && rule.BlocksInsurance(decreeTo))
                            throw new InvalidOperationException(Pay2NonSubjectRule.InsuranceDecreeError);
                        if (line.TAX_OV == true && rule.BlocksTax(decreeTo))
                            throw new InvalidOperationException(Pay2NonSubjectRule.TaxDecreeError);
                    }
                    if (itemCode == "SANOVAT_PAYE")
                    {
                        line.NOMINAL_AMOUNT_OV ??= line.AMOUNT;
                        line.OFFICIAL_AMOUNT_OV ??= line.AMOUNT;
                        if (line.NOMINAL_AMOUNT_OV < 0 || line.OFFICIAL_AMOUNT_OV < 0)
                            throw new InvalidOperationException("مقادیر اسمی و رسمی پایه سنوات نمی‌توانند منفی باشند.");
                        line.AMOUNT = line.OFFICIAL_AMOUNT_OV.Value;
                    }
                    else
                    {
                        line.NOMINAL_AMOUNT_OV = null;
                        line.OFFICIAL_AMOUNT_OV = null;
                    }
                    if (itemCode != null && _autoDeductionCodes.Contains(itemCode))
                        throw new InvalidOperationException("آیتم‌های کسر اتوماتیک را نمی‌توان به صورت دستی به حکم اضافه کرد.");

                    // اعتبارسنجی: مقدار اعشاری فقط برای آیتم حق شیفت درصدی مجاز است
                    if (line.AMOUNT != Math.Truncate(line.AMOUNT))
                    {
                        bool isShiftPct = await IsShiftPctItemAsync(conn, tran, line.ITEM_ID, decId: line.DEC_ID, tmplId: null, shiftModeOv: line.SHIFT_MODE_OV);
                        if (!isShiftPct)
                            throw new InvalidOperationException("مبلغ اعشاری فقط برای آیتم «حق شیفت درصدی» مجاز است.");
                    }

                    int count = await conn.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM PAY2_DECREE_LINE WHERE DEC_ID=@DEC_ID AND ITEM_ID=@ITEM_ID",
                        new { line.DEC_ID, line.ITEM_ID }, tran);

                    if (count == 0)
                    {
                        const string insertSql = @"INSERT INTO PAY2_DECREE_LINE (DEC_ID, ITEM_ID, AMOUNT, NOMINAL_AMOUNT_OV, OFFICIAL_AMOUNT_OV, INS_OV, TAX_OV, BASIS_OV, SHIFT_MODE_OV)
                                           VALUES (@DEC_ID, @ITEM_ID, @AMOUNT, @NOMINAL_AMOUNT_OV, @OFFICIAL_AMOUNT_OV, @INS_OV, @TAX_OV, @BASIS_OV, @SHIFT_MODE_OV)";
                        await conn.ExecuteAsync(insertSql, line, tran);
                    }
                    else
                    {
                        const string updateSql = @"UPDATE PAY2_DECREE_LINE 
                                           SET AMOUNT=@AMOUNT, NOMINAL_AMOUNT_OV=@NOMINAL_AMOUNT_OV, OFFICIAL_AMOUNT_OV=@OFFICIAL_AMOUNT_OV, INS_OV=@INS_OV, TAX_OV=@TAX_OV, BASIS_OV=@BASIS_OV, SHIFT_MODE_OV=@SHIFT_MODE_OV
                                           WHERE DEC_ID=@DEC_ID AND ITEM_ID=@ITEM_ID";
                        await conn.ExecuteAsync(updateSql, line, tran);
                    }
                });
                return Ok();
            }
            catch (KeyNotFoundException) { return NotFound("حکم مورد نظر یافت نشد."); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }


        [HttpDelete("decree/{decId:int}/line/{itemId:int}")]
        [Pay2Authorize(Pay2Forms.Decree, Pay2Perm.Upd)]
        public async Task<IActionResult> DeleteDecreeLine(int decId, int itemId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Decree, decId);
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var decRow = await conn.QuerySingleOrDefaultAsync(
                        "SELECT IS_CONFIRMED FROM PAY2_DECREE WHERE DEC_ID = @decId", new { decId }, tran)
                        ?? throw new KeyNotFoundException();
                    if ((bool)decRow.IS_CONFIRMED)
                        throw new InvalidOperationException("این حکم قفل (تأیید نهایی) شده است! اجازه حذف مبالغ آن را ندارید.");

                    const string sql = "DELETE FROM PAY2_DECREE_LINE WHERE DEC_ID=@decId AND ITEM_ID=@itemId";
                    await conn.ExecuteAsync(sql, new { decId, itemId }, tran);
                });
                return Ok();
            }
            catch (KeyNotFoundException) { return NotFound("حکم مورد نظر یافت نشد."); }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("lookup")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<LookupDto<int>>>> GetEmployeesLookup()
        {
            var (noScope, allowedWsIds) = await GetWorkshopScopeAsync();
            const string sql = @"
                SELECT EMP_ID AS Id, EMP_CODE + ' - ' + LAST_NAME + ' ' + FIRST_NAME AS Name
                FROM PAY2_EMPLOYEE
                WHERE IS_ACTIVE = 1 AND (@noScope = 1 OR WS_ID IN @allowedWsIds)";
            return Ok(await _db.DoGetDataSQLAsync<LookupDto<int>>(sql, new { noScope, allowedWsIds }));
        }

        [HttpGet("{empId:int}/leave-balance")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<int>> GetLeaveBalance(int empId, [FromQuery] int year, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = "SELECT ISNULL(BALANCE_MIN, 0) FROM PAY2_LEAVE_BAL WHERE EMP_ID = @empId AND YEAR = @year";
            var bal = await _db.DoGetDataSQLAsyncSingle<int?>(sql, new { empId, year });
            return Ok(bal ?? 0);
        }

        [HttpGet("{empId:int}/leaves")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2LeaveDto>>> GetLeaves(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = "SELECT * FROM PAY2_LEAVE WHERE EMP_ID = @empId ORDER BY START_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2LeaveDto>(sql, new { empId }));
        }

        [HttpPost("leave/save")]
        public async Task<IActionResult> SaveLeave([FromBody] Pay2LeaveDto leave)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            // ثبت برگه جدید نیاز به مجوز درج و ویرایش برگه موجود نیاز به مجوز ویرایش دارد
            var accessLeave = HttpContext.RequestServices.GetRequiredService<IPay2AccessService>();
            var neededLeavePerm = leave.LEV_ID == 0 ? Pay2Perm.Inp : Pay2Perm.Upd;
            if (!await accessLeave.HasAndAuditAsync(HttpContext, userCod, Pay2Forms.Employee, neededLeavePerm))
                return StatusCode(403, leave.LEV_ID == 0
                    ? "برای ثبت مرخصی جدید دسترسی لازم را ندارید."
                    : "برای ویرایش مرخصی دسترسی لازم را ندارید.");

            await HttpContext.RequestServices.GetRequiredService<Pay2ScopeResolver>()
                .EnsureWorkshopAsync(userCod, Pay2ScopeKind.Employee, leave.EMP_ID);

            // 🚀 فراخوانی سقف مرخصی ساعتی از تنظیمات داینامیک (پیش‌فرض 200 دقیقه)
            int maxHourlyMins = await _db.DoGetDataSQLAsyncSingle<int?>(
                "SELECT TRY_CAST(CFG_VALUE AS INT) FROM PAY2_CONFIG WHERE CFG_KEY = 'LEAVE_HOURLY_MAX_MINS'") ?? 200;

            // 🚀 اعتبارسنجی مرخصی ساعتی
            if (leave.LEV_TYPE == Pay2LeaveDto.HOURLY_TYPE)
            {
                if (leave.REQ_DAYS > 0)
                    return BadRequest("در مرخصی ساعتی امکان ثبت روز وجود ندارد.");

                int requestedMins = (leave.REQ_HOURS * 60) + leave.REQ_MINUTES;
                if (requestedMins > maxHourlyMins)
                {
                    int maxH = maxHourlyMins / 60;
                    int maxM = maxHourlyMins % 60;
                    return BadRequest($"مرخصی ساعتی نمی‌تواند بیشتر از {maxH} ساعت و {maxM} دقیقه ({maxHourlyMins} دقیقه) باشد.");
                }

                // مرخصی ساعتی همیشه تک‌روزه است
                leave.END_DATE = leave.START_DATE;
            }

            if (leave.REQ_MINUTES >= 60)
                return BadRequest("دقیقه باید کمتر از ۶۰ باشد.");

            try
            {
                if (leave.LEV_ID == 0)
                {
                    const string insertSql = @"
                INSERT INTO PAY2_LEAVE (EMP_ID, LEV_TYPE, REQUEST_DATE, START_DATE, END_DATE, REQ_DAYS, REQ_HOURS, REQ_MINUTES, BAL_BEFORE, DESCRIPTION, REFER_TO, STATUS, CREATED_AT, CREATED_BY)
                VALUES (@EMP_ID, @LEV_TYPE, @REQUEST_DATE, @START_DATE, @END_DATE, @REQ_DAYS, @REQ_HOURS, @REQ_MINUTES, @BAL_BEFORE, @DESCRIPTION, @REFER_TO, @STATUS, GETDATE(), @User)";

                    var p = new DynamicParameters(leave);
                    p.Add("User", userCod);
                    await _db.DoExecuteSQLAsync(insertSql, p);
                }
                else
                {
                    const string updateSql = @"
                UPDATE PAY2_LEAVE SET LEV_TYPE=@LEV_TYPE, START_DATE=@START_DATE, END_DATE=@END_DATE, REQ_DAYS=@REQ_DAYS, REQ_HOURS=@REQ_HOURS, REQ_MINUTES=@REQ_MINUTES, DESCRIPTION=@DESCRIPTION, REFER_TO=@REFER_TO, STATUS=@STATUS
                WHERE LEV_ID=@LEV_ID";
                    await _db.DoExecuteSQLAsync(updateSql, leave);
                }
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpDelete("leave/{levId:int}")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteLeave(int levId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Leave, levId);
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // 🚀 گارد امنیتی: جلوگیری از حذف مرخصی‌های تایید شده یا در جریان
                    var status = await conn.QuerySingleOrDefaultAsync<byte?>("SELECT STATUS FROM PAY2_LEAVE WHERE LEV_ID = @levId", new { levId }, tran);

                    if (status == null)
                        throw new InvalidOperationException("مرخصی یافت نشد.");

                    if (status > 1)
                        throw new InvalidOperationException("این مرخصی تأیید شده است یا در جریان می‌باشد و امکان حذف فیزیکی آن وجود ندارد.");

                    await conn.ExecuteAsync("DELETE FROM PAY2_LEAVE WHERE LEV_ID = @levId", new { levId }, tran);
                });
                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("{empId:int}/contracts")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2ContractDto>>> GetContracts(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = "SELECT * FROM PAY2_CONTRACT WHERE EMP_ID = @empId ORDER BY START_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2ContractDto>(sql, new { empId }));
        }

        [HttpPost("contract/save")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.Inp)]
        public async Task<IActionResult> SaveContract([FromBody] Pay2ContractDto contract)
        {
            try
            {
                if (contract.CON_ID == 0)
                {
                    const string insertSql = @"
                INSERT INTO PAY2_CONTRACT (EMP_ID, CON_TYPE, START_DATE, END_DATE, TRIAL_END, WEEKLY_HOURS, NOTES, CREATED_AT)
                VALUES (@EMP_ID, @CON_TYPE, @START_DATE, @END_DATE, @TRIAL_END, @WEEKLY_HOURS, @NOTES, GETDATE())";

                    await _db.DoExecuteSQLAsync(insertSql, contract);
                }
                else
                {
                    const string updateSql = @"
                UPDATE PAY2_CONTRACT 
                SET CON_TYPE=@CON_TYPE, START_DATE=@START_DATE, END_DATE=@END_DATE, TRIAL_END=@TRIAL_END, WEEKLY_HOURS=@WEEKLY_HOURS, NOTES=@NOTES
                WHERE CON_ID=@CON_ID";

                    await _db.DoExecuteSQLAsync(updateSql, contract);
                }
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("contract/{conId:int}")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteContract(int conId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Contract, conId);
            try
            {
                await _db.DoExecuteSQLAsync("DELETE FROM PAY2_CONTRACT WHERE CON_ID = @conId", new { conId });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{empId:int}/leave-balances")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2LeaveBalDto>>> GetLeaveBalances(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = "SELECT * FROM PAY2_LEAVE_BAL WHERE EMP_ID = @empId ORDER BY YEAR DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2LeaveBalDto>(sql, new { empId }));
        }

        [HttpGet("{empId:int}/leave-statement")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        [Pay2Authorize(Pay2Forms.Reports, Pay2Perm.See)]
        public async Task<ActionResult<Pay2LeaveStatementDto>> GetLeaveStatement(
            int empId, [FromQuery] int year, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            if (year is < 1200 or > 1600)
                return BadRequest("سال گزارش مرخصی معتبر نیست.");

            await scopeResolver.EnsureWorkshopAsync(
                int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"),
                Pay2ScopeKind.Employee,
                empId);

            var statement = await BuildLeaveStatementAsync(empId, year);
            return statement == null
                ? NotFound("پرسنل مورد نظر یافت نشد.")
                : Ok(statement);
        }

        [HttpGet("{empId:int}/leave-statement/pdf")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        [Pay2Authorize(Pay2Forms.Reports, Pay2Perm.See)]
        [Pay2Authorize(Pay2Forms.ActExport, Pay2Perm.Run)]
        public async Task<IActionResult> GetLeaveStatementPdf(
            int empId, [FromQuery] int year, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            if (year is < 1200 or > 1600)
                return BadRequest("سال گزارش مرخصی معتبر نیست.");

            await scopeResolver.EnsureWorkshopAsync(
                int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"),
                Pay2ScopeKind.Employee,
                empId);

            var statement = await BuildLeaveStatementAsync(empId, year);
            if (statement == null)
                return NotFound("پرسنل مورد نظر یافت نشد.");

            var pdf = new Safir.Server.Reports.LeaveStatementDocument(statement).GeneratePdf();
            return File(pdf, "application/pdf", $"LeaveStatement_{statement.EmployeeCode}_{year}.pdf");
        }

        [HttpGet("me/leave-statement")]
        public async Task<IActionResult> GetMyLeaveStatement([FromQuery] int year)
        {
            if (year is < 1200 or > 1600)
                return BadRequest("سال گزارش مرخصی معتبر نیست.");

            var resolution = await ResolveCurrentEmployeeAsync();
            if (resolution.Error != null)
                return resolution.Error;

            var statement = await BuildLeaveStatementAsync(resolution.EmployeeId!.Value, year);
            return statement == null
                ? NotFound("پرسنل متناظر با کاربر جاری یافت نشد.")
                : Ok(statement);
        }

        [HttpGet("me/leave-statement/pdf")]
        public async Task<IActionResult> GetMyLeaveStatementPdf([FromQuery] int year)
        {
            if (year is < 1200 or > 1600)
                return BadRequest("سال گزارش مرخصی معتبر نیست.");

            var resolution = await ResolveCurrentEmployeeAsync();
            if (resolution.Error != null)
                return resolution.Error;

            var statement = await BuildLeaveStatementAsync(resolution.EmployeeId!.Value, year);
            if (statement == null)
                return NotFound("پرسنل متناظر با کاربر جاری یافت نشد.");

            var pdf = new Safir.Server.Reports.LeaveStatementDocument(statement).GeneratePdf();
            return File(pdf, "application/pdf", $"LeaveStatement_{statement.EmployeeCode}_{year}.pdf");
        }

        private async Task<(int? EmployeeId, IActionResult? Error)> ResolveCurrentEmployeeAsync()
        {
            if (!int.TryParse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value, out var userCo))
                return (null, Unauthorized());

            const string sql = @"
                SELECT E.EMP_ID
                FROM dbo.SALA_DTL U
                INNER JOIN dbo.PAY2_EMPLOYEE E
                    ON NULLIF(LTRIM(RTRIM(U.HES)), N'') = NULLIF(LTRIM(RTRIM(E.ACC_T)), N'')
                WHERE U.IDD = @userCo";
            var matches = (await _db.DoGetDataSQLAsync<int>(sql, new { userCo })).Distinct().Take(2).ToList();

            if (matches.Count == 0)
                return (null, NotFound("حساب کاربری شما هنوز به پرونده پرسنلی PAY2 متصل نشده است."));
            if (matches.Count > 1)
                return (null, Conflict("کد حساب کاربری به بیش از یک پرونده پرسنلی متصل است؛ لطفاً با مدیر سیستم تماس بگیرید."));

            return (matches[0], null);
        }

        private async Task<Pay2LeaveStatementDto?> BuildLeaveStatementAsync(int empId, int year)
        {
            const string statementSql = @"
                SELECT E.EMP_CODE AS EmployeeCode,
                       LTRIM(RTRIM(E.FIRST_NAME + N' ' + E.LAST_NAME)) AS EmployeeName,
                       @year AS [Year],
                       ISNULL(LB.ENTITLEMENT_MIN, @defaultEntitlementMin) AS EntitlementMin,
                       ISNULL(LB.CARRIED_IN_MIN, 0) AS CarriedInMin,
                       ISNULL(LB.USED_MIN, 0) AS UsedMin,
                       ISNULL(LB.ENTITLEMENT_MIN, @defaultEntitlementMin) + ISNULL(LB.CARRIED_IN_MIN, 0) - ISNULL(LB.USED_MIN, 0) AS BalanceMin
                FROM dbo.PAY2_EMPLOYEE E
                LEFT JOIN dbo.PAY2_LEAVE_BAL LB ON LB.EMP_ID = E.EMP_ID AND LB.[YEAR] = @year
                WHERE E.EMP_ID = @empId";

            const string configSql = @"
                SELECT CFG_KEY, CFG_VALUE
                FROM dbo.PAY2_CONFIG
                WHERE CFG_KEY IN ('LEAVE_MINS_PER_DAY', 'LEAVE_CARRYOVER_MAX', 'LEAVE_ANNUAL_DAYS')";
            const string historySql = @"
                SELECT START_DATE, END_DATE, REQ_DAYS, REQ_HOURS, REQ_MINUTES, DESCRIPTION,
                       CAST(REQ_DAYS * @leaveMinsPerDay + REQ_HOURS * 60 + REQ_MINUTES AS INT) AS TotalDeductedMinutes
                FROM dbo.PAY2_LEAVE
                WHERE EMP_ID = @empId
                  AND START_DATE / 10000 = @year
                  AND LEV_TYPE IN (1, 6)
                  AND STATUS = 4
                ORDER BY START_DATE, LEV_ID";

            const string previousBalanceSql = @"
                SELECT ISNULL(ENTITLEMENT_MIN + CARRIED_IN_MIN - USED_MIN, 0)
                FROM dbo.PAY2_LEAVE_BAL
                WHERE EMP_ID = @empId AND [YEAR] = @previousYear";

            return await _db.ExecuteInTransactionAsync<Pay2LeaveStatementDto?>(async (conn, tran) =>
            {
                var config = (await conn.QueryAsync<Pay2ConfigValueDto>(configSql, transaction: tran))
                    .ToDictionary(x => x.CFG_KEY, x => x.CFG_VALUE, StringComparer.OrdinalIgnoreCase);
                var leaveMinsPerDay = ParsePositiveConfig(config, "LEAVE_MINS_PER_DAY", 440);
                var leaveAnnualDays = ParsePositiveConfig(config, "LEAVE_ANNUAL_DAYS", 26);
                var defaultEntitlementMin = (int)Math.Min((long)leaveMinsPerDay * leaveAnnualDays, int.MaxValue);

                var statement = await conn.QuerySingleOrDefaultAsync<Pay2LeaveStatementDto>(
                    statementSql, new { empId, year, defaultEntitlementMin }, tran);
                if (statement == null)
                    return null;

                statement.LeaveMinsPerDay = leaveMinsPerDay;
                statement.LeaveCarryoverMax = ParseNonNegativeConfig(config, "LEAVE_CARRYOVER_MAX", 9);

                statement.History = (await conn.QueryAsync<Pay2LeaveStatementLineDto>(
                    historySql, new { empId, year, leaveMinsPerDay = statement.LeaveMinsPerDay }, tran)).ToList();
                statement.HistoryRequestedMin = statement.History.Sum(x => x.TotalDeductedMinutes);

                statement.PreviousYearBalanceMin = await conn.QuerySingleOrDefaultAsync<int?>(
                    previousBalanceSql, new { empId, previousYear = year - 1 }, tran) ?? 0;
                statement.CarryoverLimitMin = (int)Math.Min(
                    (long)statement.LeaveCarryoverMax * statement.LeaveMinsPerDay, int.MaxValue);
                statement.EligibleCarryoverMin = Math.Min(
                    Math.Max(statement.PreviousYearBalanceMin, 0), statement.CarryoverLimitMin);
                statement.ExpiredCarryoverMin = Math.Max(
                    statement.PreviousYearBalanceMin - statement.CarriedInMin, 0);

                statement.PrintDate = $"{FormatShamsiDate(Safir.Shared.Utility.CL_Tarikh.GetCurrentPersianDateAsLong())} - {DateTime.Now:HH:mm:ss}";
                return statement;
            }, IsolationLevel.RepeatableRead);
        }

        private static int ParsePositiveConfig(
            IReadOnlyDictionary<string, string> config, string key, int fallback) =>
            config.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed > 0
                ? parsed
                : fallback;

        private static int ParseNonNegativeConfig(
            IReadOnlyDictionary<string, string> config, string key, int fallback) =>
            config.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed >= 0
                ? parsed
                : fallback;

        private static string FormatShamsiDate(long date)
        {
            var value = date.ToString("00000000");
            return $"{value[..4]}/{value.Substring(4, 2)}/{value.Substring(6, 2)}";
        }

        private sealed class Pay2ConfigValueDto
        {
            public string CFG_KEY { get; set; } = "";
            public string CFG_VALUE { get; set; } = "";
        }

        [HttpPost("leave-balance/save")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.Upd)]
        public async Task<IActionResult> SaveLeaveBalance([FromBody] Pay2LeaveBalDto bal)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // اول چک می‌کنیم آیا برای این سال رکوردی هست یا نه
                    int count = await conn.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM PAY2_LEAVE_BAL WHERE EMP_ID=@EMP_ID AND YEAR=@YEAR",
                        new { bal.EMP_ID, bal.YEAR }, tran);

                    if (count == 0)
                    {
                        // درج جدید
                        const string insertSql = @"
                    INSERT INTO PAY2_LEAVE_BAL (EMP_ID, YEAR, ENTITLEMENT_MIN, USED_MIN, CARRIED_IN_MIN, CARRIED_OUT_MIN, UPDATED_AT)
                    VALUES (@EMP_ID, @YEAR, @ENTITLEMENT_MIN, @USED_MIN, @CARRIED_IN_MIN, @CARRIED_OUT_MIN, GETDATE())";
                        await conn.ExecuteAsync(insertSql, bal, tran);
                    }
                    else
                    {
                        // آپدیت
                        const string updateSql = @"
                    UPDATE PAY2_LEAVE_BAL 
                    SET ENTITLEMENT_MIN=@ENTITLEMENT_MIN, USED_MIN=@USED_MIN, CARRIED_IN_MIN=@CARRIED_IN_MIN, CARRIED_OUT_MIN=@CARRIED_OUT_MIN, UPDATED_AT=GETDATE()
                    WHERE EMP_ID=@EMP_ID AND YEAR=@YEAR";
                        await conn.ExecuteAsync(updateSql, bal, tran);
                    }
                });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("leave-balance/{empId:int}/{year:int}")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteLeaveBalance(int empId, int year, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            try
            {
                await _db.DoExecuteSQLAsync("DELETE FROM PAY2_LEAVE_BAL WHERE EMP_ID = @empId AND YEAR = @year", new { empId, year });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpGet("{empId:int}/loans")]
        [Pay2Authorize(Pay2Forms.Loan, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2LoanDto>>> GetLoans(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = "SELECT * FROM PAY2_LOAN WHERE EMP_ID = @empId ORDER BY LOAN_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2LoanDto>(sql, new { empId }));
        }

        [HttpPost("loan/save")]
        [Pay2Authorize(Pay2Forms.Loan, Pay2Perm.Inp)]
        public async Task<IActionResult> SaveLoan([FromBody] Pay2LoanDto loan)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    int savedLoanId = loan.LOAN_ID;

                    if (loan.LOAN_ID == 0)
                    {
                        // 🟢 درج وام جدید
                        const string insertSql = @"
                    INSERT INTO PAY2_LOAN (EMP_ID, WS_ID, LOAN_TYPE, LOAN_DATE, AMOUNT, INSTALLMENT, TOTAL_INST, PAID_INST, FIRST_PAY, PURPOSE, IS_ACTIVE, CREATED_AT, CREATED_BY)
                    OUTPUT INSERTED.LOAN_ID
                    VALUES (@EMP_ID, @WS_ID, @LOAN_TYPE, @LOAN_DATE, @AMOUNT, @INSTALLMENT, @TOTAL_INST, 0, @FIRST_PAY, @PURPOSE, @IS_ACTIVE, GETDATE(), @User)";

                        var p = new DynamicParameters(loan);
                        p.Add("User", userCod);
                        savedLoanId = await conn.QuerySingleAsync<int>(insertSql, p, tran);

                        // فراخوانی SP تولید اقساط فقط برای وام جدید
                        await conn.ExecuteAsync("SP_PAY2_LOAN_GEN_SCHED", new { LOAN_ID = savedLoanId }, tran, commandType: CommandType.StoredProcedure);
                    }
                    else
                    {
                        // 🟢 بررسی وضعیت اقساط پرداخت شده قبل از آپدیت
                        int paidInst = await conn.QuerySingleAsync<int>("SELECT PAID_INST FROM PAY2_LOAN WHERE LOAN_ID = @LOAN_ID", new { loan.LOAN_ID }, tran);

                        if (paidInst > 0)
                        {
                            // 🚀 اگر وامی در جریان پرداخت است، مقادیر مالیاتی قفل هستند و فقط توضیحات و وضعیت فعال بودن آپدیت می‌شود
                            const string updatePartialSql = @"
                        UPDATE PAY2_LOAN 
                        SET PURPOSE=@PURPOSE, IS_ACTIVE=@IS_ACTIVE
                        WHERE LOAN_ID=@LOAN_ID";
                            await conn.ExecuteAsync(updatePartialSql, loan, tran);
                            // ⚠️ فراخوانی SP لغو می‌شود تا اقساط پرداخت شده تخریب نشوند و خطای UNIQUE رخ ندهد.
                        }
                        else
                        {
                            // اگر هیچ قسطی پرداخت نشده، کاربر مجاز است کل ساختار وام را تغییر دهد
                            const string updateFullSql = @"
                        UPDATE PAY2_LOAN 
                        SET LOAN_TYPE=@LOAN_TYPE, LOAN_DATE=@LOAN_DATE, AMOUNT=@AMOUNT, INSTALLMENT=@INSTALLMENT, TOTAL_INST=@TOTAL_INST, FIRST_PAY=@FIRST_PAY, PURPOSE=@PURPOSE, IS_ACTIVE=@IS_ACTIVE
                        WHERE LOAN_ID=@LOAN_ID";
                            await conn.ExecuteAsync(updateFullSql, loan, tran);

                            // بازتولید اقساط چون مقادیر مالی تغییر کرده است
                            await conn.ExecuteAsync("SP_PAY2_LOAN_GEN_SCHED", new { LOAN_ID = savedLoanId }, tran, commandType: CommandType.StoredProcedure);
                        }
                    }
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("loan/{loanId:int}")]
        [Pay2Authorize(Pay2Forms.Loan, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteLoan(int loanId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Loan, loanId);
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // 🚀 گارد امنیتی بک‌اند: جلوگیری از حذف وامی که در حقوق پرسنل کسر شده است
                    int paidInst = await conn.QuerySingleAsync<int>("SELECT PAID_INST FROM PAY2_LOAN WHERE LOAN_ID = @loanId", new { loanId }, tran);

                    if (paidInst > 0)
                        throw new InvalidOperationException("این وام دارای اقساط کسر شده در فیش حقوقی است و به دلیل حفظ سوابق مالی، غیرقابل حذف می‌باشد. در صورت لزوم می‌توانید وضعیت آن را غیرفعال کنید.");

                    // برای جلوگیری از خطای کلید خارجی، ابتدا اقساط (جدول فرزند) پاک می‌شوند
                    await conn.ExecuteAsync("DELETE FROM PAY2_LOAN_SCHED WHERE LOAN_ID = @loanId", new { loanId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_LOAN WHERE LOAN_ID = @loanId", new { loanId }, tran);
                });
                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                // پیام خطای فارسی ما مستقیماً به کلاینت منتقل می‌شود
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطای سیستمی: " + ex.Message);
            }
        }

        [HttpGet("{empId:int}/overrides")]
        [Pay2Authorize(Pay2Forms.Decree, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2OverrideDto>>> GetOverrides(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = @"
        SELECT O.EMP_ID, O.ITEM_ID, I.ITEM_NAME, O.INS_OV, O.TAX_OV, O.BASIS_OV, 
               O.VALID_FROM, O.VALID_TO, O.REASON
        FROM PAY2_OVERRIDE O
        INNER JOIN PAY2_ITEM_DEF I ON O.ITEM_ID = I.ITEM_ID
        WHERE O.EMP_ID = @empId
        ORDER BY O.VALID_FROM DESC, I.SORT_ORDER ASC";

            return Ok(await _db.DoGetDataSQLAsync<Pay2OverrideDto>(sql, new { empId }));
        }

        [HttpPost("override/save")]
        [Pay2Authorize(Pay2Forms.ActOverride, Pay2Perm.Run)]
        public async Task<IActionResult> SaveOverride([FromBody] Pay2OverrideDto ovr, [FromQuery] bool isEditing)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    if (ovr.INS_OV == true || ovr.TAX_OV == true)
                    {
                        string itemCode = await conn.QuerySingleOrDefaultAsync<string>(
                            "SELECT ITEM_CODE FROM PAY2_ITEM_DEF WHERE ITEM_ID=@ITEM_ID", new { ovr.ITEM_ID }, tran)
                            ?? throw new InvalidOperationException("آیتم حقوقی مورد نظر یافت نشد.");

                        if (Pay2NonSubjectRule.IsRuleItem(itemCode))
                        {
                            var rule = await Pay2NonSubjectRule.LoadAsync(conn, tran);
                            if (ovr.INS_OV == true && rule.BlocksInsurance(ovr.VALID_TO))
                                throw new InvalidOperationException(Pay2NonSubjectRule.InsuranceOverrideError);
                            if (ovr.TAX_OV == true && rule.BlocksTax(ovr.VALID_TO))
                                throw new InvalidOperationException(Pay2NonSubjectRule.TaxOverrideError);
                        }
                    }

                    if (!isEditing)
                    {
                        // درج جدید (بررسی تکراری بودن کلید اصلی مرکب)
                        int exists = await conn.QuerySingleAsync<int>(
                            "SELECT COUNT(1) FROM PAY2_OVERRIDE WHERE EMP_ID=@EMP_ID AND ITEM_ID=@ITEM_ID AND VALID_FROM=@VALID_FROM",
                            new { ovr.EMP_ID, ovr.ITEM_ID, ovr.VALID_FROM }, tran);

                        if (exists > 0)
                            throw new InvalidOperationException("این استثنا با همین تاریخ شروع، قبلاً برای این آیتم ثبت شده است.");

                        const string insertSql = @"
                    INSERT INTO PAY2_OVERRIDE (EMP_ID, ITEM_ID, INS_OV, TAX_OV, BASIS_OV, VALID_FROM, VALID_TO, REASON, CREATED_AT, CREATED_BY)
                    VALUES (@EMP_ID, @ITEM_ID, @INS_OV, @TAX_OV, @BASIS_OV, @VALID_FROM, @VALID_TO, @REASON, GETDATE(), @User)";

                        var p = new DynamicParameters(ovr);
                        p.Add("User", userCod);
                        await conn.ExecuteAsync(insertSql, p, tran);
                    }
                    else
                    {
                        // آپدیت (تاریخ پایان و گزینه‌ها قابل تغییر است)
                        const string updateSql = @"
                    UPDATE PAY2_OVERRIDE 
                    SET INS_OV=@INS_OV, TAX_OV=@TAX_OV, BASIS_OV=@BASIS_OV, VALID_TO=@VALID_TO, REASON=@REASON
                    WHERE EMP_ID=@EMP_ID AND ITEM_ID=@ITEM_ID AND VALID_FROM=@VALID_FROM";

                        await conn.ExecuteAsync(updateSql, ovr, tran);
                    }
                });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("override/{empId:int}/{itemId:int}/{validFrom:long}")]
        [Pay2Authorize(Pay2Forms.ActOverride, Pay2Perm.Run)]
        public async Task<IActionResult> DeleteOverride(int empId, int itemId, long validFrom, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            try
            {
                const string sql = "DELETE FROM PAY2_OVERRIDE WHERE EMP_ID=@empId AND ITEM_ID=@itemId AND VALID_FROM=@validFrom";
                await _db.DoExecuteSQLAsync(sql, new { empId, itemId, validFrom });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        [HttpGet("{empId:int}/advance-excls")]
        [Pay2Authorize(Pay2Forms.Advance, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2AdvanceExclDto>>> GetAdvanceExcls(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = "SELECT * FROM PAY2_ADVANCE_EXCL WHERE EMP_ID = @empId ORDER BY PERIOD_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2AdvanceExclDto>(sql, new { empId }));
        }

        [HttpPost("advance-excl/save")]
        [Pay2Authorize(Pay2Forms.ActAdvExcl, Pay2Perm.Run)]
        public async Task<IActionResult> SaveAdvanceExcl([FromBody] Pay2AdvanceExclDto excl)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    if (excl.EXCL_ID == 0)
                    {
                        const string insertSql = @"
                    INSERT INTO PAY2_ADVANCE_EXCL (EMP_ID, PERIOD_DATE, EXCL_AMOUNT, REASON, DEED_N_S, CREATED_AT, CREATED_BY)
                    VALUES (@EMP_ID, @PERIOD_DATE, @EXCL_AMOUNT, @REASON, @DEED_N_S, GETDATE(), @User)";

                        var p = new DynamicParameters(excl);
                        p.Add("User", userCod);
                        await conn.ExecuteAsync(insertSql, p, tran);
                    }
                    else
                    {
                        const string updateSql = @"
                    UPDATE PAY2_ADVANCE_EXCL 
                    SET PERIOD_DATE=@PERIOD_DATE, EXCL_AMOUNT=@EXCL_AMOUNT, REASON=@REASON, DEED_N_S=@DEED_N_S
                    WHERE EXCL_ID=@EXCL_ID";

                        await conn.ExecuteAsync(updateSql, excl, tran);
                    }
                });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("advance-excl/{exclId:int}")]
        [Pay2Authorize(Pay2Forms.ActAdvExcl, Pay2Perm.Run)]
        public async Task<IActionResult> DeleteAdvanceExcl(int exclId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.AdvanceExcl, exclId);
            try
            {
                await _db.DoExecuteSQLAsync("DELETE FROM PAY2_ADVANCE_EXCL WHERE EXCL_ID = @exclId", new { exclId });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{empId:int}/settlements")]
        [Pay2Authorize(Pay2Forms.Settlement, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2SettlementDto>>> GetSettlements(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            const string sql = "SELECT * FROM PAY2_SETTLEMENT WHERE EMP_ID = @empId ORDER BY SETTLE_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2SettlementDto>(sql, new { empId }));
        }

        [HttpPost("{empId:int}/settlement/calculate")]
        [Pay2Authorize(Pay2Forms.Settlement, Pay2Perm.Inp)]
        public async Task<IActionResult> CalculateSettlement(int empId, [FromQuery] int wsId, [FromBody] Pay2SettlementInputDto input, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                // 1. بررسی عدم وجود پیش‌نویس باز
                int draftCount = await _db.DoGetDataSQLAsyncSingle<int>(
                    "SELECT COUNT(1) FROM PAY2_SETTLEMENT WHERE EMP_ID = @empId AND STATUS = 1", new { empId });

                if (draftCount > 0)
                    return BadRequest("این پرسنل یک تسویه حساب پیش‌نویس دارد. لطفاً ابتدا آن را حذف یا نهایی کنید.");

                // 2. فراخوانی SP موتور تسویه حساب
                string sql = @"
            DECLARE @newId INT; 
            EXEC SP_PAY2_CALC_SETTLE 
                @EMP_ID = @EMP_ID, 
                @WS_ID = @WS_ID, 
                @SETTLE_DATE = @SETTLE_DATE, 
                @END_DATE = @END_DATE, 
                @PREV_CREDIT = @PREV_CREDIT, 
                @OTHER_INCOME = @OTHER_INCOME, 
                @OTHER_DED = @OTHER_DED, 
                @CALC_BY = @User, 
                @NEW_SET_ID = @newId OUTPUT;
            SELECT @newId;";

                var p = new DynamicParameters(input);
                p.Add("EMP_ID", empId);
                p.Add("WS_ID", wsId);
                p.Add("User", userCod);

                await _db.DoGetDataSQLAsyncSingle<int?>(sql, p);

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("settlement/{setId:int}/finalize")]
        [Pay2Authorize(Pay2Forms.ActSettleFinalize, Pay2Perm.Run)]
        public async Task<IActionResult> FinalizeSettlement(int setId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Settlement, setId);
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.DoExecuteSQLAsync("EXEC SP_PAY2_FINALIZE_SETTLE @SET_ID = @setId, @APPROVED_BY = @userCod", new { setId, userCod });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpDelete("settlement/{setId:int}")]
        [Pay2Authorize(Pay2Forms.Settlement, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteSettlement(int setId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Settlement, setId);
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // 🚀 فیکس امنیتی: WITH (UPDLOCK) — اگر مدیر دیگری در حال تأیید نهایی این تسویه است،
                    // این کوئری تا پایان کار او منتظر می‌ماند و سپس وضعیت جدید را می‌خواند و بلاک می‌شود.
                    const string sqlCheck = "SELECT STATUS FROM PAY2_SETTLEMENT WITH (UPDLOCK) WHERE SET_ID=@setId";
                    var status = await conn.QuerySingleOrDefaultAsync<byte?>(sqlCheck, new { setId }, tran);

                    if (!status.HasValue)
                        throw new InvalidOperationException("تسویه حساب یافت نشد.");

                    if (status.Value > 1)
                        throw new InvalidOperationException("تسویه حساب تأیید نهایی شده است و قابل حذف نیست.");

                    // حذف ایمن پیش‌نویس (غیرفعال‌سازی پرسنل در زمان Finalize انجام می‌شود، نه در محاسبه،
                    // پس حذف پیش‌نویس نیازی به فعال‌سازی مجدد پرسنل ندارد.)
                    await conn.ExecuteAsync("DELETE FROM PAY2_SETTLEMENT WHERE SET_ID = @setId", new { setId }, tran);
                });
                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطای سیستمی در حذف تسویه حساب. " + ex.Message);
            }
        }

        [HttpDelete("{empId:int}")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteEmployee(int empId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Employee, empId);
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // ۱. 🚀 مهار باگ Dynamic Casting + بهینه‌سازی پرفورمنس با اجرای تمام چک‌ها در یک کوئری سریع سمت SQL
                    string checkSql = @"
                        IF EXISTS (SELECT 1 FROM PAY2_RUN_LINE WHERE EMP_ID = @empId)
                            SELECT 1 AS ErrorCode, N'این پرسنل دارای محاسبه حقوق (فیش صادر شده) است و قابل حذف نیست.' AS ErrorMessage
                        ELSE IF EXISTS (SELECT 1 FROM PAY2_ATTENDANCE WHERE EMP_ID = @empId)
                            SELECT 2 AS ErrorCode, N'برای این پرسنل کارکرد ماهیانه ثبت شده است و قابل حذف نیست.' AS ErrorMessage
                        ELSE IF EXISTS (SELECT 1 FROM PAY2_SETTLEMENT WHERE EMP_ID = @empId)
                            SELECT 3 AS ErrorCode, N'این پرسنل دارای سابقه تسویه حساب است و قابل حذف نیست.' AS ErrorMessage
                        ELSE IF EXISTS (SELECT 1 FROM PAY2_LOAN WHERE EMP_ID = @empId)
                            SELECT 4 AS ErrorCode, N'این پرسنل دارای سابقه وام است. حذف فیزیکی ممنوع است.' AS ErrorMessage
                        ELSE IF EXISTS (SELECT 1 FROM PAY2_DECREE WHERE EMP_ID = @empId AND IS_CONFIRMED = 1)
                            SELECT 5 AS ErrorCode, N'این پرسنل دارای احکام کارگزینی تأیید شده است. مدارک رسمی قابل حذف نیستند.' AS ErrorMessage
                        ELSE IF EXISTS (SELECT 1 FROM PAY2_LEAVE WHERE EMP_ID = @empId AND STATUS > 1)
                            SELECT 6 AS ErrorCode, N'این پرسنل دارای مرخصی تأیید شده است و سوابق آن قابل حذف نیست.' AS ErrorMessage
                        -- 🚀 فیکس امنیتی: جلوگیری از حذف بدهی مالی شرکت به پرسنل و بالعکس 
                        ELSE IF EXISTS (SELECT 1 FROM PAY2_LEAVE_BAL WHERE EMP_ID = @empId AND BALANCE_MIN <> 0) 
                            SELECT 7 AS ErrorCode, N'این پرسنل دارای مانده مرخصی (بستانکار/بدهکار) است. به جای حذف فیزیکی، او را غیرفعال یا تسویه کنید.' AS ErrorMessage 
                        ELSE 
                            SELECT 0 AS ErrorCode, N'OK' AS ErrorMessage";

                    var validationResult = await conn.QuerySingleAsync(checkSql, new { empId }, tran);

                    // بررسی خطوط قرمز (Red Lines)
                    if (validationResult.ErrorCode != 0)
                        throw new InvalidOperationException((string)validationResult.ErrorMessage);

                    // ۲. پاک‌سازی ایمن زباله‌های پیش‌نویس (Drafts)
                    await conn.ExecuteAsync("DELETE FROM PAY2_DECREE_LINE WHERE DEC_ID IN (SELECT DEC_ID FROM PAY2_DECREE WHERE EMP_ID = @empId)", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_DECREE WHERE EMP_ID = @empId", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_CONTRACT WHERE EMP_ID = @empId", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_LEAVE_BAL WHERE EMP_ID = @empId", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_LEAVE WHERE EMP_ID = @empId", new { empId }, tran); // فقط پیش‌نویس‌ها مانده‌اند
                    await conn.ExecuteAsync("DELETE FROM PAY2_OVERRIDE WHERE EMP_ID = @empId", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_ADVANCE_EXCL WHERE EMP_ID = @empId", new { empId }, tran);

                    // ۳. حذف فیزیکی خود پرسنل
                    int deletedRows = await conn.ExecuteAsync("DELETE FROM PAY2_EMPLOYEE WHERE EMP_ID = @empId", new { empId }, tran);

                    if (deletedRows == 0)
                        throw new InvalidOperationException("پرسنل مورد نظر در سیستم یافت نشد.");
                });

                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message); // نمایش پیام فارسی دقیق به کاربر
            }
            catch (Exception ex)
            {
                return StatusCode(500, "خطای سیستمی هنگام حذف پرسنل: " + ex.Message);
            }
        }

        [HttpGet("templates")]
        [Pay2Authorize(Pay2Forms.ItemDef, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2ItemTemplateDto>>> GetTemplates()
        {
            var (noScope, allowedWsIds) = await GetWorkshopScopeAsync();
            const string sql = @"
        SELECT T.TMPL_ID, T.TMPL_CODE, T.TMPL_NAME, T.WS_ID,
               T.IS_ACTIVE, T.NOTES, W.WS_NAME AS WorkshopName
        FROM PAY2_ITEM_TEMPLATE T
        LEFT JOIN PAY2_WORKSHOP W ON T.WS_ID = W.WS_ID
        WHERE (@noScope = 1 OR T.WS_ID IS NULL OR T.WS_ID IN @allowedWsIds)
        ORDER BY T.TMPL_ID DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2ItemTemplateDto>(sql, new { noScope, allowedWsIds }));
        }

        [HttpPost("template/save")]
        [Pay2Authorize(Pay2Forms.ItemDef, Pay2Perm.Inp)]
        public async Task<IActionResult> SaveTemplate([FromBody] Pay2ItemTemplateDto tmpl)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    if (tmpl.TMPL_ID == 0)
                    {
                        int count = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_ITEM_TEMPLATE WHERE TMPL_CODE = @TMPL_CODE", new { tmpl.TMPL_CODE }, tran);
                        if (count > 0) throw new InvalidOperationException("کد قالب (انگلیسی) تکراری است.");

                        const string insertSql = @"INSERT INTO PAY2_ITEM_TEMPLATE (TMPL_CODE, TMPL_NAME, WS_ID, IS_ACTIVE, NOTES) 
                                           VALUES (@TMPL_CODE, @TMPL_NAME, @WS_ID, @IS_ACTIVE, @NOTES)";
                        await conn.ExecuteAsync(insertSql, tmpl, tran);
                    }
                    else
                    {
                        int count = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_ITEM_TEMPLATE WHERE TMPL_CODE = @TMPL_CODE AND TMPL_ID <> @TMPL_ID", new { tmpl.TMPL_CODE, tmpl.TMPL_ID }, tran);
                        if (count > 0) throw new InvalidOperationException("کد قالب (انگلیسی) تکراری است.");

                        const string updateSql = @"UPDATE PAY2_ITEM_TEMPLATE 
                                           SET TMPL_CODE=@TMPL_CODE, TMPL_NAME=@TMPL_NAME, WS_ID=@WS_ID, IS_ACTIVE=@IS_ACTIVE, NOTES=@NOTES 
                                           WHERE TMPL_ID=@TMPL_ID";
                        await conn.ExecuteAsync(updateSql, tmpl, tran);
                    }
                });
                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return BadRequest("خطای سرور: " + ex.Message); }
        }

        [HttpDelete("template/{tmplId:int}")]
        [Pay2Authorize(Pay2Forms.ItemDef, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteTemplate(int tmplId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await conn.ExecuteAsync("DELETE FROM PAY2_ITEM_TMPL_LINE WHERE TMPL_ID = @tmplId", new { tmplId }, tran);
                    int rows = await conn.ExecuteAsync("DELETE FROM PAY2_ITEM_TEMPLATE WHERE TMPL_ID = @tmplId", new { tmplId }, tran);
                    if (rows == 0) throw new InvalidOperationException("قالب یافت نشد.");
                });
                return Ok();
            }
            catch (System.Data.SqlClient.SqlException ex) when (ex.Number == 547)
            {
                return BadRequest("این قالب در احکام پرسنل استفاده شده و قابل حذف نیست. لطفاً آن را غیرفعال کنید.");
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpGet("template/{tmplId:int}/lines")]
        [Pay2Authorize(Pay2Forms.ItemDef, Pay2Perm.See)]
        public async Task<ActionResult<IEnumerable<Pay2ItemTmplLineDto>>> GetTemplateLines(int tmplId)
        {
            const string sql = @"SELECT L.TMPL_ID, L.ITEM_ID, I.ITEM_NAME, L.DEF_AMOUNT, L.INS_OV, L.TAX_OV, L.BASIS_OV, L.SHIFT_MODE_OV
                         FROM PAY2_ITEM_TMPL_LINE L
                         INNER JOIN PAY2_ITEM_DEF I ON L.ITEM_ID = I.ITEM_ID
                         WHERE L.TMPL_ID = @tmplId
                         ORDER BY I.SORT_ORDER";
            return Ok(await _db.DoGetDataSQLAsync<Pay2ItemTmplLineDto>(sql, new { tmplId }));
        }

        [HttpPost("template/line/save")]
        [Pay2Authorize(Pay2Forms.ItemDef, Pay2Perm.Upd)]
        public async Task<IActionResult> SaveTemplateLine([FromBody] Pay2ItemTmplLineDto line)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    if (line.INS_OV == true || line.TAX_OV == true)
                    {
                        string itemCode = await conn.QuerySingleOrDefaultAsync<string>(
                            "SELECT ITEM_CODE FROM PAY2_ITEM_DEF WHERE ITEM_ID=@ITEM_ID", new { line.ITEM_ID }, tran)
                            ?? throw new InvalidOperationException("آیتم حقوقی مورد نظر یافت نشد.");

                        // قالب حکم تاریخ ندارد و هر حکمی از روی آن ساخته شود می‌تواند
                        // در ماه‌های آینده بیفتد، پس بازه‌ی باز (null) در نظر گرفته می‌شود.
                        if (Pay2NonSubjectRule.IsRuleItem(itemCode))
                        {
                            var rule = await Pay2NonSubjectRule.LoadAsync(conn, tran);
                            if (line.INS_OV == true && rule.BlocksInsurance(null))
                                throw new InvalidOperationException(Pay2NonSubjectRule.InsuranceTemplateError);
                            if (line.TAX_OV == true && rule.BlocksTax(null))
                                throw new InvalidOperationException(Pay2NonSubjectRule.TaxTemplateError);
                        }
                    }

                    // اعتبارسنجی: مقدار اعشاری فقط برای آیتم حق شیفت درصدی مجاز است
                    line.SHIFT_MODE_OV = string.IsNullOrWhiteSpace(line.SHIFT_MODE_OV) ? null : line.SHIFT_MODE_OV;
                    if (line.DEF_AMOUNT != Math.Truncate(line.DEF_AMOUNT))
                    {
                        bool isShiftPct = await IsShiftPctItemAsync(conn, tran, line.ITEM_ID, decId: null, tmplId: line.TMPL_ID, shiftModeOv: line.SHIFT_MODE_OV);
                        if (!isShiftPct)
                            throw new InvalidOperationException("مبلغ اعشاری فقط برای آیتم «حق شیفت درصدی» مجاز است.");
                    }

                    int count = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_ITEM_TMPL_LINE WHERE TMPL_ID=@TMPL_ID AND ITEM_ID=@ITEM_ID", new { line.TMPL_ID, line.ITEM_ID }, tran);

                    if (count == 0)
                    {
                        const string insertSql = @"INSERT INTO PAY2_ITEM_TMPL_LINE (TMPL_ID, ITEM_ID, DEF_AMOUNT, INS_OV, TAX_OV, BASIS_OV, SHIFT_MODE_OV)
                                           VALUES (@TMPL_ID, @ITEM_ID, @DEF_AMOUNT, @INS_OV, @TAX_OV, @BASIS_OV, @SHIFT_MODE_OV)";
                        await conn.ExecuteAsync(insertSql, line, tran);
                    }
                    else
                    {
                        const string updateSql = @"UPDATE PAY2_ITEM_TMPL_LINE 
                                           SET DEF_AMOUNT=@DEF_AMOUNT, INS_OV=@INS_OV, TAX_OV=@TAX_OV, BASIS_OV=@BASIS_OV, SHIFT_MODE_OV=@SHIFT_MODE_OV
                                           WHERE TMPL_ID=@TMPL_ID AND ITEM_ID=@ITEM_ID";
                        await conn.ExecuteAsync(updateSql, line, tran);
                    }
                });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpDelete("template/{tmplId:int}/line/{itemId:int}")]
        [Pay2Authorize(Pay2Forms.ItemDef, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteTemplateLine(int tmplId, int itemId)
        {
            try
            {
                await _db.DoExecuteSQLAsync("DELETE FROM PAY2_ITEM_TMPL_LINE WHERE TMPL_ID=@tmplId AND ITEM_ID=@itemId", new { tmplId, itemId });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpGet("jobs/paged")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<PagedResult<Pay2JobDto>>> GetPagedJobs([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null, [FromQuery] bool isFuzzy = false)
        {
            try
            {
                if (isFuzzy && !string.IsNullOrWhiteSpace(search))
                {
                    // --- 🚀 جستجوی هوشمند (Fuzzy) بر بستر Cache سرور ---

                    // 1. نرم‌ال‌سازی متن کاربر
                    string lowerTerm = Safir.Shared.Utility.CL_METHODS.ToStandardSearchText(search).ToLowerInvariant();

                    // 2. واکشی از کش (یا دیتابیس در صورت خالی بودن کش)
                    if (!_cache.TryGetValue("AllJobsCache", out List<Pay2JobDto>? allJobs) || allJobs == null)
                    {
                        allJobs = (await _db.DoGetDataSQLAsync<Pay2JobDto>("SELECT JOB_ID, JOB_CODE, JOB_NAME, JOB_GROUP, IS_ACTIVE FROM PAY2_JOB")).ToList();
                        _cache.Set("AllJobsCache", allJobs, TimeSpan.FromMinutes(30)); // 30 دقیقه اعتبار
                    }

                    const int MinimumScoreThreshold = 50;

                    // 3. پردازش موازی در RAM سرور
                    var matches = allJobs
                        .AsParallel()
                        .WithDegreeOfParallelism(Environment.ProcessorCount)
                        .Select(job => new
                        {
                            Item = job,
                            Score = Math.Max(
                                Fuzz.PartialTokenSetRatio(lowerTerm, job.JOB_NAME?.ToLowerInvariant() ?? ""),
                                Fuzz.PartialTokenSetRatio(lowerTerm, job.JOB_CODE?.ToLowerInvariant() ?? "")
                            )
                        })
                        .Where(x => x.Score >= MinimumScoreThreshold)
                        .OrderByDescending(x => x.Score)
                        .Select(x => x.Item)
                        .ToList();

                    var pagedItems = matches.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                    return Ok(new PagedResult<Pay2JobDto>
                    {
                        Items = pagedItems,
                        TotalCount = matches.Count,
                        PageNumber = page,
                        PageSize = pageSize
                    });
                }
                else
                {
                    // --- 🚀 جستجوی استاندارد (SQL) ---
                    var parameters = new DynamicParameters();
                    string whereClause = "";

                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        string cleanSearch = Safir.Shared.Utility.CL_METHODS.ToStandardSearchText(search);

                        if (!string.IsNullOrWhiteSpace(cleanSearch))
                        {
                            string searchPattern = "%" + cleanSearch.Replace(" ", "%") + "%";
                            whereClause = @"
                        WHERE JOB_CODE LIKE @Search 
                        OR REPLACE(REPLACE(JOB_NAME, N'ي', N'ی'), N'ك', N'ک') LIKE @Search";
                            parameters.Add("Search", searchPattern);
                        }
                    }

                    string countSql = $"SELECT COUNT(1) FROM PAY2_JOB {whereClause}";
                    string dataSql = $@"
                SELECT JOB_ID, JOB_CODE, JOB_NAME, JOB_GROUP, IS_ACTIVE 
                FROM PAY2_JOB 
                {whereClause}
                ORDER BY JOB_NAME
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    parameters.Add("Offset", (page - 1) * pageSize);
                    parameters.Add("PageSize", pageSize);

                    int totalCount = await _db.DoGetDataSQLAsyncSingle<int>(countSql, parameters);
                    var items = await _db.DoGetDataSQLAsync<Pay2JobDto>(dataSql, parameters);

                    return Ok(new PagedResult<Pay2JobDto>
                    {
                        Items = items?.ToList() ?? new List<Pay2JobDto>(),
                        TotalCount = totalCount,
                        PageNumber = page,
                        PageSize = pageSize
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("jobs/save")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.Inp)]
        public async Task<IActionResult> SaveJob([FromBody] Pay2JobDto job)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    if (job.JOB_ID == 0) // درج جدید
                    {
                        int codeExists = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_JOB WHERE JOB_CODE = @Code", new { Code = job.JOB_CODE }, tran);
                        if (codeExists > 0) throw new InvalidOperationException($"کد شغل «{job.JOB_CODE}» تکراری است.");

                        const string insertSql = "INSERT INTO PAY2_JOB (JOB_CODE, JOB_NAME, JOB_GROUP, IS_ACTIVE) VALUES (@JOB_CODE, @JOB_NAME, @JOB_GROUP, @IS_ACTIVE)";
                        await conn.ExecuteAsync(insertSql, job, tran);
                    }
                    else // ویرایش
                    {
                        int codeExists = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_JOB WHERE JOB_CODE = @Code AND JOB_ID <> @Id", new { Code = job.JOB_CODE, Id = job.JOB_ID }, tran);
                        if (codeExists > 0) throw new InvalidOperationException($"کد شغل «{job.JOB_CODE}» تکراری است.");

                        const string updateSql = "UPDATE PAY2_JOB SET JOB_CODE=@JOB_CODE, JOB_NAME=@JOB_NAME, JOB_GROUP=@JOB_GROUP, IS_ACTIVE=@IS_ACTIVE WHERE JOB_ID=@JOB_ID";
                        await conn.ExecuteAsync(updateSql, job, tran);
                    }
                });
                _cache.Remove("AllJobsCache"); // پاک کردن کش پس از ذخیره
                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, "خطای سیستمی: " + ex.Message); }
        }

        [HttpDelete("jobs/{id:int}")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.Del)]
        public async Task<IActionResult> DeleteJob(int id)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    int usageCount = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_EMPLOYEE WHERE JOB_ID = @Id", new { Id = id }, tran);

                    if (usageCount > 0)
                    {
                        // Soft Delete: اگر شغلی به پرسنل وصل است، فقط غیرفعالش کن
                        await conn.ExecuteAsync("UPDATE PAY2_JOB SET IS_ACTIVE = 0 WHERE JOB_ID = @Id", new { Id = id }, tran);
                    }
                    else
                    {
                        // Hard Delete
                        await conn.ExecuteAsync("DELETE FROM PAY2_JOB WHERE JOB_ID = @Id", new { Id = id }, tran);
                    }
                });
                _cache.Remove("AllJobsCache"); // پاک کردن کش پس از حذف
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }


        [HttpGet("effective-shift-mode")]
        [Pay2Authorize(Pay2Forms.Employee, Pay2Perm.See)]
        public async Task<ActionResult<string>> GetEffectiveShiftModeAsync([FromQuery] int? decId, [FromQuery] int? tmplId, [FromQuery] int? wsId)
        {
            try
            {
                string globalShiftMode = "PCT";
                string? wsShiftMode = null;
                string? empShiftMode = null;

                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    globalShiftMode = await conn.QueryFirstOrDefaultAsync<string>("SELECT CFG_VALUE FROM PAY2_CONFIG WHERE CFG_KEY = 'SHIFT_MODE'", null, tran) ?? "PCT";

                    if (decId.HasValue)
                    {
                        var decInfo = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT D.SHIFT_MODE AS DecShiftMode, W.SHIFT_MODE AS WsShiftMode FROM PAY2_DECREE D LEFT JOIN PAY2_WORKSHOP W ON D.WS_ID = W.WS_ID WHERE D.DEC_ID = @decId", new { decId }, tran);
                        if (decInfo != null)
                        {
                            empShiftMode = decInfo.DecShiftMode;
                            wsShiftMode = decInfo.WsShiftMode;
                        }
                    }
                    else if (tmplId.HasValue)
                    {
                        var tmplInfo = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT W.SHIFT_MODE AS WsShiftMode FROM PAY2_ITEM_TEMPLATE T LEFT JOIN PAY2_WORKSHOP W ON T.WS_ID = W.WS_ID WHERE T.TMPL_ID = @tmplId", new { tmplId }, tran);
                        if (tmplInfo != null)
                        {
                            wsShiftMode = tmplInfo.WsShiftMode;
                        }
                    }
                    else if (wsId.HasValue)
                    {
                        wsShiftMode = await conn.QueryFirstOrDefaultAsync<string>("SELECT SHIFT_MODE FROM PAY2_WORKSHOP WHERE WS_ID = @wsId", new { wsId }, tran);
                    }
                });

                string effectiveMode = empShiftMode ?? wsShiftMode ?? globalShiftMode;
                return Ok(effectiveMode);
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        private static async Task<bool> IsShiftPctItemAsync(IDbConnection conn, IDbTransaction tran, int itemId, int? decId = null, int? tmplId = null, string? shiftModeOv = null)
        {
            string? itemCode = await conn.QuerySingleOrDefaultAsync<string>(
                "SELECT ITEM_CODE FROM PAY2_ITEM_DEF WHERE ITEM_ID = @itemId", new { itemId }, tran);
            if (!string.Equals(itemCode, "SHIFT", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(shiftModeOv))
                return !string.Equals(shiftModeOv, "FIXED", StringComparison.OrdinalIgnoreCase);

            string? empShiftMode = null;
            string? wsShiftMode = null;

            if (decId.HasValue)
            {
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SELECT D.SHIFT_MODE AS DecShiftMode, W.SHIFT_MODE AS WsShiftMode FROM PAY2_DECREE D LEFT JOIN PAY2_WORKSHOP W ON D.WS_ID = W.WS_ID WHERE D.DEC_ID = @decId", new { decId }, tran);
                if (row != null)
                {
                    empShiftMode = row.DecShiftMode;
                    wsShiftMode = row.WsShiftMode;
                }
            }
            else if (tmplId.HasValue)
            {
                var row = await conn.QueryFirstOrDefaultAsync(
                    "SELECT W.SHIFT_MODE AS WsShiftMode FROM PAY2_ITEM_TEMPLATE T LEFT JOIN PAY2_WORKSHOP W ON T.WS_ID = W.WS_ID WHERE T.TMPL_ID = @tmplId", new { tmplId }, tran);
                if (row != null)
                {
                    wsShiftMode = row.WsShiftMode;
                }
            }

            string? globalShiftMode = await conn.QuerySingleOrDefaultAsync<string>(
                "SELECT CFG_VALUE FROM PAY2_CONFIG WHERE CFG_KEY = 'SHIFT_MODE'", null, tran);

            string effectiveMode = string.IsNullOrWhiteSpace(empShiftMode) ?
                (string.IsNullOrWhiteSpace(wsShiftMode) ? (string.IsNullOrWhiteSpace(globalShiftMode) ? "PCT" : globalShiftMode) : wsShiftMode)
                : empShiftMode;

            return !string.Equals(effectiveMode, "FIXED", StringComparison.OrdinalIgnoreCase);
        }

        [HttpPut("settlement/{setId:int}/revert")]
        [Pay2Authorize(Pay2Forms.ActSettleFinalize, Pay2Perm.Run)]
        public async Task<IActionResult> RevertSettlement(int setId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Settlement, setId);
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // فقط تسویه‌هایی که تایید نهایی (۲) شده‌اند قابل برگشت به پیش‌نویس (۱) هستند.
                    // قفل ردیف تا پایان تراکنش (هماهنگ با UPDLOCK در Finalize/Delete).
                    int? empId = await conn.QuerySingleOrDefaultAsync<int?>(
                        "SELECT EMP_ID FROM PAY2_SETTLEMENT WITH (UPDLOCK) WHERE SET_ID = @setId AND STATUS = 2",
                        new { setId }, tran);

                    if (!empId.HasValue)
                        throw new InvalidOperationException("تسویه حساب یافت نشد یا در وضعیتی نیست که قابل برگشت باشد.");

                    await conn.ExecuteAsync(
                        "UPDATE PAY2_SETTLEMENT SET STATUS = 1, APPROVED_BY = NULL, APPROVED_AT = NULL WHERE SET_ID = @setId AND STATUS = 2",
                        new { setId }, tran);

                    // 🚀 رفع حالت یتیم: غیرفعال‌سازی پرسنل و بستن وام‌ها در Finalize انجام شده،
                    // پس در برگشت باید پرسنل دوباره فعال و وام‌های «بسته‌شده در تسویه» باز شوند.
                    await conn.ExecuteAsync(
                        "UPDATE PAY2_EMPLOYEE SET IS_ACTIVE = 1, FIRE_DATE = NULL WHERE EMP_ID = @empId",
                        new { empId }, tran);

                    await conn.ExecuteAsync(
                        "UPDATE PAY2_LOAN SET IS_ACTIVE = 1, PURPOSE = REPLACE(ISNULL(PURPOSE, N''), N' (بسته‌شده در تسویه)', N'') " +
                        "WHERE EMP_ID = @empId AND IS_ACTIVE = 0 AND PURPOSE LIKE N'%(بسته‌شده در تسویه)%'",
                        new { empId }, tran);
                });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpPost("settlement/{setId:int}/generate-deed")]
        [Pay2Authorize(Pay2Forms.ActDeed, Pay2Perm.Run)]
        public async Task<IActionResult> GenerateSettlementDeed(int setId, [FromServices] Pay2ScopeResolver scopeResolver)
        {
            await scopeResolver.EnsureWorkshopAsync(int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0"), Pay2ScopeKind.Settlement, setId);
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // تغییر وضعیت به سند صادر شده (۳)
                    int rows = await conn.ExecuteAsync(
                        "UPDATE PAY2_SETTLEMENT SET STATUS = 3 WHERE SET_ID = @setId AND STATUS = 2",
                        new { setId }, tran);

                    if (rows == 0)
                        throw new InvalidOperationException("تسویه حساب یافت نشد یا تأیید نهایی نشده است.");
                });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        private async Task<List<Pay2LeaveReportRowDto>> FetchLeaveReportDataAsync(int wsId, int year, int empId, long currentDate)
        {
            // محاسبه درصد گذشته از سال (برای استحقاق Pro-rata)
            int currentMonth = (int)((currentDate / 100) % 100);
            int currentDay = (int)(currentDate % 100);

            int passedDays = 0;
            for (int i = 1; i < currentMonth; i++) passedDays += (i <= 6) ? 31 : 30;
            passedDays += currentDay;

            double ratio = Math.Min(1.0, passedDays / 365.0);

            string sql = @"
                SELECT 
                    E.EMP_CODE, 
                    E.LAST_NAME + N' ' + E.FIRST_NAME AS FULL_NAME,
                    ISNULL(LB.ENTITLEMENT_MIN, 11440) AS ENTITLEMENT_MIN,
                    ISNULL(LB.CARRIED_IN_MIN, 0) AS CARRIED_IN_MIN,
                    ISNULL(LB.USED_MIN, 0) AS USED_MIN,
                    (ISNULL(LB.ENTITLEMENT_MIN, 11440) + ISNULL(LB.CARRIED_IN_MIN, 0) - ISNULL(LB.USED_MIN, 0)) AS BALANCE_MIN,
                    CAST((ISNULL(LB.ENTITLEMENT_MIN, 11440) + ISNULL(LB.CARRIED_IN_MIN, 0) - ISNULL(LB.USED_MIN, 0)) AS DECIMAL(10,2)) / 440.0 AS BALANCE_DAYS,
                    
                    CAST(ISNULL(LB.ENTITLEMENT_MIN, 11440) * @Ratio AS INT) AS PRORATA_ENTITLEMENT_MIN,
                    (CAST(ISNULL(LB.ENTITLEMENT_MIN, 11440) * @Ratio AS INT) + ISNULL(LB.CARRIED_IN_MIN, 0) - ISNULL(LB.USED_MIN, 0)) AS PRORATA_BALANCE_MIN,
                    CAST((CAST(ISNULL(LB.ENTITLEMENT_MIN, 11440) * @Ratio AS INT) + ISNULL(LB.CARRIED_IN_MIN, 0) - ISNULL(LB.USED_MIN, 0)) AS DECIMAL(10,2)) / 440.0 AS PRORATA_BALANCE_DAYS
                FROM PAY2_EMPLOYEE E WITH (NOLOCK)
                LEFT JOIN PAY2_LEAVE_BAL LB WITH (NOLOCK) ON E.EMP_ID = LB.EMP_ID AND LB.YEAR = @year
                WHERE E.WS_ID = @wsId AND E.IS_ACTIVE = 1
                AND (@empId = 0 OR E.EMP_ID = @empId)
                ORDER BY E.LAST_NAME, E.FIRST_NAME";

            return (await _db.DoGetDataSQLAsync<Pay2LeaveReportRowDto>(sql, new { wsId, year, Ratio = ratio, empId })).ToList();
        }

        [HttpGet("leave-report/excel")]
        [Pay2Authorize(Pay2Forms.Reports, Pay2Perm.See)]
        [Pay2Authorize(Pay2Forms.ActExport, Pay2Perm.Run)]
        public async Task<IActionResult> GetLeaveReportExcel([FromQuery] int wsId, [FromQuery] int year, [FromQuery] int empId, [FromQuery] long currentDate)
        {
            try
            {
                var data = await FetchLeaveReportDataAsync(wsId, year, empId, currentDate);
                var wsName = await _db.DoGetDataSQLAsyncSingle<string>("SELECT WS_NAME FROM PAY2_WORKSHOP WHERE WS_ID = @wsId", new { wsId });

                using var wb = new ClosedXML.Excel.XLWorkbook();
                var sheet = wb.Worksheets.Add($"مرخصی {year}");
                sheet.RightToLeft = true;

                sheet.Cell(1, 1).Value = $"گزارش مانده مرخصی پرسنل - کارگاه: {wsName} - سال: {year} - (محاسبه تا تاریخ: {currentDate})";
                sheet.Range(1, 1, 1, 9).Merge().Style.Font.SetBold().Font.SetFontSize(14).Fill.SetBackgroundColor(ClosedXML.Excel.XLColor.LightBlue);

                string[] headers = { "کد پرسنلی", "نام و نام خانوادگی", "استحقاق پایان سال (دقیقه)", "انتقالی از قبل (دقیقه)", "استحقاق تا امروز (دقیقه)", "استفاده شده (دقیقه)", "مانده پایان سال (روز)", "مانده تا امروز (روز)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = sheet.Cell(2, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.SetBold().Fill.SetBackgroundColor(ClosedXML.Excel.XLColor.LightGray);
                }

                int r = 3;
                foreach (var item in data)
                {
                    sheet.Cell(r, 1).Value = item.EMP_CODE;
                    sheet.Cell(r, 2).Value = item.FULL_NAME;
                    sheet.Cell(r, 3).Value = item.ENTITLEMENT_MIN;
                    sheet.Cell(r, 4).Value = item.CARRIED_IN_MIN;
                    sheet.Cell(r, 5).Value = item.PRORATA_ENTITLEMENT_MIN;
                    sheet.Cell(r, 6).Value = item.USED_MIN;
                    sheet.Cell(r, 7).Value = item.BALANCE_DAYS;
                    sheet.Cell(r, 8).Value = item.PRORATA_BALANCE_DAYS;

                    sheet.Range(r, 3, r, 6).Style.NumberFormat.Format = "#,##0";
                    sheet.Range(r, 7, r, 8).Style.NumberFormat.Format = "0.00";
                    r++;
                }

                sheet.Columns(1, 9).AdjustToContents();

                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"LeaveBalance_{year}.xlsx");
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("leave-report/pdf")]
        [Pay2Authorize(Pay2Forms.Reports, Pay2Perm.See)]
        [Pay2Authorize(Pay2Forms.ActExport, Pay2Perm.Run)]
        public async Task<IActionResult> GetLeaveReportPdf([FromQuery] int wsId, [FromQuery] int year, [FromQuery] int empId, [FromQuery] long currentDate)
        {
            try
            {
                var data = await FetchLeaveReportDataAsync(wsId, year, empId, currentDate);
                var wsName = await _db.DoGetDataSQLAsyncSingle<string>("SELECT WS_NAME FROM PAY2_WORKSHOP WHERE WS_ID = @wsId", new { wsId });

                string formattedDate = $"{currentDate.ToString()[..4]}/{currentDate.ToString().Substring(4, 2)}/{currentDate.ToString().Substring(6, 2)}";

                var document = new Safir.Server.Reports.LeaveReportDocument(data, wsName ?? "", year, formattedDate);
                byte[] pdfBytes = document.GeneratePdf();

                return File(pdfBytes, "application/pdf", $"LeaveReport_{year}.pdf");
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

    }
}
