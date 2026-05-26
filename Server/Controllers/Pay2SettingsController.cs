using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;
using System.Security.Claims;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/pay2/settings")]
    [Authorize]
    public class Pay2SettingsController : ControllerBase
    {
        private readonly IDatabaseService _db;

        public Pay2SettingsController(IDatabaseService db)
        {
            _db = db;
        }

        [HttpGet("configs")]
        public async Task<ActionResult<IEnumerable<Pay2ConfigDto>>> GetConfigs()
        {
            const string sql = @"
SELECT
    CFG_KEY,
    CFG_VALUE,
    CFG_OPTIONS,
    CFG_DEFAULT,
    CFG_SECTION,
    LABEL_FA,
    DESC_FA,
    OPT_LABELS,
    DATA_TYPE,
    ACCESS_LEVEL
FROM PAY2_CONFIG
ORDER BY
    CASE CFG_SECTION
        WHEN N'محاسبه' THEN 1
        WHEN N'بیمه' THEN 2
        WHEN N'مالیات' THEN 3
        WHEN N'مساعده' THEN 4
        WHEN N'مرخصی' THEN 5
        WHEN N'تسویه' THEN 6
        WHEN N'امنیت' THEN 7
        ELSE 99
    END,
    CFG_KEY;";

            var rows = await _db.DoGetDataSQLAsync<Pay2ConfigDto>(sql);
            return Ok(rows ?? Enumerable.Empty<Pay2ConfigDto>());
        }

        [HttpPost("configs/save")]
        public async Task<IActionResult> SaveConfigs([FromBody] Pay2ConfigSaveRequest request)
        {
            if (request == null || request.Items == null || request.Items.Count == 0)
                return BadRequest("هیچ تنظیمی برای ذخیره ارسال نشده است.");

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod))
                return Unauthorized("شناسه کاربر معتبر نیست.");

            try
            {
                // 🚀 حل مشکل N+1 Query: واکشی تمامی تنظیمات پایه به صورت یکجا
                var existingConfigs = (await _db.DoGetDataSQLAsync<Pay2ConfigDto>(
                    "SELECT CFG_KEY, CFG_OPTIONS, DATA_TYPE FROM PAY2_CONFIG"))
                    .ToDictionary(x => x.CFG_KEY, x => x);

                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    foreach (var item in request.Items)
                    {
                        if (string.IsNullOrWhiteSpace(item.CFG_KEY))
                            continue;

                        // بررسی از روی دیکشنری کش شده به جای کوئری دیتابیس
                        if (!existingConfigs.TryGetValue(item.CFG_KEY, out var dbCfg))
                            throw new InvalidOperationException($"کلید تنظیمات نامعتبر است: {item.CFG_KEY}");

                        var normalizedValue = NormalizeConfigValue(item.CFG_VALUE, dbCfg.DATA_TYPE);

                        if (!string.IsNullOrWhiteSpace(dbCfg.CFG_OPTIONS))
                        {
                            var allowed = dbCfg.CFG_OPTIONS
                                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                            if (!allowed.Contains(normalizedValue))
                                throw new InvalidOperationException($"مقدار انتخاب‌شده برای «{item.CFG_KEY}» معتبر نیست.");
                        }

                        await conn.ExecuteAsync(@"
UPDATE PAY2_CONFIG
SET CFG_VALUE = @Value,
    CHANGED_AT = GETDATE(),
    CHANGED_BY = @UserCod,
    CHANGE_NOTE = @Note
WHERE CFG_KEY = @Key;",
                            new
                            {
                                Key = item.CFG_KEY,
                                Value = normalizedValue,
                                UserCod = userCod,
                                Note = string.IsNullOrWhiteSpace(request.ChangeNote)
                                    ? "بروزرسانی از پنل Blazor"
                                    : request.ChangeNote
                            },
                            tran);
                    }
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ToFriendlyError(ex.Message));
            }
        }

        [HttpGet("tax/years")]
        public async Task<ActionResult<IEnumerable<short>>> GetTaxYears()
        {
            const string sql = @"
SELECT DISTINCT TAX_YEAR
FROM PAY2_TAX_BRACKET
ORDER BY TAX_YEAR DESC;";

            var rows = await _db.DoGetDataSQLAsync<short>(sql);
            return Ok(rows ?? Enumerable.Empty<short>());
        }

        [HttpGet("tax/brackets")]
        public async Task<ActionResult<IEnumerable<Pay2TaxBracketDto>>> GetTaxBrackets([FromQuery] short? year)
        {
            const string sql = @"
SELECT
    BRK_ID,
    TAX_YEAR,
    UPPER_LIMIT,
    RATE_PCT,
    FIXED_TAX,
    SORT_ORDER
FROM PAY2_TAX_BRACKET
WHERE (@Year IS NULL OR TAX_YEAR = @Year)
ORDER BY TAX_YEAR DESC, SORT_ORDER;";

            // حل مشکل NullReference در صورتی که دیتابیس خالی باشد
            var rows = (await _db.DoGetDataSQLAsync<Pay2TaxBracketDto>(sql, new { Year = year }) ?? Enumerable.Empty<Pay2TaxBracketDto>()).ToList();

            foreach (var row in rows)
            {
                row.UPPER_LIMIT_STR = row.UPPER_LIMIT.ToString();
                row.RATE_PCT_STR = row.RATE_PCT.ToString("0.##");
                row.FIXED_TAX_STR = row.FIXED_TAX.ToString();
            }

            return Ok(rows);
        }

        [HttpPost("tax/brackets/save")]
        public async Task<IActionResult> SaveTaxBrackets([FromBody] Pay2TaxBracketSaveRequest request)
        {
            if (request == null)
                return BadRequest("اطلاعات پله‌های مالیاتی ارسال نشده است.");

            if (request.TAX_YEAR < 1300 || request.TAX_YEAR > 1499)
                return BadRequest("سال مالیاتی نامعتبر است.");

            if (request.Items == null || request.Items.Count == 0)
                return BadRequest("حداقل یک پله مالیاتی باید ثبت شود.");

            var rows = request.Items
                .OrderBy(x => x.SORT_ORDER)
                .ToList();

            // بررسی مقادیر تکراری برای سقف پله‌ها
            if (rows.GroupBy(x => x.UPPER_LIMIT).Any(g => g.Count() > 1))
            {
                return BadRequest("سقف پله‌ها نمی‌تواند تکراری باشد.");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].SORT_ORDER = (short)(i + 1);

                if (rows[i].UPPER_LIMIT <= 0)
                    return BadRequest("سقف پله مالیاتی باید بزرگتر از صفر باشد.");

                if (rows[i].RATE_PCT < 0 || rows[i].RATE_PCT > 100)
                    return BadRequest("نرخ مالیات باید بین صفر تا صد باشد.");

                if (i > 0 && rows[i].UPPER_LIMIT <= rows[i - 1].UPPER_LIMIT)
                    return BadRequest("سقف پله‌ها باید به ترتیب صعودی باشد.");
            }

            RecalculateFixedTax(rows);

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await conn.ExecuteAsync(
                        "DELETE FROM PAY2_TAX_BRACKET WHERE TAX_YEAR = @Year",
                        new { Year = request.TAX_YEAR },
                        tran);

                    const string insertSql = @"
INSERT INTO PAY2_TAX_BRACKET
    (TAX_YEAR, UPPER_LIMIT, RATE_PCT, FIXED_TAX, SORT_ORDER)
VALUES
    (@TAX_YEAR, @UPPER_LIMIT, @RATE_PCT, @FIXED_TAX, @SORT_ORDER);";

                    foreach (var row in rows)
                    {
                        row.TAX_YEAR = request.TAX_YEAR;
                        await conn.ExecuteAsync(insertSql, row, tran);
                    }
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ToFriendlyError(ex.Message));
            }
        }

        [HttpPost("tax/brackets/copy-year")]
        public async Task<IActionResult> CopyTaxYear([FromBody] Pay2TaxBracketCopyRequest request)
        {
            if (request == null)
                return BadRequest("درخواست کپی ارسال نشده است.");

            if (request.SourceYear < 1300 || request.SourceYear > 1499 ||
                request.TargetYear < 1300 || request.TargetYear > 1499)
                return BadRequest("سال مبدأ یا مقصد نامعتبر است.");

            if (request.SourceYear == request.TargetYear)
                return BadRequest("سال مبدأ و مقصد نمی‌توانند یکسان باشند.");

            try
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    var exists = await conn.QuerySingleAsync<int>(
                        "SELECT COUNT(1) FROM PAY2_TAX_BRACKET WHERE TAX_YEAR = @Year",
                        new { Year = request.SourceYear },
                        tran);

                    if (exists == 0)
                        throw new InvalidOperationException("برای سال مبدأ هیچ پله مالیاتی وجود ندارد.");

                    await conn.ExecuteAsync(
                        "DELETE FROM PAY2_TAX_BRACKET WHERE TAX_YEAR = @Year",
                        new { Year = request.TargetYear },
                        tran);

                    await conn.ExecuteAsync(@"
INSERT INTO PAY2_TAX_BRACKET
    (TAX_YEAR, UPPER_LIMIT, RATE_PCT, FIXED_TAX, SORT_ORDER)
SELECT
    @TargetYear, UPPER_LIMIT, RATE_PCT, FIXED_TAX, SORT_ORDER
FROM PAY2_TAX_BRACKET
WHERE TAX_YEAR = @SourceYear
ORDER BY SORT_ORDER;",
                        new
                        {
                            request.SourceYear,
                            request.TargetYear
                        },
                        tran);
                });

                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ToFriendlyError(ex.Message));
            }
        }

        private static void RecalculateFixedTax(List<Pay2TaxBracketDto> rows)
        {
            long prevLimit = 0;
            long fixedTax = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].FIXED_TAX = fixedTax;

                var taxableInThisStep = rows[i].UPPER_LIMIT - prevLimit;
                if (taxableInThisStep < 0)
                    taxableInThisStep = 0;

                fixedTax += (long)Math.Round(taxableInThisStep * (double)rows[i].RATE_PCT / 100D);

                prevLimit = rows[i].UPPER_LIMIT;
            }
        }

        private static string NormalizeConfigValue(string? value, string? dataType)
        {
            var raw = (value ?? string.Empty).Trim();

            return (dataType ?? "TEXT").Trim().ToUpperInvariant() switch
            {
                "BOOL" => NormalizeBoolValue(raw),

                "INT" => long.TryParse(NormalizeNumericText(raw), out var i)
                    ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : throw new InvalidOperationException("مقدار عدد صحیح نامعتبر است."),

                "DECIMAL" => decimal.TryParse(
                        NormalizeNumericText(raw),
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var d)
                    ? d.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : throw new InvalidOperationException("مقدار اعشاری نامعتبر است."),

                "DATE" => NormalizeDateValue(raw),

                _ => raw
            };
        }

        private static string NormalizeBoolValue(string value)
        {
            var text = NormalizeNumericText(value).Trim();

            if (text == "1")
                return "1";

            if (text == "0")
                return "0";

            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "1";

            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return "0";

            throw new InvalidOperationException("مقدار بله/خیر نامعتبر است.");
        }

        private static string NormalizeDateValue(string value)
        {
            var text = NormalizeNumericText(value);

            if (string.IsNullOrWhiteSpace(text))
                return "";

            if (text.Length != 8 || !long.TryParse(text, out _))
                throw new InvalidOperationException("مقدار تاریخ نامعتبر است.");

            return text;
        }

        private static string NormalizeNumericText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var result = new List<char>();

            foreach (var ch in value.Trim())
            {
                if (ch >= '0' && ch <= '9')
                    result.Add(ch);
                else if (ch >= '۰' && ch <= '۹')
                    result.Add((char)('0' + (ch - '۰')));
                else if (ch >= '٠' && ch <= '٩')
                    result.Add((char)('0' + (ch - '٠')));
                else if (ch == '.' || ch == '-')
                    result.Add(ch);
            }

            return new string(result.ToArray());
        }

        private static string ToFriendlyError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "خطا در ذخیره تنظیمات سیستم.";

            if (message.Contains("UQ_BRK"))
                return "برای این سال مالیاتی، شماره پله تکراری ثبت شده است.";

            if (message.Contains("PAY2_CONFIG"))
                return "خطا در بروزرسانی تنظیمات سیستم.";

            if (message.Contains("PAY2_TAX_BRACKET"))
                return "خطا در بروزرسانی پله‌های مالیاتی.";

            return message;
        }
    }
}