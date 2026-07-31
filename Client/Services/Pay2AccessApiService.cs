using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Safir.Shared.Models.Permissions;

namespace Safir.Client.Services
{
    public class Pay2AccessApiService
    {
        private readonly HttpClient _http;
        private Pay2AccessDto? _access;

        public Pay2AccessDto Access => _access ?? new Pay2AccessDto();

        public Pay2AccessApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task EnsureLoadedAsync(bool forceReload = false)
        {
            if (_access == null || forceReload)
            {
                try
                {
                    _access = await _http.GetFromJsonAsync<Pay2AccessDto>("api/pay2/access/me");
                }
                catch
                {
                    _access = new Pay2AccessDto(); // Fallback empty
                }
            }
        }
    }
}
