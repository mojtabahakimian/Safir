using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kala
{
    public class ItemDisplayDto
    {
        public string CODE { get; set; } = string.Empty;
        public string? NAME { get; set; }
        public decimal MABL_F { get; set; } // فی عمده
        public decimal? B_SEF { get; set; }  // فی خرده
        public decimal? MAX_M { get; set; }  // قیمت مصرف کننده
        public string? TOZIH { get; set; }  // توضیحات
        public double? MENUIT { get; set; } // Group Code
        public bool ImageExists { get; set; } = false;
        public string? VahedName { get; set; } // <<< نام واحد کالا >>>
        public int VahedCode { get; set; }    // <<< کد واحد کالا (برای استفاده‌های احتمالی دیگر) >>>
    }
}
