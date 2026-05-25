using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Security.Claims;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/advances")]
    [Authorize]
    public class Pay2AdvancesController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public Pay2AdvancesController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet("settings")]
        public async Task<ActionResult<Pay2SmartAdvanceSettingsDto>> GetSettings([FromQuery] int wsId)
        {
            if (wsId <= 0)
                return BadRequest("کارگاه نامعتبر است.");

            const string sql = @"
                SELECT
                    @wsId AS WS_ID,

                    ISNULL((
                        SELECT TOP 1 ACC_CODE
                        FROM PAY2_WORKSHOP_ACC
                        WHERE WS_ID = @wsId AND ACC_KEY = N'ADV_HES'
                    ), N'') AS ADV_HES,

                    ISNULL((
                        SELECT TOP 1 CFG_VALUE
                        FROM PAY2_CONFIG
                        WHERE CFG_KEY = N'ADV_SCOPE'
                    ), N'CURRENT_MONTH') AS ADV_SCOPE,

                    CAST(ISNULL((
                        SELECT TOP 1 TRY_CAST(CFG_VALUE AS INT)
                        FROM PAY2_CONFIG
                        WHERE CFG_KEY = N'ADV_MIN_POSITIVE'
                    ), 1) AS BIT) AS ADV_MIN_POSITIVE,

                    CAST(ISNULL((
                        SELECT TOP 1 TRY_CAST(CFG_VALUE AS INT)
                        FROM PAY2_CONFIG
                        WHERE CFG_KEY = N'ADV_USE_HES_T_FILTER'
                    ), 1) AS BIT) AS ADV_USE_HES_T_FILTER;";

            var result = await _db.DoGetDataSQLAsyncSingle<Pay2SmartAdvanceSettingsDto>(sql, new { wsId });
            return Ok(result ?? new Pay2SmartAdvanceSettingsDto { WS_ID = wsId });
        }

        [HttpPost("settings/save")]
        public async Task<IActionResult> SaveSettings([FromBody] Pay2SmartAdvanceSettingsDto settings)
        {
            if (settings.WS_ID <= 0)
                return BadRequest("کارگاه نامعتبر است.");

            settings.ADV_HES = NormalizeAccountCode(settings.ADV_HES);

            if (string.IsNullOrWhiteSpace(settings.ADV_HES))
                return BadRequest("کد حساب مساعده الزامی است.");

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    const string sql = @"
                        MERGE PAY2_WORKSHOP_ACC AS T
                        USING (
                            SELECT 
                                @WS_ID AS WS_ID,
                                N'ADV_HES' AS ACC_KEY,
                                @ADV_HES AS ACC_CODE,
                                N'حساب مساعده هوشمند' AS ACC_DESC
                        ) AS S
                        ON T.WS_ID = S.WS_ID AND T.ACC_KEY = S.ACC_KEY
                        WHEN MATCHED THEN
                            UPDATE SET ACC_CODE = S.ACC_CODE, ACC_DESC = S.ACC_DESC
                        WHEN NOT MATCHED THEN
                            INSERT (WS_ID, ACC_KEY, ACC_CODE, ACC_DESC)
                            VALUES (S.WS_ID, S.ACC_KEY, S.ACC_CODE, S.ACC_DESC);";

                    await conn.ExecuteAsync(sql, new { settings.WS_ID, settings.ADV_HES }, tran);
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("calculate")]
        public async Task<ActionResult<IEnumerable<Pay2SmartAdvanceRowDto>>> Calculate([FromBody] Pay2SmartAdvanceCalcRequest request)
        {
            if (request.WS_ID <= 0)
                return BadRequest("کارگاه نامعتبر است.");


            const string periodCheckSql = @"
SELECT COUNT(1)
FROM PAY2_PERIOD
WHERE WS_ID = @WS_ID
  AND PERIOD_DATE = @PERIOD_DATE;";

            var periodExistsRows = await _db.DoGetDataSQLAsync<int>(periodCheckSql, new
            {
                request.WS_ID,
                request.PERIOD_DATE
            });

            if (periodExistsRows.FirstOrDefault() == 0)
                return BadRequest("دوره انتخاب‌شده برای این کارگاه در سیستم وجود ندارد.");

            try
            {
                var settings = await _db.DoGetDataSQLAsyncSingle<Pay2SmartAdvanceSettingsDto>(@"
                    SELECT
                        @WS_ID AS WS_ID,
                        ISNULL((
                            SELECT TOP 1 ACC_CODE
                            FROM PAY2_WORKSHOP_ACC
                            WHERE WS_ID = @WS_ID AND ACC_KEY = N'ADV_HES'
                        ), N'') AS ADV_HES,
                        N'' AS ADV_SCOPE,
                        CAST(1 AS BIT) AS ADV_MIN_POSITIVE,
                        CAST(1 AS BIT) AS ADV_USE_HES_T_FILTER;",
                    new { request.WS_ID });

                if (settings == null || string.IsNullOrWhiteSpace(settings.ADV_HES))
                    return BadRequest("برای این کارگاه، کد حساب مساعده هوشمند در PAY2_WORKSHOP_ACC با کلید ADV_HES تنظیم نشده است.");

                double payrollNs = request.PAYROLL_N_S.GetValueOrDefault();

                if (payrollNs <= 0)
                {
                    var periodPayrollNs = await _db.DoGetDataSQLAsyncSingle<double?>(@"
                        SELECT DEED_N_S_PAY
                        FROM PAY2_PERIOD
                        WHERE WS_ID = @WS_ID AND PERIOD_DATE = @PERIOD_DATE;",
                        new { request.WS_ID, request.PERIOD_DATE });

                    payrollNs = periodPayrollNs.GetValueOrDefault();

                    if (payrollNs <= 0)
                        payrollNs = 999999999D;
                }

                var rows = await _db.DoGetDataSQLAsync<Pay2SmartAdvanceRowDto>(
                    "EXEC dbo.SP_PAY2_GET_ADVANCES @PERIOD_DATE=@PERIOD_DATE, @PAYROLL_N_S=@PAYROLL_N_S, @WS_ID=@WS_ID",
                    new
                    {
                        request.PERIOD_DATE,
                        PAYROLL_N_S = payrollNs,
                        request.WS_ID
                    });

                return Ok(rows ?? Enumerable.Empty<Pay2SmartAdvanceRowDto>());
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private static bool IsValidPayrollPeriodDate(long periodDate)
        {
            int year = (int)(periodDate / 10000);
            int month = (int)((periodDate / 100) % 100);
            int day = (int)(periodDate % 100);

            return year >= 1300 &&
                   year <= 1499 &&
                   month >= 1 &&
                   month <= 12 &&
                   day == 0;
        }

        private static string? NormalizeAccountCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var result = new List<char>();

            foreach (var ch in value.Trim())
            {
                if (ch >= '0' && ch <= '9')
                    result.Add(ch);
                else if (ch >= '۰' && ch <= '۹')
                    result.Add((char)('0' + (ch - '۰')));
                else if (ch >= '٠' && ch <= '٩')
                    result.Add((char)('0' + (ch - '٠')));
                else if (ch == '-')
                    result.Add(ch);
            }

            var text = new string(result.ToArray()).Trim('-');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
    }
}