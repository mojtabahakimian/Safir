// File: Server/Controllers/ProformasController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Kharid; // For DTOs
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Safir.Shared.Utility;
using Safir.Shared.Models.User_Model; // For SALA_DTL
using System.Data; // For IsolationLevel
using Dapper;     // For QuerySingleAsync etc.
using System.Collections.Generic;
using System.Linq;
using Safir.Shared.Constants; // For BaseknowClaimTypes
using Microsoft.Extensions.Configuration; // For reading configuration
using System.Data.SqlClient;
using static MudBlazor.CategoryTypes;
using QuestPDF.Fluent;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProformasController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<ProformasController> _logger;
        private readonly IUserService _userService;
        private readonly IAppSettingsService _appSettingsService;
        private readonly IConfiguration _configuration;

        private const int ProformaTag = 20;
        private const int DefaultDepatmanOnError = 20; //واحد های زیر مجموعه سازمان
        private const int DefaultShiftOnError = 1;

        private class UserDefaultDep
        {
            public int? TFSAZMAN { get; set; }
            public int? SHIFT { get; set; }
        }

        public ProformasController(
            IDatabaseService dbService,
            ILogger<ProformasController> logger,
            IUserService userService,
            IAppSettingsService appSettingsService,
            IConfiguration configuration)
        {
            _dbService = dbService;
            _logger = logger;
            _userService = userService;
            _appSettingsService = appSettingsService;
            _configuration = configuration;
        }

        // --- Helpers (Unchanged) ---
        private async Task<double> GetConversionRateAsync(string itemCode, int unitCode, IDbConnection connection, IDbTransaction transaction)
        {
            string sql = "SELECT NESBAT FROM dbo.VAHEDS WHERE CODE = @Code AND VAHED = @Unit";
            var rate = await connection.QuerySingleOrDefaultAsync<double?>(sql, new { Code = itemCode, Unit = unitCode }, transaction: transaction);
            if (!rate.HasValue)
            {
                string baseUnitSql = "SELECT VAHED FROM dbo.STUF_DEF WHERE CODE = @Code";
                int? baseUnit = await connection.QuerySingleOrDefaultAsync<int?>(baseUnitSql, new { Code = itemCode }, transaction: transaction);
                if (baseUnit.HasValue && baseUnit.Value == unitCode) return 1.0;
            }
            return rate ?? 1.0;
        }
        private async Task<double> GetVatRateAsync(string itemCode, IDbConnection connection, IDbTransaction transaction)
        {
            string sql = "SELECT VRA FROM dbo.STUF_DEF WHERE CODE = @Code";
            var rate = await connection.QuerySingleOrDefaultAsync<double?>(sql, new { Code = itemCode }, transaction: transaction);
            return rate ?? 0;
        }
        // --- End Helpers ---

        [HttpPost]
        public async Task<ActionResult<ProformaSaveResponseDto>> CreateProforma([FromBody] ProformaSaveRequestDto request)
        {
            // Basic validation (Unchanged)
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                _logger.LogWarning("CreateProforma validation failed: {ValidationErrors}", string.Join("; ", errors));
                return BadRequest(new ProformaSaveResponseDto { Success = false, Message = "اطلاعات ارسال شده نامعتبر است: " + string.Join("; ", errors) });
            }
            if (request.Header == null || request.Lines == null || !request.Lines.Any())
            {
                return BadRequest(new ProformaSaveResponseDto { Success = false, Message = "اطلاعات سربرگ یا سطرهای پیش فاکتور ارسال نشده است." });
            }

            var userIdString = User.FindFirstValue(BaseknowClaimTypes.IDD);
            var userName = User.Identity?.Name ?? "UnknownUser";
            if (!int.TryParse(userIdString, out int userId))
            {
                _logger.LogError("User ID claim (IDD) could not be parsed for user: {UserName}", userName);
                return Unauthorized(new ProformaSaveResponseDto { Success = false, Message = "اطلاعات کاربر نامعتبر است." });
            }

            long currentDate = CL_Tarikh.PersianCalendarHelper.GetCurrentPersianDateAsLong();
            int currentTime = int.Parse(DateTime.Now.ToString("HHmmss"));

            // --- Inventory Pre-Check (Unchanged) ---
            bool inventoryIssueFound = false;
            if (!request.OverrideInventoryCheck)
            {
                foreach (var line in request.Lines)
                {
                    var itemDefExists = await _dbService.DoGetDataSQLAsyncSingle<int?>("SELECT 1 FROM dbo.STUF_DEF WHERE CODE = @ItemCode", new { ItemCode = line.ItemCode });
                    if (!itemDefExists.HasValue)
                    {
                        _logger.LogError("Pre-Transaction Check: Item CODE {ItemCode} not found in STUF_DEF.", line.ItemCode);
                        return BadRequest(new ProformaSaveResponseDto { Success = false, Message = $"کالای '{line.ItemCode}' در سیستم تعریف نشده است." });
                    }
                    var inventoryDetails = await _dbService.GetItemInventoryDetailsAsync(line.ItemCode, line.AnbarCode);
                    if (inventoryDetails == null)
                    {
                        inventoryIssueFound = true;
                        _logger.LogWarning("Pre-Transaction Check: STUF_FSK record missing for Item {ItemCode}, Anbar {AnbarCode}. Confirmation needed.", line.ItemCode, line.AnbarCode);
                    }
                    else
                    {
                        decimal currentInv = inventoryDetails.CurrentInventory ?? 0;
                        decimal minInv = inventoryDetails.MinimumInventory ?? 0;
                        decimal resultingInv = currentInv - line.Quantity;
                        if (resultingInv < minInv - 0.0001m)
                        {
                            inventoryIssueFound = true;
                            _logger.LogWarning("Pre-Transaction Check: Low stock for Item {ItemCode}, Anbar {AnbarCode}. Resulting: {ResultingInv}, Min: {MinInv}. Confirmation needed.", line.ItemCode, line.AnbarCode, resultingInv, minInv);
                        }
                    }
                    if (inventoryIssueFound) break;
                }
            }
            if (inventoryIssueFound && !request.OverrideInventoryCheck)
            {
                _logger.LogWarning("Inventory/STUF_FSK issue detected, returning confirmation request. Customer: {CustomerHes}", request.Header.CustomerHesCode);
                return Ok(new ProformaSaveResponseDto
                {
                    Success = false,
                    RequiresInventoryConfirmation = true,
                    Message = "برخی کالاها موجودی کافی ندارند , مایل به ادامه ثبت هستید?"
                });
            }
            // --- End Inventory Pre-Check ---

            // --- Start Database Transaction ---
            try
            {
                double generatedProformaNumber = await _dbService.ExecuteInTransactionAsync<double>(async (connection, transaction) =>
                {
                    // 0. Get User Default Department and Shift (Unchanged)
                    int userDefaultDep = DefaultDepatmanOnError;
                    int userDefaultShift = DefaultShiftOnError;
                    const string getDefaultDepSql = "SELECT TFSAZMAN, SHIFT FROM dbo.DEFAULTDEP WHERE USERID = @UserId";
                    try
                    {
                        var defaultDepData = await connection.QuerySingleOrDefaultAsync<UserDefaultDep>(getDefaultDepSql, new { UserId = userId }, transaction: transaction);
                        if (defaultDepData != null)
                        {
                            userDefaultDep = defaultDepData.TFSAZMAN ?? DefaultDepatmanOnError;
                            userDefaultShift = defaultDepData.SHIFT ?? DefaultShiftOnError;
                        }
                        else
                        {
                            _logger.LogWarning("Transaction: No entry found in DEFAULTDEP for UserID {UserId}. Using defaults (Dep={Dep}, Shift={Shift}).", userId, DefaultDepatmanOnError, DefaultShiftOnError);
                        }
                    }
                    catch (Exception depEx)
                    {
                        _logger.LogError(depEx, "Transaction: Error querying DEFAULTDEP for UserID {UserId}. Using defaults.", userId);
                    }

                    // 0.5 Get Customer Kind (Unchanged)
                    int customerKindCodeToInsert = 1;
                    const string getCustomerKindSql = "SELECT CUST_COD FROM dbo.CUST_HESAB WHERE hes = @CustomerHesCode";
                    int? customerSpecificKind = await connection.QuerySingleOrDefaultAsync<int?>(getCustomerKindSql, new { CustomerHesCode = request.Header.CustomerHesCode }, transaction: transaction);
                    if (customerSpecificKind.HasValue)
                    {
                        customerKindCodeToInsert = customerSpecificKind.Value;
                    }
                    else
                    {
                        _logger.LogWarning("Transaction: CUST_COD not found for Customer {CustomerHes} in CUST_HESAB. Falling back to first CUSTKIND.", request.Header.CustomerHesCode);
                        const string getFirstKindSql = "SELECT TOP 1 CUST_COD FROM dbo.CUSTKIND ORDER BY CUST_COD";
                        int? fallbackKind = await connection.QuerySingleOrDefaultAsync<int?>(getFirstKindSql, transaction: transaction);
                        if (fallbackKind.HasValue)
                        {
                            customerKindCodeToInsert = fallbackKind.Value;
                        }
                        else
                        {
                            _logger.LogError("Transaction: Could not determine CUST_KIND for Customer {CustomerHes} and no fallback found in CUSTKIND. Rolling back.", request.Header.CustomerHesCode);
                            throw new InvalidOperationException("امکان تعیین نوع مشتری وجود ندارد. جدول CUSTKIND خالی است؟");
                        }
                    }

                    // 1. Get Next Number (Unchanged)
                    string getMaxSql = "SELECT ISNULL(MAX(NUMBER), 0) + 1 FROM dbo.HEAD_LST WITH (UPDLOCK, HOLDLOCK) WHERE TAG = @Tag";
                    double nextNumber = await connection.QuerySingleAsync<double>(getMaxSql, new { Tag = ProformaTag }, transaction: transaction);

                    // 2. Insert HEAD_LST (Unchanged parameters)
                    string insertHeadSql = @"
            INSERT INTO dbo.HEAD_LST(
              NUMBER, TAG, DATE_N, MAS, CUST_NO, MOLAH, MABL_HAZ, TAKHFIF,
              DEPATMAN, SHIFT, CUST_KIND, USER_NAME, SHARAYET, MBAA, HMBAA, TAMIR, TICMBAA,
              OKF, SADER, ARZD, ARZKIND, CDDATE, CDTIME, OKDATE, OKTIME, JAY,
              MODAT_PPID, PEPID, PEID, VAS, CRT, UID
            ) VALUES (
              @NUMBER, @TAG, @DATE_N, @MAS, @CUST_NO, @MOLAH, @MABL_HAZ, @TAKHFIF,
              @DEPATMAN, @SHIFT, @CUST_KIND, @USER_NAME, @SHARAYET, 0, NULL, 0, @TICMBAA,
              0, 0, 0, 0, @CurrentDate, @CurrentTime, 0, 0, @JAY,
              @MODAT_PPID, @PEPID, @PEID, 1, GETDATE(), @UserId
            )";
                    var headParams = new
                    {
                        NUMBER = nextNumber,
                        TAG = ProformaTag,
                        DATE_N = request.Header.Date ?? currentDate,
                        MAS = request.Header.AgreedDuration ?? 0,
                        CUST_NO = request.Header.CustomerHesCode,
                        MOLAH = request.Header.Notes,
                        MABL_HAZ = request.Header.ShippingCost ?? 0,
                        TAKHFIF = request.Header.TotalDiscount ?? 0,
                        DEPATMAN = userDefaultDep,
                        SHIFT = userDefaultShift,
                        CUST_KIND = customerKindCodeToInsert,
                        USER_NAME = userName,
                        SHARAYET = request.Header.Conditions,
                        TICMBAA = request.Header.ApplyVat,
                        JAY = request.Header.CalculateAward,
                        MODAT_PPID = request.Header.PaymentTermId,
                        PEPID = request.Header.PriceListId,
                        PEID = request.Header.DiscountListId,
                        CurrentDate = currentDate,
                        CurrentTime = currentTime,
                        UserId = userId
                    };
                    int headRowsAffected = await connection.ExecuteAsync(insertHeadSql, headParams, transaction: transaction);
                    if (headRowsAffected <= 0) throw new InvalidOperationException("Insert into HEAD_LST failed.");

                    // 3. Insert INVO_LST Lines (Discount calculation logic updated)
                    string insertLineSql = @"
            INSERT INTO dbo.INVO_LST (
              NUMBER, TAG, ANBAR, CODE, MEGH, MEGHk, MABL, MABL_K,
              VAHED_K, N_KOL, TKHN, N_MOIN, IMBAA, MANDAH, RADIF,
              FROM_A, JAY, CRT, UID
            ) VALUES (
              @NUMBER, @TAG, @ANBAR, @CODE, @MEGH, @MEGHk, @MABL, @MABL_K,
              @VAHED_K, @N_KOL, @TKHN, @N_MOIN, @IMBAA, @MANDAH, @RADIF,
              0, 0, GETDATE(), @UserId
            )";
                    const string checkStufFskSql = "SELECT 1 FROM dbo.STUF_FSK WHERE CODE = @CODE AND ANBAR = @ANBAR";
                    const string insertStufFskSql = @"INSERT INTO dbo.STUF_FSK (CODE, ANBAR, MOGODI_A, FI_A, MABL_A, MANDAH_A, MIN_M, MAX_M, CRT, UID) VALUES (@CODE, @ANBAR, 0, 0, 0, 0, 0, 0, GETDATE(), @UserId)";
                    int radifCounter = 0;
                    decimal totalCalculatedVat = 0;

                    foreach (var line in request.Lines)
                    {
                        radifCounter++;
                        // STUF_FSK Check/Insert (Unchanged)
                        var fskExists = await connection.QuerySingleOrDefaultAsync<int?>(checkStufFskSql, new { CODE = line.ItemCode, ANBAR = line.AnbarCode }, transaction: transaction);
                        if (!fskExists.HasValue)
                        {
                            int fskRows = await connection.ExecuteAsync(insertStufFskSql, new { CODE = line.ItemCode, ANBAR = line.AnbarCode, UserId = userId }, transaction: transaction);
                            if (fskRows <= 0) throw new InvalidOperationException($"Failed to auto-create STUF_FSK record for CODE={line.ItemCode}, ANBAR={line.AnbarCode}.");
                        }

                        // --- Line Calculations (Using Correct Discount Logic) ---
                        double nesbat = await GetConversionRateAsync(line.ItemCode, line.SelectedUnitCode, connection, transaction);
                        decimal meghK = line.Quantity * (decimal)nesbat; // Quantity in base unit
                                                                         // MABL_K is Total Price in Base Unit: QtyInBaseUnit * PricePerBaseUnit
                                                                         // We have PricePerUnit (which is price for the SELECTED unit from client)
                                                                         // Let's calculate MABL_K = Quantity * PricePerUnit (Total price for selected unit)
                        decimal mablK = line.Quantity * line.PricePerUnit; // Total price for the quantity of the selected unit

                        decimal nKolValue = (decimal)(line.DiscountPercent ?? 0); // N_KOL (%)
                        decimal tkhnValue = (decimal)(line.CashDiscountPercent ?? 0); // TKHN (%)

                        // Calculate discount amounts based on user's formula structure using MABL_K
                        decimal discountAmount = Math.Round((nKolValue * mablK) / 100m); // Discount based on N_KOL
                        decimal cashDiscountAmount = Math.Round(((mablK - discountAmount) * tkhnValue) / 100m); // Discount based on TKHN applied to remaining amount
                        decimal nMoin = discountAmount + cashDiscountAmount; // Total discount amount (N_MOIN)

                        decimal imbaaLine = 0; // VAT Amount
                        if (request.Header.ApplyVat)
                        {
                            bool isVatApplicable = await connection.QuerySingleOrDefaultAsync<bool?>("SELECT CMBAA FROM STUF_DEF WHERE CODE = @Code", new { Code = line.ItemCode }, transaction) ?? false;
                            if (isVatApplicable)
                            {
                                double vatRate = await GetVatRateAsync(line.ItemCode, connection, transaction);
                                // VAT is calculated on price AFTER discounts
                                imbaaLine = Math.Round(((mablK - nMoin) * (decimal)vatRate) / 100m);
                                totalCalculatedVat += imbaaLine;
                            }
                        }
                        // --- End Calculations ---

                        var lineParams = new
                        {
                            NUMBER = nextNumber,
                            TAG = ProformaTag,
                            ANBAR = line.AnbarCode,
                            CODE = line.ItemCode,
                            MEGH = line.Quantity,
                            MEGHk = meghK, // MEGHk is Qty in base unit
                            MABL = line.PricePerUnit, // Price per selected unit
                            MABL_K = mablK, // Total price for selected unit qty before discount
                            VAHED_K = line.SelectedUnitCode,
                            N_KOL = nKolValue, // Store Discount Percentage
                            TKHN = tkhnValue, // Store Cash Discount Percentage
                            N_MOIN = nMoin, // <<<--- Store Calculated Total Discount AMOUNT ---<<<
                            IMBAA = imbaaLine,
                            MANDAH = line.Notes,
                            RADIF = radifCounter,
                            UserId = userId
                        };
                        int lineRowsAffected = await connection.ExecuteAsync(insertLineSql, lineParams, transaction: transaction);
                        if (lineRowsAffected <= 0) throw new InvalidOperationException($"Insert into INVO_LST failed for item {line.ItemCode}.");
                    }
                    _logger.LogInformation("Transaction: Inserted {LineCount} lines into INVO_LST for Number {Number}", request.Lines.Count, nextNumber);

                    // 4. Update Header VAT (Unchanged)
                    if (request.Header.ApplyVat && totalCalculatedVat > 0)
                    {
                        string? defaultVatAccount = _configuration.GetValue<string>("AppSettings:DefaultVatAccountCode");
                        string updateVatSql = "UPDATE dbo.HEAD_LST SET MBAA = @TotalVat, HMBAA = @Hmbaa WHERE NUMBER = @Number AND TAG = @Tag";
                        await connection.ExecuteAsync(updateVatSql, new { TotalVat = totalCalculatedVat, Hmbaa = defaultVatAccount, Number = nextNumber, Tag = ProformaTag }, transaction);
                    }

                    // Log override warning (Unchanged)
                    if (inventoryIssueFound && request.OverrideInventoryCheck)
                    {
                        _logger.LogWarning("Proforma {Number} saved with inventory/STUF_FSK override. Customer: {CustomerHes}.", nextNumber, request.Header.CustomerHesCode);
                    }

                    // 5. Get necessary claims for the task
                    var erjabeUserIdString = User.FindFirstValue(BaseknowClaimTypes.erjabe); // کد کاربری که تسک به او ارجاع می‌شود
                    var currentUserName = User.FindFirstValue(ClaimTypes.Name); // نام کاربری که پیش‌فاکتور را ثبت کرده
                    var currentUserIdString = User.FindFirstValue(ClaimTypes.NameIdentifier); // کد کاربری که پیش‌فاکتور را ثبت کرده

                    if (!string.IsNullOrEmpty(erjabeUserIdString) && int.TryParse(erjabeUserIdString, out int personelId) &&
                        !string.IsNullOrEmpty(currentUserName) && int.TryParse(currentUserIdString, out int currentUserId))
                    {
                        _logger.LogInformation("Transaction: Preparing to insert automation task for Proforma {ProformaNumber} to User ID {PersonelId}", nextNumber, personelId);

                        // Get customer name for description (optional, but improves task description)
                        string customerNameForTask = request.Header.CustomerHesCode; // Default to code
                        try
                        {
                            const string custNameSql = "SELECT TOP 1 NAME FROM CUST_HESAB WHERE hes = @HesCode";
                            customerNameForTask = await connection.QuerySingleOrDefaultAsync<string>(custNameSql, new { HesCode = request.Header.CustomerHesCode }, transaction: transaction) ?? request.Header.CustomerHesCode;
                        }
                        catch (Exception nameEx)
                        {
                            _logger.LogWarning(nameEx, "Transaction: Could not fetch customer name for task description. Using code '{HesCode}'.", request.Header.CustomerHesCode);
                        }


                        // Prepare task details based on PHP example
                        // $SHARHT = " پیش فاکتور شماره $NUMBERP مشتری :$N_RASID";
                        string taskDescription = $"پیش فاکتور شماره {nextNumber} مشتری: {customerNameForTask}";

                        // $current_time = date("H")*100+date("i");
                        // We use CURRENT_TIMESTAMP in SQL, but if STTIME needs HHMM format:
                        int taskStartTime = int.Parse(DateTime.Now.ToString("HHmm"));

                        // SANAD_NO in PHP example -> request.Header.Date (Date of Proforma)
                        long taskStartDate = request.Header.Date ?? currentDate; // Use proforma date or current date

                        // Prepare SQL Insert for tasks table
                        const string insertTaskSql = @"
                    INSERT INTO tasks (
                        PERSONEL, USERNAME, TASK, COMP_COD, STDATE, STTIME, SKID, NUM, TG, CTIM, USERCO
                    ) VALUES (
                        @Personel, @Username, @TaskDesc, @CompCod, @StDate, @StTime, 20, @Num, 20, CURRENT_TIMESTAMP, @UserCo
                    )";
                        // SKID = 20 (Hardcoded based on PHP)
                        // TG = 20 (Hardcoded based on PHP)
                        // CTIM = CURRENT_TIMESTAMP

                        var taskParams = new
                        {
                            Personel = personelId,              // User to assign the task to (from erjabe claim)
                            Username = currentUserName,         // User who created the proforma
                            TaskDesc = taskDescription,         // Description of the task
                            CompCod = request.Header.CustomerHesCode, // Customer Code (N_RASID in PHP)
                            StDate = taskStartDate,             // Proforma Date (SANAD_NO in PHP)
                            StTime = taskStartTime,             // Current Time HHmm (current_time in PHP)
                            Num = nextNumber,                   // Proforma Number (NUMBERP in PHP)
                            UserCo = currentUserId              // User who created the proforma (UIDD in PHP)
                        };

                        // Execute Insert Task
                        int taskRowsAffected = await connection.ExecuteAsync(insertTaskSql, taskParams, transaction: transaction);
                        if (taskRowsAffected <= 0)
                        {
                            _logger.LogError("Transaction: Automation Task insert failed for Proforma {ProformaNumber}. Rolling back.", nextNumber);
                            throw new InvalidOperationException("Database insert for automation task failed."); // Trigger rollback
                        }
                        _logger.LogInformation("Transaction: Automation Task inserted successfully for Proforma {ProformaNumber} assigned to User ID {PersonelId}.", nextNumber, personelId);

                    }
                    else
                    {
                        // Log a warning if claims are missing, but don't necessarily fail the transaction
                        // unless task creation is absolutely critical.
                        _logger.LogWarning("Transaction: Could not insert automation task for Proforma {ProformaNumber} due to missing claims (erjabe: {ErjabeClaim}, Name: {NameClaim}, ID: {IdClaim}). Proforma creation will proceed without task.",
                            nextNumber, erjabeUserIdString, currentUserName, currentUserIdString);
                        // To make task creation mandatory, uncomment the line below:
                        // throw new InvalidOperationException("Failed to create automation task due to missing user claims.");
                    }

                    return nextNumber; // Return generated proforma number

                }, IsolationLevel.Serializable); //ReadCommitted




                _logger.LogInformation("Proforma saved successfully with Number: {ProformaNumber}", generatedProformaNumber);
                return Ok(new ProformaSaveResponseDto { Success = true, ProformaNumber = generatedProformaNumber, Message = $"پیش فاکتور با شماره {generatedProformaNumber} با موفقیت ثبت شد." });
            }
            // Exception handling remains unchanged from previous correct version
            catch (SqlException sqlEx) when (sqlEx.Number == 547)
            {
                _logger.LogError(sqlEx, "Foreign Key constraint violation during proforma save transaction. FK Error Number: {ErrorNumber}", sqlEx.Number);
                string userMessage = "خطا در ارتباط بین جداول هنگام ذخیره پیش فاکتور رخ داد. لطفاً اطلاعات را بررسی کنید.";
                if (sqlEx.Message.Contains("FK_INVO_LST_HEAD_LST")) userMessage = "خطا: ایجاد سربرگ پیش فاکتور ناموفق بود.";
                else if (sqlEx.Message.Contains("FK_INVO_LST_STUF_FSK")) userMessage = "خطا: تعریف کالا/انبار در سیستم ناقص است.";
                else if (sqlEx.Message.Contains("FK_INVO_LST_TCOD_VAHEDS")) userMessage = "خطا: واحد کالای انتخاب شده نامعتبر است.";
                return StatusCode(500, new ProformaSaveResponseDto { Success = false, Message = userMessage });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Operation failed within transaction: {ErrorMessage}", ex.Message);
                if (ex.Message.StartsWith("امکان تعیین نوع مشتری"))
                {
                    return StatusCode(500, new ProformaSaveResponseDto { Success = false, Message = ex.Message });
                }
                return BadRequest(new ProformaSaveResponseDto { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error saving proforma for customer {CustomerHes}.", request.Header?.CustomerHesCode);
                return StatusCode(500, new ProformaSaveResponseDto { Success = false, Message = "خطای پیش‌بینی نشده‌ای هنگام ذخیره پیش فاکتور رخ داد." });
            }
        }


        [HttpGet("{proformaNumber}/pdf")]
        public async Task<IActionResult> GetProformaPdf(double proformaNumber)
        {
            _logger.LogInformation("Request received for Proforma PDF. Number: {ProformaNumber}", proformaNumber);

            if (proformaNumber <= 0)
            {
                return BadRequest("Invalid Proforma Number.");
            }

            try
            {
                // 1. Fetch Data for the report
                var printData = await FetchProformaPrintDataAsync(proformaNumber);
                if (printData == null)
                {
                    _logger.LogWarning("Proforma data not found for PDF generation. Number: {ProformaNumber}", proformaNumber);
                    return NotFound($"پیش فاکتور با شماره {proformaNumber} یافت نشد.");
                }

                // 2. Generate PDF using QuestPDF document class
                _logger.LogInformation("Generating Proforma PDF for Number: {ProformaNumber}", proformaNumber);
                // Inject IWebHostEnvironment if ProformaDocument needs it (e.g., for logo path)
                var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
                var document = new ProformaDocument(printData, _logger, env); // Pass data, logger, env
                byte[] pdfBytes = document.GeneratePdf(); // QuestPDF generates the PDF

                _logger.LogInformation("Proforma PDF generated successfully. Size: {Size} bytes. Number: {ProformaNumber}", pdfBytes.Length, proformaNumber);

                // 3. Return the file
                string fileName = $"Proforma_{proformaNumber.ToString("F0")}_{printData.Header.CUST_NO}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating or returning Proforma PDF for Number: {ProformaNumber}", proformaNumber);
                return StatusCode(StatusCodes.Status500InternalServerError, "خطای داخلی سرور هنگام تولید PDF پیش فاکتور رخ داد.");
            }
        }

        // --- Helper Method to Fetch Data for PDF ---
        private async Task<ProformaPrintDto?> FetchProformaPrintDataAsync(double proformaNumber)
        {
            const string sql = @"
        SELECT TOP 1 -- Header Info
          h.NUMBER, h.DATE_N, h.CUST_NO, h.MOLAH, h.SHARAYET, h.MABL_HAZ, h.TAKHFIF, h.MBAA AS TotalVatAmount,
          c.NAME AS CustomerName, c.ADDRESS AS CustomerAddress, c.TEL AS CustomerTel
        FROM dbo.HEAD_LST h
        LEFT JOIN dbo.CUST_HESAB c ON h.CUST_NO = c.hes
        WHERE h.NUMBER = @ProformaNumber AND h.TAG = @ProformaTag;

        SELECT -- Line Info
          i.RADIF, i.CODE, i.MEGH , i.IMBAA, i.MEGHk, i.MABL, i.MABL_K, i.N_KOL, i.TKHN, i.N_MOIN AS DiscountAmount,
          s.NAME AS ItemName, v.NAMES AS UnitName
        FROM dbo.INVO_LST i
        LEFT JOIN dbo.STUF_DEF s ON i.CODE = s.CODE
        LEFT JOIN dbo.TCOD_VAHEDS v ON i.VAHED_K = v.CODE
        WHERE i.NUMBER = @ProformaNumber AND i.TAG = @ProformaTag
        ORDER BY i.RADIF;
      ";

            try
            {
                using var multi = await _dbService.DoGetDataSQLAsyncMultiple(sql, new { ProformaNumber = proformaNumber, ProformaTag = ProformaTag });

                var header = await multi.ReadSingleOrDefaultAsync<ProformaPrintHeaderDto>();
                if (header == null) return null; // Proforma not found

                var lines = (await multi.ReadAsync<ProformaPrintLineDto>()).ToList();

                var printDto = new ProformaPrintDto
                {
                    Header = header,
                    Lines = lines
                };

                // Calculate Footer Summaries
                printDto.TotalAmountBeforeDiscount = lines.Sum(l => l.MABL_K);
                printDto.TotalDiscountAmount = lines.Sum(l => l.DiscountAmount);
                // VAT is directly from header if calculated/stored there
                printDto.TotalVatAmount = (decimal)lines.Sum(l => l.IMBAA);
                // Total Payable Calculation needs careful checking based on business rules
                // Assuming: (TotalBeforeDiscount - LineDiscounts - HeaderDiscount) + Services + VAT
                decimal totalAfterLineDiscounts = printDto.TotalAmountBeforeDiscount - printDto.TotalDiscountAmount;
                decimal totalAfterHeaderDiscount = totalAfterLineDiscounts - (header.TAKHFIF ?? 0);
                printDto.TotalAmountPayable = totalAfterHeaderDiscount + (header.MABL_HAZ ?? 0) + printDto.TotalVatAmount;

                // Optional: Generate AmountInWords here if library is available
                long amountToConvert = (long)Math.Max(0, Math.Round(printDto.TotalAmountPayable)); // Convert to long, ensure non-negative
                printDto.AmountInWords = CL_HESABDARI.ALPHANUM(amountToConvert); // Assuming you add 'using Humanizer;'
                // Append currency unit if needed
                if (!string.IsNullOrEmpty(printDto.AmountInWords) && printDto.AmountInWords != "صفر")
                {
                    printDto.AmountInWords += " ریال"; // Or تومان, etc.
                }

                return printDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching proforma data for printing. Number: {ProformaNumber}", proformaNumber);
                return null;
            }
        }

    }
}