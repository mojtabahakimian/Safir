using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/dashboard")]
    [Authorize]
    public class Pay2DashboardController : ControllerBase
    {
        readonly IDatabaseService _db;
        readonly ILogger<Pay2DashboardController> _logger;

        public Pay2DashboardController(IDatabaseService db, ILogger<Pay2DashboardController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpGet("{wsId:int}")]
        public async Task<ActionResult<Pay2DashboardDataDto>> GetDashboardData(int wsId)
        {
            if (wsId <= 0) return BadRequest("شناسه کارگاه نامعتبر است.");

            try
            {
                var data = new Pay2DashboardDataDto { WS_ID = wsId };

                // 1. اطلاعات کارگاه (اضافه شدن WITH (NOLOCK) برای نهایت سرعت)
                data.WorkshopName = await _db.DoGetDataSQLAsyncSingle<string>(
                    "SELECT WS_NAME FROM PAY2_WORKSHOP WITH (NOLOCK) WHERE WS_ID = @wsId", new { wsId }) ?? "نامشخص";

                // 2. تنظیمات مرخصی
                data.LeaveMinsPerDay = await _db.DoGetDataSQLAsyncSingle<int?>(
                    "SELECT TRY_CAST(CFG_VALUE AS INT) FROM PAY2_CONFIG WITH (NOLOCK) WHERE CFG_KEY = 'LEAVE_MINS_PER_DAY'") ?? 440;

                // 3. پیدا کردن آخرین دوره
                var periodSql = @"
                    SELECT TOP 1 PER_ID, PERIOD_DATE, STATUS, DEED_N_S_PAY
                    FROM PAY2_PERIOD WITH (NOLOCK)
                    WHERE WS_ID = @wsId 
                    ORDER BY PERIOD_DATE DESC";

                var latestPeriod = await _db.DoGetDataSQLAsyncSingle<PeriodInfoRow>(periodSql, new { wsId });
                int currentPerId = 0;
                double payrollNs = 999999999D;

                if (latestPeriod != null)
                {
                    currentPerId = latestPeriod.PER_ID;
                    data.LatestPeriodDate = latestPeriod.PERIOD_DATE;
                    data.PeriodStatus = latestPeriod.STATUS;

                    // تبدیل تاریخ به عنوان شمسی
                    long pDate = latestPeriod.PERIOD_DATE;
                    string year = (pDate / 10000).ToString();
                    string month = ((pDate / 100) % 100).ToString("D2");
                    data.PeriodTitle = $"{year}/{month}";

                    if (latestPeriod.DEED_N_S_PAY != null && latestPeriod.DEED_N_S_PAY > 0)
                    {
                        payrollNs = (double)latestPeriod.DEED_N_S_PAY;
                    }
                }

                // 4. تعداد پرسنل فعال
                data.ActiveEmployeesCount = await _db.DoGetDataSQLAsyncSingle<int>(
                    "SELECT COUNT(1) FROM PAY2_EMPLOYEE WITH (NOLOCK) WHERE WS_ID = @wsId AND IS_ACTIVE = 1", new { wsId });

                if (currentPerId > 0)
                {
                    // 5. تخمین حقوق خالص از آخرین محاسبه (RUN)
                    var runSql = @"
                        SELECT SUM(RL.NET_PAY) 
                        FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                        INNER JOIN PAY2_RUN R WITH (NOLOCK) ON RL.RUN_ID = R.RUN_ID
                        WHERE R.PER_ID = @currentPerId AND R.IS_LATEST = 1";
                    data.EstimatedNetPay = await _db.DoGetDataSQLAsyncSingle<long?>(runSql, new { currentPerId }) ?? 0;

                    // 6. استخراج ۵ مساعده آخر
                    var advSettings = await _db.DoGetDataSQLAsyncSingle<string>(
                        "SELECT ACC_CODE FROM PAY2_WORKSHOP_ACC WITH (NOLOCK) WHERE WS_ID = @wsId AND ACC_KEY = N'ADV_HES'", new { wsId });

                    if (!string.IsNullOrWhiteSpace(advSettings))
                    {
                        var advRows = await _db.DoGetStoreProcedureSQLAsync<Pay2DashboardAdvanceRowDto>(
                            "dbo.SP_PAY2_GET_ADVANCES",
                            new
                            {
                                PERIOD_DATE = data.LatestPeriodDate,
                                PAYROLL_N_S = payrollNs,
                                WS_ID = wsId
                            });

                        var filteredAdvances = advRows.Where(x => x.ADVANCE_DEDUCTION > 0).ToList();
                        data.TotalAdvances = filteredAdvances.Sum(x => x.ADVANCE_DEDUCTION);
                        data.RecentAdvances = filteredAdvances.OrderByDescending(x => x.ADVANCE_DEDUCTION).Take(5).ToList();
                    }
                }

                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard data for Workshop {WsId}", wsId);
                return StatusCode(500, "خطا در دریافت اطلاعات داشبورد.");
            }
        }
        public class PeriodInfoRow
        {
            public int PER_ID { get; set; }
            public long PERIOD_DATE { get; set; }
            public byte STATUS { get; set; }
            public double? DEED_N_S_PAY { get; set; }
        }
    }
}