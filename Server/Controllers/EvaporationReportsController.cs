using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Safir.Shared.Models;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace Safir.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class EvaporationReportsController : ControllerBase
    {
        private readonly string _connectionString;

        public EvaporationReportsController(Safir.Server.Services.IConnectionStringProvider connectionStringProvider)
        {
            _connectionString = connectionStringProvider.GetConnectionString();
        }

        [HttpPost]
        public async Task<IActionResult> Post(EvaporationReport evaporationReport)
        {
            if (ModelState.IsValid)
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    var sql = @"
                        INSERT INTO EvaporationReports (
                            ReportDate, Shift, OperatorName, StartTime, EndTime, DurationInMinutes,
                            OutletDryMatterPercentage, FillTime20LitreContainerInSeconds, BoilerSteamPressure,
                            TvrSteamPressure, VacuumPressure, Tower1Temperature, Tower2Temperature,
                            Tower3Temperature, CondenserInletTemperature, CondenserOutletTemperature,
                            IsTower1PumpOn, IsTower2PumpOn, DistilledWaterTemperature, CipStatus,
                            HoursSinceLastCip
                        ) VALUES (
                            @ReportDate, @Shift, @OperatorName, @StartTime, @EndTime, @DurationInMinutes,
                            @OutletDryMatterPercentage, @FillTime20LitreContainerInSeconds, @BoilerSteamPressure,
                            @TvrSteamPressure, @VacuumPressure, @Tower1Temperature, @Tower2Temperature,
                            @Tower3Temperature, @CondenserInletTemperature, @CondenserOutletTemperature,
                            @IsTower1PumpOn, @IsTower2PumpOn, @DistilledWaterTemperature, @CipStatus,
                            @HoursSinceLastCip
                        )";
                    await connection.ExecuteAsync(sql, evaporationReport);
                }
                return Ok();
            }
            return BadRequest(ModelState);
        }
    }
}
