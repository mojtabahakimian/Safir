using Safir.Shared.Models.Salary;
using System.IO;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface IPay2SmartExcelService
    {
        Task<byte[]> GenerateSmartExcelAsync(int runId);
    }
}
