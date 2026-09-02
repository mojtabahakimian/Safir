using System.Data;
using Dapper;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models.Taarif;
using Safir.Shared.Models.User_Model;
using Safir.Shared.Models.CostClose;

namespace Safir.Server.Tests;

/// <summary>
/// دیتابیس ساختگی برای اجرای واقعیِ CloseOrchestrator بدون SQL Server.
///
/// چرا: ارکستریتور تنها جایی است که حلقه‌ی همگرایی S07A↔S11، ترتیب صف
/// بازتولید، و رفتار دروازه/خطا در آن تصمیم‌گیری می‌شود — و همه‌ی این
/// تصمیم‌ها C# خالص‌اند، نه T-SQL. تنها چیزی که اینجا شبیه‌سازی می‌شود
/// موتور SQL است؛ خودِ CloseOrchestrator کد تولید است.
///
/// آنچه پاسخ داده می‌شود دقیقاً همان چند کوئری‌ای است که ارکستریتور
/// می‌فرستد (CC_Run، CC_ItemCost، STUF_DEF) — هر کوئری پیش‌بینی‌نشده‌ای
/// استثنا می‌دهد تا تغییرِ بی‌صدا در ارکستریتور از چشم تست پنهان نماند.
/// </summary>
public sealed class CostCloseFakeDatabase : IDatabaseService
{
    public CostRunDto Run { get; set; } = new()
    {
        RunId = 1,
        FiscalYear = 1405,
        PeriodMonth = 5,
        DateFrom = 14050501,
        DateTo = 14050531,
        RunKind = 0,
        Status = (byte)CostRunStatus.Draft,
    };

    /// <summary>
    /// عکس‌های پیاپیِ CC_ItemCost. هر بار که ارکستریتور نرخ‌ها را
    /// می‌خواند یکی مصرف می‌شود؛ پس از پایان فهرست، آخرین عکس تکرار
    /// می‌شود (یعنی «همگرا شد»).
    /// </summary>
    public Queue<Dictionary<long, double>> RateSnapshots { get; } = new();

    /// <summary>
    /// اگر ست شود، به‌جای صفِ بالا برای هر خواندن یک عکس تازه ساخته
    /// می‌شود — برای آزمودن حالتی که هرگز همگرا نمی‌شود.
    /// </summary>
    public Func<int, Dictionary<long, double>>? RateGenerator { get; set; }

    private Dictionary<long, double> _lastSnapshot = new();
    private int _rateReads;

    /// <summary>نامِ رویه‌هایی که باید استثنا بدهند — برای آزمودن مسیر خطا.</summary>
    public HashSet<string> ThrowOnProcedure { get; } = new();

    public List<string> ProcedureCalls { get; } = new();
    public List<CostRunStatus> StatusWrites { get; } = new();
    public List<(byte Severity, string Message)> Logs { get; } = new();

    public Task<TEntity> DoGetDataSQLAsyncSingle<TEntity>(string sql, object? parameters = null)
    {
        if (sql.Contains("CC_Run"))
            return Task.FromResult((TEntity)(object)Run);

        // نامِ کالا برای پیام لاگِ همگرایی
        if (sql.Contains("STUF_DEF"))
            return Task.FromResult((TEntity)(object)"کالای آزمایشی");

        throw new NotSupportedException($"کوئری پیش‌بینی‌نشده:\n{sql}");
    }

    public Task<IEnumerable<TEntity>> DoGetDataSQLAsync<TEntity>(string sql, object? parameters = null)
    {
        if (sql.Contains("CC_ItemCost"))
        {
            var snap = RateGenerator is not null
                ? RateGenerator(_rateReads)
                : RateSnapshots.Count > 0 ? RateSnapshots.Dequeue() : _lastSnapshot;

            _rateReads++;
            _lastSnapshot = snap;

            IEnumerable<object> rows = snap.Select(kv => (object)(kv.Key, kv.Value));
            return Task.FromResult(rows.Cast<TEntity>());
        }

        throw new NotSupportedException($"کوئری پیش‌بینی‌نشده:\n{sql}");
    }

    public Task<int> DoExecuteSQLAsync(string sql, object? parameters = null, int? commandTimeout = null)
    {
        if (sql.Contains("UPDATE dbo.CC_Run"))
            StatusWrites.Add((CostRunStatus)Read<byte>(parameters, "st"));
        else if (sql.Contains("CC_RunLog"))
            Logs.Add((Read<byte>(parameters, "sev"), Read<string>(parameters, "msg") ?? ""));
        else
            throw new NotSupportedException($"دستور پیش‌بینی‌نشده:\n{sql}");

        return Task.FromResult(1);
    }

    public Task<IEnumerable<TEntity>> DoGetStoreProcedureSQLAsync<TEntity>(
        string storedProcedureName, object? parameters = null, int commandTimeout = 30)
    {
        ProcedureCalls.Add(storedProcedureName);

        if (ThrowOnProcedure.Contains(storedProcedureName))
            throw new InvalidOperationException($"خطای ساختگی در {storedProcedureName}");

        return Task.FromResult(Enumerable.Empty<TEntity>());
    }

    private static T? Read<T>(object? parameters, string name)
    {
        var prop = parameters?.GetType().GetProperty(name);
        return prop is null ? default : (T?)prop.GetValue(parameters);
    }

    // ── بقیه‌ی متدهای رابط در این تست‌ها استفاده نمی‌شوند ──────────────
    private static Exception Unused([System.Runtime.CompilerServices.CallerMemberName] string m = "")
        => new NotSupportedException($"{m} در دیتابیس تست پیاده‌سازی نشده است.");

    public Task<decimal?> GetItemInventoryAsync(string itemCode) => throw Unused();
    public Task<InventoryDetailsDto?> GetItemInventoryDetailsAsync(string itemCode, int anbarCode) => throw Unused();
    public Task<IEnumerable<InventoryDetailsDto>> GetItemsInventoryDetailsAsync(IEnumerable<string> itemCodes, int anbarCode) => throw Unused();
    public Task<int?> GetDefaultPaymentTermIdForUserAsync(int userId) => throw Unused();
    public Task<int?> GetLatestDiscountListIdAsync(long currentDate, int departmentId) => throw Unused();
    public Task<UserDefaultDep?> GetUserDefaultDepAsync(int userId) => throw Unused();
    public Task<SqlMapper.GridReader> DoGetDataSQLAsyncMultiple(string sql, object? parameters = null) => throw Unused();
    public Task<IEnumerable<LookupDto<int>>> GetCustomerKindsAsync() => throw Unused();
    public Task<CustomerHesabInfo?> GetCustomerHesabInfoByHesCodeAsync(string customerHesCode) => throw Unused();
    public Task<IEnumerable<LookupDto<int>>> GetDepartmentsAsync() => throw Unused();
    public Task<IEnumerable<PaymentTermDto>> GetPaymentTermsAsync() => throw Unused();
    public Task<IEnumerable<PriceListDto>> GetPriceListsAsync() => throw Unused();
    public Task<int?> GetDefaultPriceListIdAsync(long currentDate, int departmentId) => throw Unused();
    public Task<IEnumerable<DiscountListDto>> GetDiscountListsAsync() => throw Unused();
    public Task<int?> GetDefaultDiscountListIdAsync(long currentDate, int departmentId) => throw Unused();
    public Task<IEnumerable<PaymentTermDto>> GetDynamicPaymentTermsAsync(int? departmentId, int? selectedDiscountListId, long currentDate) => throw Unused();
    public Task<IEnumerable<TCOD_ANBAR>> GetUserAnbarhaAsync(int userId) => throw Unused();
    public Task<string?> GetUserStateJsonAsync(int userId) => throw Unused();
    public Task SaveUserStateJsonAsync(int userId, string stateJson) => throw Unused();
    public Task ClearUserStateAsync(int userId) => throw Unused();
    public Task ExecuteInTransactionAsync(Func<IDbConnection, IDbTransaction, Task> a, IsolationLevel i = IsolationLevel.RepeatableRead) => throw Unused();
    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IDbConnection, IDbTransaction, Task<TResult>> a, IsolationLevel i = IsolationLevel.RepeatableRead) => throw Unused();
}
