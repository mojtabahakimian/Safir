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

        public static string ALPHANUM(double AANUMBER)
        {
            string ALPHANUMRet = default;
            string AECHO, ATALAF, STRNUM, ACOL1, ACOL2, ACOL3, ACOL;
            string[,] MATANUM = new string[11, 4];
            string[] BNBIST = new string[10], MEGH = new string[4], AAS = new string[5], VA = new string[3];
            int K, i, ALEN, SETAYEE, j, AM2, AM3, AM, ALINK, ALENT;
            AANUMBER = Math.Round(AANUMBER);
            VA[1] = "";
            VA[2] = " و ";
            AAS[1] = " هزار";
            AAS[2] = " ميليون";
            AAS[3] = " ميليارد";
            AAS[4] = " ترليون";
            ACOL1 = "    يك  دو  سه  چهارپنج شش  هفت هشت نه";
            K = 1;
            for (i = 1; i <= 10; i++)
            {
                MATANUM[i, 1] = Strings.Trim(Strings.Mid(ACOL1, K, 4));
                K = K + 4;
            }
            ACOL2 = "     ده   بيست سي   چهل  پنجاهشصت  هفتادهشتادنود  ";
            K = 1;
            for (i = 1; i <= 10; i++)
            {
                MATANUM[i, 2] = Strings.Trim(Strings.Mid(ACOL2, K, 5));
                K = K + 5;
            }
            ACOL3 = "      صد    دويست سيصد  چهارصدپانصد ششصد  هفتصد هشتصد نهصد  ";
            K = 1;
            for (i = 1; i <= 10; i++)
            {
                MATANUM[i, 3] = Strings.Trim(Strings.Mid(ACOL3, K, 6));
                K = K + 6;
            }
            ACOL = "يازده دوازدهسيزده چهاردهپانزدهشانزدههفده  هيجده نوزده ";
            K = 1;
            for (i = 1; i <= 9; i++)
            {
                BNBIST[i] = Strings.Trim(Strings.Mid(ACOL, K, 6));
                K = K + 6;
            }
            STRNUM = Strings.Trim(Conversion.Str(AANUMBER));
            ATALAF = "";
            ALEN = Strings.Len(STRNUM);
            SETAYEE = 0;
            K = 0;
            while (K < ALEN)
            {
                AECHO = "";
                j = 0;
                for (i = 1; i <= 3; i++)
                    MEGH[i] = "";
                while (j < 3 & j < ALEN)
                {
                    if (ALEN <= K)
                    {
                        MEGH[j + 1] = "";
                    }
                    else
                    {
                        MEGH[j + 1] = Strings.Mid(STRNUM, ALEN - K, 1);
                    }
                    j = j + 1;
                    K = K + 1;
                }
                if (Conversion.Val(MEGH[3]) > 0d && Conversion.Val(MEGH[1]) > 0d || Conversion.Val(MEGH[3]) > 0d && Conversion.Val(MEGH[2]) > 0d)
                {
                    AM3 = 2;
                }
                else
                {
                    AM3 = 1;
                }
                if (Conversion.Val(MEGH[2]) > 0d && Conversion.Val(MEGH[1]) > 0d)
                {
                    AM2 = 2;
                }
                else
                {
                    AM2 = 1;
                }
                SETAYEE = SETAYEE + 1;
                AM = (int)Math.Round(Conversion.Val(MEGH[2] + MEGH[1]));
                if (AM >= 11 & AM <= 19)
                {
                    AECHO = MATANUM[(int)Math.Round(Conversion.Val(MEGH[3]) + 1d), 3] + VA[AM3] + BNBIST[AM - 10];
                }
                else
                {
                    AECHO = MATANUM[(int)Math.Round(Conversion.Val(MEGH[3]) + 1d), 3] + VA[AM3] + MATANUM[(int)Math.Round(Conversion.Val(MEGH[2]) + 1d), 2] + VA[AM2] + MATANUM[(int)Math.Round(Conversion.Val(MEGH[1]) + 1d), 1];
                }
                ALINK = 1;
                if (SETAYEE > 1 & !(MEGH[1] == "0" & MEGH[2] == "0" & MEGH[3] == "0"))
                {
                    AECHO = AECHO + AAS[SETAYEE - 1];
                    ALINK = 2;
                }
                ATALAF = AECHO + VA[ALINK] + ATALAF;
            }
            ALENT = Strings.Len(Strings.Trim(ATALAF));
            if (!string.IsNullOrEmpty(ATALAF))
            {
                if (Strings.Mid(Strings.Trim(ATALAF), ALENT - 1, 2) == " و")
                {
                    ALPHANUMRet = Strings.Mid(Strings.Trim(ATALAF), 1, ALENT - 1);
                }
                else
                {
                    ALPHANUMRet = Strings.Trim(ATALAF);
                }
            }
            else
            {
                ALPHANUMRet = "صفر";
            }

            return ALPHANUMRet;
        }
    }
}