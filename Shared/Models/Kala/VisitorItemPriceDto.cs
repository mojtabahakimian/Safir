using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kala
{
    public class VisitorItemPriceDto
    {
        public string CODE { get; set; } // کد کالا
        public decimal? PRICE1 { get; set; } // قیمت از اعلامیه
        public int PEPID { get; set; } // شناسه اعلامیه قیمت
        // سایر فیلدهای مورد نیاز از کوئری مانند PORSANT, PGID ممکن است اضافه شوند اگر لازم باشد
    }
}
