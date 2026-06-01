using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;

namespace Safir.Server.Services
{
    public class AppSettingsService : IAppSettingsService
    {
        private readonly IDatabaseService _dbService;
        private readonly ILogger<AppSettingsService> _logger;

        public AppSettingsService(IDatabaseService dbService, ILogger<AppSettingsService> logger)
        {
            _dbService = dbService;
            _logger = logger;
        }

        public async Task<SAZMAN?> GetSazmanSettingsAsync()
        {
            try
            {
                _logger.LogDebug("Reading SAZMAN settings from database...");
                const string sql = "SELECT TOP (1) NAME, YEA, BEDEHKAR, GHAYM FROM dbo.SAZMAN";
                var result = await _dbService.DoGetDataSQLAsyncSingle<SAZMAN>(sql);
                if (result == null)
                    _logger.LogWarning("No records found in dbo.SAZMAN.");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading settings from dbo.SAZMAN.");
                return null;
            }
        }

        public async Task<int?> GetDefaultBedehkarKolAsync()
        {
            var settings = await GetSazmanSettingsAsync();
            return settings?.BEDEHKAR;
        }
    }
}
