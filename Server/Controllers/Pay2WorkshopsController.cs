using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Security.Claims;

namespace Safir.Server.Controllers;

[ApiController]
[Route("api/pay2/workshops")]
[Authorize]   // ← اضافه شد: تمام endpoint ها نیاز به احراز هویت دارند
public class Pay2WorkshopsController : ControllerBase
{
    private readonly IDatabaseService _db;

    public Pay2WorkshopsController(IDatabaseService db)
    {
        _db = db;
    }

    // ── GET api/pay2/workshops ──────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Pay2WorkshopDto>>> GetAll()
    {
        const string sql = @"
SELECT WS_ID, WS_CODE, WS_NAME, NATIONAL_ID, SOCIAL_INS_CODE, TAX_CODE,
       ADDRESS, PHONE, IS_ACTIVE, ISNULL(INS_MODE, 1) AS INS_MODE
FROM   PAY2_WORKSHOP
ORDER  BY WS_ID";

        var data = await _db.DoGetDataSQLAsync<Pay2WorkshopDto>(sql);
        return Ok(data);
    }

    // ── GET api/pay2/workshops/{wsId}/accounts ──────────────────────────────────
    [HttpGet("{wsId:int}/accounts")]
    public async Task<ActionResult<Pay2WorkshopAccDto>> GetAccounts(int wsId)
    {
        const string sql = @"
SELECT ACC_KEY, ACC_CODE
FROM   PAY2_WORKSHOP_ACC
WHERE  WS_ID = @wsId";

        var rows = await _db.DoGetDataSQLAsync<AccRow>(sql, new { wsId });

        var acc = new Pay2WorkshopAccDto { WS_ID = wsId };

        foreach (var row in rows)
        {
            switch (row.ACC_KEY)
            {
                // ── مساعده هوشمند: یک key ترکیبی ─────────────────────────────────────
                // SP_PAY2_GET_ADVANCES دنبال ACC_KEY='ADV_HES' می‌گردد و مقدار را parse می‌کند.
                // فرمت: "کل-معین" یا "کل-معین-تفصیلی" (مثال: "112-1" یا "213-1-5")
                case "ADV_HES": acc.ADV_HES = row.ACC_CODE; break;

                // ── سند حقوق ──────────────────────────────────────────────────────────
                case "SALARY_EXP": acc.SALARY_EXP = row.ACC_CODE; break;
                case "SALARY_PAYABLE": acc.SALARY_PAYABLE = row.ACC_CODE; break;

                // ── سند بیمه و مالیات ─────────────────────────────────────────────────
                case "INS_EXP": acc.INS_EXP = row.ACC_CODE; break;
                case "INS_PAYABLE": acc.INS_PAYABLE = row.ACC_CODE; break;
                case "TAX_PAYABLE": acc.TAX_PAYABLE = row.ACC_CODE; break;
            }
        }

        return Ok(acc);
    }

    // ── POST api/pay2/workshops/save ────────────────────────────────────────────
    [HttpPost("save")]
    public async Task<ActionResult<int>> Save(Pay2WorkshopSaveRequest request)
    {
        var w = request.Workshop;
        var a = request.Accounts;

        // validation پایه
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

            // بررسی تکراری بودن WS_CODE
            var duplicateId = await conn.QueryFirstOrDefaultAsync<int?>(@"
SELECT TOP 1 WS_ID
FROM   PAY2_WORKSHOP WITH (UPDLOCK, ROWLOCK)
WHERE  WS_CODE = @WS_CODE
  AND  WS_ID  <> @WS_ID",
              new { WS_CODE = w.WS_CODE!.Trim(), WS_ID = w.WS_ID },
              tran);

            if (duplicateId.HasValue)
                throw new InvalidOperationException("این کد کارگاه قبلاً برای کارگاه دیگری ثبت شده است.");

            // ── INSERT یا UPDATE کارگاه ──────────────────────────────────────────────
            if (w.WS_ID == 0)
            {
                const string insertSql = @"
INSERT INTO PAY2_WORKSHOP
  (WS_CODE, WS_NAME, NATIONAL_ID, SOCIAL_INS_CODE, TAX_CODE,
   ADDRESS, PHONE, IS_ACTIVE, INS_MODE, CREATED_BY)
OUTPUT INSERTED.WS_ID
VALUES
  (@WS_CODE, @WS_NAME, @NATIONAL_ID, @SOCIAL_INS_CODE, @TAX_CODE,
   @ADDRESS, @PHONE, @IS_ACTIVE, @INS_MODE, @CREATED_BY)";

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
  WS_CODE         = @WS_CODE,
  WS_NAME         = @WS_NAME,
  NATIONAL_ID     = @NATIONAL_ID,
  SOCIAL_INS_CODE = @SOCIAL_INS_CODE,
  TAX_CODE        = @TAX_CODE,
  ADDRESS         = @ADDRESS,
  PHONE           = @PHONE,
  IS_ACTIVE       = @IS_ACTIVE,
  INS_MODE        = @INS_MODE
WHERE WS_ID = @WS_ID";

                await conn.ExecuteAsync(updateSql, w, tran);
            }

            // ── UPSERT/DELETE سرفصل‌های حسابداری ────────────────────────────────────
            // قانون: اگر مقدار خالی باشد → DELETE رکورد، در غیر این صورت → UPSERT
            const string accUpsertSql = @"
IF EXISTS (SELECT 1 FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID AND ACC_KEY = @ACC_KEY)
  UPDATE PAY2_WORKSHOP_ACC
     SET ACC_CODE = @ACC_CODE
   WHERE WS_ID = @WS_ID AND ACC_KEY = @ACC_KEY
ELSE
  INSERT INTO PAY2_WORKSHOP_ACC (WS_ID, ACC_KEY, ACC_CODE)
  VALUES (@WS_ID, @ACC_KEY, @ACC_CODE)";

            const string accDeleteSql = @"
DELETE FROM PAY2_WORKSHOP_ACC
WHERE WS_ID = @WS_ID AND ACC_KEY = @ACC_KEY";

            // لیست جفت‌های (ACC_KEY, مقدار) — فقط یک key برای مساعده (ADV_HES)
            var accEntries = new[]
            {
        ("ADV_HES",        a.ADV_HES),        // ← یک key ترکیبی — SP این را می‌خواند
        ("SALARY_EXP",     a.SALARY_EXP),
        ("SALARY_PAYABLE", a.SALARY_PAYABLE),
        ("INS_EXP",        a.INS_EXP),
        ("INS_PAYABLE",    a.INS_PAYABLE),
        ("TAX_PAYABLE",    a.TAX_PAYABLE),
      };

            foreach (var (key, code) in accEntries)
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    // مقدار خالی → حذف رکورد (اگر وجود داشت)
                    await conn.ExecuteAsync(accDeleteSql,
                      new { WS_ID = newOrUpdatedWsId, ACC_KEY = key },
                      tran);
                }
                else
                {
                    // مقدار دارد → UPSERT
                    await conn.ExecuteAsync(accUpsertSql, new
                    {
                        WS_ID = newOrUpdatedWsId,
                        ACC_KEY = key,
                        ACC_CODE = code.Trim()
                    }, tran);
                }
            }

            return newOrUpdatedWsId;
        });

        return Ok(wsId);
    }

    // ── helper private class ──────────────────────────────────────────────────
    private class AccRow
    {
        public string ACC_KEY { get; set; } = "";
        public string ACC_CODE { get; set; } = "";
    }
}