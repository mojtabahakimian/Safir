using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Safir.Shared.Models.Kharid
{
    public class ProformaSaveResponseDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public double? ProformaNumber { get; set; } // شماره پیش فاکتور صادر شده

        // <<< فیلد جدید اضافه شد >>>
        /// <summary>
        /// اگر true باشد، کلاینت باید از کاربر تأییدیه کمبود موجودی را بگیرد.
        /// </summary>
        public bool RequiresInventoryConfirmation { get; set; } = false;
    }
}
