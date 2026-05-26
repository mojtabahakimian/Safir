using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Security.Claims;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/itemdefs")]
    [Authorize]
    public class Pay2ItemDefController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public Pay2ItemDefController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Pay2ItemDefDto>>> GetAll()
        {
            const string sql = "SELECT * FROM PAY2_ITEM_DEF ORDER BY SORT_ORDER ASC, ITEM_NAME ASC";
            var data = await _db.DoGetDataSQLAsync<Pay2ItemDefDto>(sql);
            return Ok(data);
        }

        [HttpPost("save")]
        public async Task<ActionResult<int>> Save([FromBody] Pay2ItemDefDto item)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) return Unauthorized();

            // --- اضافه شدن: نرمال‌سازی و اعتبارسنجی سمت سرور ---
            item.ITEM_CODE = item.ITEM_CODE?.Trim().ToUpperInvariant();
            item.ITEM_NAME = item.ITEM_NAME?.Trim();

            if (string.IsNullOrWhiteSpace(item.ITEM_CODE))
                return BadRequest("کد آیتم الزامی است.");
            if (string.IsNullOrWhiteSpace(item.ITEM_NAME))
                return BadRequest("نام آیتم الزامی است.");
            // --------------------------------------------------

            try
            {
                int newId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    int currentId = item.ITEM_ID;

                    // بررسی تکراری بودن کد آیتم
                    int exists = await conn.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM PAY2_ITEM_DEF WHERE ITEM_CODE = @ITEM_CODE AND ITEM_ID <> @ITEM_ID",
                        new { item.ITEM_CODE, item.ITEM_ID }, tran);

                    if (exists > 0)
                        throw new InvalidOperationException($"کد آیتم «{item.ITEM_CODE}» تکراری است.");

                    if (item.ITEM_ID == 0)
                    {
                        const string insertSql = @"
                            INSERT INTO PAY2_ITEM_DEF 
                            (ITEM_CODE, ITEM_NAME, ITEM_TYPE, CALC_BASIS, INS_SUBJECT, TAX_SUBJECT, INS_BASE_DAYS, PAY_BASE_DAYS, IS_SYSTEM, SHOW_IN_SLIP, SORT_ORDER, IS_ACTIVE, NOTES, CREATED_BY)
                            OUTPUT INSERTED.ITEM_ID
                            VALUES (@ITEM_CODE, @ITEM_NAME, @ITEM_TYPE, @CALC_BASIS, @INS_SUBJECT, @TAX_SUBJECT, @INS_BASE_DAYS, @PAY_BASE_DAYS, 0, @SHOW_IN_SLIP, @SORT_ORDER, @IS_ACTIVE, @NOTES, @User)";

                        var p = new DynamicParameters(item);
                        p.Add("User", userCod);
                        currentId = await conn.QuerySingleAsync<int>(insertSql, p, tran);
                    }
                    else
                    {
                        // بررسی اینکه آیا کاربر در حال تغییر کد آیتم سیستمی است؟
                        bool isSystem = await conn.QuerySingleAsync<bool>("SELECT IS_SYSTEM FROM PAY2_ITEM_DEF WHERE ITEM_ID = @ITEM_ID", new { item.ITEM_ID }, tran);
                        if (isSystem)
                        {
                            // برای آیتم سیستمی اجازه تغییر ITEM_CODE و ITEM_TYPE داده نمی‌شود
                            const string updateSysSql = @"
                                UPDATE PAY2_ITEM_DEF SET 
                                ITEM_NAME=@ITEM_NAME, CALC_BASIS=@CALC_BASIS, INS_SUBJECT=@INS_SUBJECT, TAX_SUBJECT=@TAX_SUBJECT, 
                                INS_BASE_DAYS=@INS_BASE_DAYS, PAY_BASE_DAYS=@PAY_BASE_DAYS, SHOW_IN_SLIP=@SHOW_IN_SLIP, SORT_ORDER=@SORT_ORDER, IS_ACTIVE=@IS_ACTIVE, NOTES=@NOTES
                                WHERE ITEM_ID=@ITEM_ID";
                            await conn.ExecuteAsync(updateSysSql, item, tran);
                        }
                        else
                        {
                            const string updateSql = @"
                                UPDATE PAY2_ITEM_DEF SET 
                                ITEM_CODE=@ITEM_CODE, ITEM_NAME=@ITEM_NAME, ITEM_TYPE=@ITEM_TYPE, CALC_BASIS=@CALC_BASIS, INS_SUBJECT=@INS_SUBJECT, TAX_SUBJECT=@TAX_SUBJECT, 
                                INS_BASE_DAYS=@INS_BASE_DAYS, PAY_BASE_DAYS=@PAY_BASE_DAYS, SHOW_IN_SLIP=@SHOW_IN_SLIP, SORT_ORDER=@SORT_ORDER, IS_ACTIVE=@IS_ACTIVE, NOTES=@NOTES
                                WHERE ITEM_ID=@ITEM_ID";
                            await conn.ExecuteAsync(updateSql, item, tran);
                        }
                    }

                    return currentId;
                });

                return Ok(newId);
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    bool isSystem = await conn.QuerySingleAsync<bool>("SELECT IS_SYSTEM FROM PAY2_ITEM_DEF WHERE ITEM_ID = @id", new { id }, tran);
                    if (isSystem)
                        throw new InvalidOperationException("آیتم‌های سیستمی قابل حذف نیستند. می‌توانید آن‌ها را غیرفعال کنید.");

                    // بررسی اتصال به احکام و قالب‌ها
                    int usedInDecrees = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_DECREE_LINE WHERE ITEM_ID = @id", new { id }, tran);
                    if (usedInDecrees > 0)
                        throw new InvalidOperationException("این آیتم در احکام پرسنل استفاده شده و قابل حذف فیزیکی نیست. لطفاً آن را غیرفعال کنید.");

                    int usedInTemplates = await conn.QuerySingleAsync<int>("SELECT COUNT(1) FROM PAY2_ITEM_TMPL_LINE WHERE ITEM_ID = @id", new { id }, tran);
                    if (usedInTemplates > 0)
                        throw new InvalidOperationException("این آیتم در قالب‌های حکم استفاده شده است.");

                    await conn.ExecuteAsync("DELETE FROM PAY2_ATT_VALUE WHERE ITEM_ID = @id", new { id }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_OVERRIDE WHERE ITEM_ID = @id", new { id }, tran);
                    await conn.ExecuteAsync("DELETE FROM PAY2_ITEM_DEF WHERE ITEM_ID = @id", new { id }, tran);
                });
                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }
    }
}