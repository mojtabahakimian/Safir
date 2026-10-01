using Dapper;
using Microsoft.Data.SqlClient;
using ScriptSqly.Migrations;
using Xunit;
using Engine = ScriptSqly.Migrations.ScriptSqly;

namespace Safir.Server.Tests;

[CollectionDefinition("SqlUpgrade", DisableParallelization = true)]
public class SqlUpgradeCollection { }

[Collection("SqlUpgrade")]
public class MigrationSkipTests
{
    [SqlRepairFact]
    public void MultipleColumnAddsAndFollowingDdlAreNotSilentlySkipped()
    {
        var cs=Environment.GetEnvironmentVariable("SAFIR_REPAIR_TEST_CONNECTION")!;
        Assert.StartsWith("SafirTest",new SqlConnectionStringBuilder(cs).InitialCatalog);
        using var db=new SqlConnection(cs);
        db.Execute("CREATE TABLE dbo.codex_skip_regression (ExistingColumn int);");
        try
        {
            Assert.Equal(1,Engine.RunSqlTracked(cs,"ALTER TABLE dbo.codex_skip_regression ADD ExistingColumn int").Skipped);
            var steps=new List<MigrationStep>();
            Assert.Throws<SqlException>(()=>Engine.RunSqlTracked(cs,
                "ALTER TABLE dbo.codex_skip_regression ADD ExistingColumn int, MissingColumn int",steps.Add));
            Assert.Contains(steps,x=>x.State=="failed");
            Assert.Throws<SqlException>(()=>Engine.RunSqlTracked(cs,
                "ALTER TABLE dbo.codex_skip_regression ADD ExistingColumn int DROP TABLE dbo.codex_skip_regression"));
            Assert.NotNull(db.ExecuteScalar<int?>("SELECT OBJECT_ID('dbo.codex_skip_regression')"));
        }
        finally { db.Execute("DROP TABLE dbo.codex_skip_regression"); }
    }
}
