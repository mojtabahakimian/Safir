using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kala
{
    public class UnitInfo
    {
        public int VahedCode { get; set; }      // کد واحد
        public string VahedName { get; set; }   // نام واحد
        public double Nesbat { get; set; }      // نسبت به واحد اصلی کالا
    }
}
