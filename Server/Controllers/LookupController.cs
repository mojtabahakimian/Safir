using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models; // برای DTO ها
using Safir.Shared.Models.Automation;
using Safir.Shared.Utility;
using Safir.Shared.Models.Kala;
using System.Security.Claims;
using Safir.Shared.Models.Kharid;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Ensure only authenticated users can access lookup data
    public class LookupController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<LookupController> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor; // <<< اضافه شد

        [HttpGet("personnel")] // آدرس: /api/lookup/personnel
        public async Task<ActionResult<IEnumerable<PersonelLookupModel>>> GetPersonnel()
        {
            // کوئری مشابه آنچه در WPF برای پر کردن کمبوباکس مجری استفاده می‌شد
            const string sql = "SELECT IDD as USERCO, SAL_NAME FROM SALA_DTL WHERE (ENABL=0) AND (IDD <> 1) ORDER BY SAL_NAME"; // IDD=1 معمولا کاربر سیستم است
            try
            {
                // خواندن داده‌های خام از دیتابیس
                var usersRaw = await _dbService.DoGetDataSQLAsync<dynamic>(sql); // یا یک مدل موقت

                if (usersRaw == null) return Ok(Enumerable.Empty<PersonelLookupModel>());

                // Decode کردن نام‌ها و ایجاد لیست نهایی
                List<PersonelLookupModel> personnelList = usersRaw.Select(u => new PersonelLookupModel
                {
                    USERCO = (int)u.USERCO,
                    // استفاده از متد Decode و اصلاح کاراکترها
                    SAL_NAME = CL_METHODS.FixPersianChars(CL_METHODS.DECODEUN(u.SAL_NAME ?? string.Empty))
                })
                .OrderBy(p => p.SAL_NAME) // مرتب‌سازی بر اساس نام Decode شده
                .ToList();

                _logger.LogInformation("API: Successfully fetched and decoded {Count} personnel.", personnelList.Count);
                return Ok(personnelList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error fetching personnel lookup.");
                return StatusCode(500, "Internal server error while fetching personnel.");
            }
        }

        [HttpGet("subordinates")] // آدرس: /api/lookup/subordinates
        public async Task<ActionResult<IEnumerable<PersonelLookupModel>>> GetSubordinates()
        {
            // دریافت کد کاربر فعلی از Claims
            var currentUserIdClaim = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdClaim, out int currentUserCod))
            {
                _logger.LogWarning("GetSubordinates: Could not parse User ID from claims.");
                // return Unauthorized("اطلاعات کاربر نامعتبر است."); // یا می‌توانید لیست خالی برگردانید
                return Ok(Enumerable.Empty<PersonelLookupModel>());
            }

            _logger.LogInformation("API: Fetching subordinates for User ID: {CurrentUserId}", currentUserCod);

            // کوئری مشابه کد WPF شما (با پارامتر)
            // SUBUSERCO به USERCO مپ می‌شود تا با PersonelLookupModel سازگار باشد
            const string sql = @"
            SELECT
                SALA_DTL.SAL_NAME,
                CHARTSAZMANI.SUBUSERCO AS USERCO
            FROM dbo.CHARTSAZMANI
            LEFT OUTER JOIN dbo.SALA_DTL ON CHARTSAZMANI.SUBUSERCO = SALA_DTL.IDD
            WHERE CHARTSAZMANI.USERCO = @UserCode
              AND (dbo.SALA_DTL.ENABL = 0)"; // فقط کاربران فعال

            try
            {
                // استفاده از dynamic چون مدل COMBOPERSONEL در Shared نیست
                var subordinatesRaw = await _dbService.DoGetDataSQLAsync<dynamic>(sql, new { UserCode = currentUserCod });

                if (subordinatesRaw == null)
                {
                    _logger.LogInformation("API: No subordinates found for User ID: {CurrentUserId}", currentUserCod);
                    return Ok(Enumerable.Empty<PersonelLookupModel>());
                }

                // Decode کردن نام‌ها و ایجاد لیست نهایی
                var subordinatesList = subordinatesRaw
                    .Where(u => u.SAL_NAME != null && u.USERCO != null) // اطمینان از null نبودن داده‌های ضروری
                    .Select(u => new PersonelLookupModel
                    {
                        // USERCO در مدل ما از SUBUSERCO پر می‌شود طبق کوئری
                        USERCO = (int)u.USERCO,
                        // استفاده از متد Decode و اصلاح کاراکترها
                        SAL_NAME = CL_METHODS.FixPersianChars(CL_METHODS.DECODEUN((string)u.SAL_NAME))
                    })
                    .OrderBy(p => p.SAL_NAME) // مرتب‌سازی بر اساس نام Decode شده
                    .ToList();

                _logger.LogInformation("API: Successfully fetched and decoded {Count} subordinates for User ID: {CurrentUserId}", subordinatesList.Count, currentUserCod);
                return Ok(subordinatesList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error fetching subordinates for User ID: {CurrentUserId}", currentUserCod);
                return StatusCode(500, "Internal server error while fetching subordinates.");
            }
        }

        public LookupController(
            IDatabaseService dbService,
            ILogger<LookupController> logger,
            IHttpContextAccessor httpContextAccessor) // <<< اضافه شد
        {
            _dbService = dbService;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor; // <<< اضافه شد
        }

        // --- Endpoint for Ostans ---
        [HttpGet("ostans")]
        public async Task<ActionResult<IEnumerable<LookupDto<int?>>>> GetOstans()
        {
            const string sql = "SELECT OSCODE as Id, OSNAME as Name FROM TCOD_OSTAN ORDER BY OSNAME";
            try
            {
                var data = await _dbService.DoGetDataSQLAsync<LookupDto<int?>>(sql);
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Ostans");
                return StatusCode(500, "Internal server error while fetching Ostans.");
            }
        }

        // --- Endpoint for Shahrs (Cities) ---
        [HttpGet("shahrs")]
        public async Task<ActionResult<IEnumerable<CityLookupDto>>> GetShahrs()
        {
            // Include OSCODE as ParentId for filtering on the client
            const string sql = "SELECT CITYCODE as Id, CITYNAME as Name, OSCODE as ParentId FROM TCOD_CITY ORDER BY CITYNAME";
            try
            {
                var data = await _dbService.DoGetDataSQLAsync<CityLookupDto>(sql);
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Shahrs");
                return StatusCode(500, "Internal server error while fetching Shahrs.");
            }
        }

        // --- Endpoint for Customer Types ---
        [HttpGet("customertypes")]
        public async Task<ActionResult<IEnumerable<LookupDto<int?>>>> GetCustomerTypes()
        {
            const string sql = "SELECT CUST_COD as Id, CUSTKNAME as Name FROM CUSTKIND ORDER BY CUSTKNAME";
            try
            {
                var data = await _dbService.DoGetDataSQLAsync<LookupDto<int?>>(sql);
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Customer Types");
                return StatusCode(500, "Internal server error while fetching Customer Types.");
            }
        }

        // --- Endpoint for Routes ---
        [HttpGet("routes")]
        public async Task<ActionResult<IEnumerable<RouteLookupDto>>> GetRoutes()
        {
            // Query from WPF code
            const string sql = @"SELECT
                                    Visit_route.ROUTE_NAME as RouteName,
                                    Visit_route.ROUTE_NAME + N' - ' + CUST_HESAB.NAME + N' - ' + CUST_HESAB.hes AS DisplayName
                                  FROM Visit_route
                                  INNER JOIN CUST_HESAB ON Visit_route.HES = CUST_HESAB.hes
                                  WHERE (Visit_route.RACTIVE = 1)
                                  OPTION (MERGE JOIN)";
            // Consider adding ORDER BY if needed
            try
            {
                var data = await _dbService.DoGetDataSQLAsync<RouteLookupDto>(sql);
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Routes");
                return StatusCode(500, "Internal server error while fetching Routes.");
            }
        }

        // اکشن جدید برای واحدها
        [HttpGet("units")] // مسیر: api/lookup/units
        public async Task<ActionResult<IEnumerable<TCOD_VAHEDS>>> GetUnits()
        {
            const string sql = "SELECT CODE, NAMES FROM dbo.TCOD_VAHEDS ORDER BY NAMES";
            try
            {
                var data = await _dbService.DoGetDataSQLAsync<TCOD_VAHEDS>(sql);
                return Ok(data ?? new List<TCOD_VAHEDS>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Units (TCOD_VAHEDS)");
                return StatusCode(500, "Internal server error while fetching units.");
            }
        }

        // --- Personality Types (شخصیت) ---
        // This was static in WPF. If it remains static, no endpoint is needed.
        // If it needs to come from DB, create an endpoint similar to others.
        // Example static return (if desired via API):
        [HttpGet("personalitytypes")]
        public ActionResult<IEnumerable<LookupDto<int>>> GetPersonalityTypes()
        {
            var types = new List<LookupDto<int>>
            {
                new() { Id = 1, Name = "حقیقی" },
                new() { Id = 2, Name = "حقوقی" },
                new() { Id = 3, Name = "مشارکت مدنی"}, // From WPF code
                new() { Id = 4, Name = "اتباع غیر ایرانی"} // From WPF code
            };
            return Ok(types);
            // Note: Blazor model uses int? but WPF uses int. Adjusted DTO to int.
            // Ensure consistency or handle nullability.
        }

        [HttpGet("anbarha")] // مسیر: api/lookup/anbarha
        public async Task<ActionResult<IEnumerable<TCOD_ANBAR>>> GetAnbarha()
        {
            const string sql = "SELECT CODE, NAMES FROM dbo.TCOD_ANBAR ORDER BY NAMES";
            try
            {
                var data = await _dbService.DoGetDataSQLAsync<TCOD_ANBAR>(sql);
                return Ok(data ?? new List<TCOD_ANBAR>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Anbarha (TCOD_ANBAR)");
                return StatusCode(500, "Internal server error while fetching Anbarha.");
            }
        }

        [HttpGet("useranbarha")] // مسیر: api/lookup/useranbarha
        public async Task<ActionResult<IEnumerable<TCOD_ANBAR>>> GetUserAnbarha()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            {
                return Unauthorized();
            }

            try
            {
                var data = await _dbService.GetUserAnbarhaAsync(userId);
                return Ok(data ?? new List<TCOD_ANBAR>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user anbar list for {UserId}", userId);
                return StatusCode(500, "Internal server error while fetching user anbarha.");
            }
        }

        #region ELEMIEH_GHEYMAT
        [HttpGet("customerkinds")] // نوع مشتری
        public async Task<IActionResult> GetCustomerKinds()
        {
            try { return Ok(await _dbService.GetCustomerKindsAsync()); }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }

        [HttpGet("customerhesabinfo/{customerHesCode}")] // اطلاعات نوع مشتری پیش‌فرض
        public async Task<IActionResult> GetCustomerHesabInfo(string customerHesCode)
        {
            try { return Ok(await _dbService.GetCustomerHesabInfoByHesCodeAsync(customerHesCode)); }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }

        [HttpGet("departments")] // واحدها (دپارتمان‌ها)
        public async Task<IActionResult> GetDepartments()
        {
            try { return Ok(await _dbService.GetDepartmentsAsync()); }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }


        [HttpGet("defaultpaymentterm/user/{userId}")] // نحوه پرداخت پیش‌فرض کاربر
        public async Task<IActionResult> GetDefaultPaymentTermIdForUser(int userId)
        {
            try { return Ok(await _dbService.GetDefaultPaymentTermIdForUserAsync(userId)); }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }

        [HttpGet("pricelists")] // اعلامیه‌های قیمت
        public async Task<IActionResult> GetPriceLists()
        {
            try { return Ok(await _dbService.GetPriceListsAsync()); }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }

        [HttpGet("defaultpricelist/department/{departmentId}")] // اعلامیه قیمت پیش‌فرض
        public async Task<IActionResult> GetDefaultPriceListId(int departmentId)
        {
            try
            {
                var currentDate = CL_Tarikh.GetCurrentPersianDateAsLong();
                return Ok(await _dbService.GetDefaultPriceListIdAsync(currentDate, departmentId));
            }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }

        [HttpGet("discountlists")] // اعلامیه‌های تخفیف
        public async Task<IActionResult> GetDiscountLists()
        {
            try { return Ok(await _dbService.GetDiscountListsAsync()); }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }

        [HttpGet("defaultdiscountlist/department/{departmentId}")] // اعلامیه تخفیف پیش‌فرض
        public async Task<IActionResult> GetDefaultDiscountListId(int departmentId)
        {
            try
            {
                var currentDate = CL_Tarikh.GetCurrentPersianDateAsLong();
                return Ok(await _dbService.GetDefaultDiscountListIdAsync(currentDate, departmentId));
            }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }


        [HttpGet("paymentterms")] // نحوه‌های پرداخت
        public async Task<IActionResult> GetPaymentTerms()
        {
            try { return Ok(await _dbService.GetPaymentTermsAsync()); }
            catch (Exception ex) { return StatusCode(500, $"Internal server error: {ex.Message}"); }
        }


        [HttpGet("paymentterms/dynamic")] // آدرس جدید
        public async Task<IActionResult> GetDynamicPaymentTerms([FromQuery] int? departmentId, [FromQuery] int? selectedDiscountListId)
        {
            try
            {
                long currentDate = CL_Tarikh.GetCurrentPersianDateAsLong(); // دریافت تاریخ جاری در سرور
                var paymentTerms = await _dbService.GetDynamicPaymentTermsAsync(departmentId, selectedDiscountListId, currentDate);
                return Ok(paymentTerms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching dynamic payment terms.");
                return StatusCode(500, "Internal server error fetching dynamic payment terms.");
            }
        }

        [HttpGet("GetPriceElamieTfDetails")]
        public async Task<ActionResult<PriceElamieTfDtlDto>> GetPriceElamieTfDetails(
        [FromQuery] int elamiehTakhfifId,
        [FromQuery] int custTypeCode,
        [FromQuery] int paymentTermId)
        {
            try
            {
                // توجه: کوئری زیر باید با DatabaseService شما و به صورت پارامتری اجرا شود تا از SQL Injection جلوگیری شود.
                // این فقط یک مثال برای نمایش ساختار است.
                string query = @"
                SELECT TOP 1 PEID, CUSTCODE, PPID, TF1, TF2
                FROM dbo.PRICE_ELAMIETF_DTL
                WHERE (PEID = @ElamiehTakhfifId_Param) AND (CUSTCODE = @CustKindCode_Param) AND (PPID = @NahvahPayment_Param)";

                // Example with Dapper using your DatabaseService (adapt as needed)
                var parameters = new { ElamiehTakhfifId_Param = elamiehTakhfifId, CustKindCode_Param = custTypeCode, NahvahPayment_Param = paymentTermId };
                var result = await _dbService.DoGetDataSQLAsyncSingle<PriceElamieTfDtlDto>(query, parameters);

                if (result == null)
                {
                    // It's better to return an empty object or specific DTO if that's expected by client on no data
                    // rather than NotFound, if the client code doesn't specifically handle 404 for this.
                    // Or, if client handles 404, then NotFound() is appropriate.
                    _logger.LogInformation("No PriceElamieTfDetails found for PEID: {PEID}, CustCode: {CustCode}, PPID: {PPID}", elamiehTakhfifId, custTypeCode, paymentTermId);
                    return Ok(new PriceElamieTfDtlDto()); // Return an empty DTO to avoid null issues if client expects an object
                                                          // Or return NotFound(); if your client handles it
                }
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching PriceElamieTfDetails for PEID: {PEID}, CustCode: {CustCode}, PPID: {PPID}", elamiehTakhfifId, custTypeCode, paymentTermId);
                return StatusCode(500, "Internal server error while fetching price elamie TF details.");
            }
        }

        [HttpGet("customerlookup")] // مسیر: api/lookup/customerlookup
        public async Task<ActionResult<IEnumerable<LookupDto<string>>>> GetCustomerLookup([FromQuery] string? searchTerm = null)
        {
            string sql;
            object? parameters = null;

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                sql = BuildAccountNameLookupSql();
                parameters = new
                {
                    NamePattern = "%",
                    RawNamePattern = "%"
                };

                _logger.LogInformation("API: Fetching initial customer lookup from all account levels.");
            }
            else
            {
                string rawTerm = searchTerm.Trim();
                string normalizedCode = NormalizeAccountingCodeSearch(rawTerm);
                string normalizedName = NormalizePersianSearchText(rawTerm);

                int[] codeParts = TryParseAccountCodeParts(normalizedCode);

                parameters = new
                {
                    K = codeParts.Length > 0 ? codeParts[0] : (int?)null,
                    M = codeParts.Length > 1 ? codeParts[1] : (int?)null,
                    T1 = codeParts.Length > 2 ? codeParts[2] : (int?)null,
                    T2 = codeParts.Length > 3 ? codeParts[3] : (int?)null,
                    T3 = codeParts.Length > 4 ? codeParts[4] : (int?)null,
                    T4 = codeParts.Length > 5 ? codeParts[5] : (int?)null,
                    NamePattern = $"%{normalizedName}%",
                    RawNamePattern = $"%{rawTerm}%"
                };

                sql = codeParts.Length > 0
                    ? BuildFastAccountCodeLookupSql(codeParts.Length)
                    : BuildAccountNameLookupSql();

                _logger.LogInformation(
                    "API: Customer lookup. RawTerm={RawTerm}, NormalizedCode={NormalizedCode}, PartCount={PartCount}",
                    rawTerm,
                    normalizedCode,
                    codeParts.Length);
            }

            try
            {
                var data = await _dbService.DoGetDataSQLAsync<LookupDto<string>>(sql, parameters);
                return Ok(data ?? Enumerable.Empty<LookupDto<string>>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error fetching customer lookup. SearchTerm={SearchTerm}", searchTerm);
                return StatusCode(500, "Internal server error while fetching customer lookup.");
            }
        }
        private static string BuildFastAccountCodeLookupSql(int partCount)
        {
            string where3 = BuildWhereClause(partCount, 3);
            string where4 = BuildWhereClause(partCount, 4);
            string where5 = BuildWhereClause(partCount, 5);
            string where6 = BuildWhereClause(partCount, 6);

            int sort3 = partCount == 3 ? 0 : 30;
            int sort4 = partCount == 4 ? 0 : 40;
            int sort5 = partCount == 5 ? 0 : 50;
            int sort6 = partCount == 6 ? 0 : 60;

            return $@"
SELECT TOP 50
    X.Id,
    X.Name
FROM
(
    SELECT
        CONCAT(N_KOL, N'-', NUMBER, N'-', TNUMBER) AS Id,
        NAME AS Name,
        {sort3} AS SortNo,
        3 AS LevelNo
    FROM dbo.TDETA_HES
    WHERE {where3}

    UNION ALL

    SELECT
        CONCAT(N_KOL, N'-', NUMBER, N'-', TNUMBER, N'-', TNUMBER2) AS Id,
        NAME AS Name,
        {sort4} AS SortNo,
        4 AS LevelNo
    FROM dbo.TDETA_HES2
    WHERE {where4}

    UNION ALL

    SELECT
        CONCAT(N_KOL, N'-', NUMBER, N'-', TNUMBER, N'-', TNUMBER2, N'-', TNUMBER3) AS Id,
        NAME AS Name,
        {sort5} AS SortNo,
        5 AS LevelNo
    FROM dbo.TDETA_HES3
    WHERE {where5}

    UNION ALL

    SELECT
        CONCAT(N_KOL, N'-', NUMBER, N'-', TNUMBER, N'-', TNUMBER2, N'-', TNUMBER3, N'-', TNUMBER4) AS Id,
        NAME AS Name,
        {sort6} AS SortNo,
        6 AS LevelNo
    FROM dbo.TDETA_HES4
    WHERE {where6}
) X
WHERE X.Name IS NOT NULL
ORDER BY
    X.SortNo,
    X.LevelNo,
    X.Id;";
        }
        private static string BuildAccountNameLookupSql()
        {
            return @"
SELECT TOP 50
    X.Id,
    X.Name
FROM
(
    SELECT
        CONCAT(N_KOL, N'-', NUMBER, N'-', TNUMBER) AS Id,
        NAME AS Name,
        3 AS LevelNo
    FROM dbo.TDETA_HES
    WHERE NAME LIKE @NamePattern OR NAME LIKE @RawNamePattern

    UNION ALL

    SELECT
        CONCAT(N_KOL, N'-', NUMBER, N'-', TNUMBER, N'-', TNUMBER2) AS Id,
        NAME AS Name,
        4 AS LevelNo
    FROM dbo.TDETA_HES2
    WHERE NAME LIKE @NamePattern OR NAME LIKE @RawNamePattern

    UNION ALL

    SELECT
        CONCAT(N_KOL, N'-', NUMBER, N'-', TNUMBER, N'-', TNUMBER2, N'-', TNUMBER3) AS Id,
        NAME AS Name,
        5 AS LevelNo
    FROM dbo.TDETA_HES3
    WHERE NAME LIKE @NamePattern OR NAME LIKE @RawNamePattern

    UNION ALL

    SELECT
        CONCAT(N_KOL, N'-', NUMBER, N'-', TNUMBER, N'-', TNUMBER2, N'-', TNUMBER3, N'-', TNUMBER4) AS Id,
        NAME AS Name,
        6 AS LevelNo
    FROM dbo.TDETA_HES4
    WHERE NAME LIKE @NamePattern OR NAME LIKE @RawNamePattern
) X
WHERE X.Name IS NOT NULL
ORDER BY
    X.Name,
    X.LevelNo,
    X.Id;";
        }
        private static string BuildWhereClause(int partCount, int maxLevel)
        {
            if (partCount > maxLevel)
                return "1 = 0";

            var conditions = new List<string>();

            if (partCount >= 1) conditions.Add("N_KOL = @K");
            if (partCount >= 2) conditions.Add("NUMBER = @M");
            if (partCount >= 3) conditions.Add("TNUMBER = @T1");
            if (partCount >= 4) conditions.Add("TNUMBER2 = @T2");
            if (partCount >= 5) conditions.Add("TNUMBER3 = @T3");
            if (partCount >= 6) conditions.Add("TNUMBER4 = @T4");

            return conditions.Count == 0 ? "1 = 0" : string.Join(" AND ", conditions);
        }
        private static int[] TryParseAccountCodeParts(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<int>();

            string[] rawParts = value.Split(
                '-',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (rawParts.Length == 0 || rawParts.Length > 6)
                return Array.Empty<int>();

            var result = new List<int>();

            foreach (string part in rawParts)
            {
                if (!int.TryParse(part, out int number))
                    return Array.Empty<int>();

                result.Add(number);
            }

            return result.ToArray();
        }
        private static string NormalizeAccountingCodeSearch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var chars = new List<char>();

            foreach (char ch in value.Trim())
            {
                if (ch >= '0' && ch <= '9')
                    chars.Add(ch);
                else if (ch >= '۰' && ch <= '۹')
                    chars.Add((char)('0' + (ch - '۰')));
                else if (ch >= '٠' && ch <= '٩')
                    chars.Add((char)('0' + (ch - '٠')));
                else if (ch == '-' || ch == '‐' || ch == '–' || ch == '—' || ch == '−')
                    chars.Add('-');
                else if (!char.IsWhiteSpace(ch))
                    chars.Add(ch);
            }

            return new string(chars.ToArray());
        }
        private static string NormalizePersianSearchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Trim()
                .Replace('ي', 'ی')
                .Replace('ك', 'ک')
                .Replace('ۀ', 'ه')
                .Replace('ة', 'ه')
                .Replace('ؤ', 'و')
                .Replace('إ', 'ا')
                .Replace('أ', 'ا')
                .Replace('آ', 'ا');
        }
        #endregion
    }
}