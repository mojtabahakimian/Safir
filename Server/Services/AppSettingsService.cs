using Microsoft.Extensions.DependencyInjection; // <<< ADD for CreateScope
using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Models;
using System;
using System.Linq;
using System.Threading; // <<< ADD for SemaphoreSlim
using System.Threading.Tasks;


namespace Safir.Server.Services
{
    public class AppSettingsService : IAppSettingsService
    {
        // --- REMOVE direct injection of IDatabaseService ---
        // private readonly IDatabaseService _dbService;

        private readonly IServiceProvider _serviceProvider; // <<< ADD: Inject IServiceProvider
        private readonly ILogger<AppSettingsService> _logger;
        private int? _cachedBedehkarKol = null;
        private bool _isInitialized = false;
        private static readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

        // --- Modify Constructor ---
        public AppSettingsService(IServiceProvider serviceProvider, ILogger<AppSettingsService> logger)
        {
            _serviceProvider = serviceProvider; // <<< Store IServiceProvider
            _logger = logger;
            // --- REMOVE: _dbService = dbService;
        }

        private async Task InitializeAsync()
        {
            if (_isInitialized) return;

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized) return;

                _logger.LogInformation("Initializing AppSettingsService...");
                try
                {
                    // <<< Create a scope to resolve Scoped services >>>
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>(); // <<< Resolve IDatabaseService here

                        const string sql = "SELECT TOP 1 BEDEHKAR FROM SAZMAN";
                        var settings = await dbService.DoGetDataSQLAsyncSingle<SAZMAN>(sql);

                        if (settings != null && settings.BEDEHKAR != null)
                        {
                            _cachedBedehkarKol = settings.BEDEHKAR;
                            _logger.LogInformation("Default Bedehkar KOL loaded: {BedehkarKol}", _cachedBedehkarKol);
                        }
                        else
                        {
                            _logger.LogWarning("Could not load BEDEHKAR from SAZMAN table or value is null.");
                        }
                    } // <<< Scope (and dbService instance) is disposed here

                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading settings from SAZMAN table during initialization.");
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<int?> GetDefaultBedehkarKolAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
            return _cachedBedehkarKol;
        }
    }
}