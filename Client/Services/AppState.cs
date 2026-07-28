// File: Client/Services/AppState.cs
namespace Safir.Client.Services
{
    public class AppState
    {
        public string? UUSER { get; private set; }
        public int USERCOD { get; set; }
        public int UGRP { get; set; }
        public string? USER_HES { get; private set; } // *** ADDED ***

        // آخرین کارگاه انتخاب‌شده توسط کاربر در ماژول حقوق (برای حفظ وضعیت هنگام جابجایی بین تب‌ها)
        public int LastSelectedWorkshopId { get; set; }

        // اگر کارگاه ذخیره‌شده در لیست موجود باشد همان را برمی‌گرداند، در غیر این صورت اولین کارگاه لیست
        // و مقدار نهایی را هم ذخیره می‌کند تا آی‌دی نامعتبر در حافظه باقی نماند
        public int ResolveWorkshopId(IEnumerable<int> availableWorkshopIds)
        {
            var ids = availableWorkshopIds as ICollection<int> ?? availableWorkshopIds.ToList();
            if (LastSelectedWorkshopId > 0 && ids.Contains(LastSelectedWorkshopId))
                return LastSelectedWorkshopId;

            LastSelectedWorkshopId = ids.FirstOrDefault();
            return LastSelectedWorkshopId;
        }

        public void SetUUSER(string username) { UUSER = username; }
        public void SetUSERCOD(int userco) { USERCOD = userco; }
        public void SetUGRP(int userco) { UGRP = userco; }
        public void SetUSER_HES(string? userHes) { USER_HES = userHes; } // *** ADDED ***

        public Dictionary<string, string>? Settings { get; private set; }
        public void SetSettings(Dictionary<string, string> settings) { Settings = settings; }
    }
}