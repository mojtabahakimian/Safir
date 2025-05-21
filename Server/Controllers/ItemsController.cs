// Safir.Server/Controllers/ItemsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Kala;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.StaticFiles;
using Dapper;
using System.Text;
using System.Text.RegularExpressions;
using Safir.Shared.Utility;
using System.Security.Claims; // <<< اضافه شد برای دسترسی به Claims
using Safir.Shared.Constants; // <<< اضافه شد برای BaseknowClaimTypes

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ItemsController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ItemsController> _logger;
        private readonly string? _imageBasePath;
        private static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

        public ItemsController(IDatabaseService dbService, IConfiguration configuration, ILogger<ItemsController> logger)
        {
            _dbService = dbService;
            _configuration = configuration;
            _logger = logger;
            _imageBasePath = _configuration["ImageSharePath"];
            if (string.IsNullOrEmpty(_imageBasePath))
            {
                _logger.LogWarning("ImageSharePath is not configured.");
            }
        }

        [HttpGet("bygroup/{groupCode}")]
        public async Task<ActionResult<PagedResult<ItemDisplayDto>>> GetItemsByGroup(double groupCode, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10, [FromQuery] string? searchTerm = null)
        {
            // --- Get User HES claim ---
            var userHes = User.FindFirstValue(BaseknowClaimTypes.USER_HES);
            if (string.IsNullOrEmpty(userHes))
            {
                _logger.LogWarning("User HES claim ('{UserHesClaim}') is missing or invalid for User: {Username}", BaseknowClaimTypes.USER_HES, User.Identity?.Name);
                // Return empty result or Unauthorized based on requirements
                return Ok(new PagedResult<ItemDisplayDto> { Items = new List<ItemDisplayDto>(), TotalCount = 0, PageNumber = pageNumber, PageSize = pageSize });
                // return Unauthorized("User HES claim not found.");
            }

            _logger.LogInformation("Fetching items for Group: {GroupCode}, User HES: {UserHES}, Page: {PageNumber}, Size: {PageSize}, Search: '{SearchTerm}'",
               groupCode, userHes, pageNumber, pageSize, searchTerm);

            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100; // Limit page size
            int offset = (pageNumber - 1) * pageSize;

            var whereConditions = new List<string>();
            var parameters = new DynamicParameters();

            // --- Base Filters ---
            whereConditions.Add("sd.MENUIT = @GroupCode");       // Filter by Group Code
            parameters.Add("GroupCode", groupCode);

            whereConditions.Add("vp.HES = @UserHes");            // Filter by User HES
            parameters.Add("UserHes", userHes);

            whereConditions.Add("ISNULL(sd.OKF, 1) = 1");        // Filter by OKF status

            // --- Search Term Handling ---
            string? normalizedSearchTerm = null;
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                normalizedSearchTerm = searchTerm.Trim();
                normalizedSearchTerm = Regex.Replace(normalizedSearchTerm, @"\s+", " "); // Normalize spaces
                normalizedSearchTerm = normalizedSearchTerm.FixPersianChars(); // Fix Persian characters

                // Apply search to relevant columns (Name and Code)
                string normalizedNameSql = "REPLACE(REPLACE(REPLACE(REPLACE(sd.NAME, N'ي', N'ی'), N'ك', N'ک'), NCHAR(160), N' '), N'  ', N' ')";
                whereConditions.Add($"(({normalizedNameSql} LIKE @SearchPattern) OR (sd.CODE LIKE @SearchPattern))");
                parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%");
            }
            // --- End Search ---

            string commonWhereClause = string.Join(" AND ", whereConditions);

            // --- Items Query with Pagination using CTE and ROW_NUMBER ---
            // Note: PORSANT is removed as it's not directly available in the new join structure
            /* 1) Raw : همهٔ رکوردهای واجد شرایط
            2) Dedup: حذف سطرهای تکراری (فقط ردیف dup_rnk = 1 می‌ماند)
            3) BaseItems: شماره‌گذاری برای صفحه‌بندی                                      */
            // --- Items Query: حذف تکراری‌ها سپس صفحه‌بندی ---
            string itemsSql = $@"
                                WITH Raw AS
                                (
                                    SELECT
                                        sd.CODE,
                                        sd.NAME,
                                        sd.MABL_F,
                                        sd.B_SEF,
                                        sd.MAX_M,
                                        sd.TOZIH,
                                        sd.MENUIT,
                                        sd.VAHED AS VahedCode,
                                        tv.NAMES AS VahedName,
                                        ROW_NUMBER() OVER (PARTITION BY sd.CODE ORDER BY sd.CODE) AS dup_rnk
                                    FROM dbo.STUF_DEF               sd
                                    INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON vpk.CODE = sd.CODE
                                    INNER JOIN dbo.VISITORS_PORSANT      vp  ON vp.PORID = vpk.PORID
                                    LEFT  JOIN dbo.TCOD_VAHEDS           tv  ON tv.CODE  = sd.VAHED
                                    WHERE {commonWhereClause}
                                ),
                                Dedup AS
                                (
                                    SELECT * FROM Raw WHERE dup_rnk = 1      -- حذف دوبلیکیت‌ها
                                ),
                                BaseItems AS
                                (
                                    SELECT
                                        CODE, NAME, MABL_F, B_SEF, MAX_M,
                                        TOZIH, MENUIT, VahedCode, VahedName,
                                        ROW_NUMBER() OVER (ORDER BY CODE) AS RowNum   -- برای صفحه‌بندی
                                    FROM Dedup
                                )
                                SELECT
                                    CODE, NAME, MABL_F, B_SEF, MAX_M,
                                    TOZIH, MENUIT, VahedCode, VahedName
                                FROM BaseItems
                                WHERE RowNum >  @RowStart
                                  AND RowNum <= @RowEnd;";

            // --- Count Query بدون تغییر خاص (همچنان DISTINCT روی CODE) ---
            string countSql = $@"
                                SELECT COUNT(DISTINCT sd.CODE)
                                FROM dbo.STUF_DEF sd
                                INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON sd.CODE = vpk.CODE
                                INNER JOIN dbo.VISITORS_PORSANT      vp  ON vpk.PORID = vp.PORID
                                WHERE {commonWhereClause};";


            parameters.Add("RowStart", offset);
            parameters.Add("RowEnd", offset + pageSize);

            try
            {
                // Execute queries using Dapper via the service
                IEnumerable<ItemDisplayDto> items = await _dbService.DoGetDataSQLAsync<ItemDisplayDto>(itemsSql, parameters);
                int totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                List<ItemDisplayDto> itemsList = items.ToList();

                // --- Image Check Logic (Remains the same) ---
                if (!string.IsNullOrEmpty(_imageBasePath))
                {
                    foreach (var item in itemsList)
                    {
                        string itemCodeStr = item.CODE;
                        item.ImageExists = false;
                        foreach (var ext in SupportedImageExtensions)
                        {
                            string potentialPath = Path.Combine(_imageBasePath, itemCodeStr + ext);
                            try
                            {
                                if (System.IO.File.Exists(potentialPath))
                                {
                                    item.ImageExists = true;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error checking file existence for path: {Path}", potentialPath);
                            }
                        }
                    }
                }
                else { _logger.LogWarning("ImageSharePath is not configured. Skipping image checks."); }
                // --- End Image Check ---

                var pagedResult = new PagedResult<ItemDisplayDto>
                {
                    Items = itemsList,
                    TotalCount = totalItemCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                _logger.LogInformation("Fetched page {PageNumber}/{TotalPages} for Group {GroupCode}, User HES {UserHES}, Search: '{OrigSearch}'. Found {ItemCount} items (Total Distinct: {TotalCount})",
                    pageNumber, pagedResult.TotalPages, groupCode, userHes, searchTerm ?? "N/A", itemsList.Count, totalItemCount);

                return Ok(pagedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paged items for Group {GroupCode}, User HES {UserHES}, Search: '{SearchTerm}'", groupCode, userHes, searchTerm);
                return StatusCode(StatusCodes.Status500InternalServerError, "خطا در دریافت لیست کالاها.");
            }
        }

        // --- GetItemImage endpoint remains the same ---
        [HttpGet("image/{itemCode}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetItemImage(string itemCode)
        {
            // ... (Implementation unchanged from previous steps) ...
            if (string.IsNullOrEmpty(_imageBasePath)) return StatusCode(StatusCodes.Status500InternalServerError, "Path not configured.");
            if (string.IsNullOrWhiteSpace(itemCode) || itemCode.Contains("..") || itemCode.Contains('/') || itemCode.Contains('\\')) return BadRequest();
            string? foundFilePath = SupportedImageExtensions.Select(ext => Path.Combine(_imageBasePath, itemCode + ext)).FirstOrDefault(System.IO.File.Exists);
            if (foundFilePath == null) return NotFound("Image not found.");
            try
            {
                byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(foundFilePath);
                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(foundFilePath, out var contentType)) { contentType = "application/octet-stream"; }
                return File(fileBytes, contentType);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error serving image {ItemCode}", itemCode); return StatusCode(StatusCodes.Status500InternalServerError, "Error serving image."); }
        }

        // --- GetItemInventory endpoint remains the same ---
        [HttpGet("inventory/{itemCode}")]
        public async Task<ActionResult<decimal?>> GetItemInventory(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                return BadRequest("Item code is required.");
            }

            _logger.LogInformation("Request received for inventory of item code: {ItemCode}", itemCode);

            try
            {
                var parameters = new { Code = itemCode };
                decimal? inventory = await _dbService.GetItemInventoryAsync(itemCode);

                if (inventory.HasValue)
                {
                    _logger.LogInformation("Inventory for item {ItemCode} is {Inventory}", itemCode, inventory.Value);
                    return Ok(inventory.Value);
                }
                else
                {
                    _logger.LogWarning("Inventory data not found for item {ItemCode}", itemCode);
                    return Ok(0); // Or NotFound() if 0 is not appropriate for "not found"
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching inventory for item code: {ItemCode}", itemCode);
                return StatusCode(StatusCodes.Status500InternalServerError, "خطا در دریافت موجودی کالا.");
            }
        }


        [HttpGet("{itemCode}/units")]
        public async Task<ActionResult<List<UnitInfo>>> GetItemUnits(string itemCode)
        {
            if (string.IsNullOrEmpty(itemCode))
            {
                return BadRequest("کد کالا نمی‌تواند خالی باشد.");
            }

            try
            {
                // استفاده از پارامتر برای امنیت و جلوگیری از SQL Injection
                var parameters = new { ItemCode = itemCode };

                // کوئری برای واحد اصلی کالا از STUF_DEF
                // نسبت واحد اصلی به خودش همیشه 1 است
                string primaryUnitSql = @"
                SELECT
                    sd.VAHED AS VahedCode,
                    tv.NAMES AS VahedName,
                    1.0 AS Nesbat
                FROM dbo.STUF_DEF sd
                INNER JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
                WHERE sd.CODE = @ItemCode;";

                // کوئری برای واحدهای فرعی از MODULE_D
                string subUnitsSql = @"
                SELECT
                    md.VAHED AS VahedCode,
                    tv.NAMES AS VahedName,
                    md.NESBAT AS Nesbat
                FROM dbo.MODULE_D md
                INNER JOIN dbo.TCOD_VAHEDS tv ON md.VAHED = tv.CODE
                WHERE md.CODE = @ItemCode;";

                var primaryUnitList = (await _dbService.DoGetDataSQLAsync<UnitInfo>(primaryUnitSql, parameters)).ToList();
                var subUnitsList = (await _dbService.DoGetDataSQLAsync<UnitInfo>(subUnitsSql, parameters)).ToList();

                var combinedUnits = new List<UnitInfo>();
                if (primaryUnitList.Any())
                {
                    combinedUnits.AddRange(primaryUnitList);
                }
                combinedUnits.AddRange(subUnitsList);

                // حذف موارد تکراری بر اساس کد واحد و انتخاب اولین مورد (اولویت با واحد اصلی اگر تکراری بود)
                // و مرتب‌سازی بر اساس نسبت (مثلاً از کوچکترین واحد)
                var itemSpecificUnits = combinedUnits
                    .GroupBy(u => u.VahedCode)
                    .Select(g => new UnitInfo
                    {
                        VahedCode = g.Key,
                        VahedName = g.First().VahedName, // فرض بر اینکه نام برای یک کد واحد یکسان است
                        Nesbat = g.First().Nesbat
                    })
                    .OrderBy(u => u.Nesbat)
                    .ToList();

                if (!itemSpecificUnits.Any() && !primaryUnitList.Any()) // اگر واحد اصلی هم تعریف نشده بود
                {
                    // اگر هیچ واحدی برای کالا یافت نشد، می‌توان یک واحد پیش‌فرض عمومی (مثلا "عدد" با کد فرضی و نسبت 1) برگرداند
                    // یا لیست خالی که در کلاینت مدیریت شود. فعلا لیست خالی برمیگردانیم.
                    _logger.LogWarning("هیچ واحدی (اصلی یا فرعی) برای کالای {ItemCode} یافت نشد.", itemCode);
                }


                return Ok(itemSpecificUnits);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "خطا در دریافت واحدهای کالا برای {ItemCode}", itemCode);
                return StatusCode(500, "خطای سرور در هنگام دریافت واحدهای کالا.");
            }
        }


        [HttpGet("visitor-prices")]
        public async Task<ActionResult<IEnumerable<VisitorItemPriceDto>>> GetVisitorItemPrices([FromQuery] int priceListId)
        {
            var userHes = User.FindFirstValue(BaseknowClaimTypes.USER_HES);
            if (string.IsNullOrEmpty(userHes))
            {
                _logger.LogWarning("User HES claim ('{UserHesClaim}') is missing.", BaseknowClaimTypes.USER_HES);
                return Unauthorized("اطلاعات کاربر برای دریافت قیمت‌ها معتبر نیست.");
            }

            if (priceListId <= 0)
            {
                return BadRequest("شناسه اعلامیه قیمت معتبر نیست.");
            }

            _logger.LogInformation("Fetching visitor item prices for UserHES: {UserHes} and PriceListID: {PriceListId}", userHes, priceListId);

            string sql = @"
                     SELECT DISTINCT 
                         vpk.CODE,      -- کد کالا از VISITORS_PORSANT_KALA
                         ped.PRICE1,    -- قیمت از PRICE_ELAMIE_DTL
                         ped.PEPID      -- شناسه اعلامیه قیمت از PRICE_ELAMIE_DTL
                         -- سایر فیلدهای مورد نیاز مانند vp.HES, vp.PORID, vpk.PORSANT, sd.PGID
                         -- , vp.HES, vp.PORID, vpk.PORSANT, sd.PGID 
                     FROM dbo.VISITORS_PORSANT vp
                     INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON vp.PORID = vpk.PORID
                     INNER JOIN dbo.STUF_DEF sd ON vpk.CODE = sd.CODE
                     -- جوین به PRICE_ELAMIE_DTL باید با احتیاط انجام شود، ممکن است همه کالاها در هر اعلامیه قیمت نداشته باشند
                     -- استفاده از LEFT JOIN اگر می‌خواهید همه کالاهای ویزیتور را برگردانید و قیمت را اگر موجود بود
                     -- اما کوئری شما INNER JOIN است، پس فقط کالاهایی که در آن اعلامیه قیمت دارند برمی‌گردند.
                     INNER JOIN dbo.PRICE_ELAMIE_DTL ped ON sd.PGID = ped.PGID AND ped.PEPID = @PriceListIdParam
                     WHERE vp.HES = @UserHesParam;";
            // نکته: جوین شما در کوئری اصلی کمی متفاوت بود (دو بار به STUF_DEF). من آن را بر اساس ارتباط منطقی اصلاح کردم.
            // ارتباط اصلی باید از کالای ویزیتور (VISITORS_PORSANT_KALA) به STUF_DEF و سپس از STUF_DEF.PGID به PRICE_ELAMIE_DTL.PGID باشد.

            try
            {
                var parameters = new { UserHesParam = userHes, PriceListIdParam = priceListId };
                var prices = await _dbService.DoGetDataSQLAsync<VisitorItemPriceDto>(sql, parameters);
                return Ok(prices ?? Enumerable.Empty<VisitorItemPriceDto>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching visitor item prices for UserHES {UserHes} and PriceListID {PriceListId}.", userHes, priceListId);
                return StatusCode(StatusCodes.Status500InternalServerError, "خطا در دریافت لیست قیمت کالاها از سرور.");
            }
        }
    }
}