using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// prg/Safir23/Shared/Models/Hesabdari/ThePart1.cs
namespace Safir.Shared.Models.Hesabdari
{
    public class QDAFTARTAFZIL2_H
    {
        public int? HES_K { get; set; }
        public int? HES_M { get; set; }
        public string? TAFZILN { get; set; }
        public string? HES { get; set; }
        public string? SHARH { get; set; }
        public decimal? BED { get; set; }  // <--- تغییر به decimal?
        public decimal? BES { get; set; }  // <--- تغییر به decimal?
        public double? N_S { get; set; }  // این میتواند double? یا int? یا long? باشد
        public long? DATE_S { get; set; }
        public decimal? MAND { get; set; } // <--- تغییر به decimal?
    }
}
