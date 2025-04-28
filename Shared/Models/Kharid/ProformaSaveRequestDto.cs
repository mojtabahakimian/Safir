using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kharid
{
    public class ProformaSaveRequestDto
    {
        [Required]
        public ProformaHeaderDto Header { get; set; } = new ProformaHeaderDto();

        [Required]
        [MinLength(1, ErrorMessage = "سبد خرید نمی‌تواند خالی باشد.")]
        public List<ProformaLineDto> Lines { get; set; } = new List<ProformaLineDto>();

        // <<< فیلد جدید اضافه شد >>>
        /// <summary>
        /// اگر true باشد، به معنی تأیید کاربر برای ثبت با وجود کمبود موجودی است.
        /// </summary>
        public bool OverrideInventoryCheck { get; set; } = false;
    }
}
