using Dapper;
using Safir.Shared.Interfaces;
using System.Data;
using Microsoft.Extensions.Configuration; // To get connection string
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Collections.Generic; // For IEnumerable
using System.Data.SqlClient;

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
    }
}