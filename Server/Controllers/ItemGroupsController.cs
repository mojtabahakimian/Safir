// Safir.Server/Controllers/ItemGroupsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Kala; // Add using for ItemGroupDto
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Ensure only authorized users can access
    public class ItemGroupsController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<ItemGroupsController> _logger;

        public ItemGroupsController(IDatabaseService dbService, ILogger<ItemGroupsController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<TCODE_MENUITEM>>> GetItemGroups()
        {
            // Selecting only necessary columns for the DTO
            // Excluding 'pic' for now, but can be added if needed
            // Ordering by NAMES for better display
            const string sql = @"SELECT
                                    CODE,
                                    NAMES,
                                    pic,
                                    ANBAR,
                                    ID
                                FROM dbo.TCODE_MENUITEM
                                ORDER BY NAMES;";
            try
            {
                _logger.LogInformation("Fetching item groups from TCODE_MENUITEM");
                var itemGroups = await _dbService.DoGetDataSQLAsync<TCODE_MENUITEM>(sql);

                if (itemGroups == null)
                {
                    _logger.LogWarning("Fetching item groups returned null.");
                    // Return empty list instead of NotFound or Error if null is unexpected but possible
                    return Ok(new List<TCODE_MENUITEM>());
                }

                _logger.LogInformation("Successfully fetched {Count} item groups.", itemGroups.Count());
                return Ok(itemGroups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching item groups from database.");
                // Return a server error response
                return StatusCode(StatusCodes.Status500InternalServerError, "خطا در دریافت اطلاعات گروه کالاها از سرور.");
            }
        }

        // Later, you might add an endpoint like:
        // [HttpGet("{groupCode}/items")]
        // public async Task<ActionResult<IEnumerable<ItemDto>>> GetItemsByGroup(double groupCode)
        // {
        //    // Logic to fetch items for the specific group code
        // }
    }
}