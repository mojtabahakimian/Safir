using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Taarif
{
    public class CustomerSaveResponseDto
    {
        public string? Message { get; set; }
        public int Tnumber { get; set; } // یا هر نوع داده‌ای که Tnumber دارد
        public string? Hes { get; set; }
    }
}
