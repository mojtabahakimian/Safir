// In Safir.Server/Controllers directory
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens; // For JWT
using Safir.Client.Services;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.User_Model;
using Safir.Shared.Utility;
using System.IdentityModel.Tokens.Jwt; // For JWT
using System.Security.Claims; // For JWT
using System.Text; // For JWT key encoding



[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IConfiguration _configuration;

    //private readonly DatabaseService dbms;
    //public AuthController(DatabaseService dbService, IUserService userService, IConfiguration configuration)
    //{
    //    dbms = dbService;
    //    _userService = userService;
    //    _configuration = configuration;
    //}

    public AuthController(IUserService userService, IConfiguration configuration)
    {
        _userService = userService;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResult>> Login([FromBody] LoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new LoginResult { Successful = false, Error = "نام کاربری و رمز عبور را وارد کنید." });
        }

        // --- Password Bypass (like your 007) ---
        // WARNING: This is a significant security risk in a real application.
        // Consider removing or securing this properly if needed.
        bool bypassLogin = request.Password == "442100200";
        // --- End Password Bypass ---

        // Find user by potentially decoded username (adjust if using encoded lookup)
        var user = await _userService.GetUserByDecodedUsernameAsync(request.Username);

        if (user == null)
        {
            // Even if username is wrong, return generic error to prevent username enumeration
            return Unauthorized(new LoginResult { Successful = false, Error = "نام کاربری یا رمز عبور صحیح نیست." });
        }

        // Decode the password stored in the database
        string dbDecodedPassword = CL_METHODS.DECODEPS(user.PSAL_NAME);

        // Compare passwords (Apply FixPersianChars to input if necessary for comparison)
        string inputPasswordFixed = request.Password.Trim().FixPersianChars(); // Use your FixPersianChars extension

        // --- !!! SECURITY WARNING !!! ---
        // Direct string comparison of decoded passwords is NOT secure.
        // Replace this with a proper hash comparison if you migrate to password hashing.
        // --- !!! SECURITY WARNING !!! ---
        if (!bypassLogin && !dbDecodedPassword.Equals(inputPasswordFixed)) // Use the fixed input password
        {
            return Unauthorized(new LoginResult { Successful = false, Error = "نام کاربری یا رمز عبور صحیح نیست." });
        }

        // --- Authentication Successful ---
        // Generate JWT Token
        var token = GenerateJwtToken(user);


        return Ok(new LoginResult { Successful = true, Token = token });
    }

    private string GenerateJwtToken(SALA_DTL user)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]
             ?? throw new InvalidOperationException("JWT Key not configured")));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Decode username for claims (if needed, otherwise use encoded or a display name field)
        var decodedUsername = CL_METHODS.DECODEUN(user.SAL_NAME).Fixp(); // Use Fixp as in WPF code


        // Create Claims (pieces of information about the user)
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.IDD.ToString()), // Subject (usually user ID)
            new Claim(JwtRegisteredClaimNames.UniqueName, decodedUsername), // Unique Name (can be username)
            new Claim(ClaimTypes.NameIdentifier, user.IDD.ToString()), // Standard claim for User ID
            new Claim(ClaimTypes.Name, decodedUsername), // Standard claim for Username
            new Claim(ClaimTypes.Role, user.GRSAL.ToString()), // Role claim based on GRSAL
             // Unique token identifier
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),

            new Claim(BaseknowClaimTypes.UUSER, decodedUsername),
            new Claim(BaseknowClaimTypes.IDD, user.IDD.ToString()), // Subject (usually user ID)
            new Claim(BaseknowClaimTypes.GRSAL, user.GRSAL.ToString()), // Role claim based on GRSAL
            new Claim(BaseknowClaimTypes.USER_HES, user.HES ?? string.Empty), // معین معادل یا همون کد حسابداری این کاربر در سیستم
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(8), // Token expiration time (adjust as needed)
            Issuer = jwtSettings["Issuer"],
            Audience = jwtSettings["Audience"],
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }
}