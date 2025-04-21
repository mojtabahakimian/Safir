using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Interfaces
{
    public interface IAppSettingsService
    {
        Task<int?> GetDefaultBedehkarKolAsync();
        // Add other global settings if needed
    }
}
