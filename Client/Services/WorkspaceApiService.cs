using System.Net.Http.Json;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.User_Model;

namespace Safir.Client.Services
{
    /// <summary>
    /// واحد و شیفتِ کاری (پنجره‌ی DEFAULT ِ WPF). ذخیره‌ی موفق توکنِ تازه برمی‌گرداند که همین‌جا
    /// جایگزینِ توکنِ تب می‌شود، تا TFSAZMAN و SHIFT ِ توکن با انتخابِ کاربر یکی باشد.
    /// </summary>
    public class WorkspaceApiService
    {
        private readonly HttpClient _http;
        private readonly IAuthService _auth;

        public WorkspaceApiService(HttpClient http, IAuthService auth)
        {
            _http = http;
            _auth = auth;
        }

        public async Task<WorkspaceDto?> CurrentAsync()
        {
            try { return await _http.GetFromJsonAsync<WorkspaceDto>("api/auth/workspace"); }
            catch { return null; }
        }

        public async Task<WorkspaceOptionsDto?> OptionsAsync()
        {
            try { return await _http.GetFromJsonAsync<WorkspaceOptionsDto>("api/auth/workspace/options"); }
            catch { return null; }
        }

        public async Task<WorkspaceSaveResult> SaveAsync(int? depatman, int? shift)
        {
            try
            {
                var res = await _http.PostAsJsonAsync("api/auth/workspace", new WorkspaceSaveRequest { Depatman = depatman, Shift = shift });
                WorkspaceSaveResult? r = null;
                try { r = await res.Content.ReadFromJsonAsync<WorkspaceSaveResult>(); } catch { /* متنِ ساده */ }

                if (res.IsSuccessStatusCode && r?.Ok == true && !string.IsNullOrEmpty(r.Token))
                {
                    await _auth.ApplyTokenAsync(r.Token);
                    return r;
                }
                return new WorkspaceSaveResult
                {
                    Ok = false,
                    Error = !string.IsNullOrWhiteSpace(r?.Error) ? r!.Error
                          : res.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "نشستِ شما منقضی شده؛ دوباره وارد شوید."
                          : $"ذخیره‌ی واحد و شیفت انجام نشد (کد {(int)res.StatusCode})."
                };
            }
            catch (HttpRequestException)
            {
                return new WorkspaceSaveResult { Ok = false, Error = "ارتباط با سرور برقرار نشد. دوباره تلاش کنید." };
            }
        }
    }
}
