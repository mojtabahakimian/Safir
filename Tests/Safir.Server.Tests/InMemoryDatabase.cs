using System.Data;
using Dapper;
using Safir.Shared.Models;
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models.Taarif;
using Safir.Shared.Models.User_Model;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Permissions;

namespace Safir.Server.Tests;

/// <summary>
/// یک دیتابیس درون‌حافظه‌ای که دقیقاً به همان کوئری‌هایی جواب می‌دهد که
/// کد واقعی می‌فرستد.
///
/// چرا: SQL Server در این محیط قابل نصب نیست، ولی می‌خواهیم مسیر کامل
/// «درخواست HTTP ← بررسی توکن ← اتریبیوت ← سرویس دسترسی ← تصمیم مجوز»
/// را با کد واقعی اجرا کنیم. تنها چیزی که اینجا شبیه‌سازی می‌شود موتور
/// SQL است؛ Pay2AccessService و Pay2AuthorizeAttribute و کنترلرها همگی
/// همان کد تولید هستند.
///
/// محتوای جدول‌ها همان چیزی است که
/// Server/Database/test_auth_and_acl_users.sql می‌سازد.
/// </summary>
public sealed class InMemoryDatabase : IDatabaseService
{
    public sealed record FormPerm(string Form, string Caption,
                                  bool Run, bool See, bool Inp, bool Upd, bool Del);

    /// <summary>PAY2_CONFIG — کلیدهای مربوط به کنترل دسترسی.</summary>
    public Dictionary<string, string> Config { get; } = new()
    {
        ["ACL_ENFORCE"] = "1",
        ["ACL_WS_SCOPE_ENFORCE"] = "1",
        ["ACL_CACHE_SECONDS"] = "1",     // در تست کش کوتاه باشد
        ["ACL_AUDIT_DENIED"] = "1",
        ["ACL_AUDIT_SENSITIVE"] = "1",
    };

    /// <summary>PAY2_USER_WS — کارگاه‌های مجاز هر کاربر.</summary>
    public Dictionary<int, List<int>> UserWorkshops { get; } = new();

    /// <summary>SAL_CHEK به‌همراه TFORMS — مجوزهای هر کاربر روی هر فرم.</summary>
    public Dictionary<int, List<FormPerm>> UserForms { get; } = new();

    /// <summary>PAY2_WORKSHOP — همه‌ی کارگاه‌های فعال.</summary>
    public List<int> AllWorkshops { get; } = new() { 1, 2, 3 };

    /// <summary>هر INSERT ای که در PAY2_SEC_AUDIT نوشته شده.</summary>
    public List<object?> AuditWrites { get; } = new();

    public Task<IEnumerable<TEntity>> DoGetDataSQLAsync<TEntity>(string sql, object? parameters = null)
    {
        int UserCo() => (int)(parameters!.GetType().GetProperty("userCo")!.GetValue(parameters)!);

        // PAY2_CONFIG — نوع مقصد داخل Pay2AccessService خصوصی است، پس مثل
        // خود Dapper با انعکاس می‌سازیمش و ستون‌ها را روی خصوصیات می‌ریزیم.
        if (sql.Contains("PAY2_CONFIG"))
            return Rows<TEntity>(Config.Select(kv =>
                Materialize<TEntity>(("CFG_KEY", kv.Key), ("CFG_VALUE", kv.Value))));

        // کارگاه‌های مجاز کاربر
        if (sql.Contains("PAY2_USER_WS"))
            return Rows<TEntity>(
                (UserWorkshops.TryGetValue(UserCo(), out var ws) ? ws : new List<int>())
                .Select(x => (object)x));

        if (sql.Contains("PAY2_WORKSHOP"))
        {
            IEnumerable<int> ids = AllWorkshops;

            // کوئری‌هایی که محدوده را اعمال می‌کنند @noScope و @allowedWsIds
            // می‌فرستند. بدون پیاده‌سازی این فیلتر، آزمونِ «فهرست محدود
            // می‌ماند» همیشه سبز می‌شد و چیزی را ثابت نمی‌کرد.
            var noScopeProp = parameters?.GetType().GetProperty("noScope");
            if (noScopeProp is not null && !(bool)noScopeProp.GetValue(parameters)!)
            {
                var allowed = ((IEnumerable<int>)parameters!.GetType()
                    .GetProperty("allowedWsIds")!.GetValue(parameters)!).ToHashSet();
                ids = ids.Where(allowed.Contains);
            }

            // سه شکل مصرف: شناسه‌ی خام (محاسبه‌ی محدوده)، DTO کامل (فهرست
            // کارگاه‌ها)، و dynamic (اندپوینت مدیریت دسترسی — Dapper آنجا
            // DapperRow می‌دهد و دیکشنری نزدیک‌ترین معادلِ قابل ساخت است).
            if (typeof(TEntity) == typeof(int))
                return Rows<TEntity>(ids.Select(x => (object)x));

            if (typeof(TEntity) == typeof(object))
                return Rows<TEntity>(ids.Select(id => (object)new Dictionary<string, object>
                {
                    ["WS_ID"] = id,
                    ["WS_NAME"] = $"کارگاه {id}",
                }));

            return Rows<TEntity>(ids.Select(id =>
                Materialize<TEntity>(("WS_ID", id), ("WS_NAME", $"کارگاه {id}"))));
        }

        // TFORMS + SAL_CHEK
        if (sql.Contains("TFORMS"))
            return Rows<TEntity>(
                (UserForms.TryGetValue(UserCo(), out var f) ? f : new List<FormPerm>())
                .Select(p => (object)new Pay2FormPermDto
                {
                    FormName = p.Form, Caption = p.Caption,
                    Run = p.Run, See = p.See, Inp = p.Inp, Upd = p.Upd, Del = p.Del
                }));

        throw new NotSupportedException(
            $"کوئری پیش‌بینی‌نشده در دیتابیس تست:\n{sql}");
    }

    private static Task<IEnumerable<TEntity>> Rows<TEntity>(IEnumerable<object> items)
        => Task.FromResult(items.Cast<TEntity>());

    /// <summary>ساخت یک سطر با انعکاس — همان کاری که Dapper می‌کند.</summary>
    private static object Materialize<TEntity>(params (string Column, object? Value)[] columns)
    {
        var row = Activator.CreateInstance(typeof(TEntity), nonPublic: true)
                  ?? throw new InvalidOperationException(
                      $"نمی‌توان نمونه‌ای از {typeof(TEntity).Name} ساخت.");
        foreach (var (column, value) in columns)
            typeof(TEntity).GetProperty(column)?.SetValue(row, value);
        return row;
    }

    public Task<int> DoExecuteSQLAsync(string sql, object? parameters = null, int? commandTimeout = null)
    {
        if (sql.Contains("PAY2_SEC_AUDIT")) AuditWrites.Add(parameters);
        return Task.FromResult(1);
    }

    // ── بقیه‌ی متدهای رابط در این تست‌ها استفاده نمی‌شوند ──────────────
    private static Exception Unused([System.Runtime.CompilerServices.CallerMemberName] string m = "")
        => new NotSupportedException($"{m} در دیتابیس تست پیاده‌سازی نشده است.");

    public Task<decimal?> GetItemInventoryAsync(string itemCode) => throw Unused();
    public Task<InventoryDetailsDto?> GetItemInventoryDetailsAsync(string itemCode, int anbarCode) => throw Unused();
    public Task<int?> GetDefaultPaymentTermIdForUserAsync(int userId) => throw Unused();
    public Task<int?> GetLatestDiscountListIdAsync(long currentDate, int departmentId) => throw Unused();

    /// <summary>
    /// موقع ورود صدا زده می‌شود. AuthController خطایش را می‌گیرد و ادامه
    /// می‌دهد، ولی برگرداندن null تمیزتر از پرتاب استثناست.
    /// </summary>
    public Task<UserDefaultDep?> GetUserDefaultDepAsync(int userId)
        => Task.FromResult<UserDefaultDep?>(null);
    public Task<IEnumerable<InventoryDetailsDto>> GetItemsInventoryDetailsAsync(IEnumerable<string> itemCodes, int anbarCode) => throw Unused();
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

    public Task<TEntity> DoGetDataSQLAsyncSingle<TEntity>(string sql, object? parameters = null) => throw Unused();
    public Task<IEnumerable<TEntity>> DoGetStoreProcedureSQLAsync<TEntity>(string sp, object? p = null, int t = 30) => throw Unused();
    public Task ExecuteInTransactionAsync(Func<IDbConnection, IDbTransaction, Task> a, IsolationLevel i = IsolationLevel.RepeatableRead) => throw Unused();
    public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IDbConnection, IDbTransaction, Task<TResult>> a, IsolationLevel i = IsolationLevel.RepeatableRead) => throw Unused();
}
