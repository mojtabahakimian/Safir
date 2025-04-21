using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Visitory
{
    // DTO for mapping a customer to a route
    public class RouteMappingRequest
    {
        // Properties needed based on RoutesController usage:
        //[Required(ErrorMessage = "مسیر ویزیت نمیتواند خالی باشد.")]
        public string? RouteName { get; set; }

        [Required(ErrorMessage = "کد حساب نمیتواند خالی باشد.")]
        [Range(1, int.MaxValue, ErrorMessage = "حساب معتبر نیست.")]
        public int Tnumber { get; set; } // From CustomerModel.TNUMBER

        // Kol and Moin are needed to construct CustNo based on controller logic
        // Ensure these are provided by the client when calling the API
        [Required(ErrorMessage = "حساب کل نمیتواند خالی باشد.")]
        public double Kol { get; set; } // N_KOL

        [Required(ErrorMessage = "حساب معین نمیتواند خالی باشد.")]
        public int Moin { get; set; } // NUMBER
    }
}
