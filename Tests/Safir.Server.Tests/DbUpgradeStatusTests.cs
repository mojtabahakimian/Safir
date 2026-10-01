using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Safir.Server.Services;
using Safir.Shared.Interfaces;
using Xunit;

namespace Safir.Server.Tests;

[Collection("SqlUpgrade")]
public class DbUpgradeStatusTests
{
    public class SqlDbProxy : DispatchProxy
    {
        public string Connection = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("DoGetDataSQLAsync", method!.Name);
            return typeof(SqlDbProxy).GetMethod(nameof(Query))!.MakeGenericMethod(method.GetGenericArguments()[0])
                .Invoke(this, new object[] { args![0]! });
        }
        public async Task<IEnumerable<T>> Query<T>(string sql)
        {
            using var db = new SqlConnection(Connection);
            return await db.QueryAsync<T>(sql);
        }
    }
    private sealed class ConnectionProvider(string cs) : IConnectionStringProvider
    {
        public string GetConnectionString() => cs;
    }

    [SqlRepairFact]
    public async Task OldDatabaseCanReadStatusBeforeOptionalRuleTableIsCreated()
    {
        var cs = Environment.GetEnvironmentVariable("SAFIR_REPAIR_TEST_CONNECTION")!;
        Assert.StartsWith("SafirTest", new SqlConnectionStringBuilder(cs).InitialCatalog);
        var proxy = DispatchProxy.Create<IDatabaseService, SqlDbProxy>();
        ((SqlDbProxy)(object)proxy).Connection = cs;
        var service = new DbUpgradeService(new ConnectionProvider(cs), proxy, NullLogger<DbUpgradeService>.Instance);
        using var db = new SqlConnection(cs);
        Assert.Null(db.ExecuteScalar<int?>("SELECT OBJECT_ID('dbo.CC_CheckRule')"));
        var initial = await service.GetStatusAsync();
        Assert.False(initial.Probes.Single(x => x.Name == "قاعده CHK-23").Exists);
        db.Execute("CREATE TABLE dbo.CC_CheckRule (LegacyColumn int);");
        try
        {
            Assert.False((await service.GetStatusAsync()).Probes.Single(x => x.Name == "قاعده CHK-23").Exists);
            db.Execute("ALTER TABLE dbo.CC_CheckRule ADD RuleCode nvarchar(20);");
            db.Execute("INSERT dbo.CC_CheckRule(RuleCode) VALUES(N'CHK-23');");
            var migrated = await service.GetStatusAsync();
            Assert.True(migrated.Probes.Single(x => x.Name == "قاعده CHK-23").Exists);
        }
        finally { db.Execute("DROP TABLE dbo.CC_CheckRule"); }
    }
}
