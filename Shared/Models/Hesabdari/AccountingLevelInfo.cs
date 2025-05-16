using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Hesabdari
{
    // مدل کمکی برای نگهداری اطلاعات سطح حسابداری
    public class AccountingLevelInfo
    {
        public int Level { get; set; } // 1, 2, 3, 4
        public string TargetTable { get; set; } = string.Empty;
        public string IdFieldNameInTable { get; set; } = string.Empty; // TNUMBER, TNUMBER2, ...
        public double? NKol { get; set; }
        public double? Number { get; set; } // Moin
        public double? TnumberParent { get; set; } // Taf1
        public double? Tnumber2Parent { get; set; } // Taf2
        public double? Tnumber3Parent { get; set; } // Taf3
                                                    // TAF4 is the ID for level 4 itself, not a parent for level 5
    }
}
