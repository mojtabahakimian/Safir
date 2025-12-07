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
using System.Security.Claims;
using Safir.Shared.Constants;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models.Kala;

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
            var userHes = User.FindFirstValue(BaseknowClaimTypes.USER_HES);
            if (string.IsNullOrEmpty(userHes))
            {
                return Ok(new PagedResult<ItemDisplayDto>());
            }

            int? anbarCode = await _dbService.DoGetDataSQLAsyncSingle<int?>("SELECT ANBAR FROM TCODE_MENUITEM WHERE CODE = @GroupCode", new { GroupCode = groupCode });
            if (!anbarCode.HasValue)
            {
                return BadRequest(new { Message = $"کد انبار برای گروه {groupCode} یافت نشد." });
            }

            _logger.LogInformation("Fetching items for Group: {GroupCode}, Anbar: {AnbarCode}, User HES: {UserHES}, Search: '{SearchTerm}'", groupCode, anbarCode.Value, userHes, searchTerm);

            var parameters = new DynamicParameters();
            parameters.Add("GroupCode", groupCode);
            parameters.Add("UserHes", userHes);
            parameters.Add("AnbarCode", anbarCode.Value);
            parameters.Add("Offset", (pageNumber - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var whereConditions = new List<string> { "sd.MENUIT = @GroupCode", "vp.HES = @UserHes", "ISNULL(sd.OKF, 1) = 1" };
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string normalizedSearchTerm = searchTerm.Trim().FixPersianChars();
                string normalizedNameSql = "REPLACE(REPLACE(sd.NAME, N'ي', N'ی'), N'ك', N'ک')";
                whereConditions.Add($"(({normalizedNameSql} LIKE @SearchPattern) OR (sd.CODE LIKE @SearchPattern))");
                parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%");
            }
            string commonWhereClause = string.Join(" AND ", whereConditions);

            // 1. Get Paged Items (Basic Info)
            string pagedItemsSql = $@"
                SELECT
                    sd.CODE, sd.NAME, sd.MABL_F, sd.B_SEF, sd.MAX_M, sd.TOZIH, sd.MENUIT,
                    sd.VAHED AS VahedCode, tv.NAMES AS VahedName,
                    ISNULL(FSK.MIN_M, 0) AS MinimumInventory
                FROM dbo.STUF_DEF sd
                INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON vpk.CODE = sd.CODE
                INNER JOIN dbo.VISITORS_PORSANT vp ON vp.PORID = vpk.PORID
                LEFT JOIN dbo.TCOD_VAHEDS tv ON tv.CODE = sd.VAHED
                LEFT JOIN dbo.STUF_FSK FSK ON sd.CODE = FSK.CODE AND FSK.ANBAR = @AnbarCode
                WHERE {commonWhereClause}
                ORDER BY sd.NAME
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

            string countSql = $@"
                SELECT COUNT(DISTINCT sd.CODE)
                FROM dbo.STUF_DEF sd
                INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON sd.CODE = vpk.CODE
                INNER JOIN dbo.VISITORS_PORSANT vp ON vpk.PORID = vp.PORID
                WHERE {commonWhereClause};";

            try
            {
                var items = (await _dbService.DoGetDataSQLAsync<ItemDisplayDto>(pagedItemsSql, parameters)).ToList();
                int totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                // 2. Enrich with Inventory (Efficiently)
                if (items.Any())
                {
                    await EnrichItemsWithInventoryAsync(items, anbarCode.Value);
                }

                items.ForEach(i =>
                {
                    i.MinimumInventory ??= 0;
                    if (!string.IsNullOrEmpty(_imageBasePath))
                    {
                        i.ImageExists = SupportedImageExtensions.Any(ext => System.IO.File.Exists(Path.Combine(_imageBasePath, i.CODE + ext)));
                    }
                });

                var pagedResult = new PagedResult<ItemDisplayDto>
                {
                    Items = items,
                    TotalCount = totalItemCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
                return Ok(pagedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paged items for Group {GroupCode}, Anbar {AnbarCode}", groupCode, anbarCode.Value);
                return StatusCode(500, "خطا در دریافت لیست کالاها.");
            }
        }

        private async Task EnrichItemsWithInventoryAsync(List<ItemDisplayDto> items, int anbarCode)
        {
            var codes = items.Select(i => i.CODE).ToList();
            var parameters = new DynamicParameters();
            parameters.Add("AnbarCode", anbarCode);
            parameters.Add("Codes", codes);

            // Using IN clause to fetch inventory for specific items
            // Assuming AK_MOGO_AVL_KOL and AK_MOGO_FR are performant when filtered by CODE
            string inventorySql = $@"
                SELECT 
                    sd.CODE, 
                    ROUND(ISNULL(AK.SMEGH, 0) - ISNULL(FR.MEG, 0), 2) AS CurrentInventory
                FROM dbo.STUF_DEF sd
                LEFT JOIN dbo.AK_MOGO_AVL_KOL(99999999, @AnbarCode) AK ON sd.CODE = AK.CODE AND AK.ANBAR = @AnbarCode
                LEFT JOIN dbo.AK_MOGO_FR(99999999, @AnbarCode) FR ON FR.CODE = sd.CODE AND FR.ANBAR = @AnbarCode
                WHERE sd.CODE IN @Codes";

            try 
            {
                var inventories = await _dbService.DoGetDataSQLAsync<dynamic>(inventorySql, parameters);
                var inventoryDict = inventories.ToDictionary(x => (string)x.CODE, x => (decimal?)x.CurrentInventory);

                foreach (var item in items)
                {
                    if (inventoryDict.TryGetValue(item.CODE, out decimal? inv))
                    {
                        item.CurrentInventory = inv;
                    }
                    else
                    {
                        item.CurrentInventory = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enriching items with inventory.");
            }
        }

        [HttpGet("image/{itemCode}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetItemImage(string itemCode)
        {
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
                    return Ok(0);
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
                var parameters = new { ItemCode = itemCode };

                string primaryUnitSql = @"
                SELECT
                    sd.VAHED AS VahedCode,
                    tv.NAMES AS VahedName,
                    1.0 AS Nesbat
                FROM dbo.STUF_DEF sd
                INNER JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
                WHERE sd.CODE = @ItemCode;";

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

                var itemSpecificUnits = combinedUnits
                    .GroupBy(u => u.VahedCode)
                    .Select(g => new UnitInfo
                    {
                        VahedCode = g.Key,
                        VahedName = g.First().VahedName,
                        Nesbat = g.First().Nesbat
                    })
                    .OrderBy(u => u.Nesbat)
                    .ToList();

                if (!itemSpecificUnits.Any() && !primaryUnitList.Any())
                {
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
        public async Task<ActionResult<List<VisitorItemPriceDto>>> GetVisitorPrices([FromQuery] int priceListId, [FromQuery] List<string>? itemCodes)
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

            _logger.LogInformation("Fetching visitor item prices for UserHES: {UserHes}, PriceListID: {PriceListId}, ItemCodes count: {ItemCodeCount}", userHes, priceListId, itemCodes?.Count ?? 0);

            var sql = new StringBuilder(@"
                     SELECT DISTINCT
                         vpk.CODE,      -- کد کالا از VISITORS_PORSANT_KALA
                         ped.PRICE1,    -- قیمت از PRICE_ELAMIE_DTL
                         ped.PEPID      -- شناسه اعلامیه قیمت از PRICE_ELAMIE_DTL
                     FROM dbo.VISITORS_PORSANT vp
                     INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON vp.PORID = vpk.PORID
                     INNER JOIN dbo.STUF_DEF sd ON vpk.CODE = sd.CODE
                     INNER JOIN dbo.PRICE_ELAMIE_DTL ped ON sd.PGID = ped.PGID AND ped.PEPID = @PriceListIdParam
                     WHERE vp.HES = @UserHesParam");

            var parameters = new DynamicParameters();
            parameters.Add("UserHesParam", userHes);
            parameters.Add("PriceListIdParam", priceListId);

            if (itemCodes != null && itemCodes.Any())
            {
                sql.AppendLine(" AND vpk.CODE IN @ItemCodesParam");
                parameters.Add("ItemCodesParam", itemCodes);
                _logger.LogInformation("Filtering visitor prices by {Count} specific item codes.", itemCodes.Count);
            }
            else
            {
                _logger.LogInformation("Fetching all visitor prices for selected price list (no specific item filter).");
            }

            try
            {
                var prices = await _dbService.DoGetDataSQLAsync<VisitorItemPriceDto>(sql.ToString(), parameters);
                return Ok(prices ?? Enumerable.Empty<VisitorItemPriceDto>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching visitor item prices for UserHES {UserHes} and PriceListID {PriceListId}.", userHes, priceListId);
                return StatusCode(StatusCodes.Status500InternalServerError, "خطا در دریافت لیست قیمت کالاها از سرور.");
            }
        }

        [HttpPost("search-historical-items")]
        public async Task<ActionResult<PagedResult<ItemDisplayDto>>> SearchHistoricalOrderItems([FromBody] HistoricalSearchRequestDto request)
        {
            try
            {
                if (request.AnbarCode <= 0)
                {
                    return BadRequest("کد انبار معتبر نیست.");
                }

                _logger.LogInformation("API: Searching historical items. Anbar: {AnbarCode}, Search: '{SearchTerm}', Page: {Page}", request.AnbarCode, request.SearchTerm, request.PageNumber);

                var parameters = new DynamicParameters();
                parameters.Add("AnbarCode", request.AnbarCode);
                parameters.Add("PriceListId", request.PriceListId);
                parameters.Add("CustomerTypeCode", request.CustomerTypeCode);
                parameters.Add("PaymentTermId", request.PaymentTermId);
                parameters.Add("DiscountListId", request.DiscountListId);
                parameters.Add("Offset", (request.PageNumber - 1) * request.PageSize);
                parameters.Add("PageSize", request.PageSize);

                var whereConditions = new List<string> { "i.TAG IN (9, 2)", "i.CODE IS NOT NULL", "LTRIM(RTRIM(i.CODE)) <> ''" };

                if (!string.IsNullOrWhiteSpace(request.SearchTerm))
                {
                    string normalizedSearchTerm = request.SearchTerm.Trim().FixPersianChars();
                    whereConditions.Add("(sd.NAME LIKE @SearchPattern OR sd.CODE LIKE @SearchPattern)");
                    parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%");
                }

                string whereClause = string.Join(" AND ", whereConditions);

                // 1. Get Paged Items (Basic Info)
                string pagedItemsSql;
                string countSql;

                if (string.IsNullOrWhiteSpace(request.SearchTerm))
                {
                    // No search term: Get Top 50 unique items sorted by Name
                    // Note: OFFSET-FETCH is better than TOP if we want pagination, but user logic was TOP 50 initially.
                    // I will use OFFSET-FETCH for consistency and support pagination even without search term if requested.
                    // But to match previous behavior of "Top 50" if page=1:
                    
                    pagedItemsSql = $@"
                        SELECT DISTINCT
                            sd.CODE, sd.NAME, sd.MABL_F, sd.B_SEF, sd.MAX_M, sd.TOZIH, sd.MENUIT,
                            sd.VAHED AS VahedCode, tv.NAMES AS VahedName, CAST(0 AS BIT) AS ImageExists,
                            ISNULL(FSK.MIN_M, 0) AS MinimumInventory
                        FROM dbo.INVO_LST i
                        INNER JOIN dbo.STUF_DEF sd ON i.CODE = sd.CODE
                        LEFT JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
                        LEFT JOIN dbo.STUF_FSK FSK ON sd.CODE = FSK.CODE AND FSK.ANBAR = @AnbarCode
                        WHERE {whereClause}
                        ORDER BY sd.NAME
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
                    
                    countSql = $@"SELECT COUNT(DISTINCT i.CODE) FROM dbo.INVO_LST i INNER JOIN dbo.STUF_DEF sd ON i.CODE = sd.CODE WHERE {whereClause}";
                }
                else
                {
                    // Search mode
                    pagedItemsSql = $@"
                        SELECT DISTINCT
                            sd.CODE, sd.NAME, sd.MABL_F, sd.B_SEF, sd.MAX_M, sd.TOZIH, sd.MENUIT,
                            sd.VAHED AS VahedCode, tv.NAMES AS VahedName, CAST(0 AS BIT) AS ImageExists,
                            ISNULL(FSK.MIN_M, 0) AS MinimumInventory
                        FROM dbo.INVO_LST i
                        INNER JOIN dbo.STUF_DEF sd ON i.CODE = sd.CODE
                        LEFT JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
                        LEFT JOIN dbo.STUF_FSK FSK ON sd.CODE = FSK.CODE AND FSK.ANBAR = @AnbarCode
                        WHERE {whereClause}
                        ORDER BY sd.NAME
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

                     countSql = $@"SELECT COUNT(DISTINCT i.CODE) FROM dbo.INVO_LST i INNER JOIN dbo.STUF_DEF sd ON i.CODE = sd.CODE WHERE {whereClause}";
                }

                var items = (await _dbService.DoGetDataSQLAsync<ItemDisplayDto>(pagedItemsSql, parameters)).ToList();
                int totalItemCount = 0;
                
                // Only count if needed (for pagination)
                if (string.IsNullOrWhiteSpace(request.SearchTerm) && request.PageNumber == 1 && items.Count < request.PageSize)
                {
                     totalItemCount = items.Count;
                }
                else
                {
                     totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);
                }

                // 2. Enrich with Inventory
                if (items.Any())
                {
                    await EnrichItemsWithInventoryAsync(items, request.AnbarCode);
                }

                // ... Price Logic (Kept as is) ...
                var priceLogicParams = new DynamicParameters();
                priceLogicParams.Add("PriceListId", request.PriceListId);
                priceLogicParams.Add("DiscountListId", request.DiscountListId);
                priceLogicParams.Add("CustomerTypeCode", request.CustomerTypeCode);
                priceLogicParams.Add("PaymentTermId", request.PaymentTermId);

                var headerDiscounts = await _dbService.DoGetDataSQLAsyncSingle<PriceElamieTfDtlDto>("SELECT TOP 1 TF1, TF2 FROM dbo.PRICE_ELAMIETF_DTL WHERE PEID = @DiscountListId AND CUSTCODE = @CustomerTypeCode AND PPID = @PaymentTermId", priceLogicParams);
                var visitorPrices = (await _dbService.DoGetDataSQLAsync<VisitorItemPriceDto>("SELECT ped.PRICE1, sd.CODE FROM dbo.STUF_DEF sd JOIN dbo.PRICE_ELAMIE_DTL ped ON sd.PGID = ped.PGID WHERE ped.PEPID = @PriceListId", priceLogicParams)).ToDictionary(p => p.CODE);

                foreach (var item in items)
                {
                    item.HeaderDiscountTF1 = headerDiscounts?.TF1 ?? 0;
                    item.HeaderDiscountTF2 = headerDiscounts?.TF2 ?? 0;

                    if (visitorPrices.TryGetValue(item.CODE, out var priceInfo))
                    {
                        item.PriceFromPriceList = priceInfo.PRICE1;
                        item.HasPriceInCurrentPriceList = true;
                    }
                    else if (item.MABL_F > 0)
                    {
                        item.PriceFromPriceList = item.MABL_F;
                        item.HasPriceInCurrentPriceList = true;
                    }

                    decimal? priceBeforeHeaderDiscounts = item.PriceFromPriceList;
                    if (priceBeforeHeaderDiscounts.HasValue && priceBeforeHeaderDiscounts.Value > 0)
                    {
                        decimal priceAfterTf1 = priceBeforeHeaderDiscounts.Value * (1 - (decimal)((item.HeaderDiscountTF1 ?? 0) / 100.0));
                        decimal finalPrice = priceAfterTf1 * (1 - (decimal)((item.HeaderDiscountTF2 ?? 0) / 100.0));
                        item.PriceAfterHeaderDiscounts = finalPrice;
                        if (priceBeforeHeaderDiscounts != 0)
                            item.TotalCalculatedDiscountPercent = (double)((priceBeforeHeaderDiscounts - finalPrice) / priceBeforeHeaderDiscounts * 100.0m);
                        else
                            item.TotalCalculatedDiscountPercent = 0;
                    }
                }

                var pagedResult = new PagedResult<ItemDisplayDto>
                {
                    Items = items,
                    TotalCount = totalItemCount,
                    PageNumber = request.PageNumber,
                    PageSize = request.PageSize
                };

                return Ok(pagedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "API: Error in SearchHistoricalOrderItems. Search: '{SearchTerm}'", request.SearchTerm);
                return StatusCode(500, new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "خطای داخلی سرور در دریافت کالاها.",
                    Status = 500
                });
            }
        }
    }
}