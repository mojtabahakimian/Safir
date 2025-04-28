using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kala
{
    public class STUF_DEF
    {
        //(Primary Key)
        public string CODE { get; set; } = string.Empty;

        // From STUF_DEF.NAME
        public string? NAME { get; set; }

        // From STUF_DEF.MABL_F (Assuming this is a price or relevant value)
        // Using decimal for potential monetary values, adjust if it's just float
        public decimal MABL_F { get; set; }
        public decimal? B_SEF { get; set; }  // فی خرده (فرض نام ستون) - nullable
        public decimal? MAX_M { get; set; }  // قیمت مصرف (فرض نام ستون) - nullable
        public string? TOZIH { get; set; }  // توضیحات (فرض نام ستون) - nullable

        // From STUF_DEF.VAHED (Could be joined with TCOD_VAHEDS later for name)
        public int VAHED { get; set; }

        // From STUF_DEF.MENUIT (Group Code, double? to match definition)
        public double? MENUIT { get; set; }

        // We won't include image bytes here, only the Code to build the URL
        public bool ImageExists { get; set; } = false;
    }
}
