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

        // --- فیلد جدید برای ذخیره کل تنظیمات ---
        private SAZMAN? _cachedSazmanSettings = null;


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

                _logger.LogInformation("Initializing AppSettingsService by reading SAZMAN table...");
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

                        // --- خواندن تمام ستون‌ها از اولین رکورد SAZMAN ---
                        const string sql = "SELECT TOP (1) * FROM dbo.SAZMAN"; // Use * or list all needed columns
                        // استفاده از QuerySingleOrDefaultAsync چون انتظار یک رکورد را داریم
                        _cachedSazmanSettings = await dbService.DoGetDataSQLAsyncSingle<SAZMAN>(sql);
                  
                        if (_cachedSazmanSettings != null)
                        {
                            _cachedBedehkarKol = _cachedSazmanSettings.BEDEHKAR;

                            _logger.LogInformation("SAZMAN settings loaded successfully.");
                        }
                        else
                        {
                            _logger.LogWarning("Could not load settings from SAZMAN table (no records found?).");
                        }
                    }

                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading settings from SAZMAN table during initialization.");
                    _isInitialized = true; // Prevent continuous retries on error
                }
            }
            finally
            {
                _initLock.Release();
            }
        }
        // --- پیاده‌سازی متد جدید اینترفیس ---
        public async Task<SAZMAN?> GetSazmanSettingsAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
            return _cachedSazmanSettings;
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