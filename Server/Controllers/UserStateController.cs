using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Server.Services;
using Safir.Shared.Constants;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.User_Model;
using System.Security.Claims;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserStateController : ControllerBase
    {
        private readonly IUserStateService _stateService;
        private readonly ILogger<UserStateController> _logger;

        public UserStateController(IUserStateService stateService, ILogger<UserStateController> logger)
        {
            _stateService = stateService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<UserStateDto>> Get()
        {
            if (!int.TryParse(User.FindFirstValue(BaseknowClaimTypes.IDD), out var userId))
                return Unauthorized();

            var state = await _stateService.GetUserStateAsync(userId);
            if (state == null) return Ok(new UserStateDto());
            return Ok(state);
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] UserStateDto state)
        {
            if (!int.TryParse(User.FindFirstValue(BaseknowClaimTypes.IDD), out var userId))
                return Unauthorized();
            await _stateService.SaveUserStateAsync(userId, state);
            return Ok();
        }

        [HttpDelete]
        public async Task<IActionResult> Clear()
        {
            if (!int.TryParse(User.FindFirstValue(BaseknowClaimTypes.IDD), out var userId))
                return Unauthorized();
            await _stateService.ClearUserStateAsync(userId);
            return Ok();
        }
    }
}