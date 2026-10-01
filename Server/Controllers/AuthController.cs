using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Safir.Client.Services;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using Safir.Shared.Models.User_Model;
using Safir.Shared.Utility;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;



[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{

    private readonly IUserService _userService; // رابط کاربری برای سرویس کاربر
    private readonly IDatabaseService _dbService; // رابط کاربری برای سرویس دیتابیس
    private readonly IConfiguration _configuration;
    private readonly Safir.Server.Services.IConnectionStringProvider _connection;
    private readonly Safir.Server.Services.WorkspaceService _workspace;

    public AuthController(IUserService userService, IDatabaseService dbService, IConfiguration configuration,
                          Safir.Server.Services.IConnectionStringProvider connection, IPermissionService permissions)
    {
        _userService = userService;
        _dbService = dbService; // تزریق سرویس دیتابیس
        _configuration = configuration;
        _connection = connection;
        _workspace = new Safir.Server.Services.WorkspaceService(dbService, permissions);
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

        UserDefaultDep? defaultDep = null;
        try
        {
            defaultDep = await _dbService.GetUserDefaultDepAsync(user.IDD);
        }
        catch (Exception ex)
        {
            // لاگ کردن خطا - اطلاعات واحد و شیفت حیاتی نیستند و لاگین می‌تواند ادامه یابد
            Console.WriteLine($"Error fetching UserDefaultDep for UserID {user.IDD}: {ex.Message}");
        }



        // --- Authentication Successful ---
        // Generate JWT Token
        var token = GenerateJwtToken(user, defaultDep);


        return Ok(new LoginResult { Successful = true, Token = token });
    }



    // ─────────────── واحد و شیفتِ کاری (پنجره‌ی DEFAULT ِ WPF) ───────────────

    private int CurrentUserCo => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private int? ClaimInt(string type) => int.TryParse(User.FindFirst(type)?.Value, out var v) ? v : null;

    /// <summary>واحد و شیفتِ همین جلسه (از توکن) با نام — برای منوی کناری.</summary>
    [Authorize]
    [HttpGet("workspace")]
    public async Task<ActionResult<WorkspaceDto>> Workspace()
    {
        if (CurrentUserCo <= 0) return Unauthorized();
        return Ok(await _workspace.DescribeAsync(CurrentUserCo, ClaimInt(BaseknowClaimTypes.TFSAZMAN), ClaimInt(BaseknowClaimTypes.SHIFT)));
    }

    /// <summary>فهرستِ واحدها و شیفت‌ها + انتخابِ فعلی (توکنِ همین جلسه، وگرنه DEFAULTDEP).</summary>
    [Authorize]
    [HttpGet("workspace/options")]
    public async Task<ActionResult<WorkspaceOptionsDto>> WorkspaceOptions()
    {
        if (CurrentUserCo <= 0) return Unauthorized();
        return Ok(await _workspace.OptionsAsync(CurrentUserCo, ClaimInt(BaseknowClaimTypes.TFSAZMAN), ClaimInt(BaseknowClaimTypes.SHIFT)));
    }

    /// <summary>
    /// انتخابِ واحد و شیفت: بررسی (الزامی، موجود، مجوزِ DEFAULT)، ذخیره در DEFAULTDEP و توکنِ
    /// تازه با همان واحد و شیفت — معادلِ VAHED_OF_USER / SHIFT_OF_USER در WPF.
    /// </summary>
    [Authorize]
    [HttpPost("workspace")]
    public async Task<ActionResult<WorkspaceSaveResult>> SaveWorkspace([FromBody] WorkspaceSaveRequest req)
    {
        var userCo = CurrentUserCo;
        if (userCo <= 0) return Unauthorized();

        var error = await _workspace.SaveAsync(userCo, req ?? new WorkspaceSaveRequest());
        if (error is not null) return BadRequest(new WorkspaceSaveResult { Ok = false, Error = error });

        // همان ستون‌ها و همان شرطِ فعال بودنِ ورود (ENABL = 0 — بخش ۲ ِ AGENTS.md)
        var user = await _dbService.DoGetDataSQLAsyncSingle<SALA_DTL>(
            "SELECT IDD, SAL_NAME, PSAL_NAME, GRSAL, HES, PORID, erjabe, ENABL FROM SALA_DTL WHERE IDD = @userCo AND ENABL = 0",
            new { userCo });
        if (user is null) return Unauthorized(new WorkspaceSaveResult { Ok = false, Error = "کاربر غیرفعال است." });

        var dep = new UserDefaultDep { USERID = userCo, TFSAZMAN = req!.Depatman, SHIFT = req.Shift };
        return Ok(new WorkspaceSaveResult
        {
            Ok = true,
            Token = GenerateJwtToken(user, dep),
            Workspace = await _workspace.DescribeAsync(userCo, req.Depatman, req.Shift),
        });
    }

    private string GenerateJwtToken(SALA_DTL user, UserDefaultDep? defaultDep)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Key"]
             ?? throw new InvalidOperationException("JWT Key not configured")));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Decode username for claims (if needed, otherwise use encoded or a display name field)
        var decodedUsername = CL_METHODS.DECODEUN(user.SAL_NAME).Fixp(); // Use Fixp as in WPF code

        string? _PORID_ = Convert.ToString(user?.PORID ?? null); //پيش فرض الگوي پرداخت پورسانت برای کالا های مشخص شده برای این کاربر
        string? _erjabe_ = Convert.ToString(user?.erjabe ?? null); //پيش فرض الگوي پرداخت پورسانت برای کالا های مشخص شده برای این کاربر

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
            new Claim(BaseknowClaimTypes.PORID, _PORID_ ?? string.Empty), // معین معادل یا همون کد حسابداری این کاربر در سیستم
            new Claim(BaseknowClaimTypes.erjabe, _erjabe_ ?? string.Empty), //توی اتوماسیون فاکتور برای چه کاربری ارسال بشه : کد اون کاربر
            // توکن فقط برای همین دیتابیس معتبر است؛ Program.cs درخواستِ دیتابیس دیگر را با آن رد می‌کند
            new Claim(BaseknowClaimTypes.DB, Safir.Server.Services.DbKey.DatabaseKey(_connection)),
        };

        // اضافه کردن Claim های جدید اگر مقادیر آنها موجود باشد
        if (defaultDep != null)
        {
            if (defaultDep.TFSAZMAN.HasValue)
            {
                claims.Add(new Claim(BaseknowClaimTypes.TFSAZMAN, defaultDep.TFSAZMAN.Value.ToString()));
            }
            if (defaultDep.SHIFT.HasValue)
            {
                claims.Add(new Claim(BaseknowClaimTypes.SHIFT, defaultDep.SHIFT.Value.ToString()));
            }
        }

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