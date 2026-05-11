using Dapper;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Security.Claims;

namespace Safir.Server.Controllers;

[ApiController]
[Route("api/pay2/workshops")]
public class Pay2WorkshopsController : ControllerBase
{
    private readonly IDatabaseService _db;

    public Pay2WorkshopsController(IDatabaseService db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Pay2WorkshopDto>>> GetAll()
    {
        const string sql = @"
SELECT WS_ID, WS_CODE, WS_NAME, NATIONAL_ID, SOCIAL_INS_CODE, TAX_CODE,
       ADDRESS, PHONE, IS_ACTIVE, ISNULL(INS_MODE, 1) AS INS_MODE
FROM PAY2_WORKSHOP
ORDER BY WS_ID";

        var data = await _db.DoGetDataSQLAsync<Pay2WorkshopDto>(sql);
        return Ok(data);
    }

    [HttpGet("{wsId:int}/accounts")]
    public async Task<ActionResult<Pay2WorkshopAccDto>> GetAccounts(int wsId)
    {
        const string sql = @"
SELECT ACC_KEY, ACC_CODE
FROM PAY2_WORKSHOP_ACC
WHERE WS_ID = @wsId";

        var rows = await _db.DoGetDataSQLAsync<AccRow>(sql, new { wsId });

        var acc = new Pay2WorkshopAccDto { WS_ID = wsId };

        foreach (var row in rows)
        {
            switch (row.ACC_KEY)
            {
                case "ADV_HES_K": acc.ADV_HES_K = row.ACC_CODE; break;
                case "ADV_HES_M": acc.ADV_HES_M = row.ACC_CODE; break;
                case "SALARY_EXP": acc.SALARY_EXP = row.ACC_CODE; break;
                case "SALARY_PAYABLE": acc.SALARY_PAYABLE = row.ACC_CODE; break;
                case "INS_EXP": acc.INS_EXP = row.ACC_CODE; break;
                case "INS_PAYABLE": acc.INS_PAYABLE = row.ACC_CODE; break;
                case "TAX_PAYABLE": acc.TAX_PAYABLE = row.ACC_CODE; break;
            }
        }

        return Ok(acc);
    }

    [HttpPost("save")]
    public async Task<ActionResult<int>> Save(Pay2WorkshopSaveRequest request)
    {
        var w = request.Workshop;
        var a = request.Accounts;

        if (string.IsNullOrWhiteSpace(w.WS_CODE))
            return BadRequest("کد کارگاه نمی‌تواند خالی باشد.");

        if (string.IsNullOrWhiteSpace(w.WS_NAME))
            return BadRequest("نام کارگاه نمی‌تواند خالی باشد.");

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdString, out int userCod))
            return Unauthorized();


        var wsId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
        {
            int newOrUpdatedWsId = w.WS_ID;

            // ── بررسی تکراری بودن WS_CODE — داخل transaction با UPDLOCK ──
            // UPDLOCK: رکورد رو Lock می‌کنه تا transaction تموم بشه → race condition نداریم
            var duplicateId = await conn.QueryFirstOrDefaultAsync<int?>(@"
    SELECT TOP 1 WS_ID
    FROM PAY2_WORKSHOP WITH (UPDLOCK, ROWLOCK)
    WHERE WS_CODE = @WS_CODE
      AND WS_ID <> @WS_ID",
                new { WS_CODE = w.WS_CODE!.Trim(), WS_ID = w.WS_ID },
                tran);

            if (duplicateId.HasValue)
                throw new InvalidOperationException("این کد کارگاه قبلاً برای کارگاه دیگری ثبت شده است.");

            if (w.WS_ID == 0)
            {
                const string insertSql = @"
INSERT INTO PAY2_WORKSHOP
(WS_CODE, WS_NAME, NATIONAL_ID, SOCIAL_INS_CODE, TAX_CODE, ADDRESS, PHONE, IS_ACTIVE, INS_MODE, CREATED_BY)
OUTPUT INSERTED.WS_ID
VALUES
(@WS_CODE, @WS_NAME, @NATIONAL_ID, @SOCIAL_INS_CODE, @TAX_CODE, @ADDRESS, @PHONE, @IS_ACTIVE, @INS_MODE, @CREATED_BY)";
                newOrUpdatedWsId = await conn.QueryFirstAsync<int>(insertSql, new
                {
                    w.WS_CODE,
                    w.WS_NAME,
                    w.NATIONAL_ID,
                    w.SOCIAL_INS_CODE,
                    w.TAX_CODE,
                    w.ADDRESS,
                    w.PHONE,
                    w.IS_ACTIVE,
                    w.INS_MODE,
                    CREATED_BY = userCod
                }, tran);
            }
            else
            {
                const string updateSql = @"
UPDATE PAY2_WORKSHOP SET
  WS_CODE          = @WS_CODE,
  WS_NAME          = @WS_NAME,
  NATIONAL_ID      = @NATIONAL_ID,
  SOCIAL_INS_CODE  = @SOCIAL_INS_CODE,
  TAX_CODE         = @TAX_CODE,
  ADDRESS          = @ADDRESS,
  PHONE            = @PHONE,
  IS_ACTIVE        = @IS_ACTIVE,
  INS_MODE         = @INS_MODE
WHERE WS_ID = @WS_ID";
                await conn.ExecuteAsync(updateSql, w, tran);
            }

            const string accSql = @"
IF EXISTS (SELECT 1 FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID AND ACC_KEY = @ACC_KEY)
    UPDATE PAY2_WORKSHOP_ACC
    SET ACC_CODE = @ACC_CODE
    WHERE WS_ID = @WS_ID AND ACC_KEY = @ACC_KEY
ELSE
    INSERT INTO PAY2_WORKSHOP_ACC (WS_ID, ACC_KEY, ACC_CODE)
    VALUES (@WS_ID, @ACC_KEY, @ACC_CODE)";

            var accEntries = new[]
            {
                ("ADV_HES_K", a.ADV_HES_K),
                ("ADV_HES_M", a.ADV_HES_M),
                ("SALARY_EXP", a.SALARY_EXP),
                ("SALARY_PAYABLE", a.SALARY_PAYABLE),
                ("INS_EXP", a.INS_EXP),
                ("INS_PAYABLE", a.INS_PAYABLE),
                ("TAX_PAYABLE", a.TAX_PAYABLE),
            };

            foreach (var item in accEntries)
            {
                if (string.IsNullOrWhiteSpace(item.Item2))
                {
                    await conn.ExecuteAsync(
                        "DELETE FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID AND ACC_KEY = @ACC_KEY",
                        new
                        {
                            WS_ID = newOrUpdatedWsId,
                            ACC_KEY = item.Item1
                        },
                        tran);

                    continue;
                }

                await conn.ExecuteAsync(accSql, new
                {
                    WS_ID = newOrUpdatedWsId,
                    ACC_KEY = item.Item1,
                    ACC_CODE = item.Item2.Trim()
                }, tran);
            }

            return newOrUpdatedWsId;
        });

        return Ok(wsId);
    }

    private class AccRow
    {
        public string ACC_KEY { get; set; } = "";
        public string ACC_CODE { get; set; } = "";
    }
}