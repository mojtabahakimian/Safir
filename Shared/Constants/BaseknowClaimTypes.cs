namespace Safir.Shared.Constants
{
    public static class BaseknowClaimTypes
    {
        public const string UUSER = "UUSER";
        public const string IDD = "IDD";
        public const string GRSAL = "GRSAL";
        public const string USER_HES = "HES";
        public const string PORID = "PORID";
        public const string erjabe = "erjabe";

        public const string TFSAZMAN = "TFSAZMAN"; // کد واحد سازمانی (دپارتمان) کاربر
        public const string SHIFT = "SHIFT";       // کد شیفت کاری کاربر

        // دیتابیسی که کاربر در آن وارد شده («سرور|دیتابیس»). کد کاربر و معین و دپارتمان بالا
        // فقط در همان دیتابیس معنی دارند؛ سرور درخواستی را که دیتابیسش با این نخواند رد می‌کند.
        public const string DB = "SAFIR_DB";
    }
}