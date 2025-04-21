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
    }
}