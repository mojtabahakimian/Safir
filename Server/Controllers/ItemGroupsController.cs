// Safir.Server/Controllers/ItemGroupsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Kala;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; // <<< اضافه شد
using System.IO;                      // <<< اضافه شد
using System.Linq;                    // <<< اضافه شد
using Microsoft.AspNetCore.StaticFiles; // <<< اضافه شد for ContentType

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Ensure only authorized users can access
    public class ItemGroupsController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<ItemGroupsController> _logger;
        private readonly IConfiguration _configuration; // <<< اضافه شد
        private readonly string? _baseImageSharePath; // <<< مسیر پایه از تنظیمات
        private readonly string? _groupImageFolderPath; // <<< مسیر کامل پوشه عکس گروه
        private static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" }; // <<< پسوندهای مجاز

        public ItemGroupsController(IDatabaseService dbService, ILogger<ItemGroupsController> logger, IConfiguration configuration) // <<< IConfiguration اضافه شد
        {
            _dbService = dbService;
            _logger = logger;
            _configuration = configuration; // <<< ذخیره شد

            // <<< خواندن مسیر پایه از تنظیمات >>>
            _baseImageSharePath = _configuration["ImageSharePath"];
            if (string.IsNullOrEmpty(_baseImageSharePath))
            {
                _logger.LogWarning("ImageSharePath is not configured in appsettings.json.");
                _groupImageFolderPath = null;
            }
            else
            {
                // <<< ساخت مسیر پوشه تصاویر گروه >>>
                _groupImageFolderPath = Path.Combine(_baseImageSharePath, "grp");
                _logger.LogInformation("Group image folder path set to: {Path}", _groupImageFolderPath);
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TCODE_MENUITEM>>> GetItemGroups()
        {
            // <<< ستون pic از کوئری حذف شد >>>
            const string sql = @"SELECT
                                    CODE,
                                    NAMES,
                                    ANBAR,
                                    ID
                                FROM dbo.TCODE_MENUITEM
                                ORDER BY NAMES;";
            try
            {
                _logger.LogInformation("Fetching item groups from TCODE_MENUITEM");
                var itemGroupsResult = await _dbService.DoGetDataSQLAsync<TCODE_MENUITEM>(sql);

                if (itemGroupsResult == null)
                {
                    _logger.LogWarning("Fetching item groups returned null.");
                    return Ok(new List<TCODE_MENUITEM>());
                }

                var itemGroupsList = itemGroupsResult.ToList();
                _logger.LogInformation("Successfully fetched {Count} item groups.", itemGroupsList.Count);

                // <<< بررسی وجود فایل تصویر برای هر گروه >>>
                if (!string.IsNullOrEmpty(_groupImageFolderPath))
                {
                    foreach (var group in itemGroupsList)
                    {
                        // نام فایل مورد انتظار (بدون پسوند) - تبدیل double به string
                        // نکته: اگر کد گروه اعشاری باشد (مثلا 1.5)، نام فایل هم 1.5 خواهد بود.
                        string groupCodeStr = group.CODE.ToString(System.Globalization.CultureInfo.InvariantCulture);

                        // بررسی وجود فایل با پسوندهای مختلف
                        group.ImageExists = SupportedImageExtensions.Any(ext =>
                            System.IO.File.Exists(Path.Combine(_groupImageFolderPath, groupCodeStr + ext))
                        );

                        if (group.ImageExists)
                        {
                            // _logger.LogTrace("Image found for group CODE: {GroupCode}", group.CODE);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Group image path is not configured or invalid. Skipping image existence check.");
                }


                return Ok(itemGroupsList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching item groups from database.");
                return StatusCode(StatusCodes.Status500InternalServerError, "خطا در دریافت اطلاعات گروه کالاها از سرور.");
            }
        }

        // <<< اکشن جدید برای ارائه تصویر گروه >>>
        [HttpGet("image/{groupCode}")]
        [AllowAnonymous] // یا از Authorize استفاده کنید اگر نیاز به احراز هویت است
        public async Task<IActionResult> GetGroupImage(double groupCode)
        {
            if (string.IsNullOrEmpty(_groupImageFolderPath))
            {
                _logger.LogError("Attempted to get group image, but ImageSharePath (or grp subfolder) is not configured.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Image path configuration error.");
            }

            // تبدیل کد گروه به رشته برای نام فایل
            string groupCodeStr = groupCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // <<< جلوگیری از Path Traversal - بررسی کاراکترهای نامعتبر در کد >>>
            // اگرچه groupCode از نوع double است، احتیاط بهتر است.
            if (groupCodeStr.Contains("..") || groupCodeStr.Contains('/') || groupCodeStr.Contains('\\') || groupCodeStr.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                _logger.LogWarning("Invalid characters detected in groupCode for image request: {GroupCode}", groupCode);
                return BadRequest("Invalid group code format.");
            }


            // <<< جستجو برای فایل با پسوندهای مختلف >>>
            string? foundFilePath = SupportedImageExtensions
                .Select(ext => Path.Combine(_groupImageFolderPath, groupCodeStr + ext))
                .FirstOrDefault(System.IO.File.Exists);

            if (foundFilePath == null)
            {
                _logger.LogInformation("Group image not found for CODE: {GroupCode} in path: {Path}", groupCode, _groupImageFolderPath);
                return NotFound("Image not found."); // یا یک تصویر پیش‌فرض برگردانید
            }

            try
            {
                // خواندن بایت‌های فایل
                byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(foundFilePath);

                // تعیین نوع محتوا (MIME type) بر اساس پسوند فایل
                var provider = new FileExtensionContentTypeProvider();
                if (!provider.TryGetContentType(foundFilePath, out var contentType))
                {
                    contentType = "application/octet-stream"; // نوع پیش‌فرض اگر پسوند ناشناخته بود
                }

                _logger.LogInformation("Serving group image for CODE: {GroupCode} from: {FilePath}", groupCode, foundFilePath);
                // بازگرداندن فایل
                return File(fileBytes, contentType);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("File not found race condition for group image CODE: {GroupCode}, Path: {FilePath}", groupCode, foundFilePath);
                return NotFound("Image not found.");
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO Error reading group image file for CODE: {GroupCode}, Path: {FilePath}", groupCode, foundFilePath);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error reading image file.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving group image for CODE: {GroupCode}", groupCode);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error serving image.");
            }
        }
    }
}