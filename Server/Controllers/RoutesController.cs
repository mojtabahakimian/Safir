using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Visitory; // for VISITOUR_SQL2
using Safir.Shared.Models; // for RouteMappingRequest DTO
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using Dapper; // <<< ADD for direct Dapper calls
using System.Data; // <<< ADD for IsolationLevel

namespace Safir.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RoutesController : ControllerBase
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<RoutesController> _logger;

        public RoutesController(IDatabaseService dbService, ILogger<RoutesController> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        [HttpPost("map-customer")]
        public async Task<IActionResult> MapCustomerRoute([FromBody] RouteMappingRequest request)
        {
            // Use ModelState validation based on DTO attributes
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            // Additional validation if needed
            if (request.Kol <= 0 || request.Moin <= 0) // Basic check
            {
                return BadRequest("Invalid Kol or Moin provided.");
            }

            // Construct CustNo server-side
            var custNo = $"{request.Kol}-{request.Moin}-{request.Tnumber}";

            try
            {
                // Execute all DB operations within a transaction using the service
                await _dbService.ExecuteInTransactionAsync(async (connection, transaction) =>
                {
                    const string selectSql = "SELECT IDR, ROUTE_NAME FROM VISIT_ROUTE_DTL WHERE COUST_NO = @CustomerNumber";
                    const string updateActiveSql = "UPDATE Visit_route_dtl SET RACTIVE = 1 WHERE IDR = @Id";
                    const string insertSql = "INSERT INTO Visit_route_dtl (ROUTE_NAME, COUST_NO, RACTIVE) VALUES (@RouteName, @CustomerNumber, 1)";
                    const string updateInactiveSql = "UPDATE Visit_route_dtl SET RACTIVE = 0 WHERE COUST_NO = @CustomerNumber AND ROUTE_NAME <> @RouteName";

                    // 1. Check existing routes for the customer within the transaction
                    var existingRoutes = await connection.QueryAsync<VISITOUR_SQL2>(
                        selectSql,
                        new { CustomerNumber = custNo },
                        transaction: transaction); // <<< Pass transaction

                    var thisRoute = existingRoutes.FirstOrDefault(x => x.ROUTE_NAME != null && x.ROUTE_NAME.Equals(request.RouteName, StringComparison.OrdinalIgnoreCase));

                    int rowsAffected = 0;

                    // 2. Insert or Update the specified route
                    if (thisRoute != null && thisRoute.IDR.HasValue)
                    {
                        // Activate existing route
                        rowsAffected = await connection.ExecuteAsync(
                            updateActiveSql,
                            new { Id = thisRoute.IDR.Value },
                            transaction: transaction); // <<< Pass transaction
                        _logger.LogInformation("Transaction: Activating existing route mapping for CustNo: {CustNo}, Route: {RouteName}", custNo, request.RouteName);

                        if (rowsAffected == 0) _logger.LogWarning("Transaction: UpdateActive SQL affected 0 rows for IDR {Idr}", thisRoute.IDR.Value);

                    }
                    else
                    {
                        // Insert new route mapping
                        rowsAffected = await connection.ExecuteAsync(
                           insertSql,
                           new { request.RouteName, CustomerNumber = custNo },
                           transaction: transaction); // <<< Pass transaction
                        _logger.LogInformation("Transaction: Inserting new route mapping for CustNo: {CustNo}, Route: {RouteName}", custNo, request.RouteName);

                        if (rowsAffected == 0) throw new InvalidOperationException("Database insert for route mapping failed."); // Trigger rollback
                    }

                    // 3. Deactivate other routes for this customer within the same transaction
                    // This runs regardless of whether we inserted or updated the target route
                    int inactiveRows = await connection.ExecuteAsync(
                       updateInactiveSql,
                       new { CustomerNumber = custNo, request.RouteName },
                       transaction: transaction); // <<< Pass transaction
                    _logger.LogInformation("Transaction: Deactivated {Count} other routes for CustNo: {CustNo}", inactiveRows, custNo);


                    // If we reach here without exceptions, the transaction will be committed by DatabaseService
                }, IsolationLevel.RepeatableRead); // Or other suitable isolation level

                return Ok(new { Message = "Route mapping updated successfully." });
            }
            catch (Exception ex)
            {
                // Error is logged by DatabaseService during rollback
                // Log context specific info here if needed
                _logger.LogError(ex, "Error processing route mapping transaction for CustNo: {CustNo}, Route: {RouteName}", custNo, request.RouteName);
                // Return generic error to client
                return StatusCode(500, "An error occurred while processing the route mapping.");
            }
        }

        // Other route-related endpoints...
        // ...
    }
}