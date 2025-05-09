using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // اطمینان از اینکه فقط کاربران لاگین شده دسترسی دارند
    public class PermissionsController : ControllerBase
    {
        private readonly IPermissionService _permissionService;
        private readonly ILogger<PermissionsController> _logger;

        public PermissionsController(IPermissionService permissionService, ILogger<PermissionsController> logger)
        {
            _permissionService = permissionService;
            _logger = logger;
        }

        // مثال: GET api/permissions/check/DEFA
        [HttpGet("check/{formCode}")]
        public async Task<IActionResult> CheckRunPermission(string formCode)
        {
            var userIdClaim = User.FindFirst(BaseknowClaimTypes.IDD); // استفاده از ثابت با حروف بزرگ
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                _logger.LogWarning("CheckRunPermission: Unauthorized access attempt or missing IDD claim.");
                return Unauthorized("User ID claim not found or invalid.");
            }

            if (string.IsNullOrWhiteSpace(formCode))
            {
                return BadRequest("Form code is required.");
            }

            try
            {
                bool canRun = await _permissionService.CanUserRunFormAsync(userId, formCode);
                // نتیجه true یا false را مستقیما برگردان
                return Ok(canRun);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking permission for User {UserId}, FormCode {FormCode}", userId, formCode);
                return StatusCode(500, "Internal server error while checking permission.");
            }
        }
    }
}
