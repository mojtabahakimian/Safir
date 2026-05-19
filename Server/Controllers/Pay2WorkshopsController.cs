using System.Security.Claims;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;

namespace Safir.Server.Controllers;

[ApiController]
[Route("api/pay2/workshops")]
[Authorize]
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
FROM   PAY2_WORKSHOP
ORDER  BY WS_ID";

        var data = await _db.DoGetDataSQLAsync<Pay2WorkshopDto>(sql);
        return Ok(data);
    }

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
                case "ADV_HES": acc.ADV_HES = row.ACC_CODE; break;
                case "SALARY_EXP_TOLID": acc.SALARY_EXP_TOLID = row.ACC_CODE; break;
                case "SALARY_EXP_EDARI": acc.SALARY_EXP_EDARI = row.ACC_CODE; break;
                case "SALARY_EXP_FOROSH": acc.SALARY_EXP_FOROSH = row.ACC_CODE; break;
                case "SALARY_EXP_KHADAMAT": acc.SALARY_EXP_KHADAMAT = row.ACC_CODE; break;
                case "SALARY_PAYABLE": acc.SALARY_PAYABLE = row.ACC_CODE; break;
                case "INS_EXP": acc.INS_EXP = row.ACC_CODE; break;
                case "INS_PAYABLE": acc.INS_PAYABLE = row.ACC_CODE; break;
                case "TAX_PAYABLE": acc.TAX_PAYABLE = row.ACC_CODE; break;
                case "LOAN_HES": acc.LOAN_HES = row.ACC_CODE; break;
                case "BANK_PAY_HES": acc.BANK_PAY_HES = row.ACC_CODE; break;
            }
        }

        return Ok(acc);
    }

    [HttpPost("save")]
    public async Task<ActionResult<int>> Save(Pay2WorkshopSaveRequest request)
    {
        if (request?.Workshop == null)
            return BadRequest("اطلاعات کارگاه ارسال نشده است.");

        request.Accounts ??= new Pay2WorkshopAccDto();

        var w = request.Workshop;
        var a = request.Accounts;

        var validationError = NormalizeAndValidate(w, a);
        if (!string.IsNullOrWhiteSpace(validationError))
            return BadRequest(validationError);

        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdString, out int userCod))
            return Unauthorized();

        try
        {
            var wsId = await _db.ExecuteInTransactionAsync(async (conn, tran) =>
            {
                int newOrUpdatedWsId = w.WS_ID;

                var duplicateId = await conn.QueryFirstOrDefaultAsync<int?>(@"
SELECT TOP 1 WS_ID
FROM   PAY2_WORKSHOP WITH (UPDLOCK, ROWLOCK)
WHERE  WS_CODE = @WS_CODE
  AND  WS_ID  <> @WS_ID",
                    new { w.WS_CODE, w.WS_ID },
                    tran);

                if (duplicateId.HasValue)
                    throw new InvalidOperationException("این کد کارگاه قبلاً برای کارگاه دیگری ثبت شده است.");

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

                var accEntries = new[]
                {
                    ("ADV_HES",             a.ADV_HES),
                    ("SALARY_EXP_TOLID",    a.SALARY_EXP_TOLID),
                    ("SALARY_EXP_EDARI",    a.SALARY_EXP_EDARI),
                    ("SALARY_EXP_FOROSH",   a.SALARY_EXP_FOROSH),
                    ("SALARY_EXP_KHADAMAT", a.SALARY_EXP_KHADAMAT),
                    ("SALARY_PAYABLE",      a.SALARY_PAYABLE),
                    ("INS_EXP",             a.INS_EXP),
                    ("INS_PAYABLE",         a.INS_PAYABLE),
                    ("TAX_PAYABLE",         a.TAX_PAYABLE),
                    ("LOAN_HES",            a.LOAN_HES),
                    ("BANK_PAY_HES",        a.BANK_PAY_HES)
                };

                foreach (var (key, code) in accEntries)
                {
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        await conn.ExecuteAsync(accDeleteSql,
                            new { WS_ID = newOrUpdatedWsId, ACC_KEY = key },
                            tran);
                    }
                    else
                    {
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static string? NormalizeAndValidate(Pay2WorkshopDto w, Pay2WorkshopAccDto a)
    {
        w.WS_CODE = NormalizeRequiredDigits(w.WS_CODE, 10);
        w.WS_NAME = CleanText(w.WS_NAME, 100);
        w.NATIONAL_ID = NormalizeOptionalDigits(w.NATIONAL_ID, 11);
        w.SOCIAL_INS_CODE = NormalizeOptionalDigits(w.SOCIAL_INS_CODE, 20);
        w.PHONE = NormalizeOptionalDigits(w.PHONE, 20);
        w.TAX_CODE = CleanText(w.TAX_CODE, 20);
        w.ADDRESS = CleanText(w.ADDRESS, 300);
        w.INS_MODE = w.INS_MODE == 2 ? 2 : 1;

        a.ADV_HES = NormalizeAccountCode(a.ADV_HES, 20);
        a.SALARY_EXP = CleanText(a.SALARY_EXP, 20);
        a.SALARY_EXP_TOLID = NormalizeAccountCode(a.SALARY_EXP_TOLID, 20);
        a.SALARY_EXP_EDARI = NormalizeAccountCode(a.SALARY_EXP_EDARI, 20);
        a.SALARY_EXP_FOROSH = NormalizeAccountCode(a.SALARY_EXP_FOROSH, 20);
        a.SALARY_EXP_KHADAMAT = NormalizeAccountCode(a.SALARY_EXP_KHADAMAT, 20);
        a.SALARY_PAYABLE = NormalizeAccountCode(a.SALARY_PAYABLE, 20);
        a.INS_EXP = NormalizeAccountCode(a.INS_EXP, 20);
        a.INS_PAYABLE = NormalizeAccountCode(a.INS_PAYABLE, 20);
        a.TAX_PAYABLE = NormalizeAccountCode(a.TAX_PAYABLE, 20);
        a.LOAN_HES = NormalizeAccountCode(a.LOAN_HES, 20);
        a.BANK_PAY_HES = NormalizeAccountCode(a.BANK_PAY_HES, 20);

        if (string.IsNullOrWhiteSpace(w.WS_CODE))
            return "کد کارگاه نمی‌تواند خالی باشد.";

        if (!Regex.IsMatch(w.WS_CODE, @"^\d+$"))
            return "کد کارگاه فقط باید عدد باشد.";

        if (string.IsNullOrWhiteSpace(w.WS_NAME))
            return "نام کارگاه نمی‌تواند خالی باشد.";

        if (!string.IsNullOrWhiteSpace(w.NATIONAL_ID) &&
            !Regex.IsMatch(w.NATIONAL_ID, @"^\d{11}$"))
            return "شناسه ملی باید دقیقاً ۱۱ رقم عددی باشد.";

        if (!string.IsNullOrWhiteSpace(w.SOCIAL_INS_CODE) &&
            !Regex.IsMatch(w.SOCIAL_INS_CODE, @"^\d+$"))
            return "کد کارگاه بیمه فقط باید عدد باشد.";

        if (!string.IsNullOrWhiteSpace(w.PHONE) &&
            !Regex.IsMatch(w.PHONE, @"^\d+$"))
            return "شماره تلفن فقط باید عدد باشد.";

        if (!string.IsNullOrWhiteSpace(a.ADV_HES) &&
            !Regex.IsMatch(a.ADV_HES, @"^\d+(?:-\d+)+$"))
            return "فرمت حساب مساعده هوشمند نادرست است. مثال صحیح: 112-1 یا 213-1-5";

        return null;
    }

    private static string? NormalizeRequiredDigits(string? value, int maxLength)
    {
        var normalized = NormalizeDigits(value, maxLength);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptionalDigits(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return NormalizeDigits(value, maxLength);
    }

    private static string? NormalizeDigits(string? value, int maxLength)
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
            else if (!char.IsWhiteSpace(ch))
                return "__INVALID__";

            if (result.Count > maxLength)
                return "__INVALID__";
        }

        var text = new string(result.ToArray());
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? NormalizeAccountCode(string? value, int maxLength)
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
                result.Add('-');
            else if (!char.IsWhiteSpace(ch))
                return "__INVALID__";

            if (result.Count > maxLength)
                return "__INVALID__";
        }

        var text = new string(result.ToArray());
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? CleanText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();

        return text.Length > maxLength
            ? text[..maxLength]
            : text;
    }

    private class AccRow
    {
        public string ACC_KEY { get; set; } = "";
        public string ACC_CODE { get; set; } = "";
    }
}