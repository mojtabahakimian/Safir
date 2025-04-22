using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Taarif;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.Security.Claims; // For accessing user claims
using Safir.Shared.Utility; // For CL_HESABDARI
using System.Data.SqlClient;
using Safir.Shared.Constants;
using System.Data;
using Dapper;
using Safir.Shared.Models.Visitory; // <<< ADD for VISITOUR_SQL2
using System.Linq; // <<< ADD for Linq methods like FirstOrDefault
using static MudBlazor.Icons;
using static Safir.Shared.Utility.CL_Tarikh;
using Safir.Shared.Models.Hesabdari;
using QuestPDF.Fluent;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CustomersController : ControllerBase
    {
        private readonly IDatabaseService _dbService; // Keep IDatabaseService
        private readonly IAppSettingsService _appSettingsService;
        private readonly ILogger<CustomersController> _logger;

        // private class BlockHesModel { public string HES { get; set; } } // Assuming this is still needed

        private const long DefaultStartDate = 1;
        private const long DefaultEndDate = 99991230;

        public CustomersController(
            IDatabaseService dbService,
            IAppSettingsService appSettingsService,
            ILogger<CustomersController> logger)
        {
            _dbService = dbService;
            _appSettingsService = appSettingsService;
            _logger = logger;
        }

        // --- GetAccountCodesForUserAsync remains the same ---
        private async Task<(int NKol, int Number)?> GetAccountCodesForUserAsync()
        {
            // ... (Implementation from previous analysis - unchanged) ...
            int? defaultKol = await _appSettingsService.GetDefaultBedehkarKolAsync();
            int defaultNumber = 1; // Default MOIN is 1 based on WPF

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod)) { _logger.LogError("Could not parse User ID."); return null; }

            try
            {
                // Simplified BlockHesModel definition
                string blockHesSql = "SELECT HES FROM BLOCK_HES WHERE HES LIKE '#%' AND USERCO = @UserCode";
                var blockHesResult = await _dbService.DoGetDataSQLAsyncSingle<string>(blockHesSql, new { UserCode = userCod }); // Fetch directly as string

                if (!string.IsNullOrEmpty(blockHesResult) && blockHesResult.StartsWith("#"))
                {
                    string hesCode = blockHesResult.Substring(1);
                    long parsedKol = CL_HESABDARI.GETKOL(hesCode);
                    long parsedMoin = CL_HESABDARI.GETMOIN(hesCode);
                    if (parsedKol > 0 && parsedMoin > 0) return ((int)parsedKol, (int)parsedMoin);
                    _logger.LogWarning("Failed parse BLOCK_HES '{HesCode}'. Falling back.", hesCode);
                }
                if (defaultKol.HasValue) return (defaultKol.Value, defaultNumber);
                _logger.LogError("Default BEDEHKAR KOL not configured."); return null;
            }
            catch (Exception ex) { _logger.LogError(ex, "Error determining account codes."); return null; }
        }


        [HttpPost]
        public async Task<IActionResult> CreateCustomer([FromBody] CustomerModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var accountCodes = await GetAccountCodesForUserAsync();
            if (accountCodes == null) return StatusCode(500, new ProblemDetails { Title = "Server Configuration Error", Detail = "امکان تعیین کدهای حسابداری برای کاربر وجود ندارد.", Status = StatusCodes.Status500InternalServerError });

            int determinedNKol = accountCodes.Value.NKol;
            int determinedNumber = accountCodes.Value.Number; // MOIN is assumed to be 1 from GetAccountCodesForUserAsync logic

            // --- Get Current User Info needed for VISITORS_DAY tables ---
            var visitorHes = User.FindFirstValue(BaseknowClaimTypes.USER_HES);
            var visitorUsername = User.Identity?.Name ?? "UnknownUser"; // Get username
            var visitorUidString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? visitorUid = int.TryParse(visitorUidString, out int parsedUid) ? parsedUid : (int?)null;

            // --- Check Parent (can stay outside transaction or move inside) ---
            try
            {
                string checkSql = "SELECT COUNT(*) FROM DETA_HES WHERE N_KOL = @NKol AND NUMBER = @Number";
                int parentExists = await _dbService.DoGetDataSQLAsyncSingle<int>(checkSql, new { NKol = determinedNKol, Number = determinedNumber });
                if (parentExists == 0)
                {
                    _logger.LogWarning("Parent account {NKol}-{Number} does not exist.", determinedNKol, determinedNumber);
                    return BadRequest(new ProblemDetails { Title = "Invalid Account Structure", Detail = $"حساب والد {determinedNKol}-{determinedNumber} وجود ندارد.", Status = StatusCodes.Status400BadRequest });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking parent account {NKol}-{Number}.", determinedNKol, determinedNumber);
                return StatusCode(500, new ProblemDetails { Title = "Database Error", Detail = "خطا در بررسی حساب والد.", Status = StatusCodes.Status500InternalServerError });
            }
            // --- End Check Parent ---

            try
            {
                // Use the transaction method, returning the generated TNUMBER (int)
                int generatedTnumber = await _dbService.ExecuteInTransactionAsync<int>(async (connection, transaction) =>
                {
                    // 1. Get MAX TNUMBER + 1 within the transaction with locking
                    string getMaxSql = @"SELECT ISNULL(MAX(TNUMBER), 0) + 1
                                         FROM TDETA_HES WITH (UPDLOCK, HOLDLOCK)
                                         WHERE N_KOL = @NKol AND NUMBER = @Number";
                    int nextTnumber = await connection.QuerySingleAsync<int>(
                        getMaxSql,
                        new { NKol = determinedNKol, Number = determinedNumber },
                        transaction: transaction);

                    // 2. INSERT the new customer record within the same transaction
                    string insertCustomerSql = @"
                        INSERT INTO TDETA_HES
                            (TNUMBER, NAME, TEL, MOBILE, ADDRESS, TOZIH, CODE_E, ECODE, PCODE, MCODEM, CUST_COD, OSTANID, SHAHRID, ROUTE_NAME, Longitude, Latitude, tob, N_KOL, NUMBER)
                        VALUES
                            (@TNUMBER, @NAME, @TEL, @MOBILE, @ADDRESS, @TOZIH, @CODE_E, @ECODE, @PCODE, @MCODEM, @CUST_COD, @OSTANID, @SHAHRID, @ROUTE_NAME_IN_CUSTOMER, @Longitude, @Latitude, @TOB, @NKol, @Number)"; // Renamed ROUTE_NAME parameter

                    var customerParameters = new
                    {
                        TNUMBER = nextTnumber,
                        model.NAME,
                        model.TEL,
                        model.MOBILE,
                        model.ADDRESS,
                        model.TOZIH,
                        model.CODE_E,
                        model.ECODE,
                        model.PCODE,
                        model.MCODEM,
                        model.CUST_COD,
                        model.OSTANID,
                        model.SHAHRID,
                        ROUTE_NAME_IN_CUSTOMER = model.ROUTE_NAME, // Pass route name, but parameter name changed to avoid clash
                        model.Longitude,
                        model.Latitude,
                        model.TOB,
                        NKol = determinedNKol,
                        Number = determinedNumber
                    };

                    int customerRowsAffected = await connection.ExecuteAsync(
                        insertCustomerSql,
                        customerParameters,
                        transaction: transaction);

                    if (customerRowsAffected <= 0)
                    {
                        _logger.LogError("Transaction: Customer insert failed, 0 rows affected for TNUMBER {TNUMBER}, Account {NKol}-{Number}", nextTnumber, determinedNKol, determinedNumber);
                        // Throw an exception to trigger rollback by ExecuteInTransactionAsync
                        throw new InvalidOperationException("Database insert for customer failed, 0 rows affected.");
                    }
                    _logger.LogInformation("Transaction: Customer inserted successfully. TNUMBER: {TNUMBER}", nextTnumber);

                    var custNo = $"{determinedNKol}-{determinedNumber}-{nextTnumber}";

                    // 3. <<< START: Route Update Logic (Based on WPF Code) >>>
                    if (!string.IsNullOrWhiteSpace(model.ROUTE_NAME))
                    {
                        // Construct the customer account string (COUST_NO)
                        _logger.LogInformation("Transaction: Updating route mapping for new CustNo: {CustNo} to Route: {RouteName}", custNo, model.ROUTE_NAME);

                        // 3a. Deactivate all other routes for this customer (if any exist)
                        //     This matches the logic inside the `if (RST2.Count > 0)` block in WPF
                        const string updateInactiveSql = "UPDATE dbo.Visit_route_dtl SET RACTIVE = 0 WHERE COUST_NO = @CustomerNumber AND ROUTE_NAME <> @RouteName";
                        int deactivatedCount = await connection.ExecuteAsync(
                            updateInactiveSql,
                            new { CustomerNumber = custNo, RouteName = model.ROUTE_NAME }, // Use parameterized query
                            transaction: transaction);
                        _logger.LogInformation("Transaction: Deactivated {Count} other route mappings for CustNo: {CustNo}", deactivatedCount, custNo);

                        // 3b. Check if the *selected* route mapping already exists for this customer
                        const string selectSpecificSql = "SELECT IDR FROM dbo.Visit_route_dtl WHERE COUST_NO = @CustomerNumber AND ROUTE_NAME = @RouteName";
                        var specificRoute = await connection.QuerySingleOrDefaultAsync<VISITOUR_SQL2>( // Use VISITOUR_SQL2 or just int? if only IDR is needed
                            selectSpecificSql,
                            new { CustomerNumber = custNo, RouteName = model.ROUTE_NAME }, // Use parameterized query
                            transaction: transaction);

                        if (specificRoute != null && specificRoute.IDR.HasValue) // Route already exists for this customer
                        {
                            // 3c. Activate the existing record (equivalent to inner `if (rst.Count > 0)` in WPF)
                            const string updateActiveSql = "UPDATE dbo.Visit_route_dtl SET RACTIVE = 1 WHERE IDR = @Id";
                            int activatedCount = await connection.ExecuteAsync(
                                updateActiveSql,
                                new { Id = specificRoute.IDR.Value }, // Use parameterized query
                                transaction: transaction);

                            if (activatedCount > 0)
                            {
                                _logger.LogInformation("Transaction: Activated existing route mapping IDR {Idr} for CustNo: {CustNo}", specificRoute.IDR.Value, custNo);
                            }
                            else
                            {
                                _logger.LogWarning("Transaction: Failed to activate existing route mapping IDR {Idr} for CustNo: {CustNo} (Rows Affected: 0)", specificRoute.IDR.Value, custNo);
                                // Decide if this should cause a rollback - maybe not critical if customer was inserted?
                                // throw new InvalidOperationException("Failed to activate existing route mapping.");
                            }
                        }
                        else // Route does not exist for this customer, need to insert
                        {
                            // 3d. Insert a new active route mapping (equivalent to the two `else` blocks in WPF)
                            const string insertRouteSql = "INSERT INTO dbo.Visit_route_dtl (ROUTE_NAME, COUST_NO, RACTIVE) VALUES (@RouteName, @CustomerNumber, 1)";
                            int insertedCount = await connection.ExecuteAsync(
                                insertRouteSql,
                                new { RouteName = model.ROUTE_NAME, CustomerNumber = custNo }, // Use parameterized query
                                transaction: transaction);

                            if (insertedCount <= 0)
                            {
                                _logger.LogError("Transaction: Failed to insert new route mapping for CustNo: {CustNo}, Route: {RouteName}", custNo, model.ROUTE_NAME);
                                throw new InvalidOperationException("Database insert for route mapping failed."); // Rollback transaction
                            }
                            _logger.LogInformation("Transaction: Inserted new active route mapping for CustNo: {CustNo}, Route: {RouteName}", custNo, model.ROUTE_NAME);
                        }
                        _logger.LogInformation("Transaction: Route mapping update completed for CustNo: {CustNo}", custNo);
                    }
                    else
                    {
                        _logger.LogInformation("Transaction: No ROUTE_NAME provided for new customer TNUMBER {TNUMBER}, skipping route update.", nextTnumber);
                    }
                    // <<< END: Route Update Logic >>>


                    // 4. <<< START: Add Customer to LAST EXISTING Daily Visit >>>
                    if (!string.IsNullOrEmpty(visitorHes))
                    {
                        _logger.LogInformation("Transaction: Attempting to add customer {CustNo} to last daily visit for Visitor HES {VisitorHES}", custNo, visitorHes);

                        // 4a. Find the LATEST VDATE for this visitor from VISITORS_DAY
                        const string findLastVDateSql = "SELECT MAX(VDATE) FROM dbo.VISITORS_DAY WHERE HES = @HES";
                        long? lastVDate = await connection.QuerySingleOrDefaultAsync<long?>(findLastVDateSql, new { HES = visitorHes }, transaction: transaction);

                        // 4b. If a VDATE exists, add the detail using THAT VDATE
                        if (lastVDate.HasValue)
                        {
                            long targetVDate = lastVDate.Value; // Use the existing VDATE
                            DateTime now = DateTime.Now; // Still need current time for CDATE/CRT in detail
                            _logger.LogInformation("Transaction: Found last VDATE {TargetVDate} for HES {VisitorHES}. Adding detail for customer {CustNo}.", targetVDate, visitorHes, custNo);

                            // 4c. Check if detail already exists for this customer on the LAST visit date
                            const string checkDetailSql = "SELECT COUNT(*) FROM dbo.VISITORS_DAY_DTL WHERE HES = @HES AND VDATE = @VDATE AND COUST_NO = @CustomerNumber";
                            int detailCount = await connection.QuerySingleOrDefaultAsync<int>(checkDetailSql, new { HES = visitorHes, VDATE = targetVDate, CustomerNumber = custNo }, transaction: transaction);

                            // 4d. If detail doesn't exist, insert it using the found VDATE
                            if (detailCount == 0)
                            {
                                _logger.LogInformation("Transaction: Detail for customer {CustNo} NOT found in VISITORS_DAY_DTL for HES {VisitorHES}, VDATE {TargetVDate}. Inserting detail.", custNo, visitorHes, targetVDate);
                                const string insertDetailSql = @"
                                    INSERT INTO dbo.VISITORS_DAY_DTL (HES, VDATE, COUST_NO, CDATE, RACTIVE, CLASS, TOPLACE, CRT, UID)
                                    VALUES (@HES, @VDATE, @CustomerNumber, @CurrentDateTime, 1, NULL, NULL, @CurrentDateTime, @UserId);";
                                int detailInserted = await connection.ExecuteAsync(insertDetailSql, new
                                {
                                    HES = visitorHes,
                                    VDATE = targetVDate, // <<< Use the found lastVDate
                                    CustomerNumber = custNo,
                                    CurrentDateTime = now,
                                    UserId = visitorUid
                                }, transaction: transaction);

                                if (detailInserted <= 0)
                                {
                                    _logger.LogError("Transaction: Failed to insert VISITORS_DAY_DTL detail for HES {VisitorHES}, VDATE {TargetVDate}, COUST_NO {CustNo}. Rows Affected: {Rows}", visitorHes, targetVDate, custNo, detailInserted);
                                    throw new InvalidOperationException("Database insert for VISITORS_DAY_DTL detail failed."); // Rollback
                                }
                                _logger.LogInformation("Transaction: VISITORS_DAY_DTL detail inserted successfully for customer {CustNo}, HES {VisitorHES}, VDATE {TargetVDate}.", custNo, visitorHes, targetVDate);
                            }
                            else
                            {
                                _logger.LogInformation("Transaction: Detail for customer {CustNo} already exists in VISITORS_DAY_DTL for HES {VisitorHES}, VDATE {TargetVDate}. Skipping insert.", custNo, visitorHes, targetVDate);
                            }
                        }
                        else
                        {
                            // 4e. No existing VDATE found for this visitor
                            _logger.LogWarning("Transaction: No existing VDATE found in VISITORS_DAY for HES {VisitorHES}. Customer {CustNo} was NOT added to any daily visit.", visitorHes, custNo);
                            // Decide action:
                            // - Do nothing (as implemented here)
                            // - OR: Create a visit for today and add the customer (revert to previous logic)
                            // - OR: Throw an error? throw new InvalidOperationException("No existing visit found for visitor.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Transaction: Visitor HES claim not found. Skipping daily visit update.");
                        // Consider if this should be an error that rolls back the transaction
                    }


                    // Return the generated customer number from the lambda
                    return nextTnumber;

                }, IsolationLevel.RepeatableRead); // Or IsolationLevel.ReadCommitted depending on needs

                _logger.LogInformation("Customer and Route Mapping (if applicable) created successfully via transaction. TNUMBER: {TNUMBER}", generatedTnumber);
                // Return success with the generated TNUMBER
                return Ok(new { Message = "مشتری با موفقیت ذخیره شد.", Tnumber = generatedTnumber });

            }
            catch (Exception ex)
            {
                // Check specifically for Primary Key / Unique Constraint violation (more likely on customer insert)
                if (ex is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
                {
                    _logger.LogWarning(sqlEx, "Duplicate Key violation during customer/route transaction for account {NKol}-{Number}. TNUMBER conflict likely.", determinedNKol, determinedNumber);
                    return Conflict(new ProblemDetails
                    {
                        Title = "Conflict",
                        // Detail = $"مشتری با کد {generatedTnumber} برای این نوع حساب از قبل وجود دارد یا خطای یکتایی دیگری رخ داده است.", // generatedTnumber is not available here
                        Detail = $"مشتری با اطلاعات وارد شده از قبل وجود دارد یا خطای یکتایی دیگری رخ داده است. لطفاً بررسی کنید.",
                        Status = StatusCodes.Status409Conflict
                    });
                }
                else if (ex is InvalidOperationException dbEx && dbEx.Message.Contains("Database insert")) // Catch specific exceptions thrown within transaction
                {
                    _logger.LogError(ex, "Database insert failed within transaction for Account {NKol}-{Number}.", determinedNKol, determinedNumber);
                    return StatusCode(500, new ProblemDetails
                    {
                        Title = "Database Error",
                        Detail = "خطا در درج اطلاعات در دیتابیس.",
                        Status = StatusCodes.Status500InternalServerError
                    });
                }
                else
                {
                    // Log other unexpected errors
                    _logger.LogError(ex, "Error executing customer creation transaction for Account {NKol}-{Number}.", determinedNKol, determinedNumber);
                    return StatusCode(500, new ProblemDetails
                    {
                        Title = "Internal Server Error",
                        Detail = "خطای پیش‌بینی نشده‌ای هنگام پردازش درخواست رخ داد.",
                        Status = StatusCodes.Status500InternalServerError
                    });
                }
            }
        }


        [HttpGet("{hesabCode}/statement")] // روت API: api/customers/{کد حساب}/statement
        public async Task<ActionResult<IEnumerable<QDAFTARTAFZIL2_H>>> GetCustomerStatement(string hesabCode, [FromQuery] long? startDate = DefaultStartDate, [FromQuery] long? endDate = DefaultEndDate)
        {
            if (string.IsNullOrWhiteSpace(hesabCode))
            {
                return BadRequest("Customer account code (hesabCode) is required.");
            }
            if (!startDate.HasValue || !endDate.HasValue)
            {
                return BadRequest("Start date and end date are required.");
            }

            try
            {
                _logger.LogInformation("Fetching statement for HesabCode: {HesabCode} from {StartDate} to {EndDate}", hesabCode, startDate, endDate);

                string query = "SELECT HES_K, HES_M, TAFZILN, HES, SHARH, BED, BES, N_S, DATE_S, MAND " +
                               "FROM dbo.QDAFTARTAFZIL2_H(@StartDate, @EndDate, @HesabCode);";

                var parameters = new Dictionary<string, object>
                {
                    { "@StartDate", startDate.Value },
                    { "@EndDate", endDate.Value },
                    { "@HesabCode", hesabCode }
                };

                // --- اجرای کوئری و دریافت مستقیم لیست QDAFTARTAFZIL2_H ---
                // نوع TEntity را به صورت <QDAFTARTAFZIL2_H> مشخص می کنیم
                // نتیجه مستقیماً یک IEnumerable<QDAFTARTAFZIL2_H> خواهد بود
                IEnumerable<QDAFTARTAFZIL2_H> statementItems = await _dbService.DoGetDataSQLAsync<QDAFTARTAFZIL2_H>(query, parameters);

                // حلقه foreach و تبدیل دستی DataRow حذف شد چون Dapper این کار را انجام داده است!

                // بررسی کنیم که null نباشد (گرچه QueryAsync معمولا لیست خالی برمی‌گرداند)
                if (statementItems == null)
                {
                    _logger.LogWarning("DoGetDataSQLAsync returned null for HesabCode: {HesabCode}", hesabCode);
                    statementItems = new List<QDAFTARTAFZIL2_H>(); // برگرداندن لیست خالی
                }

                // می‌توانید نتیجه را به لیست تبدیل کنید اگر لازم است
                var statementList = statementItems.ToList();

                _logger.LogInformation("Found {Count} statement items for HesabCode: {HesabCode}", statementList.Count, hesabCode);
                return Ok(statementList); // بازگرداندن لیست QDAFTARTAFZIL2_H
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL Error fetching statement for HesabCode: {HesabCode}", hesabCode);
                return StatusCode(500, "خطا در ارتباط با پایگاه داده رخ داد.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching statement for HesabCode: {HesabCode}", hesabCode);
                return StatusCode(500, "خطای داخلی سرور هنگام دریافت صورت حساب رخ داد.");
            }
        }

        private async Task<string> GetCustomerNameAsync(string hesabCode)
        {
            try
            {
                string sql = @"SELECT TOP (1) NAME
                       FROM TDETA_HES
                       WHERE HES = @HesabCode";          // ستون HES در جدول شما قرار دارد

                var name = await _dbService.DoGetDataSQLAsyncSingle<string>(sql, new { HesabCode = hesabCode });
                return name ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customer name for HesabCode {HesabCode}", hesabCode);
                return string.Empty;
            }
        }


        #region MyRegion
        // --- متد کمکی برای دریافت داده (برای جلوگیری از تکرار کد) ---
        private async Task<IEnumerable<QDAFTARTAFZIL2_H>?> FetchStatementDataAsync(string hesabCode, long? startDate, long? endDate)
        {
            // همان منطق دریافت داده از متد GetCustomerStatement
            _logger.LogInformation("Fetching statement data for HesabCode: {HesabCode} from {StartDate} to {EndDate}", hesabCode, startDate, endDate);
            string query = "SELECT HES_K, HES_M, TAFZILN, HES, SHARH, BED, BES, N_S, DATE_S, MAND " +
                           "FROM dbo.QDAFTARTAFZIL2_H(@StartDate, @EndDate, @HesabCode);";

            var parameters = new Dictionary<string, object>
        {
            { "@StartDate", startDate ?? DefaultStartDate }, // استفاده از مقادیر پیش فرض اگر null باشند
            { "@EndDate", endDate ?? DefaultEndDate },
            { "@HesabCode", hesabCode }
        };
            try
            {
                IEnumerable<QDAFTARTAFZIL2_H> statementItems = await _dbService.DoGetDataSQLAsync<QDAFTARTAFZIL2_H>(query, parameters);
                return statementItems;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FetchStatementDataAsync for HesabCode: {HesabCode}", hesabCode);
                return null; // یا throw کنید
            }

        }


        // --- Endpoint جدید برای دانلود PDF ---
        [HttpGet("{hesabCode}/statement/pdf")]
        public async Task<IActionResult> GetCustomerStatementPdf(string hesabCode, [FromQuery] long? startDate = DefaultStartDate, [FromQuery] long? endDate = DefaultEndDate)
        {
            if (string.IsNullOrWhiteSpace(hesabCode))
                return BadRequest("Customer account code (hesabCode) is required.");

            _logger.LogInformation("Request received for PDF statement. HesabCode: {HesabCode}, Start: {StartDate}, End: {EndDate}", hesabCode, startDate, endDate);


            try
            {
                // 1. دریافت داده‌های صورت حساب با استفاده از متد کمکی
                var statementItems = await FetchStatementDataAsync(hesabCode, startDate, endDate);

                if (statementItems == null)
                {
                    _logger.LogError("Failed to fetch statement data for PDF generation. HesabCode: {HesabCode}", hesabCode);
                    return StatusCode(500, "خطا در دریافت اطلاعات صورت حساب برای تولید PDF.");
                }
                if (!statementItems.Any())
                {
                    _logger.LogWarning("No statement data found to generate PDF for HesabCode: {HesabCode}", hesabCode);
                    return NotFound("داده‌ای برای تولید گزارش PDF در این بازه زمانی یافت نشد.");
                }


                _logger.LogInformation("Generating PDF for {Count} items. HesabCode: {HesabCode}", statementItems.Count(), hesabCode);


                // 2. ایجاد نمونه از کلاس سند PDF
                // ارسال ILogger به کلاس سند برای لاگ‌گیری داخلی آن
                var document = new CustomerStatementDocument(statementItems, hesabCode, startDate, endDate, _logger);

                // 3. تولید PDF به صورت بایت (byte array)
                // GeneratePdf() یک آرایه بایت از PDF تولید شده برمی‌گرداند.
                byte[] pdfBytes = document.GeneratePdf();

                _logger.LogInformation("PDF generated successfully. Size: {Size} bytes. HesabCode: {HesabCode}", pdfBytes.Length, hesabCode);


                // 4. ساخت نام فایل مناسب
                string startDateStr = startDate?.ToString() ?? "all";
                string endDateStr = endDate?.ToString() ?? "all";
                string fileName = $"Statement_{hesabCode}_{startDateStr}_{endDateStr}.pdf";

                // 5. بازگرداندن فایل PDF به کلاینت برای دانلود
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating or returning PDF statement for HesabCode: {HesabCode}", hesabCode);
                return StatusCode(500, "خطای داخلی سرور هنگام تولید گزارش PDF رخ داد.");
            }
        }

    
        #endregion
    }
}