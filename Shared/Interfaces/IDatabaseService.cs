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

    }
}
