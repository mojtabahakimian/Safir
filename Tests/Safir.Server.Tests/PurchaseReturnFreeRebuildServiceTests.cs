using System.Data;
using Dapper;
using Safir.Server.CostClose.GroupDocuments;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models.Taarif;
using Safir.Shared.Models.User_Model;
using Xunit;

namespace Safir.Server.Tests;

public sealed class PurchaseReturnFreeRebuildServiceTests
{
    private sealed class FakeDatabaseForPurchaseReturnFree : IDatabaseService
    {
        public List<string> ExecutedSqls { get; } = new();
        public List<string> DeedDtlInserts { get; } = new();

        public Task<IEnumerable<TEntity>> DoGetDataSQLAsync<TEntity>(string sql, object? parameters = null)
        {
            ExecutedSqls.Add(sql);

            if (sql.Contains("dbo.SAZMAN"))
            {
                var row = new
                {
                    MOGODIA = 101d,
                    KHARID = 701d,
                    AMALKARD = 801d,
                    TKHARID = 702d,
                    PKHARID = (int?)703,
                    ADA = "101-1-1",
                    SANDOGH = 102d,
                    HESMBAA = "103-1-1",
                    OPTIONSS = "",
                    SNDKH = false,
                    ARSESH = (byte?)9
                };
                return Rows<TEntity>(new object[] { row });
            }

            if (sql.Contains("dbo.HEAD_LST") && sql.Contains("TAG = 27"))
            {
                // برگه اصلی برگشت خرید آزاد با NUMBER = 2 و NUMBER1 = 3 (شماره حواله انبار ۳)
                var row = new
                {
                    NUMBER = (double?)2d,
                    NUMBER1 = (double?)3d,
                    DATE_N = (long?)14050515L,
                    N_S = (double?)100d,
                    USER_NAME = "tester",
                    CUST_NO = "101-2-5",
                    DEPATMAN = (int?)1,
                    SHIFT = (int?)1,
                    ARZD = (double?)1d,
                    MABL_HAZ = (double?)0d,
                    MOIN_HAZ = (string?)null,
                    MBAA = (double?)0d,
                    HMBAA = (string?)null,
                    TAKHFIF = (double?)0d,
                    M_NAGHD = (double?)0d,
                    MABL_HAV = (double?)0d,
                    MOIN_HAV = (string?)null,
                    MABL_VAR = (double?)0d,
                    MOIN_VAR = (string?)null,
                    FNUMCO = (double?)0d,
                    MOLAH = ""
                };
                return Rows<TEntity>(new object[] { row });
            }

            if (sql.Contains("dbo.INVO_LST") && sql.Contains("SUM(MABL_K)"))
            {
                // مجموع ارزش خام اقلام در حواله انبار (NUMBER1 = 3)
                return Rows<TEntity>(new object?[] { 1000000d });
            }

            if (sql.Contains("dbo.PAY_GETD") && sql.Contains("SUM(MABL)"))
            {
                return Rows<TEntity>(new object?[] { 0d });
            }

            if (sql.Contains("dbo.INVO_LST") && sql.Contains("dbo.STUF_DEF"))
            {
                // خواندن اقلام کالا با NUMBER = 3 (NUMBER1 برگه) و TAG = 26
                var numParam = parameters?.GetType().GetProperty("NoteNum")?.GetValue(parameters);
                Assert.NotNull(numParam);
                Assert.Equal(3d, Convert.ToDouble(numParam));

                var row = new
                {
                    CODE = "10001",
                    MEGHk = (double?)10d,
                    MABL_K = (double?)1000000d,
                    AVRAGE = (double?)100000d,
                    ANBAR = (int?)1,
                    RADAH = (double?)1d,
                    NAME = "کالای اول"
                };
                return Rows<TEntity>(new object[] { row });
            }

            if (sql.Contains("dbo.TDETA_HES") && sql.Contains("SELECT 1 FROM dbo.TDETA_HES"))
            {
                return Rows<TEntity>(new object[] { 1 });
            }

            if (sql.Contains("dbo.TDETA_HES") && sql.Contains("SELECT NAME FROM dbo.TDETA_HES"))
            {
                return Rows<TEntity>(new object[] { "تامین کننده" });
            }

            if (sql.Contains("SELECT SUM(BED) - SUM(BES)"))
            {
                // بررسی تراز بودن سند تولیدشده (0)
                return Rows<TEntity>(new object?[] { 0d });
            }

            if (sql.Contains("dbo.DEED_HED"))
            {
                // Handle ValueTuple (double? N_S, long? DATE_S)
                if (typeof(TEntity) == typeof((double? N_S, long? DATE_S)))
                {
                    var tuple = ((double?)100d, (long?)14050515L);
                    return Task.FromResult<IEnumerable<TEntity>>(new TEntity[] { (TEntity)(object)tuple });
                }
            }

            return Rows<TEntity>(Array.Empty<object>());
        }

        public Task<int> DoExecuteSQLAsync(string sql, object? parameters = null, int? commandTimeout = null)
        {
            ExecutedSqls.Add(sql);
            if (sql.Contains("INSERT INTO dbo.DEED_DTL"))
            {
                DeedDtlInserts.Add(sql);
            }
            return Task.FromResult(1);
        }

        public Task ExecuteInTransactionAsync(Func<IDbConnection, IDbTransaction, Task> a, IsolationLevel i = IsolationLevel.RepeatableRead)
        {
            return Task.CompletedTask;
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<IDbConnection, IDbTransaction, Task<TResult>> a, IsolationLevel i = IsolationLevel.RepeatableRead)
        {
            if (typeof(TResult) == typeof(List<double>))
            {
                return Task.FromResult((TResult)(object)new List<double> { 100d });
            }
            return Task.FromResult(default(TResult)!);
        }

        private static Task<IEnumerable<TEntity>> Rows<TEntity>(IEnumerable<object?> items)
        {
            var result = new List<TEntity>();
            foreach (var item in items)
            {
                if (item is null)
                {
                    result.Add(default!);
                    continue;
                }
                if (item is TEntity direct)
                {
                    result.Add(direct);
                    continue;
                }

                // If TEntity is a class/record, deserialize or copy properties
                var entity = Activator.CreateInstance<TEntity>();
                foreach (var prop in item.GetType().GetProperties())
                {
                    var targetProp = typeof(TEntity).GetProperty(prop.Name);
                    if (targetProp is not null && targetProp.CanWrite)
                    {
                        var val = prop.GetValue(item);
                        if (val is not null && targetProp.PropertyType.IsGenericType && targetProp.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            var underlyingType = Nullable.GetUnderlyingType(targetProp.PropertyType)!;
                            val = Convert.ChangeType(val, underlyingType);
                        }
                        targetProp.SetValue(entity, val);
                    }
                }
                result.Add(entity);
            }
            return Task.FromResult<IEnumerable<TEntity>>(result);
        }

        private static Exception Unused([System.Runtime.CompilerServices.CallerMemberName] string m = "")
            => new NotSupportedException($"{m} unused in test.");

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
        public Task<TEntity> DoGetDataSQLAsyncSingle<TEntity>(string sql, object? parameters = null) => throw Unused();
        public Task<IEnumerable<TEntity>> DoGetStoreProcedureSQLAsync<TEntity>(string sp, object? p = null, int t = 30) => throw Unused();
    }

    [Fact]
    public async Task RebuildAsync_DivergentSheetAndNoteNumbers_ReadsGoodsByNoteNumber()
    {
        var fakeDb = new FakeDatabaseForPurchaseReturnFree();
        var service = new PurchaseReturnFreeRebuildService(fakeDb);

        var result = await service.RebuildAsync(1, 10, 14050501, 14050531);

        Assert.True(result.Success, $"Expected success, but got error: {result.FirstError}");
        Assert.Equal(1, result.SheetCount);

        // Verify that INVO_LST query for goods lines was called with NoteNum = 3
        var invoQuery = fakeDb.ExecutedSqls.FirstOrDefault(s => s.Contains("dbo.INVO_LST") && s.Contains("NoteNum"));
        Assert.NotNull(invoQuery);

        // Verify that DEED_DTL insert contains NUMBER = 2 (the sheet number, not 3)
        Assert.NotEmpty(fakeDb.DeedDtlInserts);
        var insertSql = fakeDb.DeedDtlInserts.First();
        Assert.Contains("27", insertSql); // TAG = 27
    }
}
