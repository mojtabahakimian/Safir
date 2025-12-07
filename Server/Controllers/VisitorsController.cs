using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Visitory;
using System.Security.Claims;
using Safir.Shared.Constants;
using Safir.Shared.Models; // For PagedResult
using Dapper;
using Safir.Shared.Utility;

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


        [HttpGet("my-customers")]
        public async Task<ActionResult<PagedResult<VISITOR_CUSTOMERS>>> GetMyVisitorCustomers(
            [FromQuery] long? visitDate,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? searchTerm = null)
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
                else { return Ok(new PagedResult<VISITOR_CUSTOMERS>()); }
            }

            var parameters = new DynamicParameters();
            parameters.Add("UserHes", userHes);
            parameters.Add("VisitDateToQuery", dateToQuery);
            parameters.Add("Offset", (pageNumber - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var whereConditions = new List<string>
            {
                "(vd.OKF = 1)",
                "(dtl.HES = @UserHes)",
                "(dtl.VDATE = @VisitDateToQuery)"
            };

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string normalizedSearchTerm = searchTerm.Trim().FixPersianChars();
                whereConditions.Add("(ch.NAME LIKE @SearchPattern OR ch.HES LIKE @SearchPattern OR ch.TEL LIKE @SearchPattern OR ch.MOBILE LIKE @SearchPattern)");
                parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%");
            }

            string whereClause = string.Join(" AND ", whereConditions);

            // Optimized Query: Paging first, then heavy joins
            string sql = $@"
                WITH PagedVisitorCustomers AS (
                    SELECT 
                        dtl.HES, dtl.VDATE, dtl.COUST_NO, dtl.TOPLACE, 
                        ch.NAME, ch.ADDRESS, ch.TEL, ch.MOBILE, ch.Latitude, ch.Longitude
                    FROM dbo.VISITORS_DAY_DTL dtl
                    INNER JOIN dbo.CUST_HESAB ch ON dtl.COUST_NO = ch.hes
                    INNER JOIN dbo.VISITORS_DAY vd ON dtl.HES = vd.HES AND dtl.VDATE = vd.VDATE
                    WHERE {whereClause}
                    ORDER BY ch.NAME
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                )
                SELECT
                    pvc.HES AS userid, pvc.VDATE, pvc.COUST_NO AS hes,
                    ISNULL(qbm.BEDM, 0) - ISNULL(qbm.BESM, 0) AS mandahh,
                    pvc.NAME AS person,
                    pvc.ADDRESS + N' ' + ISNULL(pvc.TEL, N'') + N' ' + ISNULL(pvc.MOBILE, N'') AS addr,
                    0 AS mandahas,
                    ISNULL(az.TOPETEB, 0) AS etebar,
                    RIGHT(ISNULL(lg.lastdt, N''), 8) AS lkharid,
                    pvc.Latitude, pvc.Longitude, pvc.TOPLACE, 1 AS OKF
                FROM PagedVisitorCustomers pvc
                LEFT OUTER JOIN dbo.last_generate lg ON pvc.COUST_NO = lg.hes
                LEFT OUTER JOIN dbo.AZAE az ON pvc.COUST_NO = az.HES
                LEFT OUTER JOIN dbo.Q_BEDEHBESTANH_MAIN qbm ON pvc.COUST_NO = qbm.HES
                ORDER BY pvc.NAME";

            string countSql = $@"
                SELECT COUNT(dtl.COUST_NO)
                FROM dbo.VISITORS_DAY_DTL dtl
                INNER JOIN dbo.CUST_HESAB ch ON dtl.COUST_NO = ch.hes
                INNER JOIN dbo.VISITORS_DAY vd ON dtl.HES = vd.HES AND dtl.VDATE = vd.VDATE
                WHERE {whereClause}";

            try
            {
                var customers = await _dbService.DoGetDataSQLAsync<VISITOR_CUSTOMERS>(sql, parameters);
                int totalCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                return Ok(new PagedResult<VISITOR_CUSTOMERS>
                {
                    Items = customers.ToList(),
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching visitor customers for UserHes: {UserHes}, Date: {VisitDate}", userHes, dateToQuery);
                return StatusCode(500, "خطای داخلی سرور.");
            }
        }
    }
}