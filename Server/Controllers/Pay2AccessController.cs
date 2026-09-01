using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Security;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Permissions;
using Safir.Shared.Utility;
using Dapper;

namespace Safir.Server.Controllers
{
    [Route("api/pay2/access")]
    [ApiController]
    [Authorize]
    public class Pay2AccessController : ControllerBase
    {
        /// <summary>سطر خام SALA_DTL — نام کاربری هنوز کدشده است.</summary>
        private sealed class Pay2AclUserRow
        {
            public int UserId { get; set; }
            public string? UserName { get; set; }
        }

        private readonly IPay2AccessService _accessService;
        private readonly IDatabaseService _db;

        public Pay2AccessController(IPay2AccessService accessService, IDatabaseService db)
        {
            _accessService = accessService;
            _db = db;
        }

        private int GetCurrentUserCo() => int.Parse(User.FindFirst(BaseknowClaimTypes.IDD)?.Value ?? "0");

        // عمداً بدون [Pay2Authorize]: هر کاربر لاگین‌کرده باید بتواند دسترسی‌های خودش را
        // بخواند تا رابط کاربری بداند چه چیزی را نمایش دهد. فقط اطلاعات خودِ کاربر
        // برگردانده می‌شود و [Authorize] سطح کنترلر همچنان اعمال است.
        [HttpGet("me")]
        public async Task<IActionResult> GetMyAccess()
        {
            var access = await _accessService.GetAccessAsync(GetCurrentUserCo());
            return Ok(access);
        }

        [HttpGet("forms")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<IActionResult> GetForms()
        {
            // بدون شرط COST!_، مدیر هرگز فرم‌های ماژول بستن ماه را در این
            // گرید نمی‌بیند و راهی برای دادن دسترسی به آن‌ها ندارد — حتی به
            // خودش. IsAction هم روی COST_ACT_% هم بررسی می‌شود تا اعمال حساس
            // آن ماژول در همان بخشی نمایش داده شوند که PAY2_ACT_* هست.
            string sql = "SELECT FORMNAME as FormName, CAPTION as Caption, CASE WHEN FORMNAME LIKE 'PAY2_ACT_%' OR FORMNAME LIKE 'COST_ACT_%' THEN 1 ELSE 0 END as IsAction FROM dbo.TFORMS WHERE FORMNAME LIKE N'PAY2!_%' ESCAPE N'!' OR FORMNAME LIKE N'COST!_%' ESCAPE N'!' ORDER BY IsAction, CAPTION;";
            var res = await _db.DoGetDataSQLAsync<dynamic>(sql);
            return Ok(res);
        }

        /// <summary>
        /// همگام‌سازیِ فرم‌های ماژول بستن ماه با TFORMS — معادلِ زنده‌ی بخشِ
        /// «ثبت فرم‌ها در TFORMS»یِ 11-seed-data.sql، بدون نیاز به SSMS.
        ///
        /// چرا لازم است: هربار یک عملیاتِ حساسِ تازه (یک ثابتِ جدید در
        /// CostForms.cs) اضافه می‌شود، ردیفِ متناظرش در TFORMS فقط با اجرای
        /// دستیِ اسکریپت ساخته می‌شود — چیزی که روی این پایگاه‌داده یک‌بار
        /// فراموش شد و «بازسازی سند حواله خروج مواد» تا کشف‌شدن، برای هیچ
        /// کاربری قابل‌دسترسی نبود، بدون هیچ خطای واضحی. این اندپوینت همان
        /// فهرست را (منبعِ حقیقتِ CostForms.cs) با TFORMS مقایسه می‌کند و
        /// هرچه جا افتاده را اضافه می‌کند — ایمن برای اجرای مکرر.
        /// </summary>
        [HttpPost("sync-cost-forms")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<IActionResult> SyncCostForms()
        {
            var expected = new (string FormName, string Caption)[]
            {
                (CostForms.Dashboard,  "داشبورد بستن ماه بهای تمام‌شده"),
                (CostForms.Run,        "پیشرفت اجرای بستن ماه"),
                (CostForms.Exceptions, "مغایرت‌های بستن ماه"),
                (CostForms.Variance,   "تصمیم انحراف"),
                (CostForms.Conversion, "هزینه تبدیل"),
                (CostForms.Margin,     "سود و زیان کالا"),
                (CostForms.History,    "سوابق اجراها"),
                (CostForms.Settings,   "تنظیمات بستن ماه"),
                (CostForms.ActStart,           "شروع اجرای بستن ماه"),
                (CostForms.ActAutoFix,         "اصلاح خودکار داده"),
                (CostForms.ActResolve,         "بستن استثنا"),
                (CostForms.ActDecide,          "ثبت تصمیم انحراف"),
                (CostForms.ActApplyRate,       "اعمال ضریب تعدیل"),
                (CostForms.ActRollup,          "اجرای موتور نرخ"),
                (CostForms.ActRollback,        "بازگردانی از اسنپ‌شات"),
                (CostForms.ActApprove,         "تأیید نهایی و قفل ماه"),
                (CostForms.ActExport,          "خروجی اکسل"),
                (CostForms.ActRebuildDocs,     "بازسازی سند حواله خروج مواد"),
                (CostForms.ActPostCorrection,  "سند اصلاحی مغایرت کارت انبار/حسابداری"),
            };

            var existing = (await _db.DoGetDataSQLAsync<string>(
                "SELECT FORMNAME FROM dbo.TFORMS WHERE FORMNAME LIKE N'COST!_%' ESCAPE N'!';"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var added = new List<string>();
            foreach (var f in expected)
            {
                if (existing.Contains(f.FormName)) continue;

                await _db.DoExecuteSQLAsync(
                    @"INSERT INTO dbo.TFORMS (FORMNAME, CAPTION, kind, GRP, IDH, CRT)
                      VALUES (@FormName, @Caption, 3, 10, (SELECT ISNULL(MAX(IDH),0)+1 FROM dbo.TFORMS), GETDATE());",
                    new { f.FormName, f.Caption });
                added.Add(f.FormName);
            }

            return Ok(new { added, addedCount = added.Count, alreadyPresent = expected.Length - added.Count });
        }

        /// <summary>
        /// همه‌ی کارگاه‌های فعال — بدون اعمال محدوده‌ی کارگاهیِ خودِ درخواست‌دهنده.
        ///
        /// چرا جدا از api/pay2/workshops: آن اندپوینت (درست) فقط کارگاه‌های مجازِ
        /// کاربر را برمی‌گرداند. ولی صفحه‌ی «مدیریت دسترسی‌ها» جایی است که همین
        /// محدوده تعیین می‌شود؛ اگر آن هم محدود باشد، مدیری که هنوز هیچ کارگاهی
        /// ندارد فهرست خالی می‌بیند و هرگز نمی‌تواند به کسی — از جمله خودش —
        /// کارگاه بدهد. یعنی روشن کردن کنترل دسترسی سیستم را قفل می‌کند.
        ///
        /// این استثنا فقط پشت PAY2_ADMIN_ACL باز است و چیزی جز نام و شناسه‌ی
        /// کارگاه‌ها برنمی‌گرداند.
        /// </summary>
        [HttpGet("workshops")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<IActionResult> GetAssignableWorkshops()
        {
            const string sql = @"
SELECT WS_ID, WS_NAME
FROM dbo.PAY2_WORKSHOP
WHERE IS_ACTIVE = 1
ORDER BY WS_ID;";

            var rows = await _db.DoGetDataSQLAsync<dynamic>(sql);
            return Ok(rows);
        }

        [HttpGet("users")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<IActionResult> GetUsers()
        {
            // SAL_NAME در دیتابیس کدشده ذخیره می‌شود (نرم‌افزار حسابداری WPF با
            // CODESAL هر بایت cp1256 را ۲۰ واحد کم می‌کند). بدون رمزگشایی، این
            // صفحه رشته‌های نامفهوم مثل «MeeQ^Q³TM_QYU:» نشان می‌دهد.
            // CL_METHODS.DECODEUN دقیقاً معکوس همان عملیات است (+۲۰ روی هر بایت).
            //
            // عمداً بدون فیلتر ENABL: این صفحه برای تنظیم دسترسیِ همه‌ی کاربران
            // است، نه فقط کاربرانی که در این لحظه می‌توانند وارد شوند. ENABL=0
            // در بقیه‌ی برنامه (ورود، LookupController) یعنی «فعال»، ولی این
            // قرارداد اینجا اهمیتی ندارد — مدیر باید بتواند برای هر کاربری، چه
            // فعال چه غیرفعال، از پیش دسترسی تنظیم کند.
            const string sql = "SELECT IDD as UserId, SAL_NAME as UserName FROM dbo.SALA_DTL ORDER BY SAL_NAME;";
            var rows = await _db.DoGetDataSQLAsync<Pay2AclUserRow>(sql);

            var users = rows.Select(u => new
            {
                u.UserId,
                UserName = string.IsNullOrWhiteSpace(u.UserName)
                    ? string.Empty
                    // FixPersianChars مثل LookupController: حروف عربی ي/ك را به معادل
                    // فارسی ی/ک تبدیل می‌کند تا نمایش با بقیه‌ی برنامه یکدست باشد.
                    : CL_METHODS.DECODEUN(u.UserName).FixPersianChars()
            });

            return Ok(users);
        }

        [HttpGet("user/{userCo}")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<IActionResult> GetUserAccess(int userCo)
        {
            var access = await _accessService.GetAccessAsync(userCo);
            // Even if acl is off, the admin needs the actual DB state, so we query directly or use the DTO if AclEnforced=true.
            // But if kill switch is off, GetAccessAsync returns fake data. So we should query actual raw data for admin UI.

            // مثل GetForms، بدون COST!_ فرم‌های ماژول بستن ماه در گرید ویرایش
            // دسترسیِ این کاربر اصلاً دیده نمی‌شوند.
            string sqlForms = "SELECT F.FORMNAME as FormName, F.CAPTION as Caption, CAST(ISNULL(SC.[RUN],0) AS BIT) AS [Run], CAST(ISNULL(SC.[SEE],0) AS BIT) AS [See], CAST(ISNULL(SC.[INP],0) AS BIT) AS [Inp], CAST(ISNULL(SC.[UPD],0) AS BIT) AS [Upd], CAST(ISNULL(SC.[DEL],0) AS BIT) AS [Del] FROM dbo.TFORMS F LEFT JOIN dbo.SAL_CHEK SC ON SC.[OBJECT] = F.IDH AND SC.USERCO = @userCo WHERE F.FORMNAME LIKE N'PAY2!_%' ESCAPE N'!' OR F.FORMNAME LIKE N'COST!_%' ESCAPE N'!';";
            var forms = await _db.DoGetDataSQLAsync<Pay2FormPermDto>(sqlForms, new { userCo });

            string sqlWs = "SELECT WS_ID FROM dbo.PAY2_USER_WS WHERE USERCO = @userCo";
            var wsIds = await _db.DoGetDataSQLAsync<int>(sqlWs, new { userCo });

            return Ok(new {
                UserCo = userCo,
                Forms = forms,
                AllowedWorkshopIds = wsIds
            });
        }

        [HttpPost("user/{userCo}/save")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<IActionResult> SaveUserAccess(int userCo, [FromBody] Pay2AccessSaveRequest req)
        {
            int currentUserCo = GetCurrentUserCo();

            // Check self-lockout
            if (userCo == currentUserCo)
            {
                var adminForm = req.Forms.FirstOrDefault(f => f.FormName == Pay2Forms.AdminAcl);
                if (adminForm == null || !adminForm.Run)
                {
                    return BadRequest("شما نمی‌توانید دسترسی «مدیریت دسترسی‌ها» را از حساب کاربری خودتان حذف کنید.");
                }
            }

            await _db.ExecuteInTransactionAsync(async (conn, tran) =>
            {
                // Delete existing payroll + cost-close form perms — باید با
                // فرم‌هایی که GetUserAccess به گرید فرستاده (PAY2 و COST هر دو)
                // یکی باشد، وگرنه ذخیره نمی‌تواند تیک برداشته‌شده از یک فرم
                // COST_* را واقعاً پاک کند.
                await Dapper.SqlMapper.ExecuteAsync(conn, @"
DELETE FROM dbo.SAL_CHEK
WHERE USERCO = @userCo
  AND [OBJECT] IN (SELECT IDH FROM dbo.TFORMS
                    WHERE FORMNAME LIKE N'PAY2!_%' ESCAPE N'!'
                       OR FORMNAME LIKE N'COST!_%' ESCAPE N'!')",
                new { userCo }, tran);

                // Insert new form perms in bulk
                var validForms = req.Forms.Where(f => f.Run || f.See || f.Inp || f.Upd || f.Del).ToList();
                if (validForms.Count > 0)
                {
                    string ins = @"
INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], CRT)
SELECT @userCo, IDH, @run, @see, @inp, @upd, @del, GETDATE()
FROM dbo.TFORMS WHERE FORMNAME = @formName";
                    var formParams = validForms.Select(f => new
                    {
                        userCo,
                        formName = f.FormName,
                        run = f.Run ? 1 : 0,
                        see = f.See ? 1 : 0,
                        inp = f.Inp ? 1 : 0,
                        upd = f.Upd ? 1 : 0,
                        del = f.Del ? 1 : 0
                    });
                    await Dapper.SqlMapper.ExecuteAsync(conn, ins, formParams, tran);
                }

                // Delete existing WS scope
                await Dapper.SqlMapper.ExecuteAsync(conn, "DELETE FROM dbo.PAY2_USER_WS WHERE USERCO = @userCo", new { userCo }, tran);

                // Insert new WS scope in bulk
                if (req.AllowedWorkshopIds.Count > 0)
                {
                    var wsParams = req.AllowedWorkshopIds.Select(ws => new { userCo, ws });
                    await Dapper.SqlMapper.ExecuteAsync(conn, "INSERT INTO dbo.PAY2_USER_WS (USERCO, WS_ID, CRT) VALUES (@userCo, @ws, GETDATE())", wsParams, tran);
                }

                // Audit
                await Dapper.SqlMapper.ExecuteAsync(conn, @"
INSERT INTO dbo.PAY2_SEC_AUDIT (USERCO, FORM_NAME, PERM_FLAG, ALLOWED, DETAILS, CRT)
VALUES (@currentUserCo, 'PAY2_ADMIN_ACL', 'Run', 1, 'Updated ACL for user ' + CAST(@userCo as nvarchar), GETDATE())",
                new { currentUserCo, userCo }, tran);
            });

            await _accessService.InvalidateAsync(userCo);
            return Ok();
        }

        [HttpPost("user/{userCo}/preset/{presetKey}")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<IActionResult> ApplyPreset(int userCo, string presetKey)
        {
            int currentUserCo = GetCurrentUserCo();
            if (userCo == currentUserCo && presetKey == "NONE")
                return BadRequest("شما نمی‌توانید دسترسی «مدیریت دسترسی‌ها» را از حساب کاربری خودتان حذف کنید.");

            if (presetKey == "NONE")
            {
                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await Dapper.SqlMapper.ExecuteAsync(conn, @"
DELETE FROM dbo.SAL_CHEK
WHERE USERCO = @userCo
  AND [OBJECT] IN (SELECT IDH FROM dbo.TFORMS
                    WHERE FORMNAME LIKE N'PAY2!_%' ESCAPE N'!'
                       OR FORMNAME LIKE N'COST!_%' ESCAPE N'!')", new { userCo }, tran);
                    await Dapper.SqlMapper.ExecuteAsync(conn, "DELETE FROM dbo.PAY2_USER_WS WHERE USERCO = @userCo", new { userCo }, tran);
                });
            }
            else
            {
                string formSql = @"
SELECT FORMNAME, CAPTION,
       CASE WHEN FORMNAME LIKE 'PAY2_ACT_%' OR FORMNAME LIKE 'COST_ACT_%' THEN 1 ELSE 0 END as IsAction
FROM dbo.TFORMS
WHERE FORMNAME LIKE N'PAY2!_%' ESCAPE N'!'
   OR FORMNAME LIKE N'COST!_%' ESCAPE N'!';";
                var forms = await _db.DoGetDataSQLAsync<dynamic>(formSql);

                var newForms = new List<Pay2FormPermDto>();
                foreach(var f in forms)
                {
                    string formName = f.FORMNAME;
                    bool isAction = f.IsAction == 1;

                    bool r = false, s = false, i = false, u = false, d = false;

                    if (presetKey == "PAYROLL_MANAGER")
                    {
                        if (formName != Pay2Forms.AdminAcl) { r=true; s=true; i=true; u=true; d=true; }
                    }
                    else if (presetKey == "PAYROLL_OFFICER")
                    {
                        if (formName == Pay2Forms.Employee || formName == Pay2Forms.Decree || formName == Pay2Forms.Attendance || formName == Pay2Forms.Advance || formName == Pay2Forms.Loan)
                            { r=true; s=true; i=true; u=true; d=true; }
                        else if (formName == Pay2Forms.Run) { r=true; s=true; i=true; }
                        else if (formName == Pay2Forms.Reports) { r=true; s=true; }
                        else if (formName == Pay2Forms.ActCalc || formName == Pay2Forms.ActViewAmounts || formName == Pay2Forms.ActPeriodClose || formName == Pay2Forms.ActExport) { r=true; }
                    }
                    else if (presetKey == "HR_OFFICER")
                    {
                        if (formName == Pay2Forms.Employee || formName == Pay2Forms.Decree) { r=true; s=true; i=true; u=true; d=true; }
                        else if (formName == Pay2Forms.Attendance) { r=true; s=true; i=true; u=true; }
                        else if (formName == Pay2Forms.Dashboard) { r=true; s=true; }
                        else if (formName == Pay2Forms.ActDecreeConfirm) { r=true; }
                    }
                    else if (presetKey == "ACCOUNTANT")
                    {
                        if (formName == Pay2Forms.Run || formName == Pay2Forms.Reports || formName == Pay2Forms.Dashboard || formName.StartsWith("COST_")) { r=true; s=true; i=true; u=true; }
                        else if (formName == Pay2Forms.ActDeed || formName == Pay2Forms.ActDeedUndo || formName == Pay2Forms.ActViewAmounts || formName == Pay2Forms.ActExport) { r=true; }
                    }
                    else if (presetKey == "VIEWER")
                    {
                        if (!isAction) { r=true; s=true; }
                    }

                    if (r || s || i || u || d)
                    {
                        newForms.Add(new Pay2FormPermDto { FormName = formName, Caption = f.CAPTION, Run=r, See=s, Inp=i, Upd=u, Del=d });
                    }
                }

                await _db.ExecuteInTransactionAsync(async (conn, tran) =>
                {
                    await Dapper.SqlMapper.ExecuteAsync(conn, @"
DELETE FROM dbo.SAL_CHEK
WHERE USERCO = @userCo
  AND [OBJECT] IN (SELECT IDH FROM dbo.TFORMS
                    WHERE FORMNAME LIKE N'PAY2!_%' ESCAPE N'!'
                       OR FORMNAME LIKE N'COST!_%' ESCAPE N'!')", new { userCo }, tran);

                    if (newForms.Count > 0)
                    {
                        string ins = @"INSERT INTO dbo.SAL_CHEK (USERCO, [OBJECT], [RUN], [SEE], [INP], [UPD], [DEL], CRT)
                                       SELECT @userCo, IDH, @run, @see, @inp, @upd, @del, GETDATE() FROM dbo.TFORMS WHERE FORMNAME = @formName";
                        var formParams = newForms.Select(f => new
                        {
                            userCo,
                            formName = f.FormName,
                            run = f.Run ? 1 : 0,
                            see = f.See ? 1 : 0,
                            inp = f.Inp ? 1 : 0,
                            upd = f.Upd ? 1 : 0,
                            del = f.Del ? 1 : 0
                        });
                        await Dapper.SqlMapper.ExecuteAsync(conn, ins, formParams, tran);
                    }

                    // اگر کاربر هیچ کارگاهی اختصاص داده نشده دارد یا پریست PAYROLL_MANAGER اعمال شده، همه کارگاه‌های فعال را اختصاص می‌دهیم
                    // تا کاربر قفل نشود و لیست کارگاه‌هایش خالی نماند.
                    string checkWsSql = "SELECT COUNT(*) FROM dbo.PAY2_USER_WS WHERE USERCO = @userCo";
                    int currentWsCount = await Dapper.SqlMapper.ExecuteScalarAsync<int>(conn, checkWsSql, new { userCo }, tran);

                    if (currentWsCount == 0 || presetKey == "PAYROLL_MANAGER")
                    {
                        await Dapper.SqlMapper.ExecuteAsync(conn, "DELETE FROM dbo.PAY2_USER_WS WHERE USERCO = @userCo", new { userCo }, tran);
                        string insWs = @"
INSERT INTO dbo.PAY2_USER_WS (USERCO, WS_ID, CRT)
SELECT @userCo, WS_ID, GETDATE() FROM dbo.PAY2_WORKSHOP WHERE IS_ACTIVE = 1";
                        await Dapper.SqlMapper.ExecuteAsync(conn, insWs, new { userCo }, tran);
                    }
                });
            }

            await _accessService.InvalidateAsync(userCo);
            return Ok();
        }

        [HttpGet("audit")]
        [Pay2Authorize(Pay2Forms.AdminAcl, Pay2Perm.Run)]
        public async Task<IActionResult> GetAuditLogs([FromQuery] int userCo, [FromQuery] int page = 1)
        {
            int offset = (page - 1) * 50;
            const string sql = @"
SELECT
    A.USERCO        AS UserCo,
    A.USER_NAME     AS UserName,
    A.FORM_NAME     AS FormName,
    ISNULL(F.CAPTION, A.FORM_NAME) AS FormCaption,
    A.PERM_FLAG     AS PermFlag,
    A.WS_ID         AS WsId,
    A.ENTITY_KEY    AS EntityKey,
    A.ALLOWED       AS Allowed,
    A.HTTP_METHOD   AS HttpMethod,
    A.PATH          AS Path,
    A.IP            AS Ip,
    A.DETAILS       AS Details,
    A.CRT           AS CRT
FROM dbo.PAY2_SEC_AUDIT A
LEFT JOIN dbo.TFORMS F ON F.FORMNAME = A.FORM_NAME
WHERE A.USERCO = @userCo
ORDER BY A.CRT DESC
OFFSET @offset ROWS FETCH NEXT 50 ROWS ONLY;";

            var logs = await _db.DoGetDataSQLAsync<dynamic>(sql, new { userCo, offset });
            return Ok(logs);
        }
    }

    public class Pay2AccessSaveRequest
    {
        public List<Pay2FormPermDto> Forms { get; set; } = new();
        public List<int> AllowedWorkshopIds { get; set; } = new();
    }
}
