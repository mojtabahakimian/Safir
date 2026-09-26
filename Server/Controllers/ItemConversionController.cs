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
            [FromQuery] long? dt1, [FromQuery] long? dt2, [FromQuery] bool includeVoided = false)
            => Ok(await Service.ListAsync(dt1, dt2, includeVoided));

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
                "تبدیل {Id}: {From} → {To} به مبلغ {Value} توسط {User} (حواله {Issue}، رسید {Receipt})",
                res.ConversionId, req.FromCode, req.ToCode, res.Value, CurrentUser,
                res.IssueNumber, res.ReceiptNumber);

            return Ok(res);
        }

        [HttpDelete("{id:int}")]
        [Pay2Authorize(CostForms.ItemConversion, Pay2Perm.Del)]
        public async Task<IActionResult> Void(int id)
        {
            var (ok, error) = await Service.VoidAsync(id, CurrentUser);
            if (!ok) return BadRequest(error);

            _logger.LogInformation("تبدیل {Id} توسط {User} ابطال شد", id, CurrentUser);
            return Ok();
        }
    }
}
