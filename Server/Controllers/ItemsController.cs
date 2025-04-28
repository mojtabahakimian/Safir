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
        public async Task<ActionResult<PagedResult<ItemDisplayDto>>> GetItemsByGroup(
               double groupCode, // <<< پارامتر groupCode اکنون استفاده می‌شود >>>
               [FromQuery] int pageNumber = 1,
               [FromQuery] int pageSize = 10,
               [FromQuery] string? searchTerm = null)
        {
            var porIdClaim = User.FindFirstValue(BaseknowClaimTypes.PORID);
            if (!int.TryParse(porIdClaim, out int userPorId))
            {
                _logger.LogWarning("User PORID claim ('{PorIdClaim}') is missing or invalid for User: {Username}", porIdClaim, User.Identity?.Name);
                return Ok(new PagedResult<ItemDisplayDto> { Items = new List<ItemDisplayDto>(), TotalCount = 0, PageNumber = pageNumber, PageSize = pageSize });
            }
            _logger.LogInformation("Fetching items for Group: {GroupCode}, User PORID: {UserPorId}, Page: {PageNumber}, Size: {PageSize}, Search: '{SearchTerm}'",
               groupCode, userPorId, pageNumber, pageSize, searchTerm); // Log GroupCode


            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;
            int offset = (pageNumber - 1) * pageSize;

            var whereConditions = new List<string>();
            var parameters = new DynamicParameters();

            // --- Filter based on Group Code ---
            whereConditions.Add("sd.MENUIT = @GroupCode"); // <<< --- فعال شد --- >>>
            parameters.Add("GroupCode", groupCode);       // <<< --- پارامتر اضافه شد --- >>>

            // --- Filter based on User's PORID ---
            whereConditions.Add("vpk.PORID = @UserPorId");
            parameters.Add("UserPorId", userPorId);

            // Other base filters
            whereConditions.Add("sd.MENUIT IS NOT NULL");
            whereConditions.Add("ISNULL(sd.OKF, 1) = 1");

            // --- Search Term Handling ---
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
            // --- End Search ---

            string commonWhereClause = string.Join(" AND ", whereConditions);

            // --- Items Query ---
            string itemsSql = $@"
                WITH PagedItems AS (
                    SELECT
                        sd.CODE, sd.NAME, sd.MABL_F, sd.VAHED AS VahedCode, sd.B_SEF, sd.MAX_M, sd.TOZIH,
                        sd.MENUIT,
                        tv.NAMES AS VahedName,
                        vpk.PORSANT,
                        ROW_NUMBER() OVER (ORDER BY sd.NAME) AS RowNum
                    FROM dbo.STUF_DEF sd
                    INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON sd.CODE = vpk.CODE
                    LEFT JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
                    WHERE {commonWhereClause}
                )
                SELECT CODE, NAME, MABL_F, B_SEF, MAX_M, TOZIH, MENUIT, VahedCode, VahedName, PORSANT
                FROM PagedItems
                WHERE RowNum > @RowStart AND RowNum <= @RowEnd;";

            // --- Count Query ---
            string countSql = $@"
                 SELECT COUNT(*)
                 FROM dbo.STUF_DEF sd
                 INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON sd.CODE = vpk.CODE
                 WHERE {commonWhereClause};";


            parameters.Add("RowStart", offset);
            parameters.Add("RowEnd", offset + pageSize);

            try
            {
                IEnumerable<ItemDisplayDto> items = await _dbService.DoGetDataSQLAsync<ItemDisplayDto>(itemsSql, parameters);
                int totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                List<ItemDisplayDto> itemsList = items.ToList();

                // --- Image Check Logic (Unchanged) ---
                if (!string.IsNullOrEmpty(_imageBasePath))
                {
                    // _logger.LogInformation("Image Base Path for Checks: {BasePath}", _imageBasePath);
                    foreach (var item in itemsList)
                    {
                        string itemCodeStr = item.CODE;
                        item.ImageExists = false;
                        // _logger.LogTrace("Checking image for Item Code: {ItemCode}", itemCodeStr);
                        foreach (var ext in SupportedImageExtensions)
                        {
                            string potentialPath = Path.Combine(_imageBasePath, itemCodeStr + ext);
                            try
                            {
                                if (System.IO.File.Exists(potentialPath))
                                {
                                    item.ImageExists = true;
                                    // _logger.LogInformation("Image FOUND for Item Code: {ItemCode} at Path: {Path}", itemCodeStr, potentialPath);
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error checking file existence for path: {Path}", potentialPath);
                            }
                        }
                        // if (!item.ImageExists) { _logger.LogWarning("Image NOT found for Item Code: {ItemCode}", itemCodeStr); }
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

                _logger.LogInformation("Fetched page {PageNumber}/{TotalPages} for Group {GroupCode}, User PORID {UserPorId}, Search: '{OrigSearch}'. Found {ItemCount} items (Total: {TotalCount})",
                    pageNumber, pagedResult.TotalPages, groupCode, userPorId, searchTerm ?? "N/A", itemsList.Count, totalItemCount);

                return Ok(pagedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paged items for Group {GroupCode}, User PORID {UserPorId}, Search: '{SearchTerm}'", groupCode, userPorId, searchTerm);
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
    }
}