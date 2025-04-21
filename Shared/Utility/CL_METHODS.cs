using System.Text;

namespace Safir.Shared.Utility
{
    public static class CL_METHODS
    {
        public static string FixPersianChars(this string str)
        {
            if (!string.IsNullOrEmpty(str) && !string.IsNullOrWhiteSpace(str))
            {
                //.Replace("ئ", "ی");
                return str.Replace("ﮎ", "ک")
                .Replace("ﮏ", "ک")
                .Replace("ﮐ", "ک")
                .Replace("ﮑ", "ک")
                .Replace("ك", "ک")
                .Replace("ي", "ی")
                .Replace("ھ", "ه")

                .Replace('۰', '0')
                .Replace('۱', '1')
                .Replace('۲', '2')
                .Replace('۳', '3')
                .Replace('۴', '4')
                .Replace('۵', '5')
                .Replace('۶', '6')
                .Replace('۷', '7')
                .Replace('۸', '8')
                .Replace('۹', '9');
            }
            return str;
        }
        public static string DECODEUN(string cody)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            byte[] RawCoded = Encoding.GetEncoding(1256).GetBytes(cody);// ی 237

            var Parsy = Encoding.GetEncoding(1256);
            for (byte i = 0; i < RawCoded.Count(); i++)
            {
                RawCoded[i] = (byte)(RawCoded[i] + 20);
            }
            var result = Parsy.GetString(RawCoded);
            cody = result;
            return cody;
        }
        public static string DECODEPS(string cody)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            byte[] RawCoded = Encoding.GetEncoding(1256).GetBytes(cody);// ی 237
            var Parsy = Encoding.GetEncoding(1256);
            for (byte i = 0; i < RawCoded.Count(); i++)
            {
                RawCoded[i] = (byte)(RawCoded[i] + 10);
            }

            var result = Parsy.GetString(RawCoded);
            result = result.Substring(3, result.Length - 6);
            cody = result;
            return cody;
        }
        public static string Fixp(this string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                return str;
            // Replace with your actual Fixp implementation
            return str.Replace("ي", "ی").Replace("ك", "ک");
        }
    }
}
