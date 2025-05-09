using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kharid
{
    public class PriceListDto
    {
        public int Id { get; set; }       // PEPID
        public string Name { get; set; }  // PEPNAME
        // سایر فیلدها مانند PEPDATE و PEPDEPART برای فیلتر سمت سرور استفاده می‌شوند
    }
}
