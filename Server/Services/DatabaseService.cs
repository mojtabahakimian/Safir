using Dapper;
using Safir.Shared.Interfaces;
using System.Data;
using Microsoft.Extensions.Configuration; // To get connection string
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Collections.Generic; // For IEnumerable
using System.Data.SqlClient;
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.User_Model;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models.Taarif;
using Safir.Shared.Models;

namespace Safir.Server.Services
{
    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            // Ensure connection string is not null or empty
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                                ?? throw new InvalidOperationException("Database connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        // --- Existing Methods (DoGetDataSQLAsync, etc.) ---
        // ... (Keep existing method implementations) ...
        public async Task<IEnumerable<TEntity>> DoGetDataSQLAsync<TEntity>(string sql, object? parameters = null)
        {
            try
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                // No need to manually open Dapper does it
                var result = await db.QueryAsync<TEntity>(sql, parameters);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DoGetDataSQLAsync running SQL: {Sql}", sql);
                throw; // Re-throw to allow caller to handle
            }
        }

        public async Task<TEntity> DoGetDataSQLAsyncSingle<TEntity>(string sql, object? parameters = null)
        {
            try
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                // No need to manually open Dapper does it
                // Use QuerySingleOrDefaultAsync which returns default(TEntity) if no rows or >1 row error is not desired
                var result = await db.QuerySingleOrDefaultAsync<TEntity>(sql, parameters);
                return result;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Sequence contains no elements"))
            {
                _logger.LogWarning("DoGetDataSQLAsyncSingle expected a single result but found none for SQL: {Sql}", sql);
                return default; // Return default value (e.g., null for reference types, 0 for int)
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Sequence contains more than one element"))
            {
                _logger.LogError(ex, "DoGetDataSQLAsyncSingle expected a single result but found multiple for SQL: {Sql}", sql);
                throw; // Re-throw as this indicates unexpected data
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DoGetDataSQLAsyncSingle running SQL: {Sql}", sql);
                throw;
            }
        }

        public async Task<int> DoExecuteSQLAsync(string sql, object? parameters = null)
        {
            try
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                // No need to manually open Dapper does it
                int rowsAffected = await db.ExecuteAsync(sql, parameters);
                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DoExecuteSQLAsync running SQL: {Sql}", sql);
                throw;
            }
        }

        public async Task<IEnumerable<TEntity>> DoGetStoreProcedureSQLAsync<TEntity>(string storedProcedureName, object? parameters = null, int commandTimeout = 30)
        {
            try
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                // No need to manually open Dapper does it
                return await db.QueryAsync<TEntity>(
                       storedProcedureName,
                       parameters,
                       commandType: CommandType.StoredProcedure,
                       commandTimeout: commandTimeout);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing stored procedure: {Sp}", storedProcedureName);
                throw;
            }
        }

        // --- ADDED Transaction Method Implementation ---
        public async Task ExecuteInTransactionAsync(Func<IDbConnection, IDbTransaction, Task> actions, IsolationLevel isolationLevel = IsolationLevel.RepeatableRead)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(isolationLevel);

            try
            {
                await actions(connection, transaction); // Execute the user-provided actions
                await transaction.CommitAsync();
                _logger.LogInformation("Transaction committed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred within transaction. Rolling back.");
                try
                {
                    await transaction.RollbackAsync();
                }
                catch (Exception rbEx)
                {
                    _logger.LogError(rbEx, "Error rolling back transaction.");
                }
                throw; // Re-throw the original exception so the caller knows it failed
            }
            // Connection and transaction are disposed by 'using' statements
        }

        // --- ADDED Transaction Method Implementation (with Result) ---
        public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IDbConnection, IDbTransaction, Task<TResult>> actions, IsolationLevel isolationLevel = IsolationLevel.RepeatableRead)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction(isolationLevel);

            try
            {
                TResult result = await actions(connection, transaction); // Execute actions and get result
                await transaction.CommitAsync();
                _logger.LogInformation("Transaction committed successfully.");
                return result; // Return the result
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred within transaction. Rolling back.");
                try
                {
                    await transaction.RollbackAsync();
                }
                catch (Exception rbEx)
                {
                    _logger.LogError(rbEx, "Error rolling back transaction.");
                }
                throw; // Re-throw the original exception
            }
        }

        /* ---------- متد جدید برای موجودی انبار ---------- */
        public async Task<decimal?> GetItemInventoryAsync(string itemCode)
        {
            const string sql = @"
                DECLARE @inv DECIMAL(18,2);
                
                SELECT @inv = ROUND(ISNULL(AK.SMEGH,0) - ISNULL(FR.MEG,0), 2)
                FROM STUF_DEF SD
                JOIN TCODE_MENUITEM TM ON SD.MENUIT = TM.CODE
                JOIN STUF_FSK SF       ON SF.CODE  = SD.CODE  AND SF.ANBAR = TM.ANBAR
                LEFT JOIN AK_MOGO_AVL_KOL(99999999,1) AK ON AK.CODE = SF.CODE AND AK.ANBAR = SF.ANBAR
                LEFT JOIN AK_MOGO_FR (99999999,1)   FR ON FR.CODE = SF.CODE AND FR.ANBAR = SF.ANBAR
                WHERE SD.CODE = @ItemCode;
                
                SELECT @inv AS mand;";

            try
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                var result = await db.ExecuteScalarAsync<decimal?>(sql, new { ItemCode = itemCode });
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting inventory for {ItemCode}", itemCode);
                throw;
            }
        }

        /* --- متد جدید برای جزئیات موجودی (شامل حداقل موجودی) --- */
        public async Task<InventoryDetailsDto?> GetItemInventoryDetailsAsync(string itemCode, int anbarCode)
        {
            // SQL to get both current inventory and minimum inventory (MIN_M from STUF_FSK)
            const string sql = @"
                SELECT
                    ROUND(ISNULL(AK.SMEGH,0) - ISNULL(FR.MEG,0), 2) AS CurrentInventory,
                    FSK.MIN_M AS MinimumInventory
                FROM dbo.STUF_DEF SD
                LEFT JOIN dbo.STUF_FSK FSK ON SD.CODE = FSK.CODE AND FSK.ANBAR = @AnbarCode
                LEFT JOIN dbo.AK_MOGO_AVL_KOL(99999999, @AnbarCode) AK ON AK.CODE = SD.CODE AND AK.ANBAR = @AnbarCode
                LEFT JOIN dbo.AK_MOGO_FR(99999999, @AnbarCode) FR ON FR.CODE = SD.CODE AND FR.ANBAR = @AnbarCode
                WHERE SD.CODE = @ItemCode;";

            try
            {
                using IDbConnection db = new SqlConnection(_connectionString);
                // Use QuerySingleOrDefaultAsync as we expect one row per item/anbar combination
                var result = await db.QuerySingleOrDefaultAsync<InventoryDetailsDto>(sql, new { ItemCode = itemCode, AnbarCode = anbarCode });
                // MIN_M in STUF_FSK might be null, handle this
                if (result != null && result.MinimumInventory == null)
                {
                    result.MinimumInventory = 0; // Default to 0 if MIN_M is null
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting inventory details for Item: {ItemCode}, Anbar: {AnbarCode}", itemCode, anbarCode);
                return null; // Return null on error
            }
        }


        public async Task<SqlMapper.GridReader> DoGetDataSQLAsyncMultiple(string sql, object? parameters = null)
        {
            try
            {
                // Create a NEW connection for GridReader as it needs to stay open
                // while the reader is processed. Cannot use 'using' directly here
                // if the GridReader is returned. The caller MUST dispose the GridReader.
                // OR: Keep the connection open within this method and return mapped results (safer).
                // Let's return mapped results for better resource management.

                // *** Option 1: Return GridReader (Caller must dispose) ***
                // var connection = new SqlConnection(_connectionString);
                // await connection.OpenAsync(); // Open explicitly
                // return await connection.QueryMultipleAsync(sql, parameters);
                // WARNING: If returning GridReader, the connection remains open until the reader is disposed by the caller.

                // *** Option 2: Map results here and return specific DTO (Safer) ***
                // This requires knowing the expected result structure in advance or using generics.
                // Since FetchProformaPrintDataAsync knows the structure, let's keep the logic there
                // and just provide the basic QueryMultipleAsync capability if needed elsewhere.
                // For THIS specific use case, FetchProformaPrintDataAsync will handle mapping.
                // So, let's implement the GridReader return for general purpose, assuming caller disposes.

                var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(); // Must open manually for QueryMultipleAsync when connection is managed outside
                try
                {
                    return await connection.QueryMultipleAsync(sql, parameters);
                    // Caller IS RESPONSIBLE for disposing the GridReader AND the connection.
                    // This is generally not recommended. Let's change the approach.
                }
                catch
                {
                    connection.Dispose(); // Ensure connection is disposed on error before returning reader
                    throw;
                }

                // *** Reconsidering: Let's make FetchProformaPrintDataAsync handle its own connection ***
                // This avoids returning an open GridReader and connection.
                // So, no change needed in IDatabaseService or DatabaseService for *this* specific feature.
                // The existing DoGetDataSQLAsync<T> is sufficient if used carefully in the controller.
                // Let's revert FetchProformaPrintDataAsync to use DoGetDataSQLAsync<T> twice.

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DoGetDataSQLAsyncMultiple running SQL: {Sql}", sql);
                throw;
            }
            // If Option 2 was used, map results here and return the DTO.
            // return mappedDto;
        }

        public async Task<UserDefaultDep?> GetUserDefaultDepAsync(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            // انتخاب USERID هم از جدول برای کامل بودن مدل مفید است گرچه اینجا ورودی است
            var sql = "SELECT TFSAZMAN, SHIFT, USERID FROM dbo.DEFAULTDEP WHERE USERID = @UserId";
            return await connection.QuerySingleOrDefaultAsync<UserDefaultDep>(sql, new { UserId = userId });
        }

        #region ELAMIEH_GHEYMAT
        public async Task<IEnumerable<LookupDto<int>>> GetCustomerKindsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = "SELECT CUST_COD AS Id, CUSTKNAME AS Name FROM CUSTKIND ORDER BY CUSTKNAME";
            return await connection.QueryAsync<LookupDto<int>>(sql);
        }

        public async Task<CustomerHesabInfo?> GetCustomerHesabInfoByHesCodeAsync(string customerHesCode)
        {
            using var connection = new SqlConnection(_connectionString);
            // فرض بر این است که customerHesCode همان HES در جدول CUST_HESAB است
            var sql = "SELECT hes AS Hes, CUST_COD AS CustCod FROM dbo.CUST_HESAB WHERE hes = @CustomerHesCode";
            return await connection.QuerySingleOrDefaultAsync<CustomerHesabInfo>(sql, new { CustomerHesCode = customerHesCode });
        }

        public async Task<IEnumerable<LookupDto<int>>> GetDepartmentsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = "SELECT DEPATMAN AS Id, DEPNAME AS Name FROM DEPART ORDER BY DEPNAME"; // نام جدول DEPART بر اساس کد WPF
            return await connection.QueryAsync<LookupDto<int>>(sql);
        }

        public async Task<IEnumerable<PaymentTermDto>> GetPaymentTermsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = "SELECT PPID AS Id, PPAME AS Name, MODAT AS Modat FROM PRICE_PAYNO ORDER BY PPAME";
            return await connection.QueryAsync<PaymentTermDto>(sql);
        }

        public async Task<int?> GetDefaultPaymentTermIdForUserAsync(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = "SELECT DEFAULT_NAHVA FROM dbo.SALA_DTL WHERE IDD = @UserId";
            // اطمینان حاصل کنید که DEFAULT_NAHVA از نوع سازگار با int? است یا تبدیل لازم را انجام دهید
            var result = await connection.QuerySingleOrDefaultAsync<long?>(sql, new { UserId = userId });
            return result.HasValue ? (int?)result.Value : null; // تبدیل از long? به int?
        }

        public async Task<IEnumerable<PriceListDto>> GetPriceListsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = "SELECT PEPID AS Id, PEPNAME AS Name FROM PRICE_ELAMIE ORDER BY PEPNAME DESC";
            return await connection.QueryAsync<PriceListDto>(sql);
        }

        public async Task<int?> GetDefaultPriceListIdAsync(long currentDate, int departmentId)
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = "SELECT TOP 1 PEPID FROM dbo.PRICE_ELAMIE WHERE (PEPDATE <= @CurrentDate) AND (PEPDEPART = @DepartmentId) ORDER BY PEPID DESC";
            return await connection.QuerySingleOrDefaultAsync<int?>(sql, new { CurrentDate = currentDate, DepartmentId = departmentId });
        }

        public async Task<IEnumerable<DiscountListDto>> GetDiscountListsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = "SELECT PEID AS Id, PENAME AS Name FROM PRICE_ELAMIETF ORDER BY PENAME DESC"; // نام جدول بر اساس کد WPF
            return await connection.QueryAsync<DiscountListDto>(sql);
        }

        public async Task<int?> GetDefaultDiscountListIdAsync(long currentDate, int departmentId)
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = "SELECT TOP 1 PEID FROM dbo.PRICE_ELAMIETF WHERE (PEDATE <= @CurrentDate) AND (PEPDEPART = @DepartmentId) ORDER BY PEID DESC";
            return await connection.QuerySingleOrDefaultAsync<int?>(sql, new { CurrentDate = currentDate, DepartmentId = departmentId });
        }

        public async Task<int?> GetLatestDiscountListIdAsync(long currentDate, int departmentId)
        {
            // این کوئری مشابه منطق WPF برای پیدا کردن _PEID_ است
            var sql = @"
        SELECT TOP 1 PEID
        FROM dbo.PRICE_ELAMIETF
        WHERE (PEDATE <= @CurrentDate) AND (PEPDEPART = @DepartmentId)
        ORDER BY PEID DESC"; // یا PEDATE DESC, PEID DESC اگر ترتیب تاریخ اولویت دارد
            try
            {
                using var connection = new SqlConnection(_connectionString);
                return await connection.QuerySingleOrDefaultAsync<int?>(sql, new { CurrentDate = currentDate, DepartmentId = departmentId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching latest discount list ID for Dept {DepartmentId}, Date {CurrentDate}", departmentId, currentDate);
                return null;
            }
        }

        public async Task<IEnumerable<PaymentTermDto>> GetDynamicPaymentTermsAsync(int? departmentId, int? selectedDiscountListId, long currentDate)
        {
            List<PaymentTermDto> paymentTerms = new List<PaymentTermDto>();
            string sql;

            int? targetPeid = selectedDiscountListId;

            // اگر selectedDiscountListId داده نشده ولی departmentId داده شده، آخرین PEID را پیدا کن
            if (!targetPeid.HasValue && departmentId.HasValue)
            {
                targetPeid = await GetLatestDiscountListIdAsync(currentDate, departmentId.Value);
            }

            using var connection = new SqlConnection(_connectionString);

            if (targetPeid.HasValue)
            {
                sql = @"
            SELECT DISTINCT p.PPID AS Id, p.PPAME AS Name, p.MODAT AS Modat
            FROM dbo.PRICE_PAYNO p
            INNER JOIN dbo.PRICE_ELAMIETF_DTL dtl ON p.PPID = dtl.PPID
            WHERE dtl.PEID = @TargetPeid";
                paymentTerms.AddRange(await connection.QueryAsync<PaymentTermDto>(sql, new { TargetPeid = targetPeid.Value }));
            }
            else
            {
                // اگر هیچ PEID مرتبطی پیدا نشد (یا داده نشد)، همه را برگردان یا یک لیست پیش‌فرض
                // در کد WPF اگر _PEID_ و PEID.SelectedValue نبود، همه را برمی‌گرداند.
                // اینجا می‌توانیم همین کار را بکنیم یا تصمیم دیگری بگیریم. فعلا همه را برمی‌گردانیم.
                sql = "SELECT PPID AS Id, PPAME AS Name, MODAT AS Modat FROM dbo.PRICE_PAYNO ORDER BY PPAME";
                paymentTerms.AddRange(await connection.QueryAsync<PaymentTermDto>(sql));
            }

            // اضافه کردن گزینه "آزاد" اگر قبلا وجود نداشته باشد
            if (!paymentTerms.Any(pt => pt.Id == 0))
            {
                paymentTerms.Add(new PaymentTermDto { Id = 0, Name = "آزاد", Modat = 0 });
            }
            return paymentTerms.OrderBy(p => p.Name).ToList(); // مرتب سازی نهایی
        }
        #endregion
    }
}