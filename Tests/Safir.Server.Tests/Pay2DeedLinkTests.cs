using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Safir.Server.Services;
using Xunit;

namespace Safir.Server.Tests;

/// <summary>
/// فقط وقتی اجرا می‌شود که <c>SAFIR_TEST_SQL</c> یک connection string به یک SQL Server
/// (ترجیحاً tempdb) باشد؛ وگرنه skip می‌شود، نه fail. جدول‌های آزمایشی داخل یک
/// تراکنش ساخته می‌شوند و در پایان Rollback می‌شوند؛ چیزی روی سرور نمی‌ماند.
/// skip یعنی «هنوز آزمایش نشده»، نه «سبز».
/// </summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAFIR_TEST_SQL")))
            Skip = "SAFIR_TEST_SQL تنظیم نشده (connection string به SQL Server، ترجیحاً tempdb).";
    }
}

/// <summary>
/// رگرسیونِ باگ «لغو صدور سندِ حقوق چیزی را پاک نمی‌کند و بازصدور سندِ دوم می‌سازد»:
/// WPF شماره‌ی سند (N_S) را بازشماره‌گذاری می‌کند ولی base ثابت می‌ماند؛ پیوند باید با base باشد.
/// </summary>
public class Pay2DeedLinkTests
{
    private const long Month = 14050500;
    private static readonly string Title = Pay2DeedLink.Title(Month);

    private static async Task<T> InScratchAsync<T>(Func<SqlConnection, SqlTransaction, Task<T>> body)
    {
        await using var conn = new SqlConnection(Environment.GetEnvironmentVariable("SAFIR_TEST_SQL"));
        await conn.OpenAsync();

        if (await conn.ExecuteScalarAsync<int?>("SELECT OBJECT_ID('DEED_HED')") != null)
            throw new InvalidOperationException("DEED_HED از قبل هست؛ SAFIR_TEST_SQL باید به دیتابیسِ خالی (مثل tempdb) اشاره کند نه دیتابیس واقعی.");

        await using var tran = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            await conn.ExecuteAsync(@"
                CREATE TABLE DEED_HED (N_S FLOAT NOT NULL PRIMARY KEY, base INT IDENTITY(1,1) NOT NULL, SHARH_S NVARCHAR(255) NULL);
                CREATE TABLE PAY2_PERIOD (PER_ID INT NOT NULL PRIMARY KEY, WS_ID INT NOT NULL, PERIOD_DATE BIGINT NOT NULL, DEED_BASE INT NULL);", transaction: tran);
            return await body(conn, tran);
        }
        finally
        {
            await tran.RollbackAsync();
        }
    }

    private static Task<int> AddDeed(SqlConnection c, SqlTransaction t, double ns, string title) =>
        c.ExecuteScalarAsync<int>(
            "INSERT DEED_HED (N_S, SHARH_S) VALUES (@ns, @title); SELECT SCOPE_IDENTITY();", new { ns, title }, t);

    private static Task AddPeriod(SqlConnection c, SqlTransaction t, int perId, int wsId, int? deedBase) =>
        c.ExecuteAsync("INSERT PAY2_PERIOD (PER_ID, WS_ID, PERIOD_DATE, DEED_BASE) VALUES (@perId, @wsId, @Month, @deedBase)",
            new { perId, wsId, Month, deedBase }, t);

    [SqlFact]
    public async Task Link_follows_the_doc_when_WPF_renumbers_it()
    {
        var found = await InScratchAsync(async (c, t) =>
        {
            int b = await AddDeed(c, t, 100, Title);
            await AddPeriod(c, t, 1, 1, b);
            // بازشماره‌گذاری WPF: شماره عوض می‌شود، base نه.
            await c.ExecuteAsync("UPDATE DEED_HED SET N_S = 250 WHERE base = @b", new { b }, t);
            return await Pay2DeedLink.FindAsync(c, t, 1, Month);
        });

        Assert.Equal(250d, found);
    }

    [SqlFact]
    public async Task Renumbered_doc_is_not_confused_with_another_doc_that_took_the_old_number()
    {
        var found = await InScratchAsync(async (c, t) =>
        {
            int b = await AddDeed(c, t, 100, Title);
            await AddPeriod(c, t, 1, 1, b);
            await c.ExecuteAsync("UPDATE DEED_HED SET N_S = 250 WHERE base = @b", new { b }, t);
            await AddDeed(c, t, 100, "سندِ دیگرِ حسابداری"); // شماره‌ی قدیمیِ سند حالا مالِ سندِ دیگری است
            return await Pay2DeedLink.FindAsync(c, t, 1, Month);
        });

        Assert.Equal(250d, found);
    }

    [SqlFact]
    public async Task No_doc_means_null()
    {
        var found = await InScratchAsync(async (c, t) =>
        {
            await AddPeriod(c, t, 1, 1, null);
            await AddDeed(c, t, 7, "سند نامربوط");
            return await Pay2DeedLink.FindAsync(c, t, 1, Month);
        });

        Assert.Null(found);
    }

    [SqlFact]
    public async Task Single_unlinked_doc_with_the_exact_title_is_adopted()
    {
        var found = await InScratchAsync(async (c, t) =>
        {
            await AddDeed(c, t, 4860, Title);
            await AddPeriod(c, t, 1, 1, null);
            return await Pay2DeedLink.FindAsync(c, t, 1, Month);
        });

        Assert.Equal(4860d, found);
    }

    [SqlFact]
    public async Task Two_unlinked_docs_with_the_title_are_refused_and_named()
    {
        var ex = await InScratchAsync(async (c, t) =>
        {
            await AddDeed(c, t, 5530, Title);
            await AddDeed(c, t, 5639, Title);
            await AddPeriod(c, t, 1, 1, null);
            return await Assert.ThrowsAsync<InvalidOperationException>(() => Pay2DeedLink.FindAsync(c, t, 1, Month));
        });

        Assert.Contains("5530", ex.Message);
        Assert.Contains("5639", ex.Message);
    }

    [SqlFact]
    public async Task Doc_linked_to_another_workshops_period_is_never_taken()
    {
        var found = await InScratchAsync(async (c, t) =>
        {
            int b = await AddDeed(c, t, 300, Title);
            await AddPeriod(c, t, 1, 1, b);      // کارگاه ۱ سندش را دارد
            await AddPeriod(c, t, 2, 2, null);   // کارگاه ۲ هنوز ندارد
            return await Pay2DeedLink.FindAsync(c, t, 2, Month);
        });

        Assert.Null(found);
    }

    [SqlFact]
    public async Task Orphan_is_refused_when_several_workshops_share_the_month()
    {
        await InScratchAsync(async (c, t) =>
        {
            await AddDeed(c, t, 400, Title);     // یتیم؛ عنوان کارگاه ندارد
            await AddPeriod(c, t, 1, 1, null);
            await AddPeriod(c, t, 2, 2, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Pay2DeedLink.FindAsync(c, t, 1, Month));
            return 0;
        });
    }
}
