using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Crm;
using Safir.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CrmController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ICrmAccessService _access;
        private readonly ILogger<CrmController> _logger;

        public CrmController(IDatabaseService dbService, ICrmAccessService access, ILogger<CrmController> logger)
        {
            _dbService = dbService;
            _access = access;
            _logger = logger;
        }

        private string GetCurrentUserName()
        {
            return User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        }

        /// <summary>
        /// دسترسی کاربر جاری. اگر کِلِیم هویت نبود یا عدد نبود، null برمی‌گرداند
        /// و فراخواننده باید 401 بدهد.
        ///
        /// اینجا عمداً هیچ مقدار پیش‌فرضی جایگزین نمی‌شود. نسخه‌ی قبلی در چنین
        /// حالتی بی‌صدا کاربر را «۷۸» فرض می‌کرد، یعنی هر خطای احراز هویت به
        /// داده‌ی یک شخص واقعی دسترسی می‌داد.
        /// </summary>
        private async Task<CrmAccessDto?> TryGetAccessAsync()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId) || userId <= 0)
            {
                _logger.LogWarning("CRM: درخواست بدون کِلِیم هویت معتبر رد شد.");
                return null;
            }

            return await _access.GetAccessAsync(userId, GetCurrentUserName());
        }

        private ActionResult Unauthenticated() =>
            Unauthorized("هویت کاربر قابل تشخیص نیست. لطفاً دوباره وارد شوید.");

        /// <summary>
        /// شرط مالکیت روی COPMANES.
        ///
        /// حالت دوم (userid تهی + تطبیق USER_NAME) برای رکوردهایی است که
        /// نرم‌افزار WPF می‌سازد و <c>userid</c> نمی‌گذارد ولی <c>USER_NAME</c>
        /// را پر می‌کند. بررسی داده‌ی مشتری نشان داد این نگاشت یک‌به‌یک است،
        /// پس تطبیق با نام امن است و رکوردهای آینده‌ی WPF را هم پوشش می‌دهد.
        /// </summary>
        private static string OwnCompanyPredicate(string alias) =>
            $" AND ({alias}.userid = @AclUserId OR ({alias}.userid IS NULL AND {alias}.USER_NAME = @AclUserName)) ";

        /// <summary>
        /// شرط مالکیت روی CRMEVENTS. رویدادها USER_NAME ندارند؛ معادلش SALER است
        /// که در داده‌ی موجود دقیقاً همان نام کاربری است.
        /// </summary>
        private static string OwnEventPredicate(string alias) =>
            $" AND ({alias}.USERID = @AclUserId OR ({alias}.USERID IS NULL AND {alias}.SALER = @AclUserName)) ";

        private static void AddAclParameters(DynamicParameters parameters, CrmAccessDto acl)
        {
            parameters.Add("AclUserId", acl.UserId);
            parameters.Add("AclUserName", acl.UserName);
        }

        private async Task<bool> CanAccessCompanyAsync(CrmAccessDto acl, int companyId)
        {
            if (!acl.RestrictToOwn) return true;

            var sql = "SELECT COUNT(1) FROM dbo.COPMANES C WITH (NOLOCK) WHERE C.ID = @Id"
                      + OwnCompanyPredicate("C");

            var count = (await _dbService.DoGetDataSQLAsync<int>(
                sql, new { Id = companyId, AclUserId = acl.UserId, AclUserName = acl.UserName })).FirstOrDefault();

            return count > 0;
        }

        private async Task<bool> CanAccessEventAsync(CrmAccessDto acl, int eventId)
        {
            if (!acl.RestrictToOwn) return true;

            var sql = "SELECT COUNT(1) FROM dbo.CRMEVENTS E WITH (NOLOCK) WHERE E.idde = @Id"
                      + OwnEventPredicate("E");

            var count = (await _dbService.DoGetDataSQLAsync<int>(
                sql, new { Id = eventId, AclUserId = acl.UserId, AclUserName = acl.UserName })).FirstOrDefault();

            return count > 0;
        }

        private ActionResult Denied() =>
            StatusCode(403, "این رکورد متعلق به کاربر دیگری است و به آن دسترسی ندارید.");

        /// <summary>
        /// وضعیت دسترسی کاربر جاری — کلاینت با این تصمیم می‌گیرد چک‌باکس
        /// «مشتریان من» را نشان بدهد یا نه.
        /// </summary>
        [HttpGet("access")]
        public async Task<ActionResult<CrmAccessDto>> GetAccess()
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();
            return Ok(acl);
        }

        [HttpGet("status-list")]
        public async Task<ActionResult<List<CrmStatusDto>>> GetStatusList()
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                var sazman = (await _dbService.DoGetDataSQLAsync<dynamic>("SELECT IT1, IT2, IT3, IT4, IT5, IT6, IT7, IT8, IT9 FROM SAZMAN")).FirstOrDefault();
                var result = new List<CrmStatusDto>();

                if (sazman != null)
                {
                    var dict = (IDictionary<string, object>)sazman;
                    for (int i = 1; i <= 9; i++)
                    {
                        var key = $"IT{i}";
                        var name = dict.ContainsKey(key) && dict[key] != null ? dict[key].ToString() : $"وضعیت {i}";
                        result.Add(new CrmStatusDto { Code = i, Name = name ?? $"وضعیت {i}" });
                    }
                }
                else
                {
                    for (int i = 1; i <= 9; i++)
                    {
                        result.Add(new CrmStatusDto { Code = i, Name = $"وضعیت {i}" });
                    }
                }

                // گرفتن تعداد از COPMANES برای هر وضعیت
                // بدون شرط مالکیت، این شمارنده‌ها جمع کل کار همه‌ی کاربران را
                // لو می‌دهند — حتی وقتی خود لیست شرکت‌ها محدود شده است.
                var countSql = "SELECT C.STATUS, COUNT(1) AS CNT FROM COPMANES C WITH (NOLOCK) WHERE 1=1 ";
                var countParams = new DynamicParameters();
                if (acl.RestrictToOwn)
                {
                    countSql += OwnCompanyPredicate("C");
                    AddAclParameters(countParams, acl);
                }
                countSql += " GROUP BY C.STATUS";

                var counts = (await _dbService.DoGetDataSQLAsync<dynamic>(countSql, countParams)).ToList();
                foreach (var st in result)
                {
                    var match = counts.FirstOrDefault(c => (int?)c.STATUS == st.Code);
                    if (match != null)
                    {
                        st.Count = (int)match.CNT;
                    }
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading CRM status list");
                return StatusCode(500, "خطا در بارگذاری لیست وضعیت‌های CRM");
            }
        }

        [HttpPost("companies")]
        public async Task<ActionResult<List<CrmCompanyDto>>> GetCompanies([FromBody] CrmFilterDto filter)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                var statusList = await GetStatusListInternal();

                var sql = @"
                    SELECT
                        C.*,
                        ISNULL(E.idcn, 0) AS IDCN
                    FROM COPMANES C
                    LEFT OUTER JOIN (
                        SELECT idc, COUNT(1) AS idcn
                        FROM CRMEVENTS
                        GROUP BY idc
                    ) E ON C.id = E.idc
                    WHERE 1=1 ";

                var parameters = new DynamicParameters();

                // وقتی محدودیت فعال است، فیلتر مالکیت اجباری است و
                // OnlyMyCompanies دیگر نقشی ندارد — کلاینت نمی‌تواند با
                // برداشتن تیک، داده‌ی بقیه را ببیند.
                if (acl.RestrictToOwn)
                {
                    sql += OwnCompanyPredicate("C");
                    AddAclParameters(parameters, acl);
                }
                else if (filter.OnlyMyCompanies)
                {
                    sql += " AND (C.userid = @UserId OR C.userid IS NULL) ";
                    parameters.Add("UserId", acl.UserId);
                }

                if (filter.Status.HasValue && filter.Status.Value > 0)
                {
                    sql += " AND C.STATUS = @Status ";
                    parameters.Add("Status", filter.Status.Value);
                }

                if (!string.IsNullOrWhiteSpace(filter.City))
                {
                    sql += " AND C.CITY LIKE @City ";
                    parameters.Add("City", $"%{filter.City.Trim()}%");
                }

                if (!string.IsNullOrWhiteSpace(filter.StatusFact))
                {
                    sql += " AND C.STATUS_FACT = @StatusFact ";
                    parameters.Add("StatusFact", filter.StatusFact.Trim());
                }

                if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
                {
                    var term = filter.SearchTerm.Trim();
                    sql += @" AND (
                        C.COMPANY_NAME LIKE @Term OR
                        C.MANAGER LIKE @Term OR
                        C.FACT_TEL LIKE @Term OR
                        C.MOBILE LIKE @Term OR
                        C.ADDR LIKE @Term OR
                        C.COMMENT LIKE @Term
                    ) ";
                    parameters.Add("Term", $"%{term}%");
                }

                if (filter.OnlyWithUpcomingFollowUps)
                {
                    int todayInt = int.TryParse(CL_Tarikh.Current_FullDate, out var td) ? td : 0;
                    sql += @" AND EXISTS (
                        SELECT 1 FROM CRMEVENTS EV
                        WHERE EV.idc = C.id AND EV.NEXT_DATE >= @TodayInt
                    ) ";
                    parameters.Add("TodayInt", todayInt);
                }

                sql += " ORDER BY C.id DESC ";

                var companies = (await _dbService.DoGetDataSQLAsync<CrmCompanyDto>(sql, parameters)).ToList();

                // اضافه کردن عنوان وضعیت
                foreach (var c in companies)
                {
                    if (c.STATUS.HasValue && statusList.TryGetValue(c.STATUS.Value, out var stName))
                    {
                        c.StatusTitle = stName;
                    }
                }

                return Ok(companies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CRM companies");
                return StatusCode(500, "خطا در دریافت لیست شرکت‌های CRM");
            }
        }

        [HttpGet("companies/{id}")]
        public async Task<ActionResult<CrmCompanyDto>> GetCompanyById(int id)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                var sql = "SELECT * FROM COPMANES WHERE ID = @Id";
                var company = await _dbService.DoGetDataSQLAsyncSingle<CrmCompanyDto>(sql, new { Id = id });
                if (company == null) return NotFound("شرکت مورد نظر یافت نشد.");

                // بدون این چک، فیلترِ لیست فقط آرایشی است: کافی است کسی
                // شماره‌ی رکورد را حدس بزند.
                if (!await CanAccessCompanyAsync(acl, id)) return Denied();

                var statusList = await GetStatusListInternal();
                if (company.STATUS.HasValue && statusList.TryGetValue(company.STATUS.Value, out var stName))
                {
                    company.StatusTitle = stName;
                }

                return Ok(company);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CRM company by id {Id}", id);
                return StatusCode(500, "خطا در دریافت اطلاعات شرکت");
            }
        }

        [HttpPost("save-company")]
        public async Task<ActionResult<int>> SaveCompany([FromBody] CrmCompanyDto company)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                bool isUpdate = company.ID != null && company.ID > 0;

                if (isUpdate && !await CanAccessCompanyAsync(acl, company.ID!.Value))
                    return Denied();

                if (company.DATE_SABT == null || company.DT == null)
                {
                    var today = CL_Tarikh.Current_FullDate;
                    company.DT = company.DT ?? (int.TryParse(today, out var dtVal) ? dtVal : null);
                    company.DATE_SABT = company.DATE_SABT ?? DateTime.Now;
                }

                // مالکیت را سرور تعیین می‌کند، نه بدنه‌ی درخواست. نسخه‌ی قبلی
                // USERID را از کلاینت می‌پذیرفت، یعنی می‌شد رکورد را به نام
                // شخص دیگری ثبت کرد یا رکورد دیگری را به نام خود برداشت.
                if (!isUpdate)
                {
                    company.USERID = acl.UserId;
                    company.USER_NAME = acl.UserName;
                }
                else
                {
                    // در ویرایش، مالک اصلی حفظ می‌شود تا کاربری که مجوز
                    // «مشاهده همه» دارد با یک ذخیره‌ی ساده رکورد را به نام
                    // خودش نکند.
                    var owner = await _dbService.DoGetDataSQLAsyncSingle<CrmCompanyOwner>(
                        "SELECT userid AS USERID, USER_NAME FROM dbo.COPMANES WHERE ID = @Id",
                        new { Id = company.ID!.Value });

                    company.USERID = owner?.USERID;
                    company.USER_NAME = owner?.USER_NAME;

                    // رکوردهای ساخته‌شده با WPF فقط USER_NAME دارند و userid
                    // ندارند. آن حالت عمداً دست‌نخورده می‌ماند، وگرنه ویرایشِ
                    // یک مدیر، رکورد را بی‌صدا از مالک واقعی‌اش می‌گرفت.
                    // فقط رکوردی که هیچ نشانی از مالک ندارد به کاربر جاری
                    // نسبت داده می‌شود.
                    if (company.USERID == null && string.IsNullOrWhiteSpace(company.USER_NAME))
                    {
                        company.USERID = acl.UserId;
                        company.USER_NAME = acl.UserName;
                    }
                }

                company.STATUS = company.STATUS ?? 1;

                var baseParams = new
                {
                    company.COMPANY_NAME,
                    company.CITY,
                    company.MANAGER,
                    company.FACT_TEL,
                    company.MOBILE,
                    company.PERNUM,
                    company.STATUS_FACT,
                    company.PRODUCTS,
                    company.ADDR,
                    company.ACCOUNTANT,
                    company.SOFTWARE,
                    company.ESP_PERSON,
                    company.REAGENT,
                    company.STATUS,
                    company.COMMENT,
                    DATE_SABT = company.DATE_SABT,
                    company.USER_NAME,
                    company.PIC,
                    company.DT,
                    company.USERID,
                    company.LONGITUDE,
                    company.LATITUDE,
                    company.OSTANID,
                    company.SHAHRID
                };

                if (!isUpdate)
                {
                    var sql = @"
                        INSERT INTO COPMANES (
                            COMPANY_NAME, CITY, MANAGER, FACT_TEL, MOBILE, PERNUM, STATUS_FACT, PRODUCTS,
                            ADDR, ACCOUNTANT, SOFTWARE, ESP_PERSON, REAGENT, STATUS, COMMENT, date_sabt,
                            USER_NAME, pic, dt, userid, Longitude, Latitude, OSTANID, SHAHRID
                        )
                        VALUES (
                            @COMPANY_NAME, @CITY, @MANAGER, @FACT_TEL, @MOBILE, @PERNUM, @STATUS_FACT, @PRODUCTS,
                            @ADDR, @ACCOUNTANT, @SOFTWARE, @ESP_PERSON, @REAGENT, @STATUS, @COMMENT, @DATE_SABT,
                            @USER_NAME, @PIC, @DT, @USERID, @LONGITUDE, @LATITUDE, @OSTANID, @SHAHRID
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

                    var newId = (await _dbService.DoGetDataSQLAsync<int>(sql, baseParams)).FirstOrDefault();
                    return Ok(newId);
                }
                else
                {
                    var sql = @"
                        UPDATE COPMANES SET
                            COMPANY_NAME=@COMPANY_NAME, CITY=@CITY, MANAGER=@MANAGER, FACT_TEL=@FACT_TEL,
                            MOBILE=@MOBILE, PERNUM=@PERNUM, STATUS_FACT=@STATUS_FACT, PRODUCTS=@PRODUCTS,
                            ADDR=@ADDR, ACCOUNTANT=@ACCOUNTANT, SOFTWARE=@SOFTWARE, ESP_PERSON=@ESP_PERSON,
                            REAGENT=@REAGENT, STATUS=@STATUS, COMMENT=@COMMENT, date_sabt=@DATE_SABT,
                            USER_NAME=@USER_NAME, pic=@PIC, dt=@DT, userid=@USERID, Longitude=@LONGITUDE,
                            Latitude=@LATITUDE, OSTANID=@OSTANID, SHAHRID=@SHAHRID
                        WHERE ID=@ID";

                    var updateParams = new DynamicParameters(baseParams);
                    updateParams.Add("ID", company.ID.Value);

                    await _dbService.DoExecuteSQLAsync(sql, updateParams);
                    return Ok(company.ID.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving CRM company");
                return StatusCode(500, "خطا در ذخیره‌سازی اطلاعات شرکت");
            }
        }

        [HttpDelete("companies/{id}")]
        public async Task<ActionResult<bool>> DeleteCompany(int id)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                if (!await CanAccessCompanyAsync(acl, id)) return Denied();

                // حذف تمام رخدادهای مرتبط ابتدا
                // (FK_CRMEVENTS_COPMANES فقط ON UPDATE CASCADE دارد، نه ON DELETE)
                await _dbService.DoExecuteSQLAsync("DELETE FROM CRMEVENTS WHERE idc = @Id", new { Id = id });
                var rows = await _dbService.DoExecuteSQLAsync("DELETE FROM COPMANES WHERE ID = @Id", new { Id = id });
                return Ok(rows > 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CRM company {Id}", id);
                return StatusCode(500, "خطا در حذف شرکت");
            }
        }

        [HttpGet("events/{companyId}")]
        public async Task<ActionResult<List<CrmEventDto>>> GetEventsByCompanyId(int companyId)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                if (!await CanAccessCompanyAsync(acl, companyId)) return Denied();

                var sql = "SELECT * FROM CRMEVENTS WHERE idc = @CompanyId ORDER BY idde DESC";
                var events = (await _dbService.DoGetDataSQLAsync<CrmEventDto>(sql, new { CompanyId = companyId })).ToList();

                var statusList = await GetStatusListInternal();
                foreach (var ev in events)
                {
                    if (ev.STATUS.HasValue && statusList.TryGetValue(ev.STATUS.Value, out var stName))
                    {
                        ev.StatusTitle = stName;
                    }
                }

                return Ok(events);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting events for company {CompanyId}", companyId);
                return StatusCode(500, "خطا در دریافت پیگیری‌ها");
            }
        }

        [HttpPost("save-event")]
        public async Task<ActionResult<int>> SaveEvent([FromBody] CrmEventDto crmEvent)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                bool isUpdate = crmEvent.IDDE != null && crmEvent.IDDE > 0;

                if (isUpdate && !await CanAccessEventAsync(acl, crmEvent.IDDE!.Value))
                    return Denied();

                if (crmEvent.IDC.HasValue && crmEvent.IDC.Value > 0 &&
                    !await CanAccessCompanyAsync(acl, crmEvent.IDC.Value))
                    return Denied();

                if (crmEvent.INFO_DATE == null || crmEvent.INFO_DATE <= 0)
                {
                    crmEvent.INFO_DATE = int.TryParse(CL_Tarikh.Current_FullDate, out var td) ? td : null;
                }

                if (crmEvent.INFO_TIME == null || crmEvent.INFO_TIME <= 0)
                {
                    var now = DateTime.Now;
                    crmEvent.INFO_TIME = (now.Hour * 100) + now.Minute;
                }

                // مثل شرکت‌ها، مالکِ رویداد را سرور تعیین می‌کند نه کلاینت.
                if (!isUpdate)
                {
                    crmEvent.USERID = acl.UserId;
                }
                else
                {
                    var owner = await _dbService.DoGetDataSQLAsyncSingle<CrmEventOwner>(
                        "SELECT USERID, SALER FROM dbo.CRMEVENTS WHERE idde = @Id",
                        new { Id = crmEvent.IDDE!.Value });

                    crmEvent.USERID = owner?.USERID;

                    // مثل شرکت‌ها: رویدادی که USERID ندارد ولی SALER دارد،
                    // مالکش همان SALER است و نباید جابه‌جا شود.
                    if (crmEvent.USERID == null && string.IsNullOrWhiteSpace(owner?.SALER))
                        crmEvent.USERID = acl.UserId;
                }

                crmEvent.CDATETI = crmEvent.CDATETI ?? DateTime.Now;

                var baseParams = new
                {
                    crmEvent.COMPANY_NAME,
                    crmEvent.INFO_DATE,
                    crmEvent.INFO_TIME,
                    crmEvent.SALER,
                    crmEvent.BUYER,
                    crmEvent.COMMENT,
                    crmEvent.NEXT_DATE,
                    crmEvent.NEXT_TIME,
                    crmEvent.STATUS,
                    crmEvent.PIC,
                    crmEvent.IDC,
                    crmEvent.PAYAM,
                    crmEvent.MITING,
                    crmEvent.USERID,
                    crmEvent.CDATETI
                };

                if (!isUpdate)
                {
                    var sql = @"
                        INSERT INTO CRMEVENTS (
                            COMPANY_NAME, INFO_DATE, INFO_TIME, SALER, BUYER, COMMENT, NEXT_DATE,
                            NEXT_TIME, STATUS, pic, idc, PAYAM, miting, USERID, CDATETI
                        )
                        VALUES (
                            @COMPANY_NAME, @INFO_DATE, @INFO_TIME, @SALER, @BUYER, @COMMENT, @NEXT_DATE,
                            @NEXT_TIME, @STATUS, @PIC, @IDC, @PAYAM, @MITING, @USERID, @CDATETI
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

                    var newId = (await _dbService.DoGetDataSQLAsync<int>(sql, baseParams)).FirstOrDefault();

                    // به‌روزرسانی وضعیت شرکت در جدول اصلی
                    if (crmEvent.IDC.HasValue && crmEvent.STATUS.HasValue)
                    {
                        await _dbService.DoExecuteSQLAsync(
                            "UPDATE COPMANES SET STATUS = @Status WHERE ID = @Id",
                            new { Status = crmEvent.STATUS.Value, Id = crmEvent.IDC.Value });
                    }

                    return Ok(newId);
                }
                else
                {
                    var sql = @"
                        UPDATE CRMEVENTS SET
                            COMPANY_NAME=@COMPANY_NAME, INFO_DATE=@INFO_DATE, INFO_TIME=@INFO_TIME,
                            SALER=@SALER, BUYER=@BUYER, COMMENT=@COMMENT, NEXT_DATE=@NEXT_DATE,
                            NEXT_TIME=@NEXT_TIME, STATUS=@STATUS, pic=@PIC, idc=@IDC, PAYAM=@PAYAM,
                            miting=@MITING, USERID=@USERID, CDATETI=@CDATETI
                        WHERE idde=@IDDE";

                    var updateParams = new DynamicParameters(baseParams);
                    updateParams.Add("IDDE", crmEvent.IDDE.Value);

                    await _dbService.DoExecuteSQLAsync(sql, updateParams);

                    if (crmEvent.IDC.HasValue && crmEvent.STATUS.HasValue)
                    {
                        await _dbService.DoExecuteSQLAsync(
                            "UPDATE COPMANES SET STATUS = @Status WHERE ID = @Id",
                            new { Status = crmEvent.STATUS.Value, Id = crmEvent.IDC.Value });
                    }

                    return Ok(crmEvent.IDDE.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving CRM event");
                return StatusCode(500, "خطا در ذخیره‌سازی پیگیری");
            }
        }

        [HttpDelete("events/{id}")]
        public async Task<ActionResult<bool>> DeleteEvent(int id)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                if (!await CanAccessEventAsync(acl, id)) return Denied();

                var rows = await _dbService.DoExecuteSQLAsync("DELETE FROM CRMEVENTS WHERE idde = @Id", new { Id = id });
                return Ok(rows > 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CRM event {Id}", id);
                return StatusCode(500, "خطا در حذف پیگیری");
            }
        }

        [HttpGet("dashboard-summary")]
        public async Task<ActionResult<CrmDashboardSummaryDto>> GetDashboardSummary()
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                int todayInt = int.TryParse(CL_Tarikh.Current_FullDate, out var td) ? td : 0;

                // داشبورد از قبل به کاربر جاری محدود بود. حالا وقتی کاربر مجوز
                // «مشاهده همه» دارد، این محدودیت برداشته می‌شود تا آمارش با
                // چیزی که در لیست می‌بیند بخواند.
                string compScope = acl.RestrictToOwn ? OwnCompanyPredicate("C") : string.Empty;
                string evScope = acl.RestrictToOwn ? OwnEventPredicate("E") : string.Empty;

                var statsSql = $@"
                    SELECT
                        (SELECT COUNT(1) FROM COPMANES C WITH(NOLOCK) WHERE 1=1 {compScope}) AS TotalCompanies,
                        (SELECT COUNT(1) FROM CRMEVENTS E WITH(NOLOCK) WHERE 1=1 {evScope}) AS TotalEvents,
                        (SELECT COUNT(1) FROM CRMEVENTS E WITH(NOLOCK) WHERE E.NEXT_DATE = @Today {evScope}) AS TodayFollowUps,
                        (SELECT COUNT(1) FROM CRMEVENTS E WITH(NOLOCK) WHERE E.NEXT_DATE < @Today AND E.NEXT_DATE > 0 {evScope}) AS OverdueFollowUps,
                        (SELECT COUNT(1) FROM CRMEVENTS E WITH(NOLOCK) WHERE (E.miting = -1 OR E.miting = 1) AND E.NEXT_DATE = @Today {evScope}) AS TodayMeetings;";

                var statsParams = new DynamicParameters();
                statsParams.Add("Today", todayInt);
                if (acl.RestrictToOwn) AddAclParameters(statsParams, acl);

                var summary = (await _dbService.DoGetDataSQLAsync<CrmDashboardSummaryDto>(
                    statsSql, statsParams)).FirstOrDefault() ?? new CrmDashboardSummaryDto();

                // لیست وضعیت‌ها و رویدادهای آینده به صورت همزمان
                var statusDict = await GetStatusListInternal();
                var statusRes = await GetStatusList();
                if (statusRes.Result is OkObjectResult okObj && okObj.Value is List<CrmStatusDto> stList)
                {
                    summary.StatusSummary = stList;
                }

                // رویدادهای آینده
                var upcomingSql = $@"
                    SELECT TOP 10
                        E.*,
                        C.COMPANY_NAME
                    FROM CRMEVENTS E WITH(NOLOCK)
                    LEFT JOIN COPMANES C WITH(NOLOCK) ON E.idc = C.id
                    WHERE E.NEXT_DATE >= @Today {evScope}
                    ORDER BY E.NEXT_DATE ASC, E.NEXT_TIME ASC";

                summary.UpcomingEvents = (await _dbService.DoGetDataSQLAsync<CrmEventDto>(
                    upcomingSql, statsParams)).ToList();

                foreach (var ev in summary.UpcomingEvents)
                {
                    if (ev.STATUS.HasValue && statusDict.TryGetValue(ev.STATUS.Value, out var stName))
                    {
                        ev.StatusTitle = stName;
                    }
                }

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CRM dashboard summary");
                return StatusCode(500, "خطا در بارگذاری خلاصه وضعیت داشبورد CRM");
            }
        }

        [HttpGet("check-duplicate")]
        public async Task<ActionResult<CrmDuplicateCheckResultDto>> CheckDuplicate(
            [FromQuery] string? companyName,
            [FromQuery] string? tel,
            [FromQuery] string? mobile,
            [FromQuery] int? excludeCompanyId = null)
        {
            try
            {
                var result = new CrmDuplicateCheckResultDto();
                var messages = new List<string>();

                // بررسی نام در مشتریان حسابداری
                if (!string.IsNullOrWhiteSpace(companyName))
                {
                    var cleanName = companyName.Trim();
                    var custCount = (await _dbService.DoGetDataSQLAsync<int>(
                        "SELECT COUNT(1) FROM cust_hesab_dtl WHERE NAME LIKE @Name OR TNAME LIKE @Name",
                        new { Name = $"%{cleanName}%" })).FirstOrDefault();

                    if (custCount > 0)
                    {
                        result.IsDuplicateNameInCustomers = true;
                        messages.Add("مشابه این نام قبلاً در لیست مشتریان اصلی تعریف شده است.");
                    }

                    var crmNameSql = "SELECT COUNT(1) FROM COPMANES WHERE COMPANY_NAME LIKE @Name";
                    if (excludeCompanyId.HasValue) crmNameSql += " AND ID <> " + excludeCompanyId.Value;

                    var crmCount = (await _dbService.DoGetDataSQLAsync<int>(
                        crmNameSql, new { Name = $"%{cleanName}%" })).FirstOrDefault();

                    if (crmCount > 0)
                    {
                        result.IsDuplicateNameInCrm = true;
                        messages.Add("مشابه این نام قبلاً در CRM ثبت شده است.");
                    }
                }

                // بررسی تلفن
                if (!string.IsNullOrWhiteSpace(tel))
                {
                    var cleanTel = tel.Trim();
                    var telCustCount = (await _dbService.DoGetDataSQLAsync<int>(
                        "SELECT COUNT(1) FROM cust_hesab WHERE TEL LIKE @Tel",
                        new { Tel = $"%{cleanTel}%" })).FirstOrDefault();

                    if (telCustCount > 0)
                    {
                        result.IsDuplicateTelInCustomers = true;
                        messages.Add("این شماره تلفن قبلاً برای یکی از طرف‌حساب‌ها ثبت شده است.");
                    }
                }

                // بررسی موبایل
                if (!string.IsNullOrWhiteSpace(mobile))
                {
                    var cleanMob = mobile.Trim();
                    var mobCustCount = (await _dbService.DoGetDataSQLAsync<int>(
                        "SELECT COUNT(1) FROM cust_hesab WHERE TEL LIKE @Mob",
                        new { Mob = $"%{cleanMob}%" })).FirstOrDefault();

                    if (mobCustCount > 0)
                    {
                        result.IsDuplicateMobileInCustomers = true;
                        messages.Add("این شماره موبایل در اطلاعات طرف‌حساب‌ها وجود دارد.");
                    }

                    var crmMobSql = "SELECT COUNT(1) FROM COPMANES WHERE MOBILE LIKE @Mob";
                    if (excludeCompanyId.HasValue) crmMobSql += " AND ID <> " + excludeCompanyId.Value;

                    var crmMobCount = (await _dbService.DoGetDataSQLAsync<int>(
                        crmMobSql, new { Mob = $"%{cleanMob}%" })).FirstOrDefault();

                    if (crmMobCount > 0)
                    {
                        result.IsDuplicateMobileInCrm = true;
                        messages.Add("این شماره موبایل قبلاً در CRM ثبت شده است.");
                    }
                }

                result.Message = messages.Count > 0 ? string.Join(" | ", messages) : null;
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking duplicates");
                return StatusCode(500, "خطا در بررسی موارد تکراری");
            }
        }

        [HttpGet("phonebook")]
        public async Task<ActionResult<List<CrmPhoneBookItemDto>>> SearchPhoneBook([FromQuery] string? query)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                var result = new List<CrmPhoneBookItemDto>();
                var term = string.IsNullOrWhiteSpace(query) ? "" : query.Trim();
                var fixTerm = term.FixPersianChars();
                var likeTerm = $"%{term}%";
                var likeFixTerm = $"%{fixTerm}%";

                // جستجو در طرف حساب ها با حذف رکوردهای بدون نام و ترتیب نام
                string custWhere;
                if (string.IsNullOrWhiteSpace(term))
                {
                    custWhere = "WHERE NAME IS NOT NULL AND RTRIM(NAME) <> ''";
                }
                else
                {
                    custWhere = @"WHERE NAME IS NOT NULL AND RTRIM(NAME) <> ''
                                  AND (NAME LIKE @LikeTerm OR NAME LIKE @LikeFixTerm
                                       OR TEL LIKE @LikeTerm OR MOBILE LIKE @LikeTerm
                                       OR hes LIKE @LikeTerm OR ADDRESS LIKE @LikeTerm)";
                }

                var custSql = $@"
                    SELECT TOP 100
                        hes AS HesCode,
                        NAME AS Name,
                        TEL AS Tel,
                        MOBILE AS Mobile,
                        ADDRESS AS Address,
                        TOZIH AS Description,
                        N'طرف‌حساب' AS SourceType
                    FROM dbo.CUST_HESAB
                    {custWhere}
                    ORDER BY NAME";

                var custResults = await _dbService.DoGetDataSQLAsync<CrmPhoneBookItemDto>(
                    custSql, new { LikeTerm = likeTerm, LikeFixTerm = likeFixTerm });
                result.AddRange(custResults);

                // جستجو در شرکت‌های CRM با حذف رکوردهای بدون نام
                // نیمه‌ی «طرف‌حساب» بالا عمداً فیلتر نمی‌شود: CUST_HESAB داده‌ی
                // مشترک حسابداری است و اصلاً ستون مالکیت ندارد. ولی نیمه‌ی CRM
                // همان داده‌ی COPMANES است و باید مثل بقیه محدود شود، وگرنه
                // دفترچه تلفن راه دور زدن محدودیت می‌شود.
                string crmWhere;
                if (string.IsNullOrWhiteSpace(term))
                {
                    crmWhere = "WHERE C.COMPANY_NAME IS NOT NULL AND RTRIM(C.COMPANY_NAME) <> ''";
                }
                else
                {
                    crmWhere = @"WHERE C.COMPANY_NAME IS NOT NULL AND RTRIM(C.COMPANY_NAME) <> ''
                                 AND (C.COMPANY_NAME LIKE @LikeTerm OR C.COMPANY_NAME LIKE @LikeFixTerm
                                      OR C.FACT_TEL LIKE @LikeTerm OR C.MOBILE LIKE @LikeTerm
                                      OR C.MANAGER LIKE @LikeTerm OR C.ADDR LIKE @LikeTerm)";
                }

                if (acl.RestrictToOwn) crmWhere += OwnCompanyPredicate("C");

                var crmSql = $@"
                    SELECT TOP 100
                        CAST(C.ID AS VARCHAR(50)) AS HesCode,
                        C.COMPANY_NAME AS Name,
                        C.FACT_TEL AS Tel,
                        C.MOBILE AS Mobile,
                        C.ADDR AS Address,
                        C.COMMENT AS Description,
                        N'شرکت CRM' AS SourceType
                    FROM COPMANES C
                    {crmWhere}
                    ORDER BY C.COMPANY_NAME";

                var crmParams = new DynamicParameters();
                crmParams.Add("LikeTerm", likeTerm);
                crmParams.Add("LikeFixTerm", likeFixTerm);
                if (acl.RestrictToOwn) AddAclParameters(crmParams, acl);

                var crmResults = await _dbService.DoGetDataSQLAsync<CrmPhoneBookItemDto>(crmSql, crmParams);
                result.AddRange(crmResults);

                return Ok(result.OrderBy(x => x.Name).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching phone book");
                return StatusCode(500, "خطا در جستجوی دفترچه تلفن");
            }
        }

        [HttpGet("distinct-salers")]
        public async Task<ActionResult<List<string>>> GetDistinctSalers()
        {
            try
            {
                var list = (await _dbService.DoGetDataSQLAsync<string>(
                    "SELECT DISTINCT SALER FROM CRMEVENTS WHERE SALER IS NOT NULL AND SALER <> '' ORDER BY SALER")).ToList();
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting distinct salers");
                return StatusCode(500, "خطا در دریافت لیست فروشندگان");
            }
        }

        [HttpGet("distinct-buyers")]
        public async Task<ActionResult<List<string>>> GetDistinctBuyers()
        {
            try
            {
                var list = (await _dbService.DoGetDataSQLAsync<string>(
                    "SELECT DISTINCT BUYER FROM CRMEVENTS WHERE BUYER IS NOT NULL AND BUYER <> '' ORDER BY BUYER")).ToList();
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting distinct buyers");
                return StatusCode(500, "خطا در دریافت لیست خریداران");
            }
        }

        [HttpGet("distinct-status-facts")]
        public async Task<ActionResult<List<string>>> GetDistinctStatusFacts()
        {
            try
            {
                var list = (await _dbService.DoGetDataSQLAsync<string>(
                    "SELECT DISTINCT STATUS_FACT FROM COPMANES WHERE STATUS_FACT IS NOT NULL AND STATUS_FACT <> '' ORDER BY STATUS_FACT")).ToList();
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting distinct status facts");
                return StatusCode(500, "خطا در دریافت لیست وضعیت‌های فاکتور");
            }
        }

        [HttpGet("notes")]
        public async Task<ActionResult<List<CrmNoteDto>>> GetNotes([FromQuery] bool onlyPending = true)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                // Notes.userid ستون NOT NULL است، پس شرط USER_NAME لازم ندارد.
                var conditions = new List<string>();
                if (onlyPending) conditions.Add("Ndone = 0");
                if (acl.RestrictToOwn) conditions.Add("userid = @UserId");

                var whereSql = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
                var sql = $"SELECT TOP 100 idd, Note, Ndate, Ntime, userid, Ndone FROM Notes {whereSql} ORDER BY idd DESC";

                var notes = (await _dbService.DoGetDataSQLAsync<CrmNoteDto>(sql, new { UserId = acl.UserId })).ToList();
                return Ok(notes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting CRM notes");
                return StatusCode(500, "خطا در دریافت یادداشت‌ها");
            }
        }

        [HttpPost("save-note")]
        public async Task<ActionResult<int>> SaveNote([FromBody] CrmNoteDto note)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                bool isUpdate = note.idd != null && note.idd > 0;

                if (isUpdate && !await CanAccessNoteAsync(acl, note.idd!.Value))
                    return Denied();

                // مالکیت یادداشت را سرور تعیین می‌کند، نه بدنه‌ی درخواست.
                note.userid = acl.UserId;

                if (note.Ndate == null || note.Ndate <= 0)
                {
                    note.Ndate = int.TryParse(CL_Tarikh.Current_FullDate, out var nd) ? nd : null;
                }
                if (string.IsNullOrWhiteSpace(note.Ntime))
                {
                    note.Ntime = DateTime.Now.ToString("HH:mm");
                }

                if (!isUpdate)
                {
                    var sql = @"
                        INSERT INTO Notes (Note, Ndate, Ntime, userid, Ndone)
                        VALUES (@Note, @Ndate, @Ntime, @userid, @Ndone);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
                    var newId = (await _dbService.DoGetDataSQLAsync<int>(sql, note)).FirstOrDefault();
                    return Ok(newId);
                }
                else
                {
                    var sql = "UPDATE Notes SET Note = @Note, Ndate = @Ndate, Ntime = @Ntime, Ndone = @Ndone WHERE idd = @idd";
                    await _dbService.DoExecuteSQLAsync(sql, note);
                    return Ok(note.idd.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving CRM note");
                return StatusCode(500, "خطا در ذخیره‌سازی یادداشت");
            }
        }

        [HttpPost("toggle-note")]
        public async Task<ActionResult<bool>> ToggleNote([FromQuery] int noteId, [FromQuery] bool done)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                if (!await CanAccessNoteAsync(acl, noteId)) return Denied();

                var sql = "UPDATE Notes SET Ndone = @Done WHERE idd = @Id";
                var rows = await _dbService.DoExecuteSQLAsync(sql, new { Done = done ? 1 : 0, Id = noteId });
                return Ok(rows > 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling note status {NoteId}", noteId);
                return StatusCode(500, "خطا در تغییر وضعیت یادداشت");
            }
        }

        [HttpDelete("notes/{id}")]
        public async Task<ActionResult<bool>> DeleteNote(int id)
        {
            var acl = await TryGetAccessAsync();
            if (acl == null) return Unauthenticated();

            try
            {
                if (!await CanAccessNoteAsync(acl, id)) return Denied();

                var rows = await _dbService.DoExecuteSQLAsync("DELETE FROM Notes WHERE idd = @Id", new { Id = id });
                return Ok(rows > 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CRM note {Id}", id);
                return StatusCode(500, "خطا در حذف یادداشت");
            }
        }

        [HttpPost("send-sms")]
        public async Task<ActionResult<bool>> SendSms([FromBody] CrmSendSmsRequestDto request, [FromServices] ISmsService smsService)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Mobile) || string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest("شماره موبایل و متن پیامک الزامی است.");
                }

                var res = await smsService.SendSmsAsync(request.Mobile.Trim(), request.Message.Trim());
                return Ok(res.IsSuccess);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SMS in CRM");
                return StatusCode(500, "خطا در ارسال پیامک");
            }
        }

        private async Task<bool> CanAccessNoteAsync(CrmAccessDto acl, int noteId)
        {
            if (!acl.RestrictToOwn) return true;

            var count = (await _dbService.DoGetDataSQLAsync<int>(
                "SELECT COUNT(1) FROM dbo.Notes WITH (NOLOCK) WHERE idd = @Id AND userid = @AclUserId",
                new { Id = noteId, AclUserId = acl.UserId })).FirstOrDefault();

            return count > 0;
        }

        /// <summary>فقط برای خواندن مالک فعلی یک شرکت هنگام ویرایش</summary>
        private sealed class CrmCompanyOwner
        {
            public int? USERID { get; set; }
            public string? USER_NAME { get; set; }
        }

        /// <summary>فقط برای خواندن مالک فعلی یک رویداد هنگام ویرایش</summary>
        private sealed class CrmEventOwner
        {
            public int? USERID { get; set; }
            public string? SALER { get; set; }
        }

        private async Task<Dictionary<int, string>> GetStatusListInternal()
        {
            var dict = new Dictionary<int, string>();
            try
            {
                var sazman = (await _dbService.DoGetDataSQLAsync<dynamic>("SELECT IT1, IT2, IT3, IT4, IT5, IT6, IT7, IT8, IT9 FROM SAZMAN")).FirstOrDefault();
                if (sazman != null)
                {
                    var dynDict = (IDictionary<string, object>)sazman;
                    for (int i = 1; i <= 9; i++)
                    {
                        var key = $"IT{i}";
                        var name = dynDict.ContainsKey(key) && dynDict[key] != null ? dynDict[key].ToString() : $"وضعیت {i}";
                        dict[i] = name ?? $"وضعیت {i}";
                    }
                }
                else
                {
                    for (int i = 1; i <= 9; i++) dict[i] = $"وضعیت {i}";
                }
            }
            catch
            {
                for (int i = 1; i <= 9; i++) dict[i] = $"وضعیت {i}";
            }
            return dict;
        }
    }
}
