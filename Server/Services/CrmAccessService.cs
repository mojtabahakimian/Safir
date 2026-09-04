using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Safir.Shared.Interfaces;
using Safir.Shared.Models.Crm;

namespace Safir.Server.Services
{
    /// <summary>
    /// کنترل دسترسی CRM — عمداً فقط دو حالت دارد: «همه را ببیند» یا «فقط مال خودش».
    ///
    /// دو منبع تصمیم:
    ///   ۱) کلید <c>CRM_ACL_ENFORCE</c> در PAY2_CONFIG — شیر اصلی. پیش‌فرض خاموش
    ///      است تا نصب روی مشتری هیچ رفتاری را عوض نکند.
    ///   ۲) فرم <c>CRMALL</c> در TFORMS + SAL_CHEK — هر کاربری که RUN این فرم را
    ///      داشته باشد همه‌ی رکوردها را می‌بیند. این همان الگویی است که
    ///      CUSTEN / AZADPAY / TFTMLOCK استفاده می‌کنند: یک ردیف TFORMS که فرم
    ///      نیست، یک کلید روشن/خاموش برای هر کاربر است که از نرم‌افزار WPF
    ///      تنظیم می‌شود.
    /// </summary>
    public class CrmAccessService : ICrmAccessService
    {
        private readonly IDatabaseService _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CrmAccessService> _logger;

        /// <summary>نام فرمِ مجازیِ «مشاهده CRM همه کاربران» در TFORMS</summary>
        public const string SeeAllUsersForm = "CRMALL";

        private const string ConfigCacheKey = "crmacl:cfg";
        private static readonly TimeSpan ConfigCacheTtl = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan UserCacheTtl = TimeSpan.FromSeconds(120);

        public CrmAccessService(IDatabaseService db, IMemoryCache cache, ILogger<CrmAccessService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        public void InvalidateConfig() => _cache.Remove(ConfigCacheKey);

        /// <summary>
        /// آیا CRM_ACL_ENFORCE روشن است؟
        ///
        /// اگر PAY2_CONFIG اصلاً وجود نداشته باشد (دیتابیسی که ماژول حقوق روی آن
        /// نصب نیست) یا کلید تعریف نشده باشد، جواب «خاموش» است — یعنی رفتار
        /// دقیقاً مثل قبل. عمداً fail-open است: نبودِ تنظیمات نباید کسی را از
        /// داده‌ی خودش محروم کند.
        /// </summary>
        private async Task<bool> IsEnforcedAsync()
        {
            if (_cache.TryGetValue(ConfigCacheKey, out bool cached))
                return cached;

            bool enforced = false;
            try
            {
                const string sql = @"
SELECT TOP 1 CFG_VALUE
FROM dbo.PAY2_CONFIG WITH (NOLOCK)
WHERE CFG_KEY = N'CRM_ACL_ENFORCE';";

                var value = await _db.DoGetDataSQLAsyncSingle<string>(sql);
                enforced = value == "1";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CRM ACL: خواندن CRM_ACL_ENFORCE ممکن نشد — محدودیت اعمال نمی‌شود.");
            }

            _cache.Set(ConfigCacheKey, enforced, ConfigCacheTtl);
            return enforced;
        }

        public async Task<CrmAccessDto> GetAccessAsync(int userId, string userName)
        {
            var dto = new CrmAccessDto
            {
                UserId = userId,
                UserName = userName ?? string.Empty,
                Enforced = await IsEnforcedAsync()
            };

            // وقتی شیر اصلی خاموش است، هیچ کوئری اضافه‌ای لازم نیست:
            // RestrictToOwn در هر حالت false می‌شود.
            if (!dto.Enforced) return dto;

            string cacheKey = $"crmacl:user:{userId}";
            if (_cache.TryGetValue(cacheKey, out bool cachedSeeAll))
            {
                dto.CanSeeAllUsers = cachedSeeAll;
                return dto;
            }

            bool seeAll = false;
            try
            {
                const string sql = @"
SELECT TOP 1 ISNULL(SC.[RUN], 0)
FROM dbo.TFORMS F WITH (NOLOCK)
INNER JOIN dbo.SAL_CHEK SC WITH (NOLOCK) ON SC.[OBJECT] = F.IDH
WHERE F.FORMNAME = @FormName AND SC.USERCO = @UserId;";

                var run = await _db.DoGetDataSQLAsyncSingle<int?>(
                    sql, new { FormName = SeeAllUsersForm, UserId = userId });

                seeAll = run.HasValue && run.Value != 0;
            }
            catch (Exception ex)
            {
                // اینجا عمداً fail-closed است: اگر نتوانستیم مجوز را بخوانیم،
                // کاربر فقط داده‌ی خودش را می‌بیند. برعکسش یعنی یک خطای موقت
                // دیتابیس، دسترسی کامل بدهد.
                _logger.LogWarning(ex,
                    "CRM ACL: خواندن مجوز {Form} برای کاربر {UserId} ممکن نشد — محدود به داده‌ی خودش شد.",
                    SeeAllUsersForm, userId);
                seeAll = false;
            }

            _cache.Set(cacheKey, seeAll, UserCacheTtl);
            dto.CanSeeAllUsers = seeAll;
            return dto;
        }
    }
}
