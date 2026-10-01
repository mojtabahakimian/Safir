using Dapper;
using Microsoft.Data.SqlClient;
using ScriptSqly.Migrations;
using Xunit;
using Engine = ScriptSqly.Migrations.ScriptSqly;

namespace Safir.Server.Tests;

public class SqlRepairFactAttribute : FactAttribute
{
    public SqlRepairFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SAFIR_REPAIR_TEST_CONNECTION")))
            Skip = "Requires an explicitly selected disposable SafirTest database.";
    }
}

public class MigrationExecutionTests
{
    [Fact]
    public void ModulePrefixDoesNotCauseRepeatButLiteralWhitespaceRemainsSignificant()
    {
        Assert.True(Engine.EquivalentDefinition("CREATE FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END",
            "CREATE OR ALTER FUNCTION [dbo].[f]() RETURNS int AS BEGIN RETURN 1 END"));
        Assert.False(Engine.EquivalentDefinition("CREATE VIEW dbo.v AS SELECT N'a b' AS x",
            "CREATE OR ALTER VIEW dbo.v AS SELECT N'a  b' AS x"));
    }

    private static string Connection()
    {
        var cs = Environment.GetEnvironmentVariable("SAFIR_REPAIR_TEST_CONNECTION")!;
        Assert.StartsWith("SafirTest", new SqlConnectionStringBuilder(cs).InitialCatalog);
        return cs;
    }

    [SqlRepairFact]
    public void UnchangedModuleIsSkippedAndFailedReplacementPreservesOldDefinition()
    {
        var cs = Connection();
        var sql = "CREATE OR ALTER FUNCTION dbo.codex_migration_regression() RETURNS int AS BEGIN RETURN 17 END";
        var steps = new List<MigrationStep>();
        Assert.True(Engine.RunSqlTracked(cs, sql, steps.Add).Success);
        var repeat = Engine.RunSqlTracked(cs, sql, steps.Add);
        Assert.Equal(1, repeat.Skipped);
        Assert.Equal(0, repeat.Executed);
        Assert.Contains(steps, s => s.State == "skipped");
        Assert.Throws<SqlException>(() => Engine.RunSqlTracked(cs,
            "CREATE OR ALTER FUNCTION dbo.codex_migration_regression() RETURNS int AS BEGIN RETURN MissingColumn END", steps.Add));
        Assert.Contains(steps, s => s.State == "failed");
        using var db = new SqlConnection(cs);
        Assert.Equal(17, db.ExecuteScalar<int>("SELECT dbo.codex_migration_regression()"));
        db.Execute("DROP FUNCTION dbo.codex_migration_regression");
    }

    [SqlRepairFact]
    public void AnotherSqlSessionCannotStartTheSameUpgrade()
    {
        var cs = Connection();
        using var db = new SqlConnection(cs);
        db.Open();
        db.Execute("EXEC sys.sp_getapplock @Resource=N'Safir.ScriptSqly.Upgrade', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0;");
        var error = Assert.Throws<InvalidOperationException>(() => Engine.RunSqlTracked(cs, "SELECT 1"));
        Assert.Contains("در حال اجراست", error.Message);
        db.Execute("EXEC sys.sp_releaseapplock @Resource=N'Safir.ScriptSqly.Upgrade', @LockOwner='Session';");
    }
}
