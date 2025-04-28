using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kala
{
    public class InventoryDetailsDto
    {
        public decimal? CurrentInventory { get; set; }
        public decimal? MinimumInventory { get; set; } // Changed from double to decimal?
    }
}
