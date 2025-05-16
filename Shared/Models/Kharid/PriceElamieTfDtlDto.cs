using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kharid
{
    public class PriceElamieTfDtlDto
    {
        public int? PEID { get; set; } // ID اعلامیه تخفیف
        public int? CUSTCODE { get; set; } // ID نوع مشتری
        public int? PPID { get; set; } // ID نحوه پرداخت
        public double? TF1 { get; set; } // درصد تخفیف اول
        public double? TF2 { get; set; } // درصد تخفیف دوم
    }
}
