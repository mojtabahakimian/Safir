using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kala
{
    public class TCODE_MENUITEM
    {
        // Note: SQL CODE is float, using double in C#
        public double CODE { get; set; }

        public string? NAMES { get; set; }

        // SQL pic is image, maps to byte[]
        // Not including pic initially for simplicity, can be added later
        public byte[]? pic { get; set; }

        public int ANBAR { get; set; }

        // The DB ID column (if needed for future operations)
        public long ID { get; set; }
    }
}
