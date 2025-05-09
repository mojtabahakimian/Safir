using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kharid
{
    public class PaymentTermDto
    {
        public int Id { get; set; }       // PPID
        public string Name { get; set; }  // PPAME
        public int? Modat { get; set; }   // MODAT (مدت به روز)
    }
}
