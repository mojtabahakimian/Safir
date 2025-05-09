using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Taarif
{
    public class CustomerHesabInfo
    {
        public string Hes { get; set; } // کد حساب مشتری (CUST_NO)
        public int? CustCod { get; set; } // کد نوع مشتری از CUST_HESAB
    }
}
