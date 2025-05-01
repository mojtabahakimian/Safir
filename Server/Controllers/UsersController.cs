// File: Safir.Server/Controllers/UsersController.cs (یا Controller مناسب دیگر)
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("permissions/can-view-subordinate-tasks")]
    public async Task<ActionResult<bool>> GetCanViewSubordinateTasksPermission()
    {
        // IUserService حالا خودش User ID رو از Context میگیره
        bool canView = await _userService.CanViewSubordinateTasksAsync();
        return Ok(canView);
    }
}