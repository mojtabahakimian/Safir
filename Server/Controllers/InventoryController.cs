// Safir/Server/Controllers/InventoryController.cs
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Safir.Shared.Models.Kala; // For InventoryDetailsDto

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/inventory")] // Base route for inventory
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

        // Existing endpoint for current inventory (optional, can be removed if details endpoint covers all cases)
        [HttpGet("{itemCode}")]
        public async Task<IActionResult> GetCurrentInventory(string itemCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
                return BadRequest("itemCode is required");

            try
            {
                var inv = await _db.GetItemInventoryAsync(itemCode);
                if (inv.HasValue)
                {
                    return Ok(inv.Value);
                }
                else
                {
                    // Return 0 or NotFound based on how you want to handle items not found or without an ANBAR
                    _logger.LogWarning("Inventory data not found or ANBAR missing for item {ItemCode} in GetCurrentInventory", itemCode);
                    return Ok(0m); // Return 0 decimal
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentInventory lookup failed for {ItemCode}", itemCode);
                return StatusCode(500, "Server error retrieving current inventory.");
            }
        }

        // New endpoint for detailed inventory (current + minimum)
        [HttpGet("{itemCode}/details")] // Route: api/inventory/{itemCode}/details?anbarCode=xxx
        public async Task<ActionResult<InventoryDetailsDto>> GetInventoryDetails(string itemCode, [FromQuery] int? anbarCode)
        {
            if (string.IsNullOrWhiteSpace(itemCode))
                return BadRequest("itemCode is required.");

            if (!anbarCode.HasValue)
                return BadRequest("anbarCode query parameter is required.");

            _logger.LogInformation("Request received for inventory details. Item: {ItemCode}, Anbar: {AnbarCode}", itemCode, anbarCode.Value);

            try
            {
                var details = await _db.GetItemInventoryDetailsAsync(itemCode, anbarCode.Value);

                if (details != null)
                {
                    _logger.LogInformation("Inventory details found for Item: {ItemCode}, Anbar: {AnbarCode}. Current: {CurrentInv}, Min: {MinInv}",
                       itemCode, anbarCode.Value, details.CurrentInventory ?? -1, details.MinimumInventory ?? -1);
                    return Ok(details);
                }
                else
                {
                    // Item or Anbar combination not found or error occurred in DB service
                    _logger.LogWarning("Inventory details NOT found for Item: {ItemCode}, Anbar: {AnbarCode}", itemCode, anbarCode.Value);
                    // Return a default object or NotFound
                    return Ok(new InventoryDetailsDto { CurrentInventory = 0, MinimumInventory = 0 });
                    // Or return NotFound("Inventory details not found for the specified item and warehouse.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetInventoryDetails failed for Item: {ItemCode}, Anbar: {AnbarCode}", itemCode, anbarCode.Value);
                return StatusCode(500, "Server error retrieving inventory details.");
            }
        }
    }
}