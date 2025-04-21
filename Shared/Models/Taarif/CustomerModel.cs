using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Taarif
{
    public class CustomerModel
    {
        public int? TNUMBER { get; set; }

        [Required(ErrorMessage = "نام مشتری اجباری است")]
        [MaxLength(99, ErrorMessage = "طول نام مشتری نباید بیشتر از 99 کاراکتر باشد")]
        public string? NAME { get; set; }

        [MaxLength(99, ErrorMessage = "آدرس طولانی است")]
        public string? ADDRESS { get; set; }

        [MaxLength(49, ErrorMessage = "شماره تلفن طولانی است")]
        public string? TEL { get; set; }

        [MaxLength(19, ErrorMessage = "کد اقتصادی طولانی است")]
        public string? ECODE { get; set; }

        [MaxLength(10, ErrorMessage = "کد پستی طولانی است")]
        public string? PCODE { get; set; }

        public string? MCODEM { get; set; }

        [MaxLength(54, ErrorMessage = "موبایل طولانی است")]
        public string? MOBILE { get; set; }

        [MaxLength(19, ErrorMessage = "فیلد سایر طولانی است")]
        public string? CODE_E { get; set; }

        [MaxLength(250, ErrorMessage = "توضیحات بیش از حد طولانی است")]
        public string? TOZIH { get; set; }

        public int? OSTANID { get; set; }

        public int? SHAHRID { get; set; }

        [Required(ErrorMessage = "نوع مشتری انتخاب نشده")]
        public int? CUST_COD { get; set; }

        [Required(ErrorMessage = "شخصیت انتخاب نشده")]
        public int? TOB { get; set; }

        public string? ROUTE_NAME { get; set; }
        public double? Longitude { get; set; }
        public double? Latitude { get; set; }
    }
}
