// MyBlazor/Server/Controllers/ItemsController.cs
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
                _logger.LogWarning("User HES claim ('{UserHesClaim}') is missing or invalid for User: {Username}", BaseknowClaimTypes.USER_HES, User.Identity?.Name);
                return Ok(new PagedResult<ItemDisplayDto> { Items = new List<ItemDisplayDto>(), TotalCount = 0, PageNumber = pageNumber, PageSize = pageSize });
            }

            _logger.LogInformation("Fetching items for Group: {GroupCode}, User HES: {UserHES}, Page: {PageNumber}, Size: {PageSize}, Search: '{SearchTerm}'",
               groupCode, userHes, pageNumber, pageSize, searchTerm);

            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;
            int offset = (pageNumber - 1) * pageSize;

            var whereConditions = new List<string>();
            var parameters = new DynamicParameters();

            whereConditions.Add("sd.MENUIT = @GroupCode");
            parameters.Add("GroupCode", groupCode);

            whereConditions.Add("vp.HES = @UserHes");
            parameters.Add("UserHes", userHes);

            whereConditions.Add("ISNULL(sd.OKF, 1) = 1");

            string? normalizedSearchTerm = null;
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                normalizedSearchTerm = searchTerm.Trim();
                normalizedSearchTerm = Regex.Replace(normalizedSearchTerm, @"\s+", " ");
                normalizedSearchTerm = normalizedSearchTerm.FixPersianChars();

                string normalizedNameSql = "REPLACE(REPLACE(REPLACE(REPLACE(sd.NAME, N'ي', N'ی'), N'ك', N'ک'), NCHAR(160), N' '), N'  ', N' ')";
                whereConditions.Add($"(({normalizedNameSql} LIKE @SearchPattern) OR (sd.CODE LIKE @SearchPattern))");
                parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%");
            }

            string commonWhereClause = string.Join(" AND ", whereConditions);

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
                                    SELECT * FROM Raw WHERE dup_rnk = 1
                                ),
                                BaseItems AS
                                (
                                    SELECT
                                        CODE, NAME, MABL_F, B_SEF, MAX_M,
                                        TOZIH, MENUIT, VahedCode, VahedName,
                                        ROW_NUMBER() OVER (ORDER BY CODE) AS RowNum
                                    FROM Dedup
                                )
                                SELECT
                                    CODE, NAME, MABL_F, B_SEF, MAX_M,
                                    TOZIH, MENUIT, VahedCode, VahedName
                                FROM BaseItems
                                WHERE RowNum >  @RowStart
                                  AND RowNum <= @RowEnd;";

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
                IEnumerable<ItemDisplayDto> items = await _dbService.DoGetDataSQLAsync<ItemDisplayDto>(itemsSql, parameters);
                int totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                List<ItemDisplayDto> itemsList = items.ToList();

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
        public async Task<ActionResult<IEnumerable<VisitorItemPriceDto>>> GetVisitorItemPrices([FromQuery] int priceListId, [FromQuery] List<string>? itemCodes = null) // Added itemCodes
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
    }
}