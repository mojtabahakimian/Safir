using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models; // برای DTO ها
using System.Collections.Generic;
using System.Threading.Tasks;
using System; // برای Exception
using Microsoft.Extensions.Logging; // برای ILogger
using Safir.Shared.Models.Automation;
using Safir.Shared.Utility;
using Safir.Shared.Models.Kala;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Ensure only authenticated users can access lookup data
    public class LookupController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<LookupController> _logger;


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
                var personnelList = usersRaw.Select(u => new PersonelLookupModel
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

        public LookupController(IDatabaseService dbService, ILogger<LookupController> logger)
        {
            _dbService = dbService;
            _logger = logger;
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
        #endregion
    }
}