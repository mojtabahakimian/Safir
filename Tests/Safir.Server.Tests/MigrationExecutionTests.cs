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

[Collection("SqlUpgrade")]
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

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Safir.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string[] SplitLegacyBatches(string script) => Regex.Split(script,
        @"^[ \t]*GO[ \t]*;?[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
        .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

    private static string[] StandaloneLegacyBatches()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "Server", "Database", "legacy_upgrade_repeat_safe.sql"));
        // The standalone file has one descriptive header outside the embedded batches.
        script = Regex.Replace(script, @"\A--[^\n]*\n", "");
        return SplitLegacyBatches(script);
    }

    [Fact]
    public void StandaloneLegacySqlMatchesEmbeddedBatchesAndIncludesConstraintChecks()
    {
        var embedded = new List<string>();
        foreach (var file in new[] { "ScriptSqly.Main.cs", "ScriptSqly.Blazor.cs" })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "External", "ScriptSqly", "ScriptSqly.Core", file));
            foreach (Match match in Regex.Matches(source, "(?:\\$@|@\\$|@)\"(?<sql>(?:[^\"]|\"\")*)\""))
                embedded.AddRange(SplitLegacyBatches(match.Groups["sql"].Value.Replace("\"\"", "\"")));
        }

        var standalone = StandaloneLegacyBatches();
        Assert.NotEmpty(standalone);
        var previousIndex = -1;
        foreach (var batch in standalone)
        {
            Assert.Contains(batch, embedded);
            var index = embedded.IndexOf(batch);
            Assert.True(index > previousIndex, $"Standalone SQL changes embedded execution order: {batch.Split('\n')[0]}");
            previousIndex = index;
        }

        var checks = embedded.Where(x => Regex.IsMatch(x,
            @"\AALTER\s+TABLE\s+\[dbo\]\.\[(?:InvoiceRewards|RewardRules|PRICE_ELAMIETF_EXCEPTION)\]\s+CHECK\s+CONSTRAINT\b",
            RegexOptions.IgnoreCase)).ToArray();
        Assert.Equal(6, checks.Length);
        foreach (var check in checks) Assert.Contains(check, standalone);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ActualLegacyGoSeparatorsHandleTerminalGoAndPreserveInlineGo(string newline)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "External", "ScriptSqly", "ScriptSqly.Core", "ScriptSqly.Main.cs"));
        var splitters = Regex.Matches(source, "Regex\\.Split\\(script,\\s*@\"(?<pattern>(?:[^\"]|\"\")*)\"");
        Assert.NotEmpty(splitters.Cast<Match>());
        foreach (Match splitter in splitters)
        {
            var actualPattern = splitter.Groups["pattern"].Value.Replace("\"\"", "\"");
            var script = $"SELECT N'GO' AS Word;{newline}  go  {newline}SELECT 2;{newline}GO;";
            var commands = Regex.Split(script, actualPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            Assert.Equal(new[] { "SELECT N'GO' AS Word;", "SELECT 2;" }, commands);
        }
    }

    [SqlRepairFact]
    public void RewardTablesWorkOnFirstRunRepeatAndRepairDisabledAndMissingForeignKeys()
    {
        var cs = Connection();
        using var db = new SqlConnection(cs);
        db.Open();
        // Refuse to replace fixture names in a pre-existing restored test database.
        Assert.Equal(0, db.ExecuteScalar<int>(@"SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo')
            AND name IN ('STUF_DEF','HEAD_LST','RewardRules','InvoiceRewards')"));
        db.Execute(@"CREATE TABLE dbo.STUF_DEF (CODE nvarchar(15) PRIMARY KEY);
            CREATE TABLE dbo.HEAD_LST (NUMBER float NOT NULL, TAG float NOT NULL, PRIMARY KEY (NUMBER,TAG));");
        try
        {
            var selected = string.Join("\nGO\n", StandaloneLegacyBatches().Where(x => Regex.IsMatch(x,
                @"(?:CREATE|ALTER)\s+TABLE\s+\[dbo\]\.\[(?:RewardRules|InvoiceRewards)\]")));
            Assert.True(Engine.RunSqlTracked(cs, selected).Success);
            Assert.Equal(4, db.ExecuteScalar<int>(@"SELECT COUNT(*) FROM sys.foreign_keys
                WHERE parent_object_id IN (OBJECT_ID('dbo.RewardRules'),OBJECT_ID('dbo.InvoiceRewards'))"));
            db.Execute(@"INSERT dbo.STUF_DEF VALUES(N'SKU'); INSERT dbo.HEAD_LST VALUES(1,0);
                INSERT dbo.RewardRules(ProductID_Target,Quantity_Threshold,Reward_ProductID) VALUES(N'SKU',1,N'SKU');
                INSERT dbo.InvoiceRewards(InvoiceNumber,InvoiceTag,RewardRuleID,ProductCode_Earned,Quantity_Earned,Reward_Given_Type)
                VALUES(1,0,1,N'SKU',1,N'Customer value');
                ALTER TABLE dbo.RewardRules NOCHECK CONSTRAINT ALL;
                ALTER TABLE dbo.InvoiceRewards NOCHECK CONSTRAINT ALL;");
            Assert.Equal(4, db.ExecuteScalar<int>(@"SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1
                AND parent_object_id IN (OBJECT_ID('dbo.RewardRules'),OBJECT_ID('dbo.InvoiceRewards'))"));
            Assert.True(Engine.RunSqlTracked(cs, selected).Success);
            Assert.Equal(0, db.ExecuteScalar<int>(@"SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1
                AND parent_object_id IN (OBJECT_ID('dbo.RewardRules'),OBJECT_ID('dbo.InvoiceRewards'))"));
            db.Execute("ALTER TABLE dbo.InvoiceRewards DROP CONSTRAINT FK_InvoiceRewards_RewardRule;");
            Assert.True(Engine.RunSqlTracked(cs, selected).Success);
            Assert.Equal(4, db.ExecuteScalar<int>(@"SELECT COUNT(*) FROM sys.foreign_keys
                WHERE parent_object_id IN (OBJECT_ID('dbo.RewardRules'),OBJECT_ID('dbo.InvoiceRewards'))"));
            Assert.Equal(1, db.ExecuteScalar<int>("SELECT COUNT(*) FROM dbo.RewardRules"));
            Assert.Equal("Customer value", db.ExecuteScalar<string>("SELECT Reward_Given_Type FROM dbo.InvoiceRewards"));
        }
        finally
        {
            db.Execute(@"IF OBJECT_ID('dbo.InvoiceRewards') IS NOT NULL DROP TABLE dbo.InvoiceRewards;
                IF OBJECT_ID('dbo.RewardRules') IS NOT NULL DROP TABLE dbo.RewardRules;
                DROP TABLE dbo.HEAD_LST; DROP TABLE dbo.STUF_DEF;");
        }
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
            var selected = string.Join("\nGO\n", StandaloneLegacyBatches().Where(x => x.Contains("INSERT INTO GSCADTL")
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
        Assert.Throws<InvalidOperationException>(() => Engine.LetsGo(cs));
        db.Execute("EXEC sys.sp_releaseapplock @Resource=N'Safir.ScriptSqly.Upgrade', @LockOwner='Session';");
    }
}
