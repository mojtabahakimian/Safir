namespace Safir.Server.Ai
{
    /// <summary>
    /// یکسان‌سازیِ متنِ فارسیِ ورودیِ جستجو.
    ///
    /// کاربری که با صفحه‌کلید عربی تایپ کند «شير» می‌فرستد و در پایگاه «شیر» است؛
    /// LIKE با collationِ این پایگاه این دو را یکی نمی‌داند (در STUF_DEF هیچ «ي/ك»
    /// عربی نیست، پس ورودی باید فارسی شود). در نام حساب‌ها برعکس است: ۳۰٪
    /// TDETA_HES «ي/ك» عربی دارد — برای جستجوی نام مشتری سمتِ SQL هم باید
    /// REPLACE شود (SqlFa).
    /// </summary>
    public static class AiText
    {
        public static string NormalizeFa(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = chars[i] switch
                {
                    'ي' or 'ى' => 'ی',                            // ي ى → ی
                    'ك'        => 'ک',                            // ك → ک
                    >= '٠' and <= '٩' => (char)('۰' + (chars[i] - '٠')), // ارقام عربی → فارسی
                    _ => chars[i]
                };
            }
            // نیم‌فاصله و فاصله‌های تکراری یک فاصله شوند؛ «Uپودر  شیر» دو فاصله دارد
            return System.Text.RegularExpressions.Regex.Replace(new string(chars).Replace('\u200C', ' '), @"\s+", " ").Trim();
        }

        /// <summary>عبارت SQL که ستونِ متنی را به همان شکلِ NormalizeFa درمی‌آورد (برای LIKE).</summary>
        public static string SqlFa(string column) =>
            $"REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE({column}, NCHAR(1610), NCHAR(1740)), NCHAR(1609), NCHAR(1740)), NCHAR(1603), NCHAR(1705)), NCHAR(8204), N' '), N'  ', N' '), N'  ', N' ')";
    }
}
