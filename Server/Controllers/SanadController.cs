using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Sanad;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Sanad;
using Safir.Shared.Models.Treasury;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// صدور و ویرایشِ اسنادِ حسابداری — همان فرمِ DEED_HEADِ WPF. مجوزها از همان SAL_CHEK می‌آیند
    /// (فرم DEED_HEAD در TFORMS): دیدن = RUN/SEE، سندِ تازه و ردیفِ تازه = INP، اصلاح = UPD، حذف = DEL.
    /// </summary>
    [ApiController]
    [Route("api/sanad")]
    [Authorize]
    public class SanadController : ControllerBase
    {
        private readonly SanadService _svc;
        private readonly ILogger<SanadController> _logger;

        public SanadController(IDatabaseService db, ILogger<SanadController> logger)
        {
            _svc = new SanadService(db);
            _logger = logger;
        }

        private int UserCo => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        private string UserName => User.FindFirst(BaseknowClaimTypes.UUSER)?.Value ?? User.Identity?.Name ?? "";
        private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

        private async Task<(SanadService.UserPerms? P, ActionResult? Deny)> Perms(Func<SanadService.UserPerms, bool> need, string what)
        {
            if (UserCo <= 0) return (null, Unauthorized());
            var p = await _svc.GetPermsAsync(UserCo);
            if (!p.Run) return (null, StatusCode(403, "اجازه‌ی باز کردن فرمِ صدور و ویرایش اسناد را ندارید."));
            if (!need(p)) return (null, StatusCode(403, $"اجازه‌ی {what} در صدور و ویرایش اسناد را ندارید."));
            return (p, null);
        }

        private ActionResult<SanadSaveResult> Result(SanadSaveResult r) => r.Ok ? Ok(r) : BadRequest(r);

        [HttpGet("meta")]
        public async Task<ActionResult<SanadMetaDto>> Meta()
        {
            if (UserCo <= 0) return Unauthorized();
            var p = await _svc.GetPermsAsync(UserCo);
            return Ok(await _svc.GetMetaAsync(p, UserCo, UserName));
        }

        [HttpGet]
        public async Task<ActionResult<List<SanadListItemDto>>> List([FromQuery] long? from, [FromQuery] long? to, [FromQuery] string? kind, [FromQuery] string? q)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Ok(await _svc.ListAsync(from, to, kind, q));
        }

        [HttpGet("{ns:double}")]
        public async Task<ActionResult<SanadDetailDto>> Get(double ns)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var d = await _svc.GetAsync(ns);
            return d is null ? NotFound("سند پیدا نشد.") : Ok(d);
        }

        /// <summary>پرونده‌ی اتوماسیون (tg = 0) شماره‌ی مبنا دارد — MenuBaseOnKindOpen(0, base).</summary>
        [HttpGet("by-base/{base:int}")]
        public async Task<ActionResult<double>> ByBase(int @base)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var ns = await _svc.NsByBaseAsync(@base);
            return ns is null ? NotFound("سندی با این شماره‌ی مبنا پیدا نشد.") : Ok(ns.Value);
        }

        [HttpGet("{ns:double}/neighbour")]
        public async Task<ActionResult<double>> Neighbour(double ns, [FromQuery] bool next)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var n = await _svc.NeighbourAsync(ns, next);
            return n is null ? NoContent() : Ok(n.Value);
        }

        [HttpGet("accounts")]
        public async Task<ActionResult<List<TreasuryAccountDto>>> Accounts([FromQuery] string? q)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Ok(await _svc.SearchAccountsAsync(q));
        }

        [HttpGet("balances")]
        public async Task<ActionResult<List<TreasuryBalanceDto>>> Balances([FromQuery] string[] hes)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Ok(await _svc.BalancesAsync(hes));
        }

        /// <summary>شرح‌های آماده (جدول SHARH) — «+» در آخرِ شرح (SHARH_LIST).</summary>
        [HttpGet("descriptions")]
        public async Task<ActionResult<List<TreasuryLookupItem>>> Descriptions()
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Ok(await _svc.CannedDescriptionsAsync());
        }

        [HttpPost]
        public async Task<ActionResult<SanadSaveResult>> Create([FromBody] SanadHeaderSaveRequest req)
        {
            var (_, deny) = await Perms(x => x.Inp, "صدور سندِ جدید");
            if (deny is not null) return deny;
            var r = await _svc.CreateAsync(req, UserName, UserCo);
            if (r.Ok) _logger.LogInformation("سند {Ns} توسط {User} صادر شد", r.Ns, UserName);
            return Result(r);
        }

        [HttpPut("{ns:double}")]
        public async Task<ActionResult<SanadSaveResult>> UpdateHeader(double ns, [FromBody] SanadHeaderSaveRequest req)
        {
            var (_, deny) = await Perms(x => x.Upd, "اصلاح");
            if (deny is not null) return deny;
            return Result(await _svc.UpdateHeaderAsync(ns, req));
        }

        [HttpPost("{ns:double}/unlock")]
        public async Task<ActionResult<SanadSaveResult>> Unlock(double ns)
        {
            var (_, deny) = await Perms(x => x.Upd, "اصلاح");
            if (deny is not null) return deny;
            var r = await _svc.UnlockAsync(ns, UserName, ClientIp);
            if (r.Ok) _logger.LogInformation("سند {Ns}: «اصلاح سند» توسط {User} (نسخه در TR_DEED_*)", ns, UserName);
            return Result(r);
        }

        [HttpDelete("{ns:double}")]
        public async Task<ActionResult<SanadSaveResult>> Delete(double ns)
        {
            var (_, deny) = await Perms(x => x.Del, "حذف");
            if (deny is not null) return deny;
            var r = await _svc.DeleteAsync(ns, UserName, ClientIp);
            if (r.Ok) _logger.LogWarning("سند {Ns} با {Rows} ردیف توسط {User} حذف شد", ns, r.Count, UserName);
            return Result(r);
        }

        [HttpPost("{ns:double}/rows")]
        public async Task<ActionResult<SanadSaveResult>> AddRow(double ns, [FromBody] SanadRowSaveRequest req)
        {
            var (_, deny) = await Perms(x => x.Inp, "افزودن ردیف");
            if (deny is not null) return deny;
            return Result(await _svc.SaveRowAsync(ns, null, req, UserName, UserCo, ClientIp));
        }

        [HttpPost("{ns:double}/rows/batch")]
        public async Task<ActionResult<SanadSaveResult>> AddRows(double ns, [FromBody] List<SanadRowSaveRequest> req)
        {
            var (_, deny) = await Perms(x => x.Inp, "افزودن ردیف");
            if (deny is not null) return deny;
            return Result(await _svc.AddRowsAsync(ns, req ?? new(), UserCo));
        }

        [HttpPut("{ns:double}/rows/{rowId:long}")]
        public async Task<ActionResult<SanadSaveResult>> UpdateRow(double ns, long rowId, [FromBody] SanadRowSaveRequest req)
        {
            var (_, deny) = await Perms(x => x.Upd, "اصلاح ردیف");
            if (deny is not null) return deny;
            return Result(await _svc.SaveRowAsync(ns, rowId, req, UserName, UserCo, ClientIp));
        }

        [HttpDelete("{ns:double}/rows/{rowId:long}")]
        public async Task<ActionResult<SanadSaveResult>> DeleteRow(double ns, long rowId)
        {
            var (_, deny) = await Perms(x => x.Del, "حذف ردیف");
            if (deny is not null) return deny;
            return Result(await _svc.DeleteRowAsync(ns, rowId, UserName, ClientIp));
        }

        // ─────────────── امضا و ارجاع ───────────────

        /// <summary>امضا یا برداشتنِ امضا — مجوزش در جدولِ SIGN است (SND_*)، نه SAL_CHEK.</summary>
        [HttpPost("{ns:double}/sign")]
        public async Task<ActionResult<SanadSaveResult>> Sign(double ns, [FromBody] TreasurySignRequest req)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var r = await _svc.SignAsync(ns, req, UserName, UserCo);
            if (r.Ok) _logger.LogInformation("سند {Ns}: امضای {Slot} = {On} توسط {User}", ns, req.Slot, req.On, UserName);
            return Result(r);
        }

        [HttpPost("{ns:double}/refer")]
        public async Task<ActionResult<SanadSaveResult>> Refer(double ns, [FromBody] TreasuryReferRequest req)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Result(await _svc.ReferAsync(ns, req, UserName, UserCo));
        }

        // ─────────────── سوابق، قطعی کردن ───────────────

        [HttpGet("{ns:double}/history")]
        public async Task<ActionResult<List<SanadHistoryItemDto>>> History(double ns)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Ok(await _svc.HistoryListAsync(ns));
        }

        [HttpGet("{ns:double}/history/{tridd:long}")]
        public async Task<ActionResult<List<SanadHistoryRowDto>>> HistoryRows(double ns, long tridd)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Ok(await _svc.HistoryRowsAsync(ns, tridd));
        }

        [HttpPost("finalize")]
        public async Task<ActionResult<SanadFinalizePreviewDto>> Finalize([FromBody] SanadFinalizeRequest req)
        {
            var (_, deny) = await Perms(x => x.Finalize, "قطعی کردنِ اسناد (فرمِ «تایید و قطعی کردن اسناد»)");
            if (deny is not null) return deny;
            if (req.From <= 0 || req.To <= 0) return BadRequest("پارامترها كافي نيست!");
            var r = await _svc.FinalizeAsync(req);
            if (req.Apply) _logger.LogWarning("اسناد {From} تا {To} توسط {User} قطعی شدند ({N} سند)", req.From, req.To, UserName, r.Applied);
            return Ok(r);
        }

        // ─────────────── چاپ و اکسل ───────────────

        [HttpGet("{ns:double}/print")]
        public async Task<ActionResult<SanadPrintDto>> Print(double ns, [FromQuery] bool full = true)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var (doc, err) = await _svc.PrintAsync(ns, full);
            return doc is null ? BadRequest(err) : Ok(doc);
        }

        [HttpGet("{ns:double}/signature/{slot:int}")]
        public async Task<IActionResult> Signature(double ns, int slot)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var bytes = await _svc.SignatureImageAsync(ns, slot);
            return bytes is null ? NotFound() : File(bytes, Safir.Server.Treasury.TreasuryService.ImageContentType(bytes));
        }

        [HttpGet("{ns:double}/excel")]
        public async Task<IActionResult> Excel(double ns)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var bytes = await _svc.ExcelAsync(ns);
            return bytes is null ? NotFound("سند پیدا نشد.")
                : File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"sanad-{ns:0}.xlsx");
        }
    }
}
