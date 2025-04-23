using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/inventory")]
    [Authorize]

    public class InventoryController : ControllerBase
    {
        private readonly IDatabaseService _db;
        private readonly ILogger<InventoryController> _logger;

        public InventoryController(IDatabaseService db, ILogger<InventoryController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpGet("{itemCode}")]
        public async Task<IActionResult> Get(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
                return BadRequest("itemCode is required");

            try
            {
                var inv = await _db.GetItemInventoryAsync(itemCode);
                return Ok(inv);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inventory lookup failed for {ItemCode}", itemCode);
                return StatusCode(500, "Server error");
            }
        }
    }
}
