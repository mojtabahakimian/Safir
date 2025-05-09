using Dapper;
using Safir.Shared.Models;
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models.Taarif;
using Safir.Shared.Models.User_Model;
using System.Data;

namespace Safir.Shared.Interfaces
{
    public interface IDatabaseService
    {
        Task<IEnumerable<TEntity>> DoGetDataSQLAsync<TEntity>(string sql, object parameters = null);
        Task<int> DoExecuteSQLAsync(string sql, object parameters = null);

        Task<TEntity> DoGetDataSQLAsyncSingle<TEntity>(string sql, object? parameters = null);
        Task<IEnumerable<TEntity>> DoGetStoreProcedureSQLAsync<TEntity>(string storedProcedureName, object parameters = null, int commandTimeout = 30);


        // --- ADDED METHOD FOR TRANSACTIONS ---
        /// <summary>
        /// Executes a series of database actions within a single transaction.
        /// </summary>
        /// <param name="actions">An asynchronous function containing the database operations to perform, accepting the connection and transaction.</param>
        /// <param name="isolationLevel">Optional transaction isolation level.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ExecuteInTransactionAsync(Func<IDbConnection, IDbTransaction, Task> actions, IsolationLevel isolationLevel = IsolationLevel.RepeatableRead);

        // --- ADDED METHOD FOR TRANSACTIONS WITH RETURN VALUE ---
        /// <summary>
        /// Executes a series of database actions within a single transaction and returns a result.
        /// </summary>
        /// <typeparam name="TResult">The type of the result to return.</typeparam>
        /// <param name="actions">An asynchronous function containing the database operations, accepting connection and transaction, and returning a result.</param>
        /// <param name="isolationLevel">Optional transaction isolation level.</param>
        /// <returns>The result returned by the actions function.</returns>
        Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IDbConnection, IDbTransaction, Task<TResult>> actions, IsolationLevel isolationLevel = IsolationLevel.RepeatableRead);


        /* --- متد جدید برای موجودی --- */
        Task<decimal?> GetItemInventoryAsync(string itemCode);

        Task<InventoryDetailsDto?> GetItemInventoryDetailsAsync(string itemCode, int anbarCode); // New Method


        Task<SqlMapper.GridReader> DoGetDataSQLAsyncMultiple(string sql, object? parameters = null);


        Task<UserDefaultDep?> GetUserDefaultDepAsync(int userId); // متد جدید

        Task<IEnumerable<LookupDto<int>>> GetCustomerKindsAsync();
        Task<CustomerHesabInfo?> GetCustomerHesabInfoByHesCodeAsync(string customerHesCode);
        Task<IEnumerable<LookupDto<int>>> GetDepartmentsAsync();
        Task<IEnumerable<PaymentTermDto>> GetPaymentTermsAsync();
        Task<int?> GetDefaultPaymentTermIdForUserAsync(int userId); // از SALA_DTL.DEFAULT_NAHVA
        Task<IEnumerable<PriceListDto>> GetPriceListsAsync();
        Task<int?> GetDefaultPriceListIdAsync(long currentDate, int departmentId);
        Task<IEnumerable<DiscountListDto>> GetDiscountListsAsync();
        Task<int?> GetDefaultDiscountListIdAsync(long currentDate, int departmentId);

        Task<IEnumerable<PaymentTermDto>> GetDynamicPaymentTermsAsync(int? departmentId, int? selectedDiscountListId, long currentDate);
        Task<int?> GetLatestDiscountListIdAsync(long currentDate, int departmentId); // برای کمک به یافتن PEID مناسب


    }
}
