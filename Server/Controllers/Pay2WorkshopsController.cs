using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Data;
using System.Security.Claims;
using System.Text.RegularExpressions;

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
                   ADDRESS, PHONE, POSTAL_CODE, EMPLOYER_NAME, IS_ACTIVE, ISNULL(INS_MODE, 1) AS INS_MODE, SHIFT_MODE,
                   PROVINCE, CITY, REGISTRATION_NUMBER, SSO_BRANCH, FINANCIAL_MANAGER, ADMIN_MANAGER,
                   ISNULL(DEFAULT_DEED_MODE, 1) AS DEFAULT_DEED_MODE
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
                case "OTHER_DED_HES": acc.OTHER_DED_HES = row.ACC_CODE; break;
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
        w.SHIFT_MODE = string.IsNullOrWhiteSpace(w.SHIFT_MODE) ? null : w.SHIFT_MODE;
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
                    new { w.WS_CODE, w.WS_ID }, tran);

                if (duplicateId.HasValue)
                    throw new InvalidOperationException("این کد کارگاه قبلاً برای کارگاه دیگری ثبت شده است.");

                if (w.WS_ID == 0)
                {
                    const string insertSql = @"
                        INSERT INTO PAY2_WORKSHOP
                        (WS_CODE, WS_NAME, NATIONAL_ID, SOCIAL_INS_CODE, TAX_CODE,
                         ADDRESS, PHONE, POSTAL_CODE, EMPLOYER_NAME, IS_ACTIVE, INS_MODE, CREATED_BY, SHIFT_MODE,
                         PROVINCE, CITY, REGISTRATION_NUMBER, SSO_BRANCH, FINANCIAL_MANAGER, ADMIN_MANAGER, DEFAULT_DEED_MODE)
                        OUTPUT INSERTED.WS_ID
                        VALUES
                        (@WS_CODE, @WS_NAME, @NATIONAL_ID, @SOCIAL_INS_CODE, @TAX_CODE,
                         @ADDRESS, @PHONE, @POSTAL_CODE, @EMPLOYER_NAME, @IS_ACTIVE, @INS_MODE, @CREATED_BY, @SHIFT_MODE,
                         @PROVINCE, @CITY, @REGISTRATION_NUMBER, @SSO_BRANCH, @FINANCIAL_MANAGER, @ADMIN_MANAGER, @DEFAULT_DEED_MODE)";

                    newOrUpdatedWsId = await conn.QueryFirstAsync<int>(insertSql, new
                    {
                        w.WS_CODE,
                        w.WS_NAME,
                        w.NATIONAL_ID,
                        w.SOCIAL_INS_CODE,
                        w.TAX_CODE,
                        w.ADDRESS,
                        w.PHONE,
                        w.POSTAL_CODE,
                        w.EMPLOYER_NAME,
                        w.IS_ACTIVE,
                        w.INS_MODE,
                        w.SHIFT_MODE,
                        CREATED_BY = userCod,
                        w.PROVINCE,
                        w.CITY,
                        w.REGISTRATION_NUMBER,
                        w.SSO_BRANCH,
                        w.FINANCIAL_MANAGER,
                        w.ADMIN_MANAGER,
                        w.DEFAULT_DEED_MODE
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
                        POSTAL_CODE     = @POSTAL_CODE,
                        EMPLOYER_NAME   = @EMPLOYER_NAME,
                        IS_ACTIVE       = @IS_ACTIVE,
                        INS_MODE        = @INS_MODE,
                        SHIFT_MODE      = @SHIFT_MODE,
                        PROVINCE        = @PROVINCE,
                        CITY            = @CITY,
                        REGISTRATION_NUMBER = @REGISTRATION_NUMBER,
                        SSO_BRANCH      = @SSO_BRANCH,
                        FINANCIAL_MANAGER = @FINANCIAL_MANAGER,
                        ADMIN_MANAGER   = @ADMIN_MANAGER,
                        DEFAULT_DEED_MODE = @DEFAULT_DEED_MODE
                        WHERE WS_ID = @WS_ID";

                    await conn.ExecuteAsync(updateSql, w, tran);
                }

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
                    ("BANK_PAY_HES",        a.BANK_PAY_HES),
                    ("OTHER_DED_HES",       a.OTHER_DED_HES)
                };

                var sqlBuilder = new System.Text.StringBuilder();
                var parameters = new DynamicParameters();
                parameters.Add("WS_ID", newOrUpdatedWsId);

                int pIndex = 0;
                foreach (var (key, code) in accEntries)
                {
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        sqlBuilder.AppendLine($"DELETE FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID AND ACC_KEY = '{key}';");
                    }
                    else
                    {
                        parameters.Add($"ACC_CODE_{pIndex}", code.Trim());
                        sqlBuilder.AppendLine($@"
                            IF EXISTS (SELECT 1 FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @WS_ID AND ACC_KEY = '{key}')
                                UPDATE PAY2_WORKSHOP_ACC
                                   SET ACC_CODE = @ACC_CODE_{pIndex}
                                 WHERE WS_ID = @WS_ID AND ACC_KEY = '{key}';
                            ELSE
                                INSERT INTO PAY2_WORKSHOP_ACC (WS_ID, ACC_KEY, ACC_CODE)
                                VALUES (@WS_ID, '{key}', @ACC_CODE_{pIndex});");
                        pIndex++;
                    }
                }

                if (sqlBuilder.Length > 0)
                {
                    await conn.ExecuteAsync(sqlBuilder.ToString(), parameters, tran);
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

    [HttpDelete("{wsId:int}")]
    public async Task<IActionResult> Delete(int wsId)
    {
        try
        {
            await _db.ExecuteInTransactionAsync(async (conn, tran) =>
            {
                int empCount = await conn.QuerySingleAsync<int>(
                    "SELECT COUNT(1) FROM PAY2_EMPLOYEE WHERE WS_ID = @wsId", new { wsId }, tran);
                if (empCount > 0)
                    throw new InvalidOperationException($"این کارگاه دارای {empCount} پرسنل است. لطفاً به جای حذف، وضعیت آن را «غیرفعال» کنید.");

                int periodCount = await conn.QuerySingleAsync<int>(
                    "SELECT COUNT(1) FROM PAY2_PERIOD WHERE WS_ID = @wsId", new { wsId }, tran);
                if (periodCount > 0)
                    throw new InvalidOperationException($"برای این کارگاه {periodCount} دوره حقوق ثبت شده است و قابل حذف فیزیکی نیست.");

                int tmplCount = await conn.QuerySingleAsync<int>(
                    "SELECT COUNT(1) FROM PAY2_ITEM_TEMPLATE WHERE WS_ID = @wsId", new { wsId }, tran);
                if (tmplCount > 0)
                    throw new InvalidOperationException("قالب‌های حقوقی به این کارگاه متصل هستند. ابتدا ارتباط آن‌ها را حذف کنید.");

                await conn.ExecuteAsync("DELETE FROM PAY2_WORKSHOP_ACC WHERE WS_ID = @wsId", new { wsId }, tran);

                int rows = await conn.ExecuteAsync("DELETE FROM PAY2_WORKSHOP WHERE WS_ID = @wsId", new { wsId }, tran);
                if (rows == 0)
                    throw new InvalidOperationException("کارگاه یافت نشد.");
            });

            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (System.Data.SqlClient.SqlException sqlEx) when (sqlEx.Number == 547)
        {
            return BadRequest("این کارگاه در بخش‌های دیگر سیستم در حال استفاده است و قابل حذف نیست.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, "خطای سیستمی در حذف کارگاه: " + ex.Message);
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
        w.DEFAULT_DEED_MODE = (byte)(w.DEFAULT_DEED_MODE == 2 ? 2 : 1);
        w.POSTAL_CODE = NormalizeOptionalDigits(w.POSTAL_CODE, 20);
        w.EMPLOYER_NAME = CleanText(w.EMPLOYER_NAME, 100);

        w.PROVINCE = CleanText(w.PROVINCE, 50);
        w.CITY = CleanText(w.CITY, 50);
        w.REGISTRATION_NUMBER = CleanText(w.REGISTRATION_NUMBER, 20);
        w.SSO_BRANCH = CleanText(w.SSO_BRANCH, 50);
        w.FINANCIAL_MANAGER = CleanText(w.FINANCIAL_MANAGER, 100);
        w.ADMIN_MANAGER = CleanText(w.ADMIN_MANAGER, 100);

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
        a.OTHER_DED_HES = NormalizeAccountCode(a.OTHER_DED_HES, 20);

        if (!string.IsNullOrWhiteSpace(w.POSTAL_CODE) && !Regex.IsMatch(w.POSTAL_CODE, @"^\d+$"))
            return "کد پستی فقط باید عدد باشد.";

        if (string.IsNullOrWhiteSpace(w.WS_CODE))
            return "کد کارگاه نمی‌تواند خالی باشد.";

        if (!Regex.IsMatch(w.WS_CODE, @"^\d+$"))
            return "کد کارگاه فقط باید عدد باشد.";

        if (string.IsNullOrWhiteSpace(w.WS_NAME))
            return "نام کارگاه نمی‌تواند خالی باشد.";

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

    private static string? CleanText(string? value, int maxLength = 0)
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