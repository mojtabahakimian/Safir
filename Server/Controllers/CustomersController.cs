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
using Safir.Shared.Models;

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

        private IWebHostEnvironment _webHostEnvironment { get; set; } = default!;
        // private class BlockHesModel { public string HES { get; set; } } // Assuming this is still needed

        private const long DefaultStartDate = 1;
        private const long DefaultEndDate = 99991229;

        public CustomersController(
            IDatabaseService dbService,
            IAppSettingsService appSettingsService,
            ILogger<CustomersController> logger,
            IWebHostEnvironment webHostEnvironment) // <--- پارامتر جدید در سازنده
        {
            _dbService = dbService;
            _appSettingsService = appSettingsService;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment; // <--- ذخیره کردن نمونه تزریق شده
        }


        // --- GetAccountCodesForUserAsync remains the same ---
        private async Task<(int NKol, int Number)?> GetAccountCodesForUserAsync()
        {
            int? defaultKol = null; //await _appSettingsService.GetDefaultBedehkarKolAsync();
            var sazmanSettings = await _appSettingsService.GetSazmanSettingsAsync();
            if (sazmanSettings?.BEDEHKAR != null)
            {
                defaultKol = (int?)sazmanSettings.BEDEHKAR;
            }
            else
            {
                _logger.LogWarning("Could not retrieve Default Bedehkar Kol from SAZMAN settings (SAZMAN object or BEDEHKAR field is null).");
            }

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

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userCod))
            {
                _logger.LogError("CreateCustomer: Could not parse User ID from claims.");
                return StatusCode(500, new ProblemDetails { Title = "Authentication Error", Detail = "اطلاعات کاربر معتبر نیست.", Status = StatusCodes.Status500InternalServerError });
            }

            string ThePhone = model?.MOBILE;
            if (string.IsNullOrEmpty(ThePhone)) { ThePhone = model?.TEL; }
            // ******** شروع بررسی شماره موبایل تکراری ********

            if (!string.IsNullOrWhiteSpace(ThePhone) || !string.IsNullOrWhiteSpace(model?.NAME))
            {
                try
                {
                    string checkDuplicatesSql = @"
                                    SELECT 
                                         CASE 
                                             WHEN TEL = @MobileParam OR MOBILE = @MobileParam THEN 'Mobile'
                                             WHEN NAME = @CustomerName THEN 'Name'
                                         END AS DuplicateType,
                                         HES, NAME
                                     FROM dbo.CUST_HESAB
                                     WHERE (TEL = @MobileParam OR MOBILE = @MobileParam OR NAME = @CustomerName)";

                    var duplicates = await _dbService.DoGetDataSQLAsync<ExistingCustomerInfo>(
                        checkDuplicatesSql,
                        new { MobileParam = ThePhone, CustomerName = model?.NAME?.FixPersianChars() }
                    );

                    bool mobileExists = duplicates.Any(d => d.DuplicateType == "Mobile");
                    bool nameExists = duplicates.Any(d => d.DuplicateType == "Name");

                    if (mobileExists || nameExists)
                    {
                        string details = "";
                        if (mobileExists)
                        {
                            var dupe = duplicates.First(d => d.DuplicateType == "Mobile");
                            details += $"شماره موبایل '{model.MOBILE}' قبلاً برای مشتری '{dupe.Name}' با کد حساب '{dupe.Hes}' ثبت شده است. ";
                        }
                        if (nameExists)
                        {
                            var dupe = duplicates.First(d => d.DuplicateType == "Name");
                            details += $"نام '{model.NAME}' قبلاً برای مشتری با کد حساب '{dupe.Hes}' ثبت شده است.";
                        }

                        _logger.LogWarning("CreateCustomer: Duplicate data detected. Details: {Details}", details);

                        return Conflict(new ProblemDetails
                        {
                            Title = "Duplicate Customer Info",
                            Detail = details,
                            Status = StatusCodes.Status409Conflict
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CreateCustomer: Error checking for duplicate customer info.");
                    return StatusCode(500, new ProblemDetails
                    {
                        Title = "Validation Error",
                        Detail = "خطا در بررسی تکراری بودن اطلاعات مشتری.",
                        Status = StatusCodes.Status500InternalServerError
                    });
                }
            }

            AccountingLevelInfo? levelInfo = await DetermineAccountingLevelAndParentsAsync(userCod);
            if (levelInfo == null)
            {
                _logger.LogError("CreateCustomer: Could not determine accounting level for UserCO: {UserCod}", userCod);
                return StatusCode(500, new ProblemDetails { Title = "Configuration Error", Detail = "امکان تعیین سطح حسابداری برای ایجاد مشتری وجود ندارد. لطفاً تنظیمات کاربر (BLOCK_HES) را بررسی کنید.", Status = StatusCodes.Status500InternalServerError });
            }

            _logger.LogInformation("Creating customer at Level: {Level}, Table: {Table}, Base HES Path: N_KOL={NKol}, NUMBER={Number}, TNUMBER_Parent={Taf1}, TNUMBER2_Parent={Taf2}, TNUMBER3_Parent={Taf3}",
                levelInfo.Level, levelInfo.TargetTable, levelInfo.NKol, levelInfo.Number, levelInfo.TnumberParent, levelInfo.Tnumber2Parent, levelInfo.Tnumber3Parent);

            // --- Check if Parent Account Exists (up to the level before the target table) ---
            // Example: If creating in TDETA_HES3, check TDETA_HES2 (or DETA_HES for TDETA_HES)
            // This needs to be adapted based on your exact parent table structure if levels are strict.
            // For simplicity here, we assume the parent path from BLOCK_HES implies parent existence or direct creation under it.
            // The original WPF code checks `DETA_HES` for level 1. We can adapt this if needed.
            // For now, we'll trust the BLOCK_HES path implies a valid insertion point.

            string blazorIndicator = " (ثبت شده از طریق Blazor) ";
            if (!string.IsNullOrWhiteSpace(model.TOZIH))
            {
                model.TOZIH = $"{model.TOZIH} {blazorIndicator}";
            }
            else
            {
                model.TOZIH = blazorIndicator;
            }
            // اطمینان از اینکه طول توضیحات از حد مجاز بیشتر نشود
            if (model.TOZIH.Length > 250) // 250 بر اساس MaxLength در CustomerModel.cs
            {
                model.TOZIH = model.TOZIH.Substring(0, 250);
            }

            string fullHesCodeForNewCustomer = ""; // Will be constructed after getting the new ID.
            int nextSequentialId = 0;

            try
            {
                nextSequentialId = await _dbService.ExecuteInTransactionAsync<int>(async (connection, transaction) =>
                {
                    // 1. Get MAX ID + 1 for the determined level and parent
                    var getMaxSqlParams = new DynamicParameters();
                    string whereClauseForMax = $"N_KOL = @NKol AND NUMBER = @Number";
                    getMaxSqlParams.Add("NKol", levelInfo.NKol);
                    getMaxSqlParams.Add("Number", levelInfo.Number);

                    if (levelInfo.Level > 1 && levelInfo.TnumberParent.HasValue)
                    {
                        whereClauseForMax += " AND TNUMBER = @TnumberParent";
                        getMaxSqlParams.Add("TnumberParent", levelInfo.TnumberParent.Value);
                    }
                    if (levelInfo.Level > 2 && levelInfo.Tnumber2Parent.HasValue)
                    {
                        whereClauseForMax += " AND TNUMBER2 = @Tnumber2Parent"; // Note: In TDETA_HES3, parent is TNUMBER2 from TDETA_HES2
                        getMaxSqlParams.Add("Tnumber2Parent", levelInfo.Tnumber2Parent.Value);
                    }
                    if (levelInfo.Level > 3 && levelInfo.Tnumber3Parent.HasValue)
                    {
                        whereClauseForMax += " AND TNUMBER3 = @Tnumber3Parent"; // Note: In TDETA_HES4, parent is TNUMBER3 from TDETA_HES3
                        getMaxSqlParams.Add("Tnumber3Parent", levelInfo.Tnumber3Parent.Value);
                    }

                    string getMaxSql = $@"SELECT ISNULL(MAX({levelInfo.IdFieldNameInTable}), 0) + 1
                                          FROM {levelInfo.TargetTable} WITH (UPDLOCK, HOLDLOCK)
                                          WHERE {whereClauseForMax}";

                    int currentNextId = await connection.QuerySingleAsync<int>(getMaxSql, getMaxSqlParams, transaction: transaction);
                    _logger.LogInformation("Transaction: Next sequential ID for Level {Level} ({IdField}) at path is {NextId}", levelInfo.Level, levelInfo.IdFieldNameInTable, currentNextId);


                    // 2. INSERT the new customer record
                    var insertParams = new DynamicParameters(model); // Add all properties from CustomerModel

                    // Add accounting level specific parent keys and the new ID
                    insertParams.Add("NKol", levelInfo.NKol);
                    insertParams.Add("Number", levelInfo.Number); // This is MOIN

                    string parentColumns = "N_KOL, NUMBER";
                    string parentValues = "@NKol, @Number";

                    if (levelInfo.Level > 1 && levelInfo.TnumberParent.HasValue)
                    {
                        insertParams.Add("TNUMBER_Parent", levelInfo.TnumberParent.Value); // Used as TNUMBER in TDETA_HES2+
                        parentColumns += ", TNUMBER";
                        parentValues += ", @TNUMBER_Parent";
                    }
                    if (levelInfo.Level > 2 && levelInfo.Tnumber2Parent.HasValue)
                    {
                        insertParams.Add("TNUMBER2_Parent", levelInfo.Tnumber2Parent.Value); // Used as TNUMBER2 in TDETA_HES3+
                        parentColumns += ", TNUMBER2";
                        parentValues += ", @TNUMBER2_Parent";
                    }
                    if (levelInfo.Level > 3 && levelInfo.Tnumber3Parent.HasValue)
                    {
                        insertParams.Add("TNUMBER3_Parent", levelInfo.Tnumber3Parent.Value); // Used as TNUMBER3 in TDETA_HES4+
                        parentColumns += ", TNUMBER3";
                        parentValues += ", @TNUMBER3_Parent";
                    }

                    // Add the new sequential ID for the current level
                    insertParams.Add(levelInfo.IdFieldNameInTable, currentNextId);

                    // Construct HES code for logging and potentially for route/visit updates
                    string tempHes = $"{levelInfo.NKol}-{levelInfo.Number}";
                    if (levelInfo.Level > 1 && levelInfo.TnumberParent.HasValue) tempHes += $"-{levelInfo.TnumberParent.Value}";
                    if (levelInfo.Level > 2 && levelInfo.Tnumber2Parent.HasValue) tempHes += $"-{levelInfo.Tnumber2Parent.Value}";
                    if (levelInfo.Level > 3 && levelInfo.Tnumber3Parent.HasValue) tempHes += $"-{levelInfo.Tnumber3Parent.Value}";
                    tempHes += $"-{currentNextId}"; // Add the new ID itself
                    fullHesCodeForNewCustomer = tempHes;


                    string insertCustomerSql = $@"
                        INSERT INTO {levelInfo.TargetTable}
                            ({levelInfo.IdFieldNameInTable}, NAME, TEL, MOBILE, ADDRESS, TOZIH, CODE_E, ECODE, PCODE, MCODEM, CUST_COD, OSTANID, SHAHRID, ROUTE_NAME, Longitude, Latitude, tob, {parentColumns})
                        VALUES
                            (@{levelInfo.IdFieldNameInTable}, @NAME, @TEL, @MOBILE, @ADDRESS, @TOZIH, @CODE_E, @ECODE, @PCODE, @MCODEM, @CUST_COD, @OSTANID, @SHAHRID, @ROUTE_NAME, @Longitude, @Latitude, @TOB, {parentValues})";


                    // Remove TNUMBER from model if we are inserting at level 1, because it's calculated as currentNextId
                    // For other levels, TNUMBER from model might be irrelevant or used differently.
                    // The `insertParams` already has the correct calculated ID field for the level.
                    // The CustomerModel's TNUMBER is just a placeholder if sent from client.
                    if (insertParams.ParameterNames.Contains("TNUMBER") && levelInfo.IdFieldNameInTable.Equals("TNUMBER", StringComparison.OrdinalIgnoreCase))
                    {
                        // This means we are at level 1. The @TNUMBER in insertParams is already set to currentNextId.
                        // If CustomerModel.TNUMBER was bound to something in Dapper by name, it could conflict.
                        // But since we add specific parameters like @NKol, @Number, @TNUMBER (as currentNextId), it should be fine.
                    }


                    int customerRowsAffected = await connection.ExecuteAsync(insertCustomerSql, insertParams, transaction: transaction);

                    if (customerRowsAffected <= 0)
                    {
                        _logger.LogError("Transaction: Customer insert failed into {TargetTable}, 0 rows affected. HES Path: {HESPath}, New ID: {NewId}",
                            levelInfo.TargetTable, fullHesCodeForNewCustomer, currentNextId);
                        throw new InvalidOperationException($"Database insert for customer into {levelInfo.TargetTable} failed, 0 rows affected.");
                    }
                    _logger.LogInformation("Transaction: Customer inserted into {TargetTable}. Full HES: {FullHES}, New ID for level ({IdField}): {NewId}",
                        levelInfo.TargetTable, fullHesCodeForNewCustomer, levelInfo.IdFieldNameInTable, currentNextId);


                    // --- Route and Daily Visit Update Logic (using fullHesCodeForNewCustomer) ---
                    var visitorHes = User.FindFirstValue(BaseknowClaimTypes.USER_HES);
                    int? visitorUid = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int pUid) ? pUid : (int?)null;

                    if (!string.IsNullOrWhiteSpace(model.ROUTE_NAME))
                    {
                        _logger.LogInformation("Transaction: Updating route mapping for new CustNo: {CustNo} to Route: {RouteName}", fullHesCodeForNewCustomer, model.ROUTE_NAME);
                        const string updateInactiveSql = "UPDATE dbo.Visit_route_dtl SET RACTIVE = 0 WHERE COUST_NO = @CustomerNumber AND ROUTE_NAME <> @RouteName";
                        await connection.ExecuteAsync(updateInactiveSql, new { CustomerNumber = fullHesCodeForNewCustomer, RouteName = model.ROUTE_NAME }, transaction: transaction);

                        const string selectSpecificSql = "SELECT IDR FROM dbo.Visit_route_dtl WHERE COUST_NO = @CustomerNumber AND ROUTE_NAME = @RouteName";
                        var specificRouteIdr = await connection.QuerySingleOrDefaultAsync<int?>(selectSpecificSql, new { CustomerNumber = fullHesCodeForNewCustomer, RouteName = model.ROUTE_NAME }, transaction: transaction);

                        if (specificRouteIdr.HasValue)
                        {
                            const string updateActiveSql = "UPDATE dbo.Visit_route_dtl SET RACTIVE = 1 WHERE IDR = @Id";
                            await connection.ExecuteAsync(updateActiveSql, new { Id = specificRouteIdr.Value }, transaction: transaction);
                        }
                        else
                        {
                            const string insertRouteSql = "INSERT INTO dbo.Visit_route_dtl (ROUTE_NAME, COUST_NO, RACTIVE) VALUES (@RouteName, @CustomerNumber, 1)";
                            await connection.ExecuteAsync(insertRouteSql, new { RouteName = model.ROUTE_NAME, CustomerNumber = fullHesCodeForNewCustomer }, transaction: transaction);
                        }
                    }

                    if (!string.IsNullOrEmpty(visitorHes))
                    {
                        _logger.LogInformation("Transaction: Attempting to add customer {CustNo} to last daily visit for Visitor HES {VisitorHES}", fullHesCodeForNewCustomer, visitorHes);
                        const string findLastVDateSql = "SELECT MAX(VDATE) FROM dbo.VISITORS_DAY WHERE HES = @HES";
                        long? lastVDate = await connection.QuerySingleOrDefaultAsync<long?>(findLastVDateSql, new { HES = visitorHes }, transaction: transaction);

                        if (lastVDate.HasValue)
                        {
                            long targetVDate = lastVDate.Value;
                            _logger.LogInformation("Transaction: Found last VDATE {TargetVDate} for HES {VisitorHES}. Adding detail for customer {CustNo}.", targetVDate, visitorHes, fullHesCodeForNewCustomer);
                            const string checkDetailSql = "SELECT COUNT(*) FROM dbo.VISITORS_DAY_DTL WHERE HES = @HES AND VDATE = @VDATE AND COUST_NO = @CustomerNumber";
                            int detailCount = await connection.QuerySingleOrDefaultAsync<int>(checkDetailSql, new { HES = visitorHes, VDATE = targetVDate, CustomerNumber = fullHesCodeForNewCustomer }, transaction: transaction);

                            if (detailCount == 0)
                            {
                                const string insertDetailSql = @"
                                    INSERT INTO dbo.VISITORS_DAY_DTL (HES, VDATE, COUST_NO, CDATE, RACTIVE, CLASS, TOPLACE, CRT, UID)
                                    VALUES (@HES, @VDATE, @CustomerNumber, @CurrentDateTime, 1, NULL, NULL, @CurrentDateTime, @UserId);";
                                int detailInserted = await connection.ExecuteAsync(insertDetailSql, new
                                {
                                    HES = visitorHes,
                                    VDATE = targetVDate,
                                    CustomerNumber = fullHesCodeForNewCustomer,
                                    CurrentDateTime = DateTime.Now,
                                    UserId = visitorUid
                                }, transaction: transaction);
                                if (detailInserted <= 0) throw new InvalidOperationException("Database insert for VISITORS_DAY_DTL detail failed.");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Transaction: No existing VDATE found in VISITORS_DAY for HES {VisitorHES}. Customer {CustNo} was NOT added to any daily visit.", visitorHes, fullHesCodeForNewCustomer);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Transaction: Visitor HES claim not found. Skipping daily visit update.");
                    }

                    return currentNextId; // Return the generated sequential ID for that level
                }, IsolationLevel.RepeatableRead);


                _logger.LogInformation("Customer created successfully with multi-level logic. Level: {Level}, Table: {Table}, Sequential ID: {SeqId}, Full HES: {FullHES}",
                    levelInfo.Level, levelInfo.TargetTable, nextSequentialId, fullHesCodeForNewCustomer);

                return Ok(new { Message = $"مشتری با موفقیت در سطح {levelInfo.Level} با کد حساب {fullHesCodeForNewCustomer} (شناسه: {nextSequentialId}) ذخیره شد.", Tnumber = nextSequentialId, Hes = fullHesCodeForNewCustomer });
            }
            catch (Exception ex)
            {
                if (ex is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601)) // Unique constraint violation
                {
                    _logger.LogWarning(sqlEx, "CreateCustomer: Duplicate Key violation. HES: {FullHES}, NextID: {NextId}", fullHesCodeForNewCustomer, nextSequentialId);
                    return Conflict(new ProblemDetails { Title = "Conflict", Detail = $"مشتری با کد {fullHesCodeForNewCustomer} یا شناسه {nextSequentialId} از قبل در این سطح وجود دارد.", Status = StatusCodes.Status409Conflict });
                }
                else if (ex is InvalidOperationException dbEx && dbEx.Message.Contains("Database insert"))
                {
                    _logger.LogError(dbEx, "CreateCustomer: Database insert failed within transaction. HES: {FullHES}", fullHesCodeForNewCustomer);
                    return StatusCode(500, new ProblemDetails { Title = "Database Error", Detail = "خطا در درج اطلاعات در پایگاه داده.", Status = StatusCodes.Status500InternalServerError });
                }
                _logger.LogError(ex, "CreateCustomer: Error during multi-level customer creation. UserCO: {UserCod}, HES Attempted: {FullHES}", userCod, fullHesCodeForNewCustomer);
                return StatusCode(500, new ProblemDetails { Title = "Internal Server Error", Detail = "خطای پیش‌بینی نشده‌ای هنگام ایجاد مشتری رخ داد.", Status = StatusCodes.Status500InternalServerError });
            }
        }

        private async Task<AccountingLevelInfo?> DetermineAccountingLevelAndParentsAsync(int userCod)
        {
            string? blockHesSettingRaw = null;
            try
            {
                string blockHesSql = "SELECT HES FROM BLOCK_HES WHERE HES LIKE '#%' AND USERCO = @UserCode";
                blockHesSettingRaw = await _dbService.DoGetDataSQLAsyncSingle<string>(blockHesSql, new { UserCode = userCod });

                if (string.IsNullOrEmpty(blockHesSettingRaw) || !blockHesSettingRaw.StartsWith("#"))
                {
                    _logger.LogWarning("BLOCK_HES setting not found or invalid for UserCO: {UserCod}. Falling back to default SAZMAN BEDEHKAR if available.", userCod);
                    // Fallback: try to use default Bedehkar from SAZMAN for level 1
                    var sazmanSettings = await _appSettingsService.GetSazmanSettingsAsync();
                    if (sazmanSettings?.BEDEHKAR != null)
                    {
                        return new AccountingLevelInfo
                        {
                            Level = 1,
                            TargetTable = "TDETA_HES",
                            IdFieldNameInTable = "TNUMBER",
                            NKol = sazmanSettings.BEDEHKAR,
                            Number = 1 // Default MOIN for level 1
                        };
                    }
                    _logger.LogError("BLOCK_HES not found for UserCO {UserCod} and SAZMAN.BEDEHKAR is not configured.", userCod);
                    return null;
                }

                string hesPath = blockHesSettingRaw.Substring(1); // Remove '#'

                double? kol = null, moin = null, taf1 = null, taf2 = null, taf3 = null, taf4 = null;
                CL_HESABDARI.GETTAF3(hesPath, ref kol, ref moin, ref taf1, ref taf2, ref taf3, ref taf4);

                if (!kol.HasValue || !moin.HasValue) // KOL and MOIN are mandatory
                {
                    _logger.LogError("Failed to parse KOL or MOIN from BLOCK_HES setting '{HesPath}' for UserCO: {UserCod}", hesPath, userCod);
                    return null;
                }

                if (!taf1.HasValue) // Level 1
                {
                    return new AccountingLevelInfo { Level = 1, TargetTable = "TDETA_HES", IdFieldNameInTable = "TNUMBER", NKol = kol, Number = moin };
                }
                if (!taf2.HasValue) // Level 2
                {
                    return new AccountingLevelInfo { Level = 2, TargetTable = "TDETA_HES2", IdFieldNameInTable = "TNUMBER2", NKol = kol, Number = moin, TnumberParent = taf1 };
                }
                if (!taf3.HasValue) // Level 3
                {
                    return new AccountingLevelInfo { Level = 3, TargetTable = "TDETA_HES3", IdFieldNameInTable = "TNUMBER3", NKol = kol, Number = moin, TnumberParent = taf1, Tnumber2Parent = taf2 };
                }
                // if (!taf4.HasValue) // Level 4 - taf4 would be the ID itself if the path was for a level 5 parent
                // For creating at level 4, taf3 is the last parent.
                return new AccountingLevelInfo { Level = 4, TargetTable = "TDETA_HES4", IdFieldNameInTable = "TNUMBER4", NKol = kol, Number = moin, TnumberParent = taf1, Tnumber2Parent = taf2, Tnumber3Parent = taf3 };
                // If logic for level 5+ existed, taf4 would be Tnumber4Parent etc. Current logic from WPF implies creating up to level 4.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error determining accounting level from BLOCK_HES UserCO: {UserCod}, Setting: '{BlockHesRaw}'", userCod, blockHesSettingRaw ?? "N/A");
                return null;
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

                //string query = "SELECT HES_K, HES_M, TAFZILN, HES, SHARH, BED, BES, N_S, DATE_S, MAND " +
                //               "FROM dbo.QDAFTARTAFZIL2_H(@StartDate, @EndDate, @HesabCode);";   
                
                string query = @"WITH CTE
AS (SELECT HES_K, HES_M, TAFZILN, HES, SHARH, BED, BES, N_S, DATE_S, MAND,id, BED - BES AS DiffAmt
    FROM dbo.QDAFTARTAFZIL2_H(@StartDate, @EndDate, @HesabCode) )
SELECT HES_K,
    DATE_S,
       (
           SELECT SUM(x.DiffAmt)
           FROM CTE AS x
           WHERE x.DATE_S < c1.DATE_S
                 OR
                 (
                     x.DATE_S = c1.DATE_S
                     AND
                     (
                         x.BED > c1.BED
                         OR
                         (
                             x.BED = c1.BED
                             AND x.id <= c1.id
                         )
                     )
                 )
       ) AS MAND,
       HES_M,
       TAFZILN,
       HES,
       SHARH,
       BED,
       BES,
       N_S   
FROM CTE AS c1
ORDER BY DATE_S, BED DESC;
";

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

                var _kol = CL_HESABDARI.GETKOL(hesabCode);
                var _moin = CL_HESABDARI.GETMOIN(hesabCode);
                var _taf = CL_HESABDARI.GETTAF(hesabCode);

                string sql = @"SELECT TOP (1) NAME
                       FROM TDETA_HES
                       WHERE N_KOL = @KOL AND NUMBER = @NUMBER AND TNUMBER = @TNUMBER";          // ستون HES در جدول شما قرار دارد

                var param = new Dictionary<string, object>
            {
                { "@KOL",      _kol  },
                { "@NUMBER",   _moin },
                { "@TNUMBER",   _taf }
            };

                var name = await _dbService.DoGetDataSQLAsyncSingle<string>(sql, param);
                return name ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customer name for HesabCode {HesabCode}", hesabCode);
                return string.Empty;
            }
        }


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

                //استخراج نام مشتری از اولین ردیف یا متد کمکی
                string customerName = statementItems.FirstOrDefault()?.TAFZILN;

                if (string.IsNullOrWhiteSpace(customerName))
                {
                    customerName = await GetCustomerNameAsync(hesabCode);   //‌ fallback
                }

                // 2. ایجاد نمونه از کلاس سند PDF
                // ارسال ILogger به کلاس سند برای لاگ‌گیری داخلی آن
                var document = new CustomerStatementDocument(
                                    statementItems,
                                    hesabCode,
                                    customerName,
                                    startDate,
                                    endDate,
                                    _logger,
                                    _webHostEnvironment);

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

        // --- START: Code to Add ---
        /// <summary>
        /// بررسی می‌کند که آیا حساب مشتری مشخص شده مسدود است یا خیر.
        /// </summary>
        /// <param name="hesCode">کد حساب مشتری (HES)</param>
        /// <returns>True اگر مسدود باشد، False در غیر این صورت.</returns>
        [HttpGet("{hesCode}/is-blocked")] // آدرس API: GET api/customers/{hesCode}/is-blocked
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<bool>> IsCustomerBlocked(string hesCode)
        {
            // 1. بررسی ورودی
            if (string.IsNullOrWhiteSpace(hesCode))
            {
                _logger.LogWarning("API: IsCustomerBlocked called with empty hesCode.");
                return BadRequest("کد حساب مشتری (hesCode) الزامی است.");
            }

            _logger.LogInformation("API: Checking block status for HES: {HesCode}", hesCode);

            // 2. تعریف کوئری SQL بر اساس منطق WPF
            // این کوئری چک می‌کند آیا رکوردی برای این HES در جدول BLOCK_CUSTOMER وجود دارد
            // که ENDBLK آن 0 باشد (فرض بر این است که 0 به معنی فعال بودن مسدودی است).
            // ISNULL(ENDBLK, 0) = 0 همچنین رکوردهایی که ENDBLK آن‌ها NULL است را به عنوان مسدود در نظر می‌گیرد.
            const string sql = @"
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM dbo.BLOCK_CUSTOMER
                    WHERE HES = @HesCode AND ISNULL(ENDBLK, 0) = 0
                ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END";

            try
            {
                // 3. استفاده از پارامتر برای جلوگیری از SQL Injection
                var parameters = new { HesCode = hesCode };

                // 4. اجرای کوئری با استفاده از سرویس دیتابیس
                // متد DoGetDataSQLAsyncSingle<bool> انتظار یک نتیجه boolean دارد
                bool isBlocked = await _dbService.DoGetDataSQLAsyncSingle<bool>(sql, parameters);

                _logger.LogInformation("API: Block status result for HES {HesCode}: {IsBlocked}", hesCode, isBlocked);

                // 5. بازگرداندن نتیجه
                return Ok(isBlocked); // True: مسدود است, False: مسدود نیست
            }
            catch (Exception ex)
            {
                // 6. ثبت خطا در صورت بروز مشکل
                _logger.LogError(ex, "API: Error checking block status for HES: {HesCode}", hesCode);
                // بازگرداندن خطای عمومی سرور
                return StatusCode(StatusCodes.Status500InternalServerError, "خطای داخلی سرور هنگام بررسی وضعیت مسدودی مشتری رخ داد.");
            }
        }

        [HttpGet("list-for-user")]
        public async Task<ActionResult<PagedResult<VISITOR_CUSTOMERS>>> GetCustomersForUserWithoutVisitPlan(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? searchTerm = null)
        {
            // چون UserHes از کلاینت برای این سناریو ارسال نمی‌شود و در کوئری هم استفاده نمی‌شود، لاگ آن را تغییر می‌دهیم.
            _logger.LogInformation("API: Fetching general active customers (unfiltered by specific user HES). Page: {PageNumber}, Size: {PageSize}, Search: '{SearchTerm}' (No Visit Plan)",
                pageNumber, pageSize, searchTerm);

            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10; // یا هر مقدار مینیمم دیگر

            int startRow = (pageNumber - 1) * pageSize + 1;
            int endRow = pageNumber * pageSize;

            var whereConditions = new List<string>();
            var parameters = new DynamicParameters(); // یک DynamicParameters جدید ایجاد کنید

            // 1. فیلتر مشتریان مسدود نشده (این باید همیشه اعمال شود)
            // اطمینان حاصل کنید که منطق ISNULL(BC.ENDBLK, 1) <> 0 با دیتابیس شما همخوانی دارد
            // (یعنی 0 به معنی مسدود و مقادیر دیگر یا NULL به معنی غیر مسدود است)
            whereConditions.Add("(BC.HES IS NULL OR ISNULL(BC.ENDBLK, 1) <> 0)");

            // 2. فیلتر جستجو (اگر searchTerm ارسال شده باشد)
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string normalizedSearchTerm = searchTerm.Trim().FixPersianChars();
                // جستجو در نام، کد حساب، تلفن و موبایل
                whereConditions.Add("(CH.NAME LIKE @SearchPattern OR CH.HES LIKE @SearchPattern OR CH.TEL LIKE @SearchPattern OR CH.MOBILE LIKE @SearchPattern)");
                parameters.Add("SearchPattern", $"%{normalizedSearchTerm}%"); // پارامتر جستجو اضافه می‌شود
            }

            // ساخت رشته WHERE نهایی
            string whereClause = whereConditions.Any() ? $"WHERE {string.Join(" AND ", whereConditions)}" : "";

            // کوئری برای دریافت مشتریان با استفاده از ROW_NUMBER() برای صفحه‌بندی
            string customersSql = $@"
        WITH FilteredCustomers AS (
            SELECT
                CH.HES AS hes,
                CH.NAME AS person,
                CH.ADDRESS + N' ' + ISNULL(CH.TEL, N'') + N' ' + ISNULL(CH.MOBILE, N'') AS addr,
                CH.Latitude, CH.Longitude,
                ROW_NUMBER() OVER (ORDER BY CH.NAME) AS RowNum
                -- دیگر ستون‌های مورد نیاز از CUST_HESAB برای مدل VISITOR_CUSTOMERS
                -- مانند کد اقتصادی (ECODE)، کد پستی (PCODE) و ... اگر در VISITOR_CUSTOMERS هستند
            FROM dbo.CUST_HESAB CH
            LEFT JOIN dbo.BLOCK_CUSTOMER BC ON CH.HES = BC.HES -- جوین برای بررسی مسدودی
            {whereClause} -- شروط فیلتر اینجا اعمال می‌شوند
        )
        SELECT hes, person, addr, Latitude, Longitude -- ستون‌های دیگر را در اینجا هم لیست کنید
        FROM FilteredCustomers
        WHERE RowNum >= @StartRow AND RowNum <= @EndRow
        ORDER BY RowNum;"; // ترتیب نمایش بر اساس RowNum برای حفظ ترتیب صفحه‌بندی

            // کوئری برای شمارش کل مشتریان با همان فیلترها
            string countSql = $@"
        SELECT COUNT(DISTINCT CH.HES)
        FROM dbo.CUST_HESAB CH
        LEFT JOIN dbo.BLOCK_CUSTOMER BC ON CH.HES = BC.HES
        {whereClause};"; // شروط فیلتر اینجا هم اعمال می‌شوند

            // اضافه کردن پارامترهای صفحه‌بندی
            parameters.Add("StartRow", startRow);
            parameters.Add("EndRow", endRow);
            // پارامتر UserHes دیگر نباید اینجا اضافه شود چون از کوئری حذف شده است.

            try
            {
                // اجرای کوئری‌ها
                IEnumerable<VISITOR_CUSTOMERS> customers = await _dbService.DoGetDataSQLAsync<VISITOR_CUSTOMERS>(customersSql, parameters);
                int totalCount = await _dbService.DoGetDataSQLAsyncSingle<int>(countSql, parameters);

                var pagedResult = new PagedResult<VISITOR_CUSTOMERS>
                {
                    Items = customers.ToList(),
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
                return Ok(pagedResult);
            }
            catch (Exception ex)
            {
                // اینجا جزئیات پارامترها را هم لاگ کنید تا دیباگ راحت‌تر شود
                _logger.LogError(ex, "API: Error fetching general active customers. Search: '{SearchTerm}'. Parameters for query: StartRow={StartRow}, EndRow={EndRow}, SearchPattern (if any)='{SearchPattern}'",
                    searchTerm, startRow, endRow, parameters.ParameterNames.Contains("SearchPattern") ? parameters.Get<string>("SearchPattern") : "N/A");
                return StatusCode(StatusCodes.Status500InternalServerError, "خطا در دریافت لیست مشتریان از سرور.");
            }
        }
        // --- END: Code to Add ---
    }
}