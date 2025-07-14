using Safir.Shared.Models.Kala;
using Safir.Shared.Models.Kharid;
using Safir.Shared.Models.Visitory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.User_Model
{
    public class UserStateDto
    {
        public bool UserHasVisitPlan { get; set; }
        public VISITOR_CUSTOMERS? CurrentCustomer { get; set; }
        public List<CartItem> CartItems { get; set; } = new();

        public LookupDto<int?>? CustomerType { get; set; }
        public LookupDto<int?>? DepartmentValue { get; set; }
        public PaymentTermDto? PaymentTerm { get; set; }
        public int? AgreedDuration { get; set; }
        public PriceListDto? PriceList { get; set; }
        public DiscountListDto? DiscountList { get; set; }
        //public int? SelectedGroupId { get; set; }
        public double? SelectedGroupId { get; set; }

    }
}
