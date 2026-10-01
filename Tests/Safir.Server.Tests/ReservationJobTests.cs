using Dapper;
using System.Reflection;
using Microsoft.Data.SqlClient;
using ScriptSqly.Migrations;
using Xunit;
using Engine = ScriptSqly.Migrations.ScriptSqly;

namespace Safir.Server.Tests;

[Collection("SqlUpgrade")]
public class ReservationJobTests
{
    private static string JobSql => (string)typeof(Engine).GetField("ReservationTimeoutJobSql",
        BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
    [Fact]
    public void JobSqlAndStandaloneStayIdentical()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Safir.sln"))) root=root.Parent;
        Assert.NotNull(root);
        Assert.Equal(JobSql.Replace("\r\n","\n").Trim(),
            File.ReadAllText(Path.Combine(root!.FullName,"Server","Database","reservation_timeout_job.sql")).Replace("\r\n","\n").Trim());
    }

    [SqlRepairFact]
    public void DatabaseJobsStaySeparateAndRepeatPreservesDisabledStateAndSchedules()
    {
        var cs=Environment.GetEnvironmentVariable("SAFIR_REPAIR_TEST_CONNECTION")!;
        var name=new SqlConnectionStringBuilder(cs).InitialCatalog;
        Assert.StartsWith("SafirTest",name);
        using var db=new SqlConnection(cs); db.Open();
        using var tx=db.BeginTransaction();
        // All msdb fixtures roll back; no job is enabled or executed.
        var legacy="SafirTestLegacy_"+Guid.NewGuid().ToString("N");
        var sql=JobSql.Replace("N'CheckReservationTimeout';","N'"+legacy+"';");
        db.Execute(sql,transaction:tx);
        var jobName="CheckReservationTimeout_"+name;
        var id=db.ExecuteScalar<Guid>("SELECT job_id FROM msdb.dbo.sysjobs WHERE name=@jobName",new{jobName},tx);
        Assert.Equal(0,db.ExecuteScalar<int>("SELECT enabled FROM msdb.dbo.sysjobs WHERE job_id=@id",new{id},tx));
        var schedule=db.ExecuteScalar<int>("SELECT schedule_id FROM msdb.dbo.sysjobschedules WHERE job_id=@id",new{id},tx);
        db.Execute("EXEC msdb.dbo.sp_update_schedule @schedule_id=@schedule,@enabled=0;",new{schedule},tx);
        db.Execute(sql,transaction:tx);
        Assert.Equal(id,db.ExecuteScalar<Guid>("SELECT job_id FROM msdb.dbo.sysjobs WHERE name=@jobName",new{jobName},tx));
        Assert.Equal(0,db.ExecuteScalar<int>("SELECT enabled FROM msdb.dbo.sysschedules WHERE schedule_id=@schedule",new{schedule},tx));

        // A legacy job for another database must not be adopted or deleted.
        db.Execute("EXEC msdb.dbo.sp_update_job @job_id=@id,@new_name=@legacy; EXEC msdb.dbo.sp_update_jobstep @job_id=@id,@step_id=1,@database_name=N'master';",new{id,legacy},tx);
        db.Execute(sql,transaction:tx);
        Assert.Equal("master",db.ExecuteScalar<string>("SELECT database_name FROM msdb.dbo.sysjobsteps WHERE job_id=@id AND step_id=1",new{id},tx));
        var secondId=db.ExecuteScalar<Guid>("SELECT job_id FROM msdb.dbo.sysjobs WHERE name=@jobName",new{jobName},tx);
        Assert.NotEqual(id,secondId);

        // Adopt the unchanged current-database legacy job, preserving its identity.
        db.Execute("EXEC msdb.dbo.sp_delete_job @job_id=@secondId,@delete_unused_schedule=1; EXEC msdb.dbo.sp_update_jobstep @job_id=@id,@step_id=1,@database_name=@name;",new{secondId,id,name},tx);
        db.Execute(sql,transaction:tx);
        Assert.Equal(id,db.ExecuteScalar<Guid>("SELECT job_id FROM msdb.dbo.sysjobs WHERE name=@jobName",new{jobName},tx));
        Assert.Equal(0,db.ExecuteScalar<int>("SELECT enabled FROM msdb.dbo.sysjobs WHERE job_id=@id",new{id},tx));
        Assert.Equal(0,db.ExecuteScalar<int>("SELECT enabled FROM msdb.dbo.sysschedules WHERE schedule_id=@schedule",new{schedule},tx));
        tx.Rollback();
    }
}
