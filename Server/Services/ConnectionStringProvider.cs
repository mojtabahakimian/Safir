using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Safir.Shared.Models;
using System;
using System.Data.SqlClient;
using System.Text;
using System.Text.Json;

namespace Safir.Server.Services
{
    public class ConnectionStringProvider : IConnectionStringProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;

        public ConnectionStringProvider(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
        {
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
        }

        public string GetConnectionString()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context != null && context.Request.Headers.TryGetValue("X-DB-Connection", out var headerValues))
            {
                var base64String = headerValues.ToString();
                if (!string.IsNullOrWhiteSpace(base64String))
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64String));
                        var settings = JsonSerializer.Deserialize<DbConnectionSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (settings != null && !string.IsNullOrWhiteSpace(settings.Server) && !string.IsNullOrWhiteSpace(settings.Database))
                        {
                            var builder = new SqlConnectionStringBuilder
                            {
                                DataSource = settings.Server,
                                InitialCatalog = settings.Database,
                                TrustServerCertificate = true // matching appsettings
                            };

                            if (settings.IsWindowsAuthentication)
                            {
                                builder.IntegratedSecurity = true;
                            }
                            else
                            {
                                builder.IntegratedSecurity = false;
                                builder.UserID = settings.UserId;
                                builder.Password = settings.Password;
                            }

                            return builder.ConnectionString;
                        }
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                    }
                }
            }

            // Fallback for background tasks or missing headers
            return _configuration.GetConnectionString("DefaultConnection")
                   ?? throw new InvalidOperationException("Default connection string is missing.");
        }
    }
}