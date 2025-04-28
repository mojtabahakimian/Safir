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

    }
}