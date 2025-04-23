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


        [HttpGet("bygroup/{groupCode}")]
        public async Task<ActionResult<PagedResult<STUF_DEF>>> GetItemsByGroup(
               double groupCode,
               [FromQuery] int pageNumber = 1,
               [FromQuery] int pageSize = 10,
               [FromQuery] string? searchTerm = null)
        {
            _logger.LogInformation("Fetching items page {PageNumber} (size {PageSize}) for group code: {GroupCode}, Search: '{SearchTerm}'",
                pageNumber, pageSize, groupCode, searchTerm);

            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;
            int offset = (pageNumber - 1) * pageSize;

            var whereConditions = new List<string>();
            var parameters = new DynamicParameters();

            whereConditions.Add("MENUIT = @GroupCode");
            parameters.Add("GroupCode", groupCode);
            whereConditions.Add("ISNULL(OKF, 1) = 1");

            // --- Normalize Search Term (C# side) and Add Search Condition ---
            string? normalizedSearchTerm = null;
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                // 1. Normalize whitespace and Persian/Arabic characters using existing helper
                normalizedSearchTerm = searchTerm.Trim();
                normalizedSearchTerm = Regex.Replace(normalizedSearchTerm, @"\s+", " "); // Normalize spaces
                normalizedSearchTerm = normalizedSearchTerm.FixPersianChars(); // <<< APPLY FixPersianChars

                // 2. Add WHERE condition using normalized search term and SQL REPLACE for NAME column
                //    (Replace Arabic ي and ك with Persian ی and ک in NAME before comparing)
                //   Also include the previous SPACE normalization for robustness
                string normalizedNameSql = "REPLACE(REPLACE(REPLACE(REPLACE(NAME, N'ي', N'ی'), N'ك', N'ک'), NCHAR(160), N' '), N'  ', N' ')";

                whereConditions.Add($"({normalizedNameSql} LIKE @SearchPattern OR CODE LIKE @SearchPattern)");
                parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%");
            }
            // --- End Search Condition Handling ---

            string commonWhereClause = string.Join(" AND ", whereConditions);

            // --- SQL Queries using ROW_NUMBER() ---
            // Applying REPLACE to NAME column for better matching of spaces
            // NOTE: This might prevent index usage on the NAME column. Test performance.
            string itemsSql = $@"
                WITH PagedItems AS (
                    SELECT
                        CODE, NAME, MABL_F, VAHED, MENUIT,
                        ROW_NUMBER() OVER (ORDER BY NAME) AS RowNum
                    FROM dbo.STUF_DEF
                    WHERE {commonWhereClause}
                )
                SELECT CODE, NAME, MABL_F, VAHED, MENUIT
                FROM PagedItems
                WHERE RowNum > @RowStart AND RowNum <= @RowEnd; ";

            // Also apply REPLACE to the count query's WHERE clause
            string countSql = $@"
                SELECT COUNT(*)
                FROM dbo.STUF_DEF
                WHERE {commonWhereClause};";

            parameters.Add("RowStart", offset);
            parameters.Add("RowEnd", offset + pageSize);

            try
            {
                IEnumerable<STUF_DEF> items = await _dbService.DoGetDataSQLAsync<STUF_DEF>(itemsSql, parameters);
                int totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                List<STUF_DEF> itemsList = items.ToList();
                // ... (Image check logic remains the same) ...
                if (!string.IsNullOrEmpty(_imageBasePath)) { foreach (var item in itemsList) { item.ImageExists = SupportedImageExtensions.Any(ext => System.IO.File.Exists(Path.Combine(_imageBasePath, item.CODE + ext))); } }

                var pagedResult = new PagedResult<STUF_DEF>
                {
                    Items = itemsList,
                    TotalCount = totalItemCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };

                _logger.LogInformation("Fetched page {PageNumber}/{TotalPages} for group {GroupCode}, Search: '{OrigSearch}' (Normalized: '{NormSearch}'). Found {ItemCount} items (Total: {TotalCount})",
                   pageNumber, pagedResult.TotalPages, groupCode, searchTerm, normalizedSearchTerm ?? "N/A", itemsList.Count, totalItemCount);


                return Ok(pagedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paged/searched items (SQL Server 2008 compat mode) for group {GroupCode}, Search: '{SearchTerm}'", groupCode, searchTerm);
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
    }
}