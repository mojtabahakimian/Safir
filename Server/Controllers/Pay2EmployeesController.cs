using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Salary;
using System.Security.Claims;

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

    }
}