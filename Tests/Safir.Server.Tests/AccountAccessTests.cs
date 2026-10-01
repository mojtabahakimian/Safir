using System.Reflection;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Safir.Server.Controllers;
using Safir.Server.Services;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.Visitory;
using Safir.Shared.Models.Kharid;
using Xunit;

namespace Safir.Server.Tests;

public class AccountAccessTests
{
    private static ClaimsPrincipal User(string? id = "181") => new(new ClaimsIdentity(
        (id is null ? Array.Empty<Claim>() : new[] { new Claim(BaseknowClaimTypes.IDD, id) })
        .Concat(new[] { new Claim(BaseknowClaimTypes.USER_HES, "115-5-26-3545") }), "test"));

    // Fail on unexpected database calls: a denied request must never load ledger/report data.
    public class DatabaseProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => throw new InvalidOperationException();
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args!);
    }

    private static IDatabaseService Db(Func<MethodInfo, object?[], object?> handler)
    {
        var db = DispatchProxy.Create<IDatabaseService, DatabaseProxy>();
        ((DatabaseProxy)(object)db).Handler = handler;
        return db;
    }

    private static IDatabaseService DenyingDb() => Db((method, args) =>
    {
        Assert.Equal("DoGetDataSQLAsyncSingle", method.Name);
        Assert.Equal(typeof(bool), method.GetGenericArguments().Single());
        Assert.Contains("BLOCKNON_HES", (string)args[0]!);
        Assert.Contains("BLOCK_HES", (string)args[0]!);
        Assert.Equal(181, args[1]!.GetType().GetProperty("AccountAccessUserCo")!.GetValue(args[1]));
        return Task.FromResult(false);
    });

    private static CustomersController Customers(IDatabaseService db) => new(db, null!,
        NullLogger<CustomersController>.Instance, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User() } }
    };

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task MissingOrInvalidUserNeverGetsUnrestrictedAccess(string? id)
    {
        Assert.False(await AccountAccessRules.CanAccessAsync(Db((_, _) => throw new Exception("Must not query")), User(id), "115-1-1037"));
    }

    [Fact]
    public async Task BlockedAccountCannotLoadStatementEvenWhenItHasNoTransactions()
    {
        var result = await Customers(DenyingDb()).GetCustomerStatement("115-1-1037");
        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Equal(AccountAccessRules.DeniedMessage, forbidden.Value);
    }

    [Fact]
    public async Task BlockedAccountCannotDownloadStatementPdf()
    {
        var result = Assert.IsType<ObjectResult>(await Customers(DenyingDb()).GetCustomerStatementPdf("115-1-1037"));
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task BlockedAccountCannotSubmitAnOrderByCallingApiDirectly()
    {
        var controller = new ProformasController(DenyingDb(), NullLogger<ProformasController>.Instance,
            null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User() } }
        };
        var result = await controller.CreateProforma(new ProformaSaveRequestDto
        {
            Header = new() { CustomerHesCode = "115-1-1037" },
            Lines = new() { new ProformaLineDto() }
        });
        Assert.Equal(403, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task GeneralListFiltersBothRowsAndTotalCountForAuthenticatedUser()
    {
        var calls = 0;
        var db = Db((method, args) =>
        {
            calls++;
            Assert.Contains(AccountAccessRules.Predicate("CH.HES"), (string)args[0]!);
            Assert.Equal(181, Assert.IsType<DynamicParameters>(args[1]).Get<int>("AccountAccessUserCo"));
            return method.GetGenericArguments().Single() == typeof(int)
                ? Task.FromResult(0)
                : Task.FromResult<IEnumerable<VISITOR_CUSTOMERS>>(Array.Empty<VISITOR_CUSTOMERS>());
        });
        var result = await Customers(db).GetCustomersForUserWithoutVisitPlan();
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task VisitPlanDoesNotBypassAccountRestrictions()
    {
        var db = Db((_, args) =>
        {
            Assert.Contains(AccountAccessRules.Predicate("dtl.COUST_NO"), (string)args[0]!);
            Assert.Equal(181, args[1]!.GetType().GetProperty("AccountAccessUserCo")!.GetValue(args[1]));
            return Task.FromResult<IEnumerable<VISITOR_CUSTOMERS>>(Array.Empty<VISITOR_CUSTOMERS>());
        });
        var controller = new VisitorsController(db, NullLogger<VisitorsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User() } }
        };
        Assert.IsType<OkObjectResult>((await controller.GetMyVisitorCustomers(14050701)).Result);
    }

    private static ReportsController Reports(IDatabaseService db)
    {
        var env = DispatchProxy.Create<Microsoft.AspNetCore.Hosting.IWebHostEnvironment, DatabaseProxy>();
        ((DatabaseProxy)(object)env).Handler = (_, _) => Path.GetTempPath();
        var conn = DispatchProxy.Create<IConnectionStringProvider, DatabaseProxy>();
        ((DatabaseProxy)(object)conn).Handler = (_, _) => "unused";
        return new ReportsController(env, conn, NullLogger<ReportsController>.Instance, db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User() } }
        };
    }

    [Theory]
    [InlineData("./R_DAFTAR_TAFZILY_2_2.mrt")]
    [InlineData("R_DAFTAR_TAFZILY_2_2.mrt.")]
    public async Task GenericStatementReportCannotBypassAccountRestrictions(string reportName)
    {
        var result = Assert.IsType<ObjectResult>(await Reports(DenyingDb()).Generate(new ReportRequest
        {
            ReportName = reportName,
            Parameters = new() { ["HESAB"] = "115-1-1037" }
        }));
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task ReportAccountCannotBeOverriddenWithDifferentlyCasedParameter()
    {
        var controller = Reports(Db((_, _) => throw new Exception("Must not query")));
        Assert.IsType<BadRequestObjectResult>(await controller.Generate(new ReportRequest
        {
            ReportName = "R_DAFTAR_TAFZILY_2_2.mrt",
            Parameters = new() { ["HESAB"] = "115-33", ["hesab"] = "115-1-1037" }
        }));
    }

    public sealed class SqlFactAttribute : FactAttribute
    {
        public SqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAFIR_ACCOUNT_ACCESS_TEST_CONNECTION")))
                Skip = "Set SAFIR_ACCOUNT_ACCESS_TEST_CONNECTION to run read-only SQL Server regression tests.";
        }
    }

    [SqlFact]
    public async Task SqlRulesMatchWpfExceptionsHierarchyAndSegmentBoundaries()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable("SAFIR_ACCOUNT_ACCESS_TEST_CONNECTION"));
        // Table variables only: never modify the customer's rules or accounts.
        var predicate = AccountAccessRules.Predicate("c.HES")
            .Replace("dbo.BLOCKNON_HES", "@Allowed").Replace("dbo.BLOCK_HES", "@Blocked");
        var sql = $@"
DECLARE @Blocked TABLE (USERCO int, HES nvarchar(80));
DECLARE @Allowed TABLE (USERCO int, HES nvarchar(80));
INSERT @Blocked VALUES (181,N'115'),(181,N'#116-1'),(181,N'117-1-2-3-4-5');
INSERT @Allowed VALUES (181,N'115-33'),(181,N'115-5-26-3545');
SELECT c.HES, CAST(CASE WHEN {predicate} THEN 1 ELSE 0 END AS bit) AS Allowed
FROM (VALUES (N'115-1-1037'),(N'115-33'),(N'115-33-26-1'),(N'115-330'),
 (N'115-5-26-3545'),(N'115-5-26-35450'),(N'116-1-2'),(N'117-1-2-3-4-5'),
 (N'117-1-2-3-4-50'),(N'118-1')) c(HES);";
        var rows = (await connection.QueryAsync<Decision>(sql, new { AccountAccessUserCo = 181 })).ToDictionary(x => x.HES, x => x.Allowed);
        Assert.False(rows["115-1-1037"]);
        Assert.True(rows["115-33"]);
        Assert.True(rows["115-33-26-1"]);
        Assert.False(rows["115-330"]);
        Assert.True(rows["115-5-26-3545"]);
        Assert.False(rows["115-5-26-35450"]);
        Assert.True(rows["116-1-2"]);
        Assert.False(rows["117-1-2-3-4-5"]);
        Assert.True(rows["117-1-2-3-4-50"]);
        Assert.True(rows["118-1"]);
        var otherUser = await connection.QueryAsync<Decision>(sql, new { AccountAccessUserCo = 78 });
        Assert.All(otherUser, row => Assert.True(row.Allowed));
    }

    private sealed class Decision
    {
        public string HES { get; set; } = "";
        public bool Allowed { get; set; }
    }
}
