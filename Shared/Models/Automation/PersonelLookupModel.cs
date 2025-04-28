// مسیر پیشنهادی: Safir.Shared/Models/Automation/PersonelLookupModel.cs
namespace Safir.Shared.Models.Automation
{
    public class PersonelLookupModel
    {
        public int USERCO { get; set; } // کد کاربر (Value)
        public string? SAL_NAME { get; set; } // نام کاربر (Display Text) - اینجا نام Decode شده قرار می‌گیرد.
        // public int? SUBUSERCO { get; set; } // این فیلد در WPF بود، شاید اینجا لازم نباشه.
    }
}