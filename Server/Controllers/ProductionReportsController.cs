using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Models.Production;
using Safir.Shared.Interfaces;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Dapper; // اضافه شده برای Extension Method های دپر (ExecuteAsync)
using System.Security.Claims; // اضافه شده برای استخراج شناسه کاربر

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    //[Authorize]
    public class ProductionReportsController : ControllerBase
    {
        private readonly IDatabaseService _db;

        // IUserStateService حذف شد چون شناسه را مستقیماً از توکن کلیمز می‌خوانیم
        public ProductionReportsController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var query = "SELECT * FROM [dbo].[PRD_ProductionReports] ORDER BY ReportDate DESC, Id DESC";

                // اصلاح متد به DoGetDataSQLAsync بر اساس اینترفیس شما
                var result = await _db.DoGetDataSQLAsync<ProductionReportDto>(query);

                return Ok(result ?? new List<ProductionReportDto>());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"خطای سرور: {ex.Message}");
            }
        }


        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProductionReportDto model)
        {
            try
            {
                // دریافت شناسه کاربر به صورت مستقیم از Claims احراز هویت
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                  ?? User.FindFirst("UserId")?.Value;

              
                if (!int.TryParse(userIdClaim, out int userId))
                {
                    userId = 78;
                    //return Unauthorized("شناسه کاربر در سیستم احراز هویت یافت نشد.");
                }

                var query = @"
                    INSERT INTO [dbo].[PRD_ProductionReports] 
                    (UserId, ReportDate, WheyCompany, ConcentrationStart, ConcentrationEnd, ConcentrationWheyQty, SprayStart, SprayEnd, SprayPowderQty, CounterNumber, Description, CreatedAt)
                    VALUES 
                    (@UserId, @ReportDate, @WheyCompany, @ConcentrationStart, @ConcentrationEnd, @ConcentrationWheyQty, @SprayStart, @SprayEnd, @SprayPowderQty, @CounterNumber, @Description, GETDATE());";

                model.UserId = userId;

                // اصلاح نحوه فراخوانی تراکنش طبق delegate تعریف شده در اینترفیس شما
                await _db.ExecuteInTransactionAsync(async (conn, tr) =>
                {
                    await conn.ExecuteAsync(query, model, transaction: tr);
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"خطای ذخیره‌سازی: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ProductionReportDto model)
        {
            try
            {
                var query = @"
                    UPDATE [dbo].[PRD_ProductionReports] SET
                        ReportDate = @ReportDate,
                        WheyCompany = @WheyCompany,
                        ConcentrationStart = @ConcentrationStart,
                        ConcentrationEnd = @ConcentrationEnd,
                        ConcentrationWheyQty = @ConcentrationWheyQty,
                        SprayStart = @SprayStart,
                        SprayEnd = @SprayEnd,
                        SprayPowderQty = @SprayPowderQty,
                        CounterNumber = @CounterNumber,
                        Description = @Description
                    WHERE Id = @Id;";

                model.Id = id;

                // اصلاح تراکنش
                await _db.ExecuteInTransactionAsync(async (conn, tr) =>
                {
                    await conn.ExecuteAsync(query, model, transaction: tr);
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"خطای بروزرسانی: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var query = "DELETE FROM [dbo].[PRD_ProductionReports] WHERE Id = @Id;";

                // اصلاح تراکنش
                await _db.ExecuteInTransactionAsync(async (conn, tr) =>
                {
                    await conn.ExecuteAsync(query, new { Id = id }, transaction: tr);
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"خطای حذف: {ex.Message}");
            }
        }
    }
}