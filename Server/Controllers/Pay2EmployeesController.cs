using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Salary;
using System.Data;
using System.Security.Claims;
using static Safir.Shared.Models.Salary.Pay2LeaveDto;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/employees")]
    [Authorize]
    public class Pay2EmployeesController : ControllerBase
    {
        private readonly IDatabaseService _db;
        public Pay2EmployeesController(IDatabaseService db) => _db = db;

        private long DecrementShamsiDate(long shamsiDate)
        {
            int y = (int)(shamsiDate / 10000);
            int m = (int)((shamsiDate % 10000) / 100);
            int d = (int)(shamsiDate % 100);

            d--;
            if (d < 1)
            {
                m--;
                if (m < 1) { m = 12; y--; }
                d = (m <= 6) ? 31 : (m <= 11) ? 30 : 29; // منطق ماه‌های شمسی
            }
            return y * 10000L + m * 100 + d;
        }

        // این متد را داخل کلاس Pay2EmployeesController اضافه کنید:
        [HttpGet("jobs-lookup")]
        public async Task<ActionResult<IEnumerable<LookupDto<int>>>> GetJobsLookup([FromQuery] string? searchTerm)
        {
            try
            {
                string sql = @"
            SELECT TOP 50 
                   JOB_ID AS Id, 
                   JOB_NAME AS Name 
            FROM   PAY2_JOB 
            WHERE  IS_ACTIVE = 1";

                object? parameters = null;

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    sql += " AND JOB_NAME LIKE @Search";
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
        public async Task<ActionResult<IEnumerable<Pay2EmployeeDto>>> GetAll()
        {
            const string sql = @"
                SELECT e.*, w.WS_NAME AS WorkshopName, j.JOB_NAME AS JobName
                FROM PAY2_EMPLOYEE e
                LEFT JOIN PAY2_WORKSHOP w ON e.WS_ID = w.WS_ID
                LEFT JOIN PAY2_JOB j ON e.JOB_ID = j.JOB_ID
                ORDER BY e.IS_ACTIVE DESC, e.EMP_ID DESC";

            var data = await _db.DoGetDataSQLAsync<Pay2EmployeeDto>(sql);
            return Ok(data);
        }

        [HttpPost("save")]
        public async Task<ActionResult<int>> SaveEmployee([FromBody] Pay2EmployeeDto emp)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                var empId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
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
            catch (System.Data.SqlClient.SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return BadRequest("کد پرسنلی یا کد ملی وارد شده در سیستم تکراری است.");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // --- بخش احکام پرسنل ---
        [HttpGet("{empId:int}/decrees")]
        public async Task<ActionResult<IEnumerable<Pay2DecreeDto>>> GetDecrees(int empId)
        {
            const string sql = @"
                SELECT D.*, T.TMPL_NAME AS TemplateName
                FROM PAY2_DECREE D
                LEFT JOIN PAY2_ITEM_TEMPLATE T ON D.TMPL_ID = T.TMPL_ID
                WHERE D.EMP_ID = @empId ORDER BY D.EFF_FROM DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2DecreeDto>(sql, new { empId }));
        }

        [HttpPost("decree/save")]
        public async Task<ActionResult<int>> SaveDecree([FromBody] Pay2DecreeDto decree)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                var decId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    int currentDecId = decree.DEC_ID;

                    // منطق هوشمند: بستن تاریخ پایان حکم قبلی
                    if (decree.DEC_ID == 0)
                    {
                        string closePrevSql = @"
                            UPDATE PAY2_DECREE 
                            SET EFF_TO = @PrevTo 
                            WHERE DEC_ID = (SELECT TOP 1 DEC_ID FROM PAY2_DECREE WHERE EMP_ID = @EmpId AND IS_CONFIRMED = 1 AND EFF_TO IS NULL AND EFF_FROM < @NewFrom ORDER BY EFF_FROM DESC)";
                        // محاسبه روز قبل (ساده‌سازی شده، برای نسخه دقیق از تابع شمسی سمت سرور استفاده کنید)
                        long prevTo = DecrementShamsiDate(decree.EFF_FROM);
                        await conn.ExecuteAsync(closePrevSql, new { PrevTo = prevTo, EmpId = decree.EMP_ID, NewFrom = decree.EFF_FROM }, tran);

                        const string insertSql = @"
                            INSERT INTO PAY2_DECREE (EMP_ID, WS_ID, ISSUED_DATE, EFF_FROM, EFF_TO, EDU_LEVEL, MARITAL, IS_MANAGER, TMPL_ID, IS_CONFIRMED, CREATED_AT, CREATED_BY, NOTES)
                            OUTPUT INSERTED.DEC_ID
                            VALUES (@EMP_ID, @WS_ID, @ISSUED_DATE, @EFF_FROM, @EFF_TO, @EDU_LEVEL, @MARITAL, @IS_MANAGER, @TMPL_ID, @IS_CONFIRMED, GETDATE(), @User, @NOTES)";

                        var p = new DynamicParameters(decree);
                        p.Add("User", userCod);
                        currentDecId = await conn.QueryFirstAsync<int>(insertSql, p, tran);

                        // تولید اقلام از روی قالب
                        if (decree.TMPL_ID.HasValue && decree.TMPL_ID > 0)
                        {
                            string sqlLines = @"
                                INSERT INTO PAY2_DECREE_LINE (DEC_ID, ITEM_ID, AMOUNT, INS_OV, TAX_OV, BASIS_OV)
                                SELECT @NewDecId, ITEM_ID, DEF_AMOUNT, INS_OV, TAX_OV, BASIS_OV
                                FROM PAY2_ITEM_TMPL_LINE WHERE TMPL_ID = @TmplId";
                            await conn.ExecuteAsync(sqlLines, new { NewDecId = currentDecId, TmplId = decree.TMPL_ID.Value }, tran);
                        }
                    }
                    else
                    {
                        const string updateSql = @"
                            UPDATE PAY2_DECREE SET ISSUED_DATE=@ISSUED_DATE, EFF_FROM=@EFF_FROM, EFF_TO=@EFF_TO, EDU_LEVEL=@EDU_LEVEL, MARITAL=@MARITAL, IS_MANAGER=@IS_MANAGER, IS_CONFIRMED=@IS_CONFIRMED, NOTES=@NOTES
                            WHERE DEC_ID=@DEC_ID";
                        await conn.ExecuteAsync(updateSql, decree, tran);
                    }
                    return currentDecId;
                });
                return Ok(decId);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // اضافه کردن به فایل Pay2EmployeesController.cs

        [HttpGet("templates-lookup")]
        public async Task<ActionResult<IEnumerable<LookupDto<int>>>> GetTemplatesLookup()
        {
            // خواندن قالب‌های فعال برای پر کردن Dropdown
            const string sql = "SELECT TMPL_ID AS Id, TMPL_NAME AS Name FROM PAY2_ITEM_TEMPLATE WHERE IS_ACTIVE = 1 ORDER BY TMPL_NAME";
            return Ok(await _db.DoGetDataSQLAsync<LookupDto<int>>(sql));
        }

        [HttpDelete("decree/{decId:int}")]
        public async Task<IActionResult> DeleteDecree(int decId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // اول اقلام ریالی حذف می‌شوند، سپس خود هدر حکم
                    await conn.ExecuteAsync("DELETE FROM PAY2_DECREE_LINE WHERE DEC_ID = @decId", new { decId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_DECREE WHERE DEC_ID = @decId", new { decId }, tran);
                });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }


        [HttpGet("itemdefs-lookup")]
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
        public async Task<ActionResult<IEnumerable<Pay2DecreeLineDto>>> GetDecreeLines(int decId)
        {
            const string sql = @"
        SELECT L.DEC_ID, L.ITEM_ID, I.ITEM_NAME, L.AMOUNT, L.INS_OV, L.TAX_OV, L.BASIS_OV
        FROM PAY2_DECREE_LINE L
        INNER JOIN PAY2_ITEM_DEF I ON L.ITEM_ID = I.ITEM_ID
        WHERE L.DEC_ID = @decId
        ORDER BY I.SORT_ORDER";

            return Ok(await _db.DoGetDataSQLAsync<Pay2DecreeLineDto>(sql, new { decId }));
        }

        [HttpPost("decree/line/save")]
        public async Task<IActionResult> SaveDecreeLine([FromBody] Pay2DecreeLineDto line)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // اول چک می‌کنیم آیا این آیتم قبلا برای این حکم ثبت شده یا خیر
                    int count = await conn.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM PAY2_DECREE_LINE WHERE DEC_ID=@DEC_ID AND ITEM_ID=@ITEM_ID",
                        new { line.DEC_ID, line.ITEM_ID }, tran);

                    if (count == 0)
                    {
                        // درج جدید
                        const string insertSql = @"INSERT INTO PAY2_DECREE_LINE (DEC_ID, ITEM_ID, AMOUNT, INS_OV, TAX_OV, BASIS_OV)
                                           VALUES (@DEC_ID, @ITEM_ID, @AMOUNT, @INS_OV, @TAX_OV, @BASIS_OV)";
                        await conn.ExecuteAsync(insertSql, line, tran);
                    }
                    else
                    {
                        // آپدیت قلم موجود
                        const string updateSql = @"UPDATE PAY2_DECREE_LINE 
                                           SET AMOUNT=@AMOUNT, INS_OV=@INS_OV, TAX_OV=@TAX_OV, BASIS_OV=@BASIS_OV
                                           WHERE DEC_ID=@DEC_ID AND ITEM_ID=@ITEM_ID";
                        await conn.ExecuteAsync(updateSql, line, tran);
                    }
                });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("decree/{decId:int}/line/{itemId:int}")]
        public async Task<IActionResult> DeleteDecreeLine(int decId, int itemId)
        {
            try
            {
                const string sql = "DELETE FROM PAY2_DECREE_LINE WHERE DEC_ID=@decId AND ITEM_ID=@itemId";
                await _db.DoExecuteSQLAsync(sql, new { decId, itemId });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("lookup")]
        public async Task<ActionResult<IEnumerable<LookupDto<int>>>> GetEmployeesLookup()
        {
            const string sql = "SELECT EMP_ID AS Id, EMP_CODE + ' - ' + LAST_NAME + ' ' + FIRST_NAME AS Name FROM PAY2_EMPLOYEE WHERE IS_ACTIVE = 1";
            return Ok(await _db.DoGetDataSQLAsync<LookupDto<int>>(sql));
        }

        [HttpGet("{empId:int}/leave-balance")]
        public async Task<ActionResult<int>> GetLeaveBalance(int empId, [FromQuery] int year)
        {
            const string sql = "SELECT ISNULL(BALANCE_MIN, 0) FROM PAY2_LEAVE_BAL WHERE EMP_ID = @empId AND YEAR = @year";
            var bal = await _db.DoGetDataSQLAsyncSingle<int?>(sql, new { empId, year });
            return Ok(bal ?? 0);
        }

        [HttpGet("{empId:int}/leaves")]
        public async Task<ActionResult<IEnumerable<Pay2LeaveDto>>> GetLeaves(int empId)
        {
            const string sql = "SELECT * FROM PAY2_LEAVE WHERE EMP_ID = @empId ORDER BY START_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2LeaveDto>(sql, new { empId }));
        }

        [HttpPost("leave/save")]
        public async Task<IActionResult> SaveLeave([FromBody] Pay2LeaveDto leave)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

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
        public async Task<IActionResult> DeleteLeave(int levId)
        {
            try
            {
                await _db.DoExecuteSQLAsync("DELETE FROM PAY2_LEAVE WHERE LEV_ID = @levId", new { levId });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpGet("{empId:int}/contracts")]
        public async Task<ActionResult<IEnumerable<Pay2ContractDto>>> GetContracts(int empId)
        {
            const string sql = "SELECT * FROM PAY2_CONTRACT WHERE EMP_ID = @empId ORDER BY START_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2ContractDto>(sql, new { empId }));
        }

        [HttpPost("contract/save")]
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
        public async Task<IActionResult> DeleteContract(int conId)
        {
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
        public async Task<ActionResult<IEnumerable<Pay2LeaveBalDto>>> GetLeaveBalances(int empId)
        {
            const string sql = "SELECT * FROM PAY2_LEAVE_BAL WHERE EMP_ID = @empId ORDER BY YEAR DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2LeaveBalDto>(sql, new { empId }));
        }

        [HttpPost("leave-balance/save")]
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
        public async Task<IActionResult> DeleteLeaveBalance(int empId, int year)
        {
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
        public async Task<ActionResult<IEnumerable<Pay2LoanDto>>> GetLoans(int empId)
        {
            const string sql = "SELECT * FROM PAY2_LOAN WHERE EMP_ID = @empId ORDER BY LOAN_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2LoanDto>(sql, new { empId }));
        }

        [HttpPost("loan/save")]
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
                        // درج وام جدید
                        const string insertSql = @"
                    INSERT INTO PAY2_LOAN (EMP_ID, WS_ID, LOAN_TYPE, LOAN_DATE, AMOUNT, INSTALLMENT, TOTAL_INST, PAID_INST, FIRST_PAY, PURPOSE, IS_ACTIVE, CREATED_AT, CREATED_BY)
                    OUTPUT INSERTED.LOAN_ID
                    VALUES (@EMP_ID, @WS_ID, @LOAN_TYPE, @LOAN_DATE, @AMOUNT, @INSTALLMENT, @TOTAL_INST, 0, @FIRST_PAY, @PURPOSE, @IS_ACTIVE, GETDATE(), @User)";

                        var p = new DynamicParameters(loan);
                        p.Add("User", userCod);
                        savedLoanId = await conn.QuerySingleAsync<int>(insertSql, p, tran);
                    }
                    else
                    {
                        // آپدیت وام موجود (فقط مقادیر هدر تغییر می‌کند)
                        const string updateSql = @"
                    UPDATE PAY2_LOAN 
                    SET LOAN_TYPE=@LOAN_TYPE, LOAN_DATE=@LOAN_DATE, AMOUNT=@AMOUNT, INSTALLMENT=@INSTALLMENT, TOTAL_INST=@TOTAL_INST, FIRST_PAY=@FIRST_PAY, PURPOSE=@PURPOSE, IS_ACTIVE=@IS_ACTIVE
                    WHERE LOAN_ID=@LOAN_ID";
                        await conn.ExecuteAsync(updateSql, loan, tran);
                    }

                    // 🔴 جادوی سیستم: تولید اتوماتیک یا آپدیت اقساط 🔴
                    // این SP که در دیتابیس شما وجود دارد، اقساط باقی‌مانده را بر اساس تاریخ FIRST_PAY می‌سازد
                    await conn.ExecuteAsync("SP_PAY2_LOAN_GEN_SCHED", new { LOAN_ID = savedLoanId }, tran, commandType: CommandType.StoredProcedure);
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("loan/{loanId:int}")]
        public async Task<IActionResult> DeleteLoan(int loanId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // برای جلوگیری از خطای کلید خارجی، ابتدا اقساط (جدول فرزند) پاک می‌شوند
                    await conn.ExecuteAsync("DELETE FROM PAY2_LOAN_SCHED WHERE LOAN_ID = @loanId", new { loanId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_LOAN WHERE LOAN_ID = @loanId", new { loanId }, tran);
                });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest("خطا در حذف وام (ممکن است اقساطی از آن در ماه قبل کسر شده باشد). " + ex.Message);
            }
        }

        [HttpGet("{empId:int}/overrides")]
        public async Task<ActionResult<IEnumerable<Pay2OverrideDto>>> GetOverrides(int empId)
        {
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
        public async Task<IActionResult> SaveOverride([FromBody] Pay2OverrideDto ovr, [FromQuery] bool isEditing)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
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
        public async Task<IActionResult> DeleteOverride(int empId, int itemId, long validFrom)
        {
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
        public async Task<ActionResult<IEnumerable<Pay2AdvanceExclDto>>> GetAdvanceExcls(int empId)
        {
            const string sql = "SELECT * FROM PAY2_ADVANCE_EXCL WHERE EMP_ID = @empId ORDER BY PERIOD_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2AdvanceExclDto>(sql, new { empId }));
        }

        [HttpPost("advance-excl/save")]
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
        public async Task<IActionResult> DeleteAdvanceExcl(int exclId)
        {
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
        public async Task<ActionResult<IEnumerable<Pay2SettlementDto>>> GetSettlements(int empId)
        {
            const string sql = "SELECT * FROM PAY2_SETTLEMENT WHERE EMP_ID = @empId ORDER BY SETTLE_DATE DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2SettlementDto>(sql, new { empId }));
        }

        [HttpPost("{empId:int}/settlement/calculate")]
        public async Task<IActionResult> CalculateSettlement(int empId, [FromQuery] int wsId, [FromBody] Pay2SettlementInputDto input)
        {
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
        public async Task<IActionResult> FinalizeSettlement(int setId)
        {
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
        public async Task<IActionResult> DeleteSettlement(int setId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // خواندن شناسه پرسنل برای بازگرداندن وضعیت او به فعال
                    int? empId = await conn.QuerySingleOrDefaultAsync<int?>("SELECT EMP_ID FROM PAY2_SETTLEMENT WHERE SET_ID=@setId AND STATUS=1", new { setId }, tran);
                    if (!empId.HasValue) throw new InvalidOperationException("تسویه حساب یافت نشد یا از حالت پیش‌نویس خارج شده است.");

                    await conn.ExecuteAsync("DELETE FROM PAY2_SETTLEMENT WHERE SET_ID = @setId", new { setId }, tran);

                    // برگرداندن پرسنل به حالت فعال (چون محاسبه تسویه باعث غیرفعال شدن او شده بود)
                    await conn.ExecuteAsync("UPDATE PAY2_EMPLOYEE SET IS_ACTIVE = 1, FIRE_DATE = NULL WHERE EMP_ID = @empId", new { empId }, tran);
                });
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{empId:int}")]
        public async Task<IActionResult> DeleteEmployee(int empId)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    // ۱. بررسی بسیار دقیق تمام وابستگی‌های عملیاتی، مالی و رسمی
                    string checkSql = @"
                SELECT 
                    (SELECT COUNT(1) FROM PAY2_RUN_LINE WHERE EMP_ID = @empId) AS RunCount,
                    (SELECT COUNT(1) FROM PAY2_ATTENDANCE WHERE EMP_ID = @empId) AS AttCount,
                    (SELECT COUNT(1) FROM PAY2_SETTLEMENT WHERE EMP_ID = @empId) AS SetCount,
                    (SELECT COUNT(1) FROM PAY2_LOAN WHERE EMP_ID = @empId) AS LoanCount,
                    (SELECT COUNT(1) FROM PAY2_DECREE WHERE EMP_ID = @empId AND IS_CONFIRMED = 1) AS ConfirmedDecreeCount,
                    (SELECT COUNT(1) FROM PAY2_LEAVE WHERE EMP_ID = @empId AND STATUS > 1) AS ApprovedLeaveCount";

                    var checks = await conn.QuerySingleAsync<dynamic>(checkSql, new { empId }, tran);

                    // ۲. اعتبارسنجی خطوط قرمز (Red Lines)
                    if (checks.RunCount > 0)
                        throw new InvalidOperationException("این پرسنل دارای محاسبه حقوق (فیش صادر شده) است و قابل حذف نیست.");

                    if (checks.AttCount > 0)
                        throw new InvalidOperationException("برای این پرسنل کارکرد ماهیانه ثبت شده است و قابل حذف نیست.");

                    if (checks.SetCount > 0)
                        throw new InvalidOperationException("این پرسنل دارای سابقه تسویه حساب است و قابل حذف نیست.");

                    if (checks.LoanCount > 0)
                        throw new InvalidOperationException("این پرسنل دارای سابقه وام است (حتی تسویه شده). به دلیل سوابق مالی، حذف فیزیکی ممنوع است.");

                    if (checks.ConfirmedDecreeCount > 0)
                        throw new InvalidOperationException("این پرسنل دارای احکام کارگزینی تأیید شده است. مدارک رسمی قابل حذف نیستند.");

                    if (checks.ApprovedLeaveCount > 0)
                        throw new InvalidOperationException("این پرسنل دارای مرخصی تأیید شده است و سوابق آن قابل حذف نیست.");

                    // ۳. اگر به این مرحله رسید، یعنی پرسنل به صورت آزمایشی/اشتباهی ثبت شده 
                    // و فقط رکوردهای پیش‌نویس (Draft) دارد. حالا با خیال راحت زباله‌ها را پاک می‌کنیم.

                    // پاک کردن اقلام احکامِ پیش‌نویس
                    await conn.ExecuteAsync("DELETE FROM PAY2_DECREE_LINE WHERE DEC_ID IN (SELECT DEC_ID FROM PAY2_DECREE WHERE EMP_ID = @empId)", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_DECREE WHERE EMP_ID = @empId", new { empId }, tran);

                    // پاک کردن سایر سوابق بی‌اهمیت
                    await conn.ExecuteAsync("DELETE FROM PAY2_CONTRACT WHERE EMP_ID = @empId", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_LEAVE_BAL WHERE EMP_ID = @empId", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_LEAVE WHERE EMP_ID = @empId", new { empId }, tran); // فقط پیش‌نویس‌ها مانده‌اند
                    await conn.ExecuteAsync("DELETE FROM PAY2_OVERRIDE WHERE EMP_ID = @empId", new { empId }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_ADVANCE_EXCL WHERE EMP_ID = @empId", new { empId }, tran);

                    // ۴. حذف فیزیکی خود پرسنل
                    int deletedRows = await conn.ExecuteAsync("DELETE FROM PAY2_EMPLOYEE WHERE EMP_ID = @empId", new { empId }, tran);

                    if (deletedRows == 0)
                    {
                        throw new InvalidOperationException("پرسنل مورد نظر در سیستم یافت نشد.");
                    }
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
        public async Task<ActionResult<IEnumerable<Pay2ItemTemplateDto>>> GetTemplates()
        {
            const string sql = @"
        SELECT T.TMPL_ID, T.TMPL_CODE, T.TMPL_NAME, T.WS_ID, 
               T.IS_ACTIVE, T.NOTES, W.WS_NAME AS WorkshopName
        FROM PAY2_ITEM_TEMPLATE T
        LEFT JOIN PAY2_WORKSHOP W ON T.WS_ID = W.WS_ID
        ORDER BY T.TMPL_ID DESC";
            return Ok(await _db.DoGetDataSQLAsync<Pay2ItemTemplateDto>(sql));
        }

        [HttpPost("template/save")]
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
        public async Task<ActionResult<IEnumerable<Pay2ItemTmplLineDto>>> GetTemplateLines(int tmplId)
        {
            const string sql = @"SELECT L.TMPL_ID, L.ITEM_ID, I.ITEM_NAME, L.DEF_AMOUNT, L.INS_OV, L.TAX_OV, L.BASIS_OV
                         FROM PAY2_ITEM_TMPL_LINE L
                         INNER JOIN PAY2_ITEM_DEF I ON L.ITEM_ID = I.ITEM_ID
                         WHERE L.TMPL_ID = @tmplId
                         ORDER BY I.SORT_ORDER";
            return Ok(await _db.DoGetDataSQLAsync<Pay2ItemTmplLineDto>(sql, new { tmplId }));
        }

        [HttpPost("template/line/save")]
        public async Task<IActionResult> SaveTemplateLine([FromBody] Pay2ItemTmplLineDto line)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    int count = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_ITEM_TMPL_LINE WHERE TMPL_ID=@TMPL_ID AND ITEM_ID=@ITEM_ID", new { line.TMPL_ID, line.ITEM_ID }, tran);

                    if (count == 0)
                    {
                        const string insertSql = @"INSERT INTO PAY2_ITEM_TMPL_LINE (TMPL_ID, ITEM_ID, DEF_AMOUNT, INS_OV, TAX_OV, BASIS_OV)
                                           VALUES (@TMPL_ID, @ITEM_ID, @DEF_AMOUNT, @INS_OV, @TAX_OV, @BASIS_OV)";
                        await conn.ExecuteAsync(insertSql, line, tran);
                    }
                    else
                    {
                        const string updateSql = @"UPDATE PAY2_ITEM_TMPL_LINE 
                                           SET DEF_AMOUNT=@DEF_AMOUNT, INS_OV=@INS_OV, TAX_OV=@TAX_OV, BASIS_OV=@BASIS_OV
                                           WHERE TMPL_ID=@TMPL_ID AND ITEM_ID=@ITEM_ID";
                        await conn.ExecuteAsync(updateSql, line, tran);
                    }
                });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

        [HttpDelete("template/{tmplId:int}/line/{itemId:int}")]
        public async Task<IActionResult> DeleteTemplateLine(int tmplId, int itemId)
        {
            try
            {
                await _db.DoExecuteSQLAsync("DELETE FROM PAY2_ITEM_TMPL_LINE WHERE TMPL_ID=@tmplId AND ITEM_ID=@itemId", new { tmplId, itemId });
                return Ok();
            }
            catch (Exception ex) { return BadRequest(ex.Message); }
        }

    }
}