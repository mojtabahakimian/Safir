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

        public Pay2AccessService(IDatabaseService db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public async Task<Pay2AccessDto> GetAccessAsync(int userCo)
        {
            string cacheKey = $"pay2acl:{userCo}";
            if (_cache.TryGetValue(cacheKey, out Pay2AccessDto? cached) && cached != null)
            {
                return cached;
            }

            var dto = new Pay2AccessDto { UserCo = userCo };

            // Query config keys
            string sqlConfig = @"
SELECT CFG_KEY, CFG_VALUE
FROM dbo.PAY2_CONFIG
WHERE CFG_KEY IN ('ACL_ENFORCE', 'ACL_WS_SCOPE_ENFORCE', 'ACL_CACHE_SECONDS');";

            var configs = await _db.DoGetDataSQLAsync<dynamic>(sqlConfig);
            int cacheSeconds = 120;

            foreach (var cfg in configs)
            {
                string key = cfg.CFG_KEY;
                string val = cfg.CFG_VALUE;
                if (key == "ACL_ENFORCE") dto.AclEnforced = (val == "1");
                else if (key == "ACL_WS_SCOPE_ENFORCE") dto.WsScopeEnforced = (val == "1");
                else if (key == "ACL_CACHE_SECONDS") int.TryParse(val, out cacheSeconds);
            }

            // Workshop scope
            string sqlWs = "SELECT WS_ID FROM dbo.PAY2_USER_WS WHERE USERCO = @userCo";
            var wsIds = await _db.DoGetDataSQLAsync<int>(sqlWs, new { userCo });

            if (dto.AclEnforced)
            {
                dto.AllowedWorkshopIds = wsIds.ToList();
            }
            else
            {
                // Kill-switch off: grant all active workshops
                string allWsSql = "SELECT WS_ID FROM dbo.PAY2_WORKSHOP WHERE IS_ACTIVE = 1";
                var allWs = await _db.DoGetDataSQLAsync<int>(allWsSql);
                dto.AllowedWorkshopIds = allWs.ToList();
            }

            // Forms access
            string sqlForms = @"
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

            if (dto.AclEnforced)
            {
                dto.Forms = forms.ToList();
            }
            else
            {
                // Kill-switch off: grant all forms
                dto.Forms = forms.Select(f => new Pay2FormPermDto
                {
                    FormName = f.FormName,
                    Caption = f.Caption,
                    Run = true, See = true, Inp = true, Upd = true, Del = true
                }).ToList();
            }

            _cache.Set(cacheKey, dto, TimeSpan.FromSeconds(cacheSeconds > 0 ? cacheSeconds : 120));
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
            _cache.Remove($"pay2acl:{userCo}");
            return Task.CompletedTask;
        }

        public async Task AuditAsync(Pay2AuditEntry entry)
        {
            string sqlConfig = @"
SELECT CFG_KEY, CFG_VALUE
FROM dbo.PAY2_CONFIG
WHERE CFG_KEY IN ('ACL_AUDIT_DENIED', 'ACL_AUDIT_SENSITIVE');";

            var configs = await _db.DoGetDataSQLAsync<dynamic>(sqlConfig);
            bool auditDenied = true;
            bool auditSensitive = true;

            foreach (var cfg in configs)
            {
                if (cfg.CFG_KEY == "ACL_AUDIT_DENIED") auditDenied = (cfg.CFG_VALUE == "1");
                else if (cfg.CFG_KEY == "ACL_AUDIT_SENSITIVE") auditSensitive = (cfg.CFG_VALUE == "1");
            }

            if (!entry.Allowed && !auditDenied) return;
            if (entry.Allowed && string.IsNullOrEmpty(entry.Details))
            {
                // Simple allowed check: only log if it's an ACT form and sensitive logging is on
                if (!auditSensitive || entry.FormName == null || !entry.FormName.StartsWith("PAY2_ACT_")) return;
            }

            string sql = @"
INSERT INTO dbo.PAY2_SEC_AUDIT
(USERCO, USER_NAME, FORM_NAME, PERM_FLAG, WS_ID, ENTITY_KEY, ALLOWED, HTTP_METHOD, PATH, IP, DETAILS, CRT)
VALUES
(@UserCo, @UserName, @FormName, @PermFlag, @WsId, @EntityKey, @Allowed, @HttpMethod, @Path, @Ip, @Details, GETDATE());";

            await _db.ExecuteInTransactionAsync(async (conn, tran) => { await Dapper.SqlMapper.ExecuteAsync(conn, sql, entry, tran); });
        }
    }
}
