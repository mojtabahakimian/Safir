using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Safir.Shared.Constants;

namespace Safir.Server.Tests;

/// <summary>
/// ساخت توکن معتبر برای تست‌های سرتاسری.
///
/// همان کلید و صادرکننده و مخاطبی که در Server/appsettings.json هست،
/// وگرنه لایه‌ی احراز هویت توکن را رد می‌کند — و همین یعنی تست واقعاً
/// از خط لوله‌ی امنیتی رد می‌شود، نه اینکه دورش بزند.
/// </summary>
internal static class TestJwt
{
    private const string Key =
        "hsgmvbpZbXTbxfHk7x+03c6Zq/K5j0NpVgdJIMYYXanQAnstSOpMoFSHExygq1LKYG2+XYLCfAmmr50UKBTclg==";
    private const string Issuer = "SafirAppIssuer";
    private const string Audience = "SafirAppAudience";

    public static string For(int userCo, string userName = "testuser")
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userCo.ToString()),
                new Claim(ClaimTypes.Name, userName),
                new Claim(BaseknowClaimTypes.IDD, userCo.ToString()),
                new Claim(BaseknowClaimTypes.UUSER, userName),
                new Claim(BaseknowClaimTypes.GRSAL, "1"),
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
