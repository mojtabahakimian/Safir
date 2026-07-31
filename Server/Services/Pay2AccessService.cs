using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Permissions;

namespace Safir.Server.Services
{
    public class Pay2AccessService : IPay2AccessService
    {
        private readonly IDatabaseService _db;
        private readonly IMemoryCache _cache;

        private const string ConfigCacheKey = "pay2acl:cfg";
        private static readonly TimeSpan ConfigCacheTtl = TimeSpan.FromSeconds(30);

        public Pay2AccessService(IDatabaseService db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        /// <summary>تنظیمات کنترل دسترسی — جدا از دسترسی هر کاربر کش می‌شود</summary>
        private sealed class AclConfig
        {
            public bool Enforce { get; init; }
            public bool WsScope { get; init; }
            public int CacheSeconds { get; init; } = 120;
            public bool AuditDenied { get; init; } = true;
            public bool AuditSensitive { get; init; } = true;
        }

        // تنظیمات با TTL کوتاه و مستقل کش می‌شوند تا:
        //  ۱) AuditAsync در هر بار فراخوانی به دیتابیس نزند،
        //  ۲) تغییر ACL_ENFORCE حداکثر ظرف ۳۰ ثانیه اعمال شود، نه ACL_CACHE_SECONDS.
        private async Task<AclConfig> GetConfigAsync()
        {
            if (_cache.TryGetValue(ConfigCacheKey, out AclConfig? cachedCfg) && cachedCfg != null)
                return cachedCfg;

            const string sql = @"
SELECT CFG_KEY, CFG_VALUE
FROM dbo.PAY2_CONFIG
WHERE CFG_KEY IN ('ACL_ENFORCE','ACL_WS_SCOPE_ENFORCE','ACL_CACHE_SECONDS','ACL_AUDIT_DENIED','ACL_AUDIT_SENSITIVE');";

            var rows = await _db.DoGetDataSQLAsync<Pay2ConfigKeyValue>(sql);
            var map = rows.ToDictionary(r => r.CFG_KEY, r => r.CFG_VALUE, StringComparer.OrdinalIgnoreCase);

            string? Get(string k) => map.TryGetValue(k, out var v) ? v : null;

            int cacheSeconds = 120;
            int.TryParse(Get("ACL_CACHE_SECONDS"), out cacheSeconds);
            if (cacheSeconds <= 0) cacheSeconds = 120;

            var cfg = new AclConfig
            {
                Enforce        = Get("ACL_ENFORCE") == "1",
                WsScope        = Get("ACL_WS_SCOPE_ENFORCE") != "0",
                CacheSeconds   = cacheSeconds,
                AuditDenied    = Get("ACL_AUDIT_DENIED") != "0",
                AuditSensitive = Get("ACL_AUDIT_SENSITIVE") != "0"
            };

            _cache.Set(ConfigCacheKey, cfg, ConfigCacheTtl);
            return cfg;
        }

        public async Task<Pay2AccessDto> GetAccessAsync(int userCo)
        {
            var cfg = await GetConfigAsync();

            // کلید کش شامل وضعیت enforce است تا با تغییر کلید اصلی،
            // نتیجه‌های کش‌شده‌ی حالت قبل دیگر برگردانده نشوند.
            string cacheKey = $"pay2acl:{userCo}:{(cfg.Enforce ? 1 : 0)}{(cfg.WsScope ? 1 : 0)}";
            if (_cache.TryGetValue(cacheKey, out Pay2AccessDto? cached) && cached != null)
                return cached;

            var dto = new Pay2AccessDto
            {
                UserCo = userCo,
                AclEnforced = cfg.Enforce,
                WsScopeEnforced = cfg.WsScope
            };

            const string sqlWs = "SELECT WS_ID FROM dbo.PAY2_USER_WS WHERE USERCO = @userCo";
            const string sqlAllWs = "SELECT WS_ID FROM dbo.PAY2_WORKSHOP WHERE IS_ACTIVE = 1";

            dto.AllowedWorkshopIds = cfg.Enforce
                ? (await _db.DoGetDataSQLAsync<int>(sqlWs, new { userCo })).ToList()
                : (await _db.DoGetDataSQLAsync<int>(sqlAllWs)).ToList();

            const string sqlForms = @"
SELECT F.FORMNAME as FormName, F.CAPTION as Caption,
       CAST(ISNULL(SC.[RUN],0) AS BIT) AS [Run],
       CAST(ISNULL(SC.[SEE],0) AS BIT) AS [See],
       CAST(ISNULL(SC.[INP],0) AS BIT) AS [Inp],
       CAST(ISNULL(SC.[UPD],0) AS BIT) AS [Upd],
       CAST(ISNULL(SC.[DEL],0) AS BIT) AS [Del]
FROM dbo.TFORMS F
LEFT JOIN dbo.SAL_CHEK SC ON SC.[OBJECT] = F.IDH AND SC.USERCO = @userCo
WHERE F.FORMNAME LIKE N'PAY2!_%' ESCAPE N'!';";

            var forms = await _db.DoGetDataSQLAsync<Pay2FormPermDto>(sqlForms, new { userCo });

            dto.Forms = cfg.Enforce
                ? forms.ToList()
                : forms.Select(f => new Pay2FormPermDto
                  {
                      FormName = f.FormName,
                      Caption = f.Caption,
                      Run = true, See = true, Inp = true, Upd = true, Del = true
                  }).ToList();

            _cache.Set(cacheKey, dto, TimeSpan.FromSeconds(cfg.CacheSeconds));
            return dto;
        }

        public async Task<bool> HasAsync(int userCo, string form, int permVal)
        {
            var access = await GetAccessAsync(userCo);
            return access.Has(form, permVal);
        }

        public async Task<bool> CanAccessWorkshopAsync(int userCo, int wsId)
        {
            var access = await GetAccessAsync(userCo);
            if (!access.AclEnforced || !access.WsScopeEnforced) return true;
            return access.AllowedWorkshopIds.Contains(wsId);
        }

        public async Task<IReadOnlyList<int>> GetAllowedWorkshopIdsAsync(int userCo)
        {
            var access = await GetAccessAsync(userCo);
            return access.AllowedWorkshopIds;
        }

        public Task InvalidateAsync(int userCo)
        {
            // هر دو حالت ممکنِ کلید کش پاک می‌شوند
            foreach (var e in new[] { "00", "01", "10", "11" })
                _cache.Remove($"pay2acl:{userCo}:{e}");
            return Task.CompletedTask;
        }

        public async Task AuditAsync(Pay2AuditEntry entry)
        {
            var cfg = await GetConfigAsync();

            if (!entry.Allowed && !cfg.AuditDenied) return;
            if (entry.Allowed && string.IsNullOrEmpty(entry.Details))
            {
                // عملیات مجازِ عادی لاگ نمی‌شود؛ فقط عملیات حساس (PAY2_ACT_*)
                if (!cfg.AuditSensitive || entry.FormName == null || !entry.FormName.StartsWith("PAY2_ACT_"))
                    return;
            }

            const string sql = @"
INSERT INTO dbo.PAY2_SEC_AUDIT
(USERCO, USER_NAME, FORM_NAME, PERM_FLAG, WS_ID, ENTITY_KEY, ALLOWED, HTTP_METHOD, PATH, IP, DETAILS, CRT)
VALUES
(@UserCo, @UserName, @FormName, @PermFlag, @WsId, @EntityKey, @Allowed, @HttpMethod, @Path, @Ip, @Details, GETDATE());";

            await _db.DoExecuteSQLAsync(sql, entry);
        }

        private sealed class Pay2ConfigKeyValue
        {
            public string CFG_KEY { get; set; } = "";
            public string CFG_VALUE { get; set; } = "";
        }
    }
}
