using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using Engine = ScriptSqly.Migrations.ScriptSqly;

namespace Safir.Server.Tests;

public class SqlCloneReviewFactAttribute : FactAttribute
{
    public SqlCloneReviewFactAttribute()
    {
        if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SAFIR_REVIEW_UPGRADE_CONNECTION")))
            Skip="Requires an explicitly selected restored SafirTest clone.";
    }
}

[Collection("SqlUpgrade")]
public class UpgradeCloneReviewTests
{
    [SqlCloneReviewFact]
    public void CompleteUpgradeCanRepeatAndPreservesAccountingAndPayroll()
    {
        var cs=Environment.GetEnvironmentVariable("SAFIR_REVIEW_UPGRADE_CONNECTION")!;
        var name=new SqlConnectionStringBuilder(cs).InitialCatalog;
        Assert.StartsWith("SafirTest",name);
        using var db=new SqlConnection(cs); db.Open();
        var snapshot=@"SELECT (SELECT COUNT_BIG(*) FROM dbo.DEED_DTL) AS DetailCount,
            (SELECT COUNT_BIG(*) FROM dbo.DEED_HED) AS HeadCount,
            (SELECT COUNT_BIG(*) FROM dbo.PAY2_RUN_LINE) AS PayrollCount,
            (SELECT SUM(NET_PAY) FROM dbo.PAY2_RUN_LINE) AS NetPay,
            (SELECT SUM(GROSS_PAY) FROM dbo.PAY2_RUN_LINE) AS GrossPay,
            (SELECT COUNT_BIG(*) FROM dbo.PGET_LST) AS TreasuryCount FOR JSON PATH";
        var before=db.ExecuteScalar<string>(snapshot);
        var first=Engine.RunTracked(cs,true,2);
        Assert.True(first.Success,string.Join("\n",first.Errors));
        var repeat=Engine.RunTracked(cs,true,2);
        Assert.True(repeat.Success,string.Join("\n",repeat.Errors));
        Assert.Equal(before,db.ExecuteScalar<string>(snapshot));
        Assert.Equal(0,db.ExecuteScalar<int>(@"SELECT enabled FROM msdb.dbo.sysjobs
            WHERE name=@job",new {job="CheckReservationTimeout_"+name}));
    }
}
