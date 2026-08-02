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

        public bool IsLoaded => _access != null;

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

        // ── میان‌برهای بررسی مجوز برای استفاده در Razor ──────────────────
        // مقادیر عددی معادل Pay2Perm سمت سرور است:
        // Run=1 | See=2 | Inp=4 | Upd=8 | Del=16
        public bool CanRun(string form) => Access.Has(form, 1);
        public bool CanSee(string form) => Access.Has(form, 2);
        public bool CanInp(string form) => Access.Has(form, 4);
        public bool CanUpd(string form) => Access.Has(form, 8);
        public bool CanDel(string form) => Access.Has(form, 16);

        /// <summary>آیا کاربر به این کارگاه دسترسی دارد؟</summary>
        public bool CanAccessWorkshop(int wsId)
            => !Access.AclEnforced || !Access.WsScopeEnforced || Access.AllowedWorkshopIds.Contains(wsId);
    }
}
