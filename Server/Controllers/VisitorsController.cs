// مسیر فایل: Safir.Server/Controllers/VisitorsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Visitory;
using System.Security.Claims;
using Safir.Shared.Constants;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class VisitorsController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<VisitorsController> _logger;

        public VisitorsController(IDatabaseService dbService, ILogger<VisitorsController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        // اندپوینت دریافت تاریخ ها (بدون تغییر)
        [HttpGet("my-visit-dates")]
        public async Task<ActionResult<IEnumerable<long>>> GetMyVisitDates()
        {
            var userHes = User.FindFirstValue(BaseknowClaimTypes.USER_HES);
            if (string.IsNullOrEmpty(userHes)) { return BadRequest("HES یافت نشد."); }
            const string sql = "SELECT DISTINCT VDATE FROM dbo.VISITORS_DAY WHERE HES = @UserHes ORDER BY VDATE DESC";
            try
            {
                var dates = await _dbService.DoGetDataSQLAsync<long>(sql, new { UserHes = userHes });
                return Ok(dates ?? new List<long>());
            }
            catch (Exception ex) { _logger.LogError(ex, "Error fetching visitor dates for UserHes: {UserHes}", userHes); return StatusCode(500); }
        }


        // --- اندپوینت بازگردانی شده برای دریافت *همه* مشتریان بر اساس تاریخ ---
        [HttpGet("my-customers")]
        public async Task<ActionResult<IEnumerable<VISITOR_CUSTOMERS>>> GetMyVisitorCustomers([FromQuery] long? visitDate)
        {
            var userHes = User.FindFirstValue(BaseknowClaimTypes.USER_HES);
            if (string.IsNullOrEmpty(userHes)) { return BadRequest("HES یافت نشد."); }

            long dateToQuery;
            if (visitDate.HasValue) { dateToQuery = visitDate.Value; }
            else
            {
                const string latestDateSql = "SELECT MAX(VDATE) FROM dbo.VISITORS_DAY WHERE HES = @UserHes AND OKF = 1";
                var latestDateResult = await _dbService.DoGetDataSQLAsyncSingle<long?>(latestDateSql, new { UserHes = userHes });
                if (latestDateResult.HasValue) { dateToQuery = latestDateResult.Value; }
                else { return Ok(new List<VISITOR_CUSTOMERS>()); } // نتیجه خالی اگر تاریخی نیست
            }

            // کوئری اصلی بدون صفحه بندی و جستجوی سروری
            const string sql = @"
                SELECT
                    dtl.HES AS userid, dtl.VDATE, dtl.COUST_NO AS hes,
                    qbm.BEDM - qbm.BESM AS mandahh, ch.NAME AS person,
                    ch.ADDRESS + N' ' + ISNULL(ch.TEL, N'') + N' ' + ISNULL(ch.MOBILE, N'') AS addr,
                    0 AS mandahas, az.TOPETEB AS etebar, RIGHT(ISNULL(lg.lastdt, N''), 8) AS lkharid,
                    ch.Latitude, ch.Longitude, dtl.TOPLACE, vd.OKF
                FROM dbo.VISITORS_DAY_DTL dtl
                INNER JOIN dbo.CUST_HESAB ch ON dtl.COUST_NO = ch.hes
                INNER JOIN dbo.VISITORS_DAY vd ON dtl.HES = vd.HES AND dtl.VDATE = vd.VDATE
                LEFT OUTER JOIN dbo.last_generate lg ON dtl.COUST_NO = lg.hes
                LEFT OUTER JOIN dbo.AZAE az ON dtl.COUST_NO = az.HES
                LEFT OUTER JOIN dbo.Q_BEDEHBESTANH_MAIN qbm ON dtl.COUST_NO = qbm.HES
                WHERE (vd.OKF = 1)
                  AND (dtl.HES = @UserHes)
                  AND (dtl.VDATE = @VisitDateToQuery)
                ORDER BY ch.NAME"; // مرتب سازی همچنان خوب است

            try
            {
                var parameters = new { UserHes = userHes, VisitDateToQuery = dateToQuery };
                var customers = await _dbService.DoGetDataSQLAsync<VISITOR_CUSTOMERS>(sql, parameters);
                return Ok(customers ?? new List<VISITOR_CUSTOMERS>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all visitor customers for UserHes: {UserHes}, Date: {VisitDate}", userHes, dateToQuery);
                return StatusCode(500, "خطای داخلی سرور.");
            }
        }
    }
}