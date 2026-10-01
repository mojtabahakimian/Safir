using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Treasury;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Treasury;

namespace Safir.Server.Controllers
{
    /// <summary>
    /// خزانه‌داری — همان فرمِ PGET_HEDِ WPF. مجوزها از همان SAL_CHEK می‌آیند که WPF
    /// می‌خواند (فرم PGET_HED در TFORMS): دیدن = RUN، افزودن = INP، اصلاح = UPD،
    /// حذف = DEL — مثل SETSECURITY / SETSECURITYSUB.
    /// </summary>
    [ApiController]
    [Route("api/treasury")]
    [Authorize]
    public class TreasuryController : ControllerBase
    {
        private readonly TreasuryService _svc;
        private readonly ILogger<TreasuryController> _logger;

        public TreasuryController(IDatabaseService db, ILogger<TreasuryController> logger)
        {
            _svc = new TreasuryService(db);
            _logger = logger;
        }

        private int UserCo => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        private string UserName => User.FindFirst(BaseknowClaimTypes.UUSER)?.Value ?? User.Identity?.Name ?? "";
        // واحد و شیفتی که کاربر موقعِ ورود انتخاب کرده — VAHED_OF_USER / SHIFT_OF_USER در WPF
        private int? UserDept => int.TryParse(User.FindFirst(BaseknowClaimTypes.TFSAZMAN)?.Value, out var d) ? d : null;
        private int? UserShift => int.TryParse(User.FindFirst(BaseknowClaimTypes.SHIFT)?.Value, out var s) ? s : null;
        private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

        private async Task<(TreasuryService.UserPerms? P, ActionResult? Deny)> Perms(Func<TreasuryService.UserPerms, bool> need, string what)
        {
            if (UserCo <= 0) return (null, Unauthorized());
            var p = await _svc.GetPermsAsync(UserCo);
            if (!p.Run) return (null, StatusCode(403, "اجازه‌ی باز کردن فرم خزانه‌داری را ندارید."));
            if (!need(p)) return (null, StatusCode(403, $"اجازه‌ی {what} در خزانه‌داری را ندارید."));
            return (p, null);
        }

        private ActionResult<TreasurySaveResult> Result(TreasurySaveResult r) => r.Ok ? Ok(r) : BadRequest(r);

        [HttpGet("meta")]
        public async Task<ActionResult<TreasuryMetaDto>> Meta()
        {
            if (UserCo <= 0) return Unauthorized();
            var p = await _svc.GetPermsAsync(UserCo);
            return Ok(await _svc.GetMetaAsync(p, UserCo, UserDept, UserShift, UserName));
        }

        [HttpGet]
        public async Task<ActionResult<List<TreasuryListItemDto>>> List([FromQuery] long? from, [FromQuery] long? to)
        {
            var (p, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Ok(await _svc.ListAsync(p!, UserDept ?? 0, UserName, from, to));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TreasuryDetailDto>> Get(int id)
        {
            var (p, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var d = await _svc.GetAsync(id, p!, UserDept ?? 0, UserName);
            return d is null ? NotFound("خزانه پیدا نشد یا اجازه‌ی دیدنش را ندارید.") : Ok(d);
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

        [HttpGet("cash-account")]
        public async Task<ActionResult<TreasuryAccountDto>> CashAccount([FromQuery] int? depatman, [FromQuery] int? shift)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var hes = await _svc.CashAccountAsync(depatman, shift);
            if (hes is null) return NotFound("حساب صندوق در حساب‌های خودگردان تعریف نشده است.");
            var name = (await _svc.SearchAccountsAsync(hes)).FirstOrDefault(a => a.Hes == hes)?.Name;
            return Ok(new TreasuryAccountDto { Hes = hes, Name = name });
        }

        [HttpPost]
        public async Task<ActionResult<TreasurySaveResult>> Create([FromBody] TreasuryHeaderSaveRequest req)
        {
            var (_, deny) = await Perms(x => x.Inp, "ثبت خزانه‌ی جدید");
            if (deny is not null) return deny;
            var r = await _svc.CreateAsync(req, UserName, UserCo);
            if (r.Ok) _logger.LogInformation("خزانه {Id} (سند {Ns}) توسط {User} ساخته شد", r.Id, r.Ns, UserName);
            return Result(r);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<TreasurySaveResult>> UpdateHeader(int id, [FromBody] TreasuryHeaderSaveRequest req)
        {
            var (p, deny) = await Perms(x => x.Upd, "اصلاح");
            if (deny is not null) return deny;
            return Result(await _svc.UpdateHeaderAsync(id, req, p!, UserDept ?? 0, UserName));
        }

        [HttpPost("{id:int}/unlock")]
        public async Task<ActionResult<TreasurySaveResult>> Unlock(int id)
        {
            var (p, deny) = await Perms(x => x.Upd, "اصلاح");
            if (deny is not null) return deny;
            var r = await _svc.UnlockAsync(id, p!, UserDept ?? 0, UserName, HttpContext.Connection.RemoteIpAddress?.ToString());
            if (r.Ok) _logger.LogInformation("اصلاح خزانه {Id} توسط {User} — نسخه‌ی قبلی در TR_PGET_* ثبت شد", id, UserName);
            return Result(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult<TreasurySaveResult>> Delete(int id)
        {
            var (p, deny) = await Perms(x => x.Del, "حذف");
            if (deny is not null) return deny;
            return Result(await _svc.DeleteAsync(id, p!, UserDept ?? 0, UserName));
        }

        [HttpPost("{id:int}/rows")]
        public async Task<ActionResult<TreasurySaveResult>> AddRow(int id, [FromBody] TreasuryRowSaveRequest req)
        {
            var (p, deny) = await Perms(x => x.Inp, "افزودن سطر");
            if (deny is not null) return deny;
            return Result(await _svc.SaveRowAsync(id, null, req, p!, UserDept ?? 0, UserName, UserCo, ClientIp));
        }

        [HttpPut("{id:int}/rows/{idh:int}")]
        public async Task<ActionResult<TreasurySaveResult>> UpdateRow(int id, int idh, [FromBody] TreasuryRowSaveRequest req)
        {
            var (p, deny) = await Perms(x => x.Upd, "اصلاح سطر");
            if (deny is not null) return deny;
            return Result(await _svc.SaveRowAsync(id, idh, req, p!, UserDept ?? 0, UserName, UserCo, ClientIp));
        }

        [HttpDelete("{id:int}/rows/{idh:int}")]
        public async Task<ActionResult<TreasurySaveResult>> DeleteRow(int id, int idh)
        {
            var (p, deny) = await Perms(x => x.Del, "حذف سطر");
            if (deny is not null) return deny;
            return Result(await _svc.DeleteRowAsync(id, idh, p!, UserDept ?? 0, UserName, ClientIp));
        }

        // ─────────────── چک‌ها ───────────────

        /// <summary>چک‌های قابلِ انتخاب برای واگذاری (assign)، برگشتِ دریافتی (return-received) و برگشتِ پرداختی (return-paid).</summary>
        [HttpGet("cheques")]
        public async Task<ActionResult<List<TreasuryChequeDto>>> Cheques([FromQuery] string mode, [FromQuery] string? q)
        {
            var (_, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Ok(await _svc.PickableChequesAsync(mode, q));
        }

        // ─────────────── امضا و ارجاع ───────────────

        /// <summary>امضا یا برداشتنِ امضا — مجوزش در جدولِ SIGN است، نه SAL_CHEK.</summary>
        [HttpPost("{id:int}/sign")]
        public async Task<ActionResult<TreasurySaveResult>> Sign(int id, [FromBody] TreasurySignRequest req)
        {
            var (p, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var r = await _svc.SignAsync(id, req, p!, UserDept ?? 0, UserName, UserCo);
            if (r.Ok) _logger.LogInformation("خزانه {Id}: امضای {Slot} = {On} توسط {User}", id, req.Slot, req.On, UserName);
            return Result(r);
        }

        [HttpPost("{id:int}/refer")]
        public async Task<ActionResult<TreasurySaveResult>> Refer(int id, [FromBody] TreasuryReferRequest req)
        {
            var (p, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            return Result(await _svc.ReferAsync(id, req, p!, UserDept ?? 0, UserName, UserCo));
        }

        // ─────────────── ضمیمه‌ی سطر ───────────────

        [HttpGet("{id:int}/rows/{idh:int}/attachment")]
        public async Task<IActionResult> Attachment(int id, int idh)
        {
            var (p, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var (bytes, _) = await _svc.GetAttachmentAsync(id, idh, p!, UserDept ?? 0, UserName);
            return bytes is null or { Length: 0 } ? NotFound("تصویری برای این سطر یافت نشد.") : File(bytes, TreasuryService.ImageContentType(bytes));
        }

        [HttpPost("{id:int}/rows/{idh:int}/attachment")]
        [RequestSizeLimit(11 * 1024 * 1024)]
        public async Task<ActionResult<TreasurySaveResult>> UploadAttachment(int id, int idh, IFormFile? file)
        {
            var (p, deny) = await Perms(x => x.Inp || x.Upd, "افزودن تصویر");
            if (deny is not null) return deny;
            if (file is null) return BadRequest(new TreasurySaveResult { Ok = false, Error = "فایلی انتخاب نشده است." });
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            return Result(await _svc.SaveAttachmentAsync(id, idh, ms.ToArray(), Path.GetExtension(file.FileName) ?? "", p!, UserDept ?? 0, UserName, UserCo));
        }

        [HttpDelete("{id:int}/rows/{idh:int}/attachment")]
        public async Task<ActionResult<TreasurySaveResult>> DeleteAttachment(int id, int idh)
        {
            var (p, deny) = await Perms(x => x.Del || x.Upd, "حذف تصویر");
            if (deny is not null) return deny;
            return Result(await _svc.DeleteAttachmentAsync(id, idh, p!, UserDept ?? 0, UserName));
        }

        // ─────────────── چاپ و اکسل ───────────────

        /// <summary>kind: amalkard (عملکرد خزانه)، daryaft (سند دریافت)، pardakht (سند پرداخت).</summary>
        [HttpGet("{id:int}/print")]
        public async Task<ActionResult<TreasuryPrintDto>> Print(int id, [FromQuery] string kind = "amalkard")
        {
            var (p, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var (doc, err) = await _svc.PrintAsync(id, kind, p!, UserDept ?? 0, UserName);
            return doc is null ? BadRequest(err) : Ok(doc);
        }

        [HttpGet("{id:int}/signature/{slot:int}")]
        public async Task<IActionResult> SignatureImage(int id, int slot)
        {
            var (p, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var bytes = await _svc.SignatureImageAsync(id, slot, p!, UserDept ?? 0, UserName);
            return bytes is null or { Length: 0 } ? NotFound() : File(bytes, TreasuryService.ImageContentType(bytes));
        }

        [HttpGet("{id:int}/excel")]
        public async Task<IActionResult> Excel(int id)
        {
            var (p, deny) = await Perms(x => x.See, "دیدن");
            if (deny is not null) return deny;
            var bytes = await _svc.ExcelAsync(id, p!, UserDept ?? 0, UserName);
            return bytes is null ? NotFound("خزانه پیدا نشد.")
                : File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"treasury-{id}.xlsx");
        }
    }
}
