using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.CostClose.ItemConversion;
using Safir.Server.Security;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.CostClose;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// تبدیل کالا به کالا — خروج یک کالا از انباری و ورود کالای دیگری به
    /// انبار دیگر، با همان ارزش.
    ///
    /// هر نوشتنی اینجا سه برگه‌ی انباری می‌سازد یا پاک می‌کند، پس همه‌ی
    /// مسیرهای نوشتن پشت مجوز می‌نشینند. خواندن آزادتر است چون فقط
    /// گزارشِ همان چیزی است که در انبار ثبت شده.
    /// </summary>
    [ApiController]
    [Route("api/item-conversion")]
    [Authorize]
    public class ItemConversionController : ControllerBase
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<ItemConversionController> _logger;

        public ItemConversionController(IDatabaseService db, ILogger<ItemConversionController> logger)
        {
            _db     = db;
            _logger = logger;
        }

        private ItemConversionService Service => new(_db);

        private string CurrentUser =>
            User.FindFirst(BaseknowClaimTypes.UUSER)?.Value
            ?? User.Identity?.Name
            ?? "نامشخص";

        [HttpGet]
        [Pay2Authorize(CostForms.ItemConversion, Pay2Perm.See)]
        public async Task<ActionResult<List<ConversionRowDto>>> List(
            [FromQuery] long? dt1, [FromQuery] long? dt2)
            => Ok(await Service.ListAsync(dt1, dt2));

        /// <summary>
        /// آنچه ثبت خواهد شد — نرخ، مبلغ، موجودی و هشدارها. هیچ چیزی
        /// نمی‌نویسد، پس صفحه می‌تواند با هر تغییرِ فرم صدایش بزند.
        /// </summary>
        [HttpPost("preview")]
        [Pay2Authorize(CostForms.ItemConversion, Pay2Perm.See)]
        public async Task<ActionResult<ConversionPreviewDto>> Preview([FromBody] CreateConversionRequest req)
            => Ok(await Service.PreviewAsync(req));

        [HttpPost]
        [Pay2Authorize(CostForms.ItemConversion, Pay2Perm.Inp)]
        public async Task<ActionResult<ConversionResultDto>> Create([FromBody] CreateConversionRequest req)
        {
            var res = await Service.CreateAsync(req, CurrentUser);

            if (!res.Ok)
            {
                _logger.LogWarning("تبدیل کالا ثبت نشد: {Error}", res.Error);
                return BadRequest(res);
            }

            _logger.LogInformation(
                "تبدیل {Number}: {From} → {To} به مبلغ {Value} توسط {User}",
                res.Number, req.FromCode, req.ToCode, res.Value, CurrentUser);

            return Ok(res);
        }

        /// <summary>
        /// سندِ یک برگه: سربرگ و آرتیکل‌هایش.
        ///
        /// آرتیکل‌ها از روی NUMBER/TAG خودِ برگه خوانده می‌شوند و نه فقط از
        /// N_S — اگر سند سربرگ داشته باشد ولی آرتیکلی نه (یا برعکس)، همین
        /// نما آن را لو می‌دهد.
        /// </summary>
        [HttpGet("{number:double}/sanad")]
        [Pay2Authorize(CostForms.ItemConversion, Pay2Perm.See)]
        public async Task<ActionResult<SanadViewDto>> GetSanad(double number)
        {
            var head = (await _db.DoGetDataSQLAsync<SanadViewDto>(@"
SELECT  SanadNo  = CAST(d.N_S AS BIGINT),
        DateS    = d.DATE_S,
        SharhS   = d.SHARH_S,
        UserName = d.USER_NAME
FROM    dbo.DEED_HED d
WHERE   d.NO_S = 10
  AND   d.N_S = (SELECT TOP 1 h.N_S FROM dbo.HEAD_LST h
                 WHERE h.TAG = 30 AND h.NUMBER = @Number)",
                new { Number = number })).FirstOrDefault();

            if (head is null) return NotFound("برای این برگه سندی صادر نشده است.");

            head.Articles = (await _db.DoGetDataSQLAsync<SanadArticleDto>(@"
SELECT  Hes   = d.HES,
        Name  = t.NAME,
        Sharh = d.SHARH,
        Bed   = ISNULL(d.BED, 0),
        Bes   = ISNULL(d.BES, 0)
FROM    dbo.DEED_DTL d
LEFT    JOIN dbo.TDETA_HES t ON t.N_KOL = d.HES_K AND t.NUMBER = d.HES_M AND t.TNUMBER = d.HES_T
WHERE   d.TAG = 30 AND d.NUMBER = @Number
ORDER BY d.BES, d.BED DESC",
                new { Number = number })).ToList();

            return Ok(head);
        }

        /// <summary>
        /// صدور (یا بازسازی) سند حسابداری همین یک برگه.
        ///
        /// ⚠️ مبلغ سند از MABL_K خودِ برگه می‌آید، و آن تا اجرای «بازسازی
        /// نرخ میانگین» عددِ لحظه‌ی ثبت است. صدور سند پیش از آن، سندی به
        /// نرخِ قدیمی می‌سازد — درست، ولی نه نهایی. برای همین اگر مبلغ صفر
        /// باشد اصلاً سند صادر نمی‌شود.
        /// </summary>
        [HttpPost("{number:double}/sanad")]
        [Pay2Authorize(CostForms.ItemConversion, Pay2Perm.Inp)]
        [Pay2Authorize(CostForms.ActRebuildDocs, Pay2Perm.Run)]
        public async Task<ActionResult<ConversionSanadResultDto>> IssueSanad(
            double number, [FromQuery] bool allowZero = false)
        {
            var svc = new Safir.Server.CostClose.GroupDocuments.ConversionRebuildService(_db);
            var res = await svc.RebuildOneAsync(number, allowZero);

            if (!res.Success)
                return BadRequest(new ConversionSanadResultDto { Ok = false, Error = res.FirstError });

            if (res.SheetCount == 0)
                return BadRequest(new ConversionSanadResultDto
                {
                    Ok    = false,
                    Error = allowZero
                        ? "سندی صادر نشد — برگه پیدا نشد یا مقصدش ناقص است."
                        : "سندی صادر نشد — یا برگه پیدا نشد، یا مقصدش ناقص است، یا مبلغش صفر است."
                });

            _logger.LogInformation("سند تبدیل برگه {Number} توسط {User} صادر شد: {Sanad}",
                number, CurrentUser, res.LastSanadNumber);

            return Ok(new ConversionSanadResultDto { Ok = true, SanadNumber = res.LastSanadNumber });
        }

        /// <summary>
        /// برگه‌ی تبدیل را پاک می‌کند — با شماره‌ی برگه، نه با id سطر، چون
        /// همان چیزی است که کاربر روی کاغذ می‌بیند.
        /// </summary>
        [HttpDelete("{number:double}")]
        [Pay2Authorize(CostForms.ItemConversion, Pay2Perm.Del)]
        public async Task<IActionResult> Delete(double number)
        {
            var (ok, error) = await Service.DeleteAsync(number);
            if (!ok) return BadRequest(error);

            _logger.LogInformation("برگه تبدیل {Number} توسط {User} پاک شد", number, CurrentUser);
            return Ok();
        }
    }
}
