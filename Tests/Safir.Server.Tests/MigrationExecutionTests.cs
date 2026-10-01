using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
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
    public void LegacySeedsFillMissingRowsWithoutOverwritingCustomerValuesAndDdlCanRepeat()
    {
        var cs = Connection();
        using var db = new SqlConnection(cs);
        db.Open();
        db.Execute(@"CREATE TABLE dbo.GSCADTL (GSCADTCOD int PRIMARY KEY, GSCANAME nvarchar(100),
            GSCAGRADE int, GSCAFROM int, GSCATO int, GSCACOD int);
            INSERT dbo.GSCADTL VALUES (1,N'Customer value',7,0,0,1);
            CREATE TABLE dbo.STUF_DEF (CODE nvarchar(15) PRIMARY KEY);");
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Safir.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var script = File.ReadAllText(Path.Combine(dir!.FullName, "Server", "Database", "legacy_upgrade_repeat_safe.sql"));
            var batches = Regex.Split(script, @"^[ \t]*GO[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var selected = string.Join("\nGO\n", batches.Where(x => x.Contains("INSERT INTO GSCADTL")
                || Regex.IsMatch(x, @"(?:CREATE|ALTER)\s+TABLE\s+\[dbo\]\.\[RewardRules\]")));
            Assert.True(Engine.RunSqlTracked(cs, selected).Success);
            Assert.True(Engine.RunSqlTracked(cs, selected).Success);
            Assert.Equal(107, db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.GSCADTL"));
            Assert.Equal("Customer value", db.ExecuteScalar<string>("SELECT GSCANAME FROM dbo.GSCADTL WHERE GSCADTCOD=1"));
            db.Execute("ALTER TABLE dbo.RewardRules DROP CONSTRAINT DF__RewardRules__CRT__1EE9A919");
            Assert.True(Engine.RunSqlTracked(cs, selected).Success);
            Assert.Equal(1, db.ExecuteScalar<int>(@"SELECT COUNT(*) FROM sys.columns
                WHERE object_id=OBJECT_ID('dbo.RewardRules') AND name='CRT' AND default_object_id<>0"));
            Assert.Throws<SqlException>(() => Engine.RunSqlTracked(cs,
                "ALTER TABLE dbo.RewardRules ADD BadReference int REFERENCES dbo.STUF_DEF(MissingColumn)"));
        }
        finally
        {
            db.Execute("IF OBJECT_ID('dbo.RewardRules') IS NOT NULL DROP TABLE dbo.RewardRules; DROP TABLE dbo.STUF_DEF; DROP TABLE dbo.GSCADTL;");
        }
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
