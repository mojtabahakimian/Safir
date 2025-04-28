using Microsoft.VisualBasic;

namespace Safir.Shared.Utility
{
    public static class CL_HESABDARI
    {
        public static long GETKOL(string SHES)
        {
            long GETKOLRet = default;
            byte i;
            i = 1;
            if (Strings.Len(SHES) < 5)
            {
            }
            else
            {
                while (Strings.Mid(SHES, i, 1) != "-")
                {
                    i = (byte)(i + 1);
                    if (i > 200)
                    {
                        return GETKOLRet;
                    }
                }
                GETKOLRet = Convert.ToInt64(Strings.Left(SHES, i - 1));
            }

            return GETKOLRet;
        }
        public static long GETMOIN(string SHES)
        {
            long GETMOINRet = default;
            byte i, j;
            i = 1;
            if (Strings.Len(SHES) < 5)
            {
            }
            else
            {
                while (Strings.Mid(SHES, i, 1) != "-")
                {
                    i = (byte)(i + 1);
                    if (i > 200)
                    {
                        return GETMOINRet;
                    }
                }
                j = (byte)(i + 1);
                while (Strings.Mid(SHES, j, 1) != "-" & j <= Strings.Len(SHES))
                    j = (byte)(j + 1);
                i = (byte)(i + 1);
                GETMOINRet = Convert.ToInt64(Strings.Mid(SHES, i, j - i));
            }

            return GETMOINRet;
        }
        public static long GETTAF(string SHES)
        {
            long GETTAFRet = default;
            byte i = 1, j, k;

            if (Strings.Len(SHES) < 5)
            {
                return GETTAFRet;
            }

            /* یافتن اولین «-» */
            while (Strings.Mid(SHES, i, 1) != "-" & i <= Strings.Len(SHES))
            {
                i++;
                if (i > 200)
                    return GETTAFRet;
            }

            /* یافتن دومین «-» */
            j = (byte)(i + 1);
            while (Strings.Mid(SHES, j, 1) != "-" & j <= Strings.Len(SHES))
                j++;

            /* اگر دومین «-» پیدا نشد، تفضیلی وجود ندارد */
            if (j > Strings.Len(SHES))
                return GETTAFRet;

            /* استخراج متنِ بعد از دومین «-» تا انتهای رشته یا سومین «-» (در صورت وجود) */
            k = (byte)(j + 1);
            while (k <= Strings.Len(SHES) & Strings.Mid(SHES, k, 1) != "-")
                k++;

            GETTAFRet = Convert.ToInt64(Strings.Mid(SHES, j + 1, k - (j + 1)));
            return GETTAFRet;
        }
        public static void GETTAF3(string SHES, ref double? TKOL, ref double? TMOIN, ref double? TTAF, ref double? TTAF2, ref double? TTAF3, ref double? TTAF4)
        {
            // Reset refs
            TKOL = TMOIN = TTAF = TTAF2 = TTAF3 = TTAF4 = null;
            if (string.IsNullOrWhiteSpace(SHES) || !SHES.Contains("-")) return;

            string[] parts = SHES.Split('-');

            try
            {
                if (parts.Length >= 1 && double.TryParse(parts[0], out double kol)) TKOL = kol; else return;
                if (parts.Length >= 2 && double.TryParse(parts[1], out double moin)) TMOIN = moin; else return;
                if (parts.Length >= 3 && double.TryParse(parts[2], out double taf)) TTAF = taf; else return;
                if (parts.Length >= 4 && double.TryParse(parts[3], out double taf2)) TTAF2 = taf2; else return;
                if (parts.Length >= 5 && double.TryParse(parts[4], out double taf3)) TTAF3 = taf3; else return;
                if (parts.Length >= 6 && double.TryParse(parts[5], out double taf4)) TTAF4 = taf4; else return;
            }
            catch
            {
                // Handle parsing errors if necessary, maybe log them
                // For simplicity, we just return if any part fails to parse
                return;
            }
        }

        /// <summary>
        /// بررسی می‌کند آیا حساب مشخص شده مسدود است یا خیر.
        /// (نیاز به پیاده‌سازی دقیق بر اساس منطق MrCorrect)
        /// </summary>
        /// <param name="sHES">کد حساب (Kol-Moin-Tafzil)</param>
        /// <returns>True اگر مسدود باشد، False در غیر این صورت.</returns>
        public static bool BLOCKEDMK(string sHES)
        {
            // TODO: Implement the actual logic from MrCorrect project.
            // This likely involves querying a specific table or flag
            // associated with the account (sHES) in the database.
            // For now, returning false as a placeholder.
            Console.WriteLine($"Warning: BLOCKEDMK function logic for HES '{sHES}' needs implementation based on MrCorrect.");
            // Example (Needs actual query and Database access - This won't work here):
            // try
            // {
            //     // string checkSql = "SELECT ISNULL(BLOCKED_FLAG, 0) FROM ACCOUNTS WHERE HES = @HesCode";
            //     // var isBlocked = _dbService.ExecuteScalar<bool>(checkSql, new { HesCode = sHES });
            //     // return isBlocked;
            //     return false;
            // }
            // catch
            // {
            //     return false; // Default to not blocked on error? Or throw?
            // }
            return false;
        }
    }
}