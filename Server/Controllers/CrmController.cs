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
        private readonly ILogger<CrmController> _logger;

        public CrmController(IDatabaseService dbService, ILogger<CrmController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdString, out int userCod) ? userCod : 78;
        }

        private string GetCurrentUserName()
        {
            return User.FindFirstValue(ClaimTypes.Name) ?? "Controller";
        }

        [HttpGet("status-list")]
        public async Task<ActionResult<List<CrmStatusDto>>> GetStatusList()
        {
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
                var counts = (await _dbService.DoGetDataSQLAsync<dynamic>("SELECT STATUS, COUNT(1) AS CNT FROM COPMANES GROUP BY STATUS")).ToList();
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
            try
            {
                int currentUserId = GetCurrentUserId();
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

                if (filter.OnlyMyCompanies)
                {
                    sql += " AND (C.userid = @UserId OR C.userid IS NULL) ";
                    parameters.Add("UserId", currentUserId);
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
            try
            {
                var sql = "SELECT * FROM COPMANES WHERE ID = @Id";
                var company = await _dbService.DoGetDataSQLAsyncSingle<CrmCompanyDto>(sql, new { Id = id });
                if (company == null) return NotFound("شرکت مورد نظر یافت نشد.");

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
            try
            {
                int currentUserId = GetCurrentUserId();
                string currentUserName = GetCurrentUserName();

                if (company.DATE_SABT == null || company.DT == null)
                {
                    var today = CL_Tarikh.Current_FullDate;
                    company.DT = company.DT ?? (int.TryParse(today, out var dtVal) ? dtVal : null);
                    company.DATE_SABT = company.DATE_SABT ?? DateTime.Now;
                }

                company.USERID = company.USERID ?? currentUserId;
                company.USER_NAME = string.IsNullOrWhiteSpace(company.USER_NAME) ? currentUserName : company.USER_NAME;
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

                if (company.ID == null || company.ID <= 0)
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
            try
            {
                // حذف تمام رخدادهای مرتبط ابتدا
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
            try
            {
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
            try
            {
                int currentUserId = GetCurrentUserId();

                if (crmEvent.INFO_DATE == null || crmEvent.INFO_DATE <= 0)
                {
                    crmEvent.INFO_DATE = int.TryParse(CL_Tarikh.Current_FullDate, out var td) ? td : null;
                }

                if (crmEvent.INFO_TIME == null || crmEvent.INFO_TIME <= 0)
                {
                    var now = DateTime.Now;
                    crmEvent.INFO_TIME = (now.Hour * 100) + now.Minute;
                }

                crmEvent.USERID = crmEvent.USERID ?? currentUserId;
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

                if (crmEvent.IDDE == null || crmEvent.IDDE <= 0)
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
            try
            {
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
            try
            {
                int currentUserId = GetCurrentUserId();
                int todayInt = int.TryParse(CL_Tarikh.Current_FullDate, out var td) ? td : 0;

                var summary = new CrmDashboardSummaryDto();

                summary.TotalCompanies = (await _dbService.DoGetDataSQLAsync<int>(
                    "SELECT COUNT(1) FROM COPMANES WHERE (userid = @UserId OR userid IS NULL)",
                    new { UserId = currentUserId })).FirstOrDefault();

                summary.TotalEvents = (await _dbService.DoGetDataSQLAsync<int>(
                    "SELECT COUNT(1) FROM CRMEVENTS WHERE (USERID = @UserId OR USERID IS NULL)",
                    new { UserId = currentUserId })).FirstOrDefault();

                summary.TodayFollowUps = (await _dbService.DoGetDataSQLAsync<int>(
                    "SELECT COUNT(1) FROM CRMEVENTS WHERE NEXT_DATE = @Today AND (USERID = @UserId OR USERID IS NULL)",
                    new { Today = todayInt, UserId = currentUserId })).FirstOrDefault();

                summary.OverdueFollowUps = (await _dbService.DoGetDataSQLAsync<int>(
                    "SELECT COUNT(1) FROM CRMEVENTS WHERE NEXT_DATE < @Today AND NEXT_DATE > 0 AND (USERID = @UserId OR USERID IS NULL)",
                    new { Today = todayInt, UserId = currentUserId })).FirstOrDefault();

                summary.TodayMeetings = (await _dbService.DoGetDataSQLAsync<int>(
                    "SELECT COUNT(1) FROM CRMEVENTS WHERE (miting = -1 OR miting = 1) AND NEXT_DATE = @Today AND (USERID = @UserId OR USERID IS NULL)",
                    new { Today = todayInt, UserId = currentUserId })).FirstOrDefault();

                // لیست وضعیت‌ها
                var statusRes = await GetStatusList();
                if (statusRes.Result is OkObjectResult okObj && okObj.Value is List<CrmStatusDto> stList)
                {
                    summary.StatusSummary = stList;
                }

                // رویدادهای آینده
                var upcomingSql = @"
                    SELECT TOP 10
                        E.*,
                        C.COMPANY_NAME
                    FROM CRMEVENTS E
                    LEFT JOIN COPMANES C ON E.idc = C.id
                    WHERE E.NEXT_DATE >= @Today AND (E.USERID = @UserId OR E.USERID IS NULL)
                    ORDER BY E.NEXT_DATE ASC, E.NEXT_TIME ASC";

                summary.UpcomingEvents = (await _dbService.DoGetDataSQLAsync<CrmEventDto>(
                    upcomingSql, new { Today = todayInt, UserId = currentUserId })).ToList();

                var statusDict = await GetStatusListInternal();
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
            try
            {
                var result = new List<CrmPhoneBookItemDto>();
                var term = string.IsNullOrWhiteSpace(query) ? "" : query.Trim();
                var likeTerm = $"%{term}%";

                // جستجو در طرف حساب ها
                var custWhere = string.IsNullOrWhiteSpace(term) ? "" : "WHERE (NAME LIKE @LikeTerm OR TEL LIKE @LikeTerm OR MOBILE LIKE @LikeTerm OR hes LIKE @LikeTerm OR ADDRESS LIKE @LikeTerm)";
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
                    custSql, new { LikeTerm = likeTerm });
                result.AddRange(custResults);

                // جستجو در شرکت‌های CRM
                var crmWhere = string.IsNullOrWhiteSpace(term) ? "" : "WHERE (COMPANY_NAME LIKE @LikeTerm OR FACT_TEL LIKE @LikeTerm OR MOBILE LIKE @LikeTerm OR MANAGER LIKE @LikeTerm OR ADDR LIKE @LikeTerm)";
                var crmSql = $@"
                    SELECT TOP 100
                        CAST(ID AS VARCHAR(50)) AS HesCode,
                        COMPANY_NAME AS Name,
                        FACT_TEL AS Tel,
                        MOBILE AS Mobile,
                        ADDR AS Address,
                        COMMENT AS Description,
                        N'شرکت CRM' AS SourceType
                    FROM COPMANES
                    {crmWhere}
                    ORDER BY COMPANY_NAME";

                var crmResults = await _dbService.DoGetDataSQLAsync<CrmPhoneBookItemDto>(
                    crmSql, new { LikeTerm = likeTerm });
                result.AddRange(crmResults);

                return Ok(result);
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
            try
            {
                int currentUserId = GetCurrentUserId();
                var whereSql = onlyPending ? "WHERE Ndone = 0 AND (userid = @UserId OR userid IS NULL)" : "WHERE (userid = @UserId OR userid IS NULL)";
                var sql = $"SELECT TOP 100 idd, Note, Ndate, Ntime, userid, Ndone FROM Notes {whereSql} ORDER BY idd DESC";

                var notes = (await _dbService.DoGetDataSQLAsync<CrmNoteDto>(sql, new { UserId = currentUserId })).ToList();
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
            try
            {
                int currentUserId = GetCurrentUserId();
                note.userid = note.userid ?? currentUserId;
                if (note.Ndate == null || note.Ndate <= 0)
                {
                    note.Ndate = int.TryParse(CL_Tarikh.Current_FullDate, out var nd) ? nd : null;
                }
                if (string.IsNullOrWhiteSpace(note.Ntime))
                {
                    note.Ntime = DateTime.Now.ToString("HH:mm");
                }

                if (note.idd == null || note.idd <= 0)
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
            try
            {
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
            try
            {
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
