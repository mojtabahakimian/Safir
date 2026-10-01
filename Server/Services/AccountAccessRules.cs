using System.Security.Claims;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;

namespace Safir.Server.Services;

/// <summary>Legacy account restrictions shared with MrCorrect (BLOCKEDMK).</summary>
public static class AccountAccessRules
{
    public const string DeniedMessage = "شما اجازه دسترسی به این حساب را ندارید.";

    public static bool TryGetUserCo(ClaimsPrincipal user, out int userCo) =>
        int.TryParse(user.FindFirstValue(BaseknowClaimTypes.IDD), out userCo) && userCo > 0;

    // accountSql is a server-owned column/expression, never request input.
    // Match whole account segments: 115-33 includes 115-33-26, but not 115-330.
    // Allowed exceptions win over blocked ancestors, just like WPF. '#' is a
    // customer-creation setting, not an account restriction. No GRSAL exemption.
    public static string Predicate(string accountSql) => $@"(
        EXISTS (
            SELECT 1 FROM dbo.BLOCKNON_HES aa
            WHERE aa.USERCO = @AccountAccessUserCo
              AND aa.HES <> N'' AND aa.HES NOT LIKE N'#%'
              AND ({accountSql} = aa.HES
                   OR LEFT({accountSql}, LEN(aa.HES) + 1) = aa.HES + N'-')
        )
        OR NOT EXISTS (
            SELECT 1 FROM dbo.BLOCK_HES ab
            WHERE ab.USERCO = @AccountAccessUserCo
              AND ab.HES <> N'' AND ab.HES NOT LIKE N'#%'
              AND ({accountSql} = ab.HES
                   OR LEFT({accountSql}, LEN(ab.HES) + 1) = ab.HES + N'-')
        )
    )";

    public static async Task<bool> CanAccessAsync(
        IDatabaseService db, ClaimsPrincipal user, string accountCode)
    {
        if (!TryGetUserCo(user, out var userCo) || string.IsNullOrWhiteSpace(accountCode))
            return false;

        return await db.DoGetDataSQLAsyncSingle<bool>(
            $"SELECT CAST(CASE WHEN {Predicate("@AccountCode")} THEN 1 ELSE 0 END AS bit)",
            new { AccountAccessUserCo = userCo, AccountCode = accountCode });
    }
}
