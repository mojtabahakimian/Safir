using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Data.SqlClient;
using Dapper;
using Xunit;
using Xunit.Abstractions;

namespace Safir.Server.Tests;

public class WorkshopScopeInsertBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public WorkshopScopeInsertBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Verify_Batch_Parameter_Mapping_Structure()
    {
        int userCo = 100;
        var allowedWsIds = new List<int> { 1, 2, 3, 5, 8 };

        // Test parameter object construction for Dapper ExecuteAsync batch insertion
        var batchParams = allowedWsIds.Select(ws => new { userCo, ws }).ToList();

        Assert.Equal(allowedWsIds.Count, batchParams.Count);
        for (int i = 0; i < allowedWsIds.Count; i++)
        {
            Assert.Equal(userCo, batchParams[i].userCo);
            Assert.Equal(allowedWsIds[i], batchParams[i].ws);
        }

        _output.WriteLine($"Batch parameters successfully constructed for {batchParams.Count} items.");
    }
}
