using Safir.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface IAppSettingsService
    {
        Task<SAZMAN?> GetSazmanSettingsAsync();
        Task<int?> GetDefaultBedehkarKolAsync();
        void ResetCache();
    }
}
