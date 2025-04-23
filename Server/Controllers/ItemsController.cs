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

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ItemsController : ControllerBase
    {
        // ... Fields and Constructor remain the same ...
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


        // <<< متد به‌روز شده برای دریافت کالاها >>>
        [HttpGet("bygroup/{groupCode}")]
        public async Task<ActionResult<PagedResult<ItemDisplayDto>>> GetItemsByGroup( // <<< نوع خروجی به ItemDisplayDto تغییر کرد
               double groupCode,
               [FromQuery] int pageNumber = 1,
               [FromQuery] int pageSize = 10,
               [FromQuery] string? searchTerm = null)
        {
            _logger.LogInformation("Fetching items page {PageNumber} (size {PageSize}) for group code: {GroupCode}, Search: '{SearchTerm}'",
                pageNumber, pageSize, groupCode, searchTerm);

            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100; // Limit page size
            int offset = (pageNumber - 1) * pageSize;

            var whereConditions = new List<string>();
            var parameters = new DynamicParameters();

            whereConditions.Add("sd.MENUIT = @GroupCode"); // sd = alias for STUF_DEF
            parameters.Add("GroupCode", groupCode);
            whereConditions.Add("ISNULL(sd.OKF, 1) = 1"); // <<< اضافه شد: فرض وجود ستون OKF برای کالاهای فعال >>>

            // --- Search Term Handling ---
            string? normalizedSearchTerm = null;
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                normalizedSearchTerm = searchTerm.Trim();
                normalizedSearchTerm = Regex.Replace(normalizedSearchTerm, @"\s+", " ");
                normalizedSearchTerm = normalizedSearchTerm.FixPersianChars();

                string normalizedNameSql = "REPLACE(REPLACE(REPLACE(REPLACE(sd.NAME, N'ي', N'ی'), N'ك', N'ک'), NCHAR(160), N' '), N'  ', N' ')";
                whereConditions.Add($"(({normalizedNameSql} LIKE @SearchPattern) OR (sd.CODE LIKE @SearchPattern))"); // Add parentheses
                parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%");
            }
            // --- End Search ---

            string commonWhereClause = string.Join(" AND ", whereConditions);

            // --- SQL Query with JOIN and new fields ---
            // <<< ستون‌های B_SEF, MAX_M, TOZIH اضافه شد (با فرض نام ستون) >>>
            // <<< JOIN با TCOD_VAHEDS برای گرفتن نام واحد اضافه شد >>>

            string itemsSql = $@"
                WITH PagedItems AS (
                    SELECT
                        sd.CODE, sd.NAME, sd.MABL_F, sd.VAHED AS VahedCode,
                        sd.MENUIT, sd.B_SEF, sd.MAX_M, sd.TOZIH,
                        tv.NAMES AS VahedName,
                        ROW_NUMBER() OVER (ORDER BY sd.NAME) AS RowNum
                    FROM dbo.STUF_DEF sd
                    LEFT JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
                    WHERE {commonWhereClause}
                )
                SELECT CODE, NAME, MABL_F, B_SEF, MAX_M, TOZIH, MENUIT, VahedCode, VahedName
                FROM PagedItems
                WHERE RowNum > @RowStart AND RowNum <= @RowEnd; ";

            //////For Test on Single Kala
            //string itemsSql = $@"
            //    WITH PagedItems AS (
            //        SELECT
            //            sd.CODE, sd.NAME, sd.MABL_F, sd.VAHED AS VahedCode,
            //            sd.MENUIT, sd.B_SEF, sd.MAX_M, sd.TOZIH,
            //            tv.NAMES AS VahedName
            //        FROM dbo.STUF_DEF sd
            //        LEFT JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
            //    )
            //    SELECT CODE, NAME, MABL_F, B_SEF, MAX_M, TOZIH, MENUIT
            //    FROM STUF_DEF 
            //    WHERE code = 3352";

            // --- Count Query ---
            string countSql = $@"
                 SELECT COUNT(*)
                 FROM dbo.STUF_DEF sd -- Alias added for consistency
                 WHERE {commonWhereClause};";


            parameters.Add("RowStart", offset);
            parameters.Add("RowEnd", offset + pageSize);

            try
            {
                // <<< نوع TEntity به ItemDisplayDto تغییر کرد >>>
                IEnumerable<ItemDisplayDto> items = await _dbService.DoGetDataSQLAsync<ItemDisplayDto>(itemsSql, parameters);
                int totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                List<ItemDisplayDto> itemsList = items.ToList();

                // <<< بررسی وجود تصویر (بدون تغییر منطق، فقط روی DTO اعمال می‌شود) >>>
                // --- بررسی دقیق وجود تصویر ---
                if (!string.IsNullOrEmpty(_imageBasePath))
                {
                    _logger.LogInformation("Image Base Path for Checks: {BasePath}", _imageBasePath); // لاگ مسیر پایه
                    foreach (var item in itemsList)
                    {
                        string itemCodeStr = item.CODE; // فرض کنیم CODE از نوع string است، اگر نه تبدیل کنید
                        item.ImageExists = false; // پیش‌فرض false
                        _logger.LogTrace("Checking image for Item Code: {ItemCode}", itemCodeStr); // لاگ کد کالا
                        foreach (var ext in SupportedImageExtensions)
                        {
                            string potentialPath = Path.Combine(_imageBasePath, itemCodeStr + ext);
                            try // اضافه کردن try-catch برای خطای احتمالی File.Exists
                            {
                                if (System.IO.File.Exists(potentialPath))
                                {
                                    item.ImageExists = true;
                                    _logger.LogInformation("Image FOUND for Item Code: {ItemCode} at Path: {Path}", itemCodeStr, potentialPath); // لاگ در صورت پیدا شدن
                                    break; // از حلقه پسوندها خارج شو چون پیدا شد
                                }
                                // else { _logger.LogTrace("File not found at: {Path}", potentialPath); } // لاگ اضافی برای هر پسوند
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error checking file existence for path: {Path}", potentialPath);
                                // ادامه بده تا پسوندهای دیگر بررسی شوند
                            }
                        }
                        if (!item.ImageExists)
                        {
                            _logger.LogWarning("Image NOT found for Item Code: {ItemCode} after checking all extensions.", itemCodeStr); // لاگ فقط اگر هیچکدام پیدا نشد
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("ImageSharePath is not configured. All items will have ImageExists=false.");
                }
                // --- پایان بررسی تصویر ---

                var pagedResult = new PagedResult<ItemDisplayDto> // <<< نوع PagedResult به ItemDisplayDto تغییر کرد
                {
                    Items = itemsList,
                    TotalCount = totalItemCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                _logger.LogInformation("Fetched page {PageNumber}/{TotalPages} for group {GroupCode}, Search: '{OrigSearch}'. Found {ItemCount} items (Total: {TotalCount})",
                    pageNumber, pagedResult.TotalPages, groupCode, searchTerm ?? "N/A", itemsList.Count, totalItemCount);


                return Ok(pagedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paged/searched items for group {GroupCode}, Search: '{SearchTerm}'", groupCode, searchTerm);
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

        // --- API جدید برای دریافت موجودی ---
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
                //decimal? inventory = await _dbService.DoGetDataSQLAsyncSingle<decimal?>(inventorySql, parameters);
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