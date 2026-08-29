using System.Diagnostics;
using Safir.Shared.Models.Permissions;
using Xunit;

namespace Safir.Server.Tests;

public class Pay2AccessPerformanceTests
{
    [Fact]
    public void Benchmark_Parameter_List_Construction_Performance()
    {
        var newForms = new List<Pay2FormPermDto>();
        for (int i = 0; i < 100; i++)
        {
            newForms.Add(new Pay2FormPermDto
            {
                FormName = $"PAY2_FORM_{i}",
                Run = i % 2 == 0,
                See = true,
                Inp = i % 3 == 0,
                Upd = i % 4 == 0,
                Del = i % 5 == 0
            });
        }

        int userCo = 101;

        var sw = Stopwatch.StartNew();
        for (int iteration = 0; iteration < 10000; iteration++)
        {
            var paramList = newForms.Select(f => new
            {
                userCo,
                formName = f.FormName,
                run = f.Run ? 1 : 0,
                see = f.See ? 1 : 0,
                inp = f.Inp ? 1 : 0,
                upd = f.Upd ? 1 : 0,
                del = f.Del ? 1 : 0
            }).ToList();

            Assert.Equal(100, paramList.Count);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5000, $"Parameter list construction took {sw.ElapsedMilliseconds}ms");
    }
}
