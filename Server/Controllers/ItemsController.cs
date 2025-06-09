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
using Safir.Shared.Models.Kharid;

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
            // ... (بخش مربوط به دریافت UserHES و تنظیمات صفحه‌بندی بدون تغییر باقی می‌ماند) ...
            var userHes = User.FindFirstValue(BaseknowClaimTypes.USER_HES);
            if (string.IsNullOrEmpty(userHes))
            {
                return Ok(new PagedResult<ItemDisplayDto> { Items = new List<ItemDisplayDto>() });
            }

            // --- دریافت کد انبار از گروه ---
            int? anbarCode = await _dbService.DoGetDataSQLAsyncSingle<int?>("SELECT ANBAR FROM TCODE_MENUITEM WHERE CODE = @GroupCode", new { GroupCode = groupCode });
            if (!anbarCode.HasValue)
            {
                return BadRequest(new { Message = $"کد انبار برای گروه {groupCode} یافت نشد." });
            }

            _logger.LogInformation("Fetching items for Group: {GroupCode}, Anbar: {AnbarCode}, User HES: {UserHES}", groupCode, anbarCode.Value, userHes);

            int offset = (pageNumber - 1) * pageSize;
            var parameters = new DynamicParameters();
            var whereConditions = new List<string> { "sd.MENUIT = @GroupCode", "vp.HES = @UserHes", "ISNULL(sd.OKF, 1) = 1" };
            parameters.Add("GroupCode", groupCode);
            parameters.Add("UserHes", userHes);
            parameters.Add("AnbarCode", anbarCode.Value); // <<< اضافه کردن کد انبار به پارامترها

            // ... (بخش مربوط به searchTerm بدون تغییر) ...

            string commonWhereClause = string.Join(" AND ", whereConditions);

            // کوئری اصلی که حالا شامل محاسبه موجودی هم می‌شود
            string itemsSql = $@"
        WITH Raw AS (
            SELECT
                sd.CODE, sd.NAME, sd.MABL_F, sd.B_SEF, sd.MAX_M, sd.TOZIH, sd.MENUIT,
                sd.VAHED AS VahedCode, tv.NAMES AS VahedName,
                -- <<<<< بخش جدید برای محاسبه موجودی >>>>>
                ROUND(ISNULL(AK.SMEGH, 0) - ISNULL(FR.MEG, 0), 2) AS CurrentInventory,
                FSK.MIN_M AS MinimumInventory,
                ROW_NUMBER() OVER (PARTITION BY sd.CODE ORDER BY sd.CODE) AS dup_rnk
            FROM dbo.STUF_DEF sd
            INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON vpk.CODE = sd.CODE
            INNER JOIN dbo.VISITORS_PORSANT vp ON vp.PORID = vpk.PORID
            LEFT JOIN dbo.TCOD_VAHEDS tv ON tv.CODE = sd.VAHED
            -- <<<<< جوین‌های جدید برای موجودی >>>>>
            LEFT JOIN dbo.STUF_FSK FSK ON sd.CODE = FSK.CODE AND FSK.ANBAR = @AnbarCode
            LEFT JOIN dbo.AK_MOGO_AVL_KOL(99999999, @AnbarCode) AK ON AK.CODE = sd.CODE AND AK.ANBAR = @AnbarCode
            LEFT JOIN dbo.AK_MOGO_FR(99999999, @AnbarCode) FR ON FR.CODE = sd.CODE AND FR.ANBAR = @AnbarCode
            WHERE {commonWhereClause}
        ),
        Dedup AS ( SELECT * FROM Raw WHERE dup_rnk = 1 ),
        BaseItems AS ( SELECT *, ROW_NUMBER() OVER (ORDER BY CODE) AS RowNum FROM Dedup )
        SELECT * FROM BaseItems
        WHERE RowNum > @RowStart AND RowNum <= @RowEnd;
    ";

            string countSql = $@"
        SELECT COUNT(DISTINCT sd.CODE)
        FROM dbo.STUF_DEF sd
        INNER JOIN dbo.VISITORS_PORSANT_KALA vpk ON sd.CODE = vpk.CODE
        INNER JOIN dbo.VISITORS_PORSANT vp ON vpk.PORID = vp.PORID
        WHERE {commonWhereClause};";

            parameters.Add("RowStart", offset);
            parameters.Add("RowEnd", offset + pageSize);

            // ... (بخش try-catch و اجرای کوئری‌ها) ...
            try
            {
                var items = (await _dbService.DoGetDataSQLAsync<ItemDisplayDto>(itemsSql, parameters)).ToList();
                int totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                // اطمینان از اینکه حداقل موجودی null به صفر تبدیل می‌شود
                items.ForEach(i => i.MinimumInventory ??= 0);

                // ... (بخش بررسی وجود تصویر) ...
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
                // ... (لاگ خطا) ...
                return StatusCode(500, "خطا در دریافت لیست کالاها.");
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


        [HttpGet("historical-order-items")]
        public async Task<ActionResult<PagedResult<ItemDisplayDto>>> GetHistoricalOrderItems(
            [FromQuery] int anbarCode,
            [FromQuery] string? searchTerm = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] int? priceListId = null,
            [FromQuery] int? customerTypeCode = null,
            [FromQuery] int? paymentTermId = null,
            [FromQuery] int? discountListId = null)
        {
            if (anbarCode <= 0) return BadRequest("کد انبار معتبر نیست.");

            _logger.LogInformation("API: Fetching historical items. Anbar: {AnbarCode}, Search: '{SearchTerm}', Page: {Page}", anbarCode, searchTerm, pageNumber);

            var parameters = new DynamicParameters();
            parameters.Add("AnbarCode", anbarCode);
            parameters.Add("PriceListId", priceListId);
            parameters.Add("CustomerTypeCode", customerTypeCode);
            parameters.Add("PaymentTermId", paymentTermId);
            parameters.Add("DiscountListId", discountListId);

            var whereConditions = new List<string> { "i.TAG IN (9, 2)", "i.CODE IS NOT NULL", "LTRIM(RTRIM(i.CODE)) <> ''" };

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string normalizedSearchTerm = searchTerm.Trim().FixPersianChars();
                whereConditions.Add("(sd.NAME LIKE @SearchPattern OR sd.CODE LIKE @SearchPattern)");
                parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%");
            }

            string whereClause = string.Join(" AND ", whereConditions);

            string countSql = $@"
        SELECT COUNT(DISTINCT i.CODE)
        FROM dbo.INVO_LST i
        INNER JOIN dbo.STUF_DEF sd ON i.CODE = sd.CODE
        WHERE {whereClause};
    ";

            // پایه کوئری که تمام جوین‌های لازم را دارد
            string baseQuery = $@"
        FROM dbo.INVO_LST i
        INNER JOIN dbo.STUF_DEF sd ON i.CODE = sd.CODE
        LEFT JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
        LEFT JOIN dbo.STUF_FSK FSK ON sd.CODE = FSK.CODE AND FSK.ANBAR = @AnbarCode
        LEFT JOIN dbo.AK_MOGO_AVL_KOL(99999999, @AnbarCode) AK ON AK.CODE = sd.CODE AND AK.ANBAR = @AnbarCode
        LEFT JOIN dbo.AK_MOGO_FR(99999999, @AnbarCode) FR ON FR.CODE = sd.CODE AND FR.ANBAR = @AnbarCode
        WHERE {whereClause}
    ";

            string finalItemsSql;
            int totalItemCount = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    // حالت بارگذاری اولیه: فقط 50 آیتم اول بدون صفحه‌بندی
                    finalItemsSql = $@"
                SELECT DISTINCT TOP 50
                    sd.CODE, sd.NAME, sd.MABL_F, sd.B_SEF, sd.MAX_M, sd.TOZIH, sd.MENUIT,
                    sd.VAHED AS VahedCode, tv.NAMES AS VahedName, CAST(0 AS BIT) AS ImageExists,
                    ROUND(ISNULL(AK.SMEGH, 0) - ISNULL(FR.MEG, 0), 2) AS CurrentInventory,
                    ISNULL(FSK.MIN_M, 0) AS MinimumInventory
                {baseQuery}
                ORDER BY sd.NAME;";

                    // در حالت اولیه، تعداد کل همان تعداد نمایش داده شده (حداکثر 50) است
                    pageNumber = 1;
                    // pageSize = 50; // این مقدار از ورودی گرفته می‌شود
                }
                else
                {
                    // حالت جستجو: تمام نتایج با صفحه‌بندی
                    totalItemCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);
                    finalItemsSql = $@"
                WITH DistinctItems AS (
                    SELECT DISTINCT sd.CODE, sd.NAME
                    {baseQuery.Replace("i.CODE", "sd.CODE")}
                )
                SELECT
                    di.CODE, di.NAME, sd.MABL_F, sd.B_SEF, sd.MAX_M, sd.TOZIH, sd.MENUIT,
                    sd.VAHED AS VahedCode, tv.NAMES AS VahedName, CAST(0 AS BIT) AS ImageExists,
                    ROUND(ISNULL(AK.SMEGH, 0) - ISNULL(FR.MEG, 0), 2) AS CurrentInventory,
                    ISNULL(FSK.MIN_M, 0) AS MinimumInventory
                FROM DistinctItems di
                INNER JOIN dbo.STUF_DEF sd ON di.CODE = sd.CODE
                LEFT JOIN dbo.TCOD_VAHEDS tv ON sd.VAHED = tv.CODE
                LEFT JOIN dbo.STUF_FSK FSK ON di.CODE = FSK.CODE AND FSK.ANBAR = @AnbarCode
                LEFT JOIN dbo.AK_MOGO_AVL_KOL(99999999, @AnbarCode) AK ON AK.CODE = di.CODE AND AK.ANBAR = @AnbarCode
                LEFT JOIN dbo.AK_MOGO_FR(99999999, @AnbarCode) FR ON FR.CODE = di.CODE AND FR.ANBAR = @AnbarCode
                ORDER BY di.NAME
                OFFSET @RowStart ROWS FETCH NEXT @PageSize ROWS ONLY;";

                    parameters.Add("RowStart", (pageNumber - 1) * pageSize);
                    parameters.Add("PageSize", pageSize);
                }

                var items = (await _dbService.DoGetDataSQLAsync<ItemDisplayDto>(finalItemsSql, parameters)).ToList();
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    totalItemCount = items.Count;
                }

                // ... (منطق اعمال قیمت و تخفیف که قبلاً داشتیم، اینجا باید دوباره اعمال شود)
                // این بخش بسیار مهم است
                var priceLogicParams = new DynamicParameters();
                priceLogicParams.Add("PriceListId", priceListId);
                priceLogicParams.Add("DiscountListId", discountListId);
                priceLogicParams.Add("CustomerTypeCode", customerTypeCode);
                priceLogicParams.Add("PaymentTermId", paymentTermId);

                var headerDiscounts = await _dbService.DoGetDataSQLAsyncSingle<PriceElamieTfDtlDto>("SELECT TF1, TF2 FROM dbo.PRICE_ELAMIETF_DTL WHERE PEID = @DiscountListId AND CUSTCODE = @CustomerTypeCode AND PPID = @PaymentTermId", priceLogicParams);
                var visitorPrices = (await _dbService.DoGetDataSQLAsync<VisitorItemPriceDto>("SELECT ped.PRICE1, sd.CODE, ped.PEPID FROM dbo.STUF_DEF sd JOIN dbo.PRICE_ELAMIE_DTL ped ON sd.PGID = ped.PGID WHERE ped.PEPID = @PriceListId", priceLogicParams)).ToDictionary(p => p.CODE);

                foreach (var item in items)
                {
                    item.HeaderDiscountTF1 = headerDiscounts?.TF1 ?? 0;
                    item.HeaderDiscountTF2 = headerDiscounts?.TF2 ?? 0;

                    if (visitorPrices.TryGetValue(item.CODE, out var priceInfo))
                    {
                        item.PriceFromPriceList = priceInfo.PRICE1;
                        item.HasPriceInCurrentPriceList = true;
                    }

                    decimal priceBeforeHeaderDiscounts = item.PriceFromPriceList ?? item.MABL_F;
                    if (priceBeforeHeaderDiscounts > 0)
                    {
                        decimal priceAfterTf1 = priceBeforeHeaderDiscounts * (1 - (decimal)(item.HeaderDiscountTF1 / 100.0));
                        decimal finalPrice = priceAfterTf1 * (1 - (decimal)(item.HeaderDiscountTF2 / 100.0));
                        item.PriceAfterHeaderDiscounts = finalPrice;
                        item.TotalCalculatedDiscountPercent = (double)((priceBeforeHeaderDiscounts - finalPrice) / priceBeforeHeaderDiscounts * 100.0m);
                    }
                }
                // پایان بخش اعمال قیمت

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
                _logger.LogError(ex, "API: Error fetching historical items. Search: '{SearchTerm}'", searchTerm);
                return StatusCode(500, "خطای داخلی سرور در دریافت کالاها.");
            }
        }
    }
}