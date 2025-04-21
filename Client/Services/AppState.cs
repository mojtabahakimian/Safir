namespace Safir.Client.Services
{
    /// <summary>
    /// Baseknow
    /// </summary>
    public class AppState
    {
        // مثلا اطلاعات کاربر جاری
        public string? UUSER { get; private set; } //Current User Name to display
        public int USERCOD { get; set; } // User Code (User id)
        public int UGRP { get; set; } //User Role (Group id)

        // روشی برای ست کردن داده
        public void SetUUSER(string username)
        {
            UUSER = username;
        }
        public void SetUSERCOD(int userco)
        {
            USERCOD = userco;
        }
        public void SetUGRP(int userco)
        {
            UGRP = userco;
        }

        // تنظیمات اپلیکیشن
        public Dictionary<string, string>? Settings { get; private set; }

        public void SetSettings(Dictionary<string, string> settings)
        {
            Settings = settings;
        }

    }

}
