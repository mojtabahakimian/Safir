using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface IClientDatabaseService
    {
        Task<IEnumerable<TEntity>> GetDataAsync<TEntity>(string endpoint, object? parameters = null);
        Task<TEntity?> GetSingleAsync<TEntity>(string endpoint, object? parameters = null);
        Task<int> ExecuteCommandAsync(string endpoint, object? parameters = null);
    }
}
