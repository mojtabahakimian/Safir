using Safir.Shared.Interfaces;
using Safir.Shared.Models.User_Model;
using Safir.Shared.Utility;

namespace Safir.Server.Services
{
    /// <summary>
    /// واحد و شیفتِ کاری — پورتِ پنجره‌ی DEFAULT ِ WPF (Wins/WinMenus/WinDEFAULT/DEFAULT.xaml.cs)
    /// که بعد از ورود باز می‌شود:
    ///  • پیش‌فرض از DEFAULTDEP ِ همان کاربر؛ اگر ردیفی نیست (۱، ۱).
    ///  • بدونِ مجوزِ فرمِ DEFAULT (LETSGO("DEFA")) فیلدها قفل‌اند — فقط همان پیش‌فرض.
    ///  • واحد و شیفت هر دو الزامی‌اند؛ انتخاب در DEFAULTDEP ذخیره می‌شود (UPDATE DEFAULTDEP ...)
    ///    تا ورودِ بعدی و فرم‌هایی که DEFAULTDEP را مستقیم می‌خوانند (پیش‌فاکتور) همان را ببینند.
    /// </summary>
    public sealed class WorkspaceService
    {
        /// <summary>کدِ دسترسی در WPF؛ PermissionService آن را به TFORMS.FORMNAME = 'DEFAULT' می‌برد.</summary>
        public const string PermissionForm = "DEFA";
        public const int FallbackId = 1;

        private readonly IDatabaseService _db;
        private readonly IPermissionService _perm;

        public WorkspaceService(IDatabaseService db, IPermissionService perm)
        {
            _db = db;
            _perm = perm;
        }

        /// <summary>DEFAULTDEP ِ کاربر؛ بدونِ ردیف همان (۱، ۱) که WPF درج می‌کند.</summary>
        public async Task<(int Depatman, int Shift)> DefaultOfAsync(int userCo)
        {
            var d = await _db.GetUserDefaultDepAsync(userCo);
            return (d?.TFSAZMAN ?? FallbackId, d?.SHIFT ?? FallbackId);
        }

        public Task<bool> CanChangeAsync(int userCo) => _perm.CanUserRunFormAsync(userCo, PermissionForm);

        /// <summary>نامِ واحد و شیفتِ جاری برای نمایش (منوی کناری).</summary>
        public async Task<WorkspaceDto> DescribeAsync(int userCo, int? depatman, int? shift)
        {
            return new WorkspaceDto
            {
                Depatman = depatman,
                Shift = shift,
                DepName = depatman is null ? null : (await _db.DoGetDataSQLAsyncSingle<string?>(
                    "SELECT TOP 1 CAST(DEPNAME AS nvarchar(200)) FROM dbo.DEPART WHERE DEPATMAN = @depatman", new { depatman }))?.FixPersianChars(),
                ShiftName = shift is null ? null : await _db.DoGetDataSQLAsyncSingle<string?>(
                    "SELECT TOP 1 CAST(SHNAME AS nvarchar(100)) FROM dbo.SHIFT WHERE SHIFT_ID = @shift", new { shift }),
                CanChange = await CanChangeAsync(userCo),
            };
        }

        /// <summary>
        /// فهرست‌ها + انتخابِ فعلی — برای مرحله‌ی دومِ ورود و پنجره‌ی تغییرِ واحد. انتخابِ فعلی =
        /// واحد و شیفتِ همین جلسه (توکن)، وگرنه DEFAULTDEP؛ کاربرِ بدونِ مجوز همیشه DEFAULTDEP.
        /// </summary>
        public async Task<WorkspaceOptionsDto> OptionsAsync(int userCo, int? sessionDepatman = null, int? sessionShift = null)
        {
            var canChange = await CanChangeAsync(userCo);
            var (dep, shift) = await DefaultOfAsync(userCo);
            if (canChange)
            {
                dep = sessionDepatman ?? dep;
                shift = sessionShift ?? shift;
            }
            var o = new WorkspaceOptionsDto
            {
                // DEPART بیش از هزار شعبه‌ی مشتری هم دارد؛ واحدهایی که پیش‌فرضِ کاربری‌اند یا در خزانه
                // به کار رفته‌اند اول می‌آیند (WPF فقط بر اساسِ نام مرتب می‌کند).
                Departments = (await _db.DoGetDataSQLAsync<WorkspaceLookupItem>(@"
                    SELECT d.DEPATMAN Id, CAST(d.DEPNAME AS nvarchar(200)) Name,
                           CAST(CASE WHEN u.n IS NULL THEN 0 ELSE 1 END AS bit) Frequent
                    FROM dbo.DEPART d
                    LEFT JOIN (SELECT x.DEPATMAN, COUNT(*) n FROM (
                                   SELECT TFSAZMAN DEPATMAN FROM dbo.DEFAULTDEP
                                   UNION ALL SELECT DEPATMAN FROM dbo.PGET_HED) x
                               GROUP BY x.DEPATMAN) u ON u.DEPATMAN = d.DEPATMAN
                    ORDER BY CASE WHEN u.n IS NULL THEN 1 ELSE 0 END, u.n DESC, d.DEPNAME")).ToList(),
                Shifts = (await _db.DoGetDataSQLAsync<WorkspaceLookupItem>(
                    "SELECT SHIFT_ID Id, CAST(SHNAME AS nvarchar(100)) Name FROM dbo.SHIFT ORDER BY SHNAME")).ToList(),
                CanChange = canChange,
            };
            foreach (var d in o.Departments) d.Name = (d.Name ?? "").FixPersianChars().Trim();

            // مثل SelectedValue در WPF: اگر پیش‌فرض در فهرست نیست، خالی تا کاربر انتخاب کند
            var depItem = o.Departments.FirstOrDefault(x => x.Id == dep);
            var shiftItem = o.Shifts.FirstOrDefault(x => x.Id == shift);
            o.Depatman = depItem?.Id;
            o.DepName = depItem?.Name;
            o.Shift = shiftItem?.Id;
            o.ShiftName = shiftItem?.Name;
            return o;
        }

        /// <summary>
        /// قواعدِ DEFAULT_VSH_SAVE_Click (الزامی بودن) + قفلِ بدونِ مجوز + وجودِ واحد و شیفت.
        /// </summary>
        public static string? Validate(WorkspaceSaveRequest req, (int Depatman, int Shift) current, bool canChange,
                                       bool depExists, bool shiftExists)
        {
            if (req.Depatman is null) return "واحد جاری سیستم نمی‌تواند خالی باشد.";
            if (req.Shift is null) return "شیفت سیستم نمی‌تواند خالی باشد.";
            if (!canChange && (req.Depatman != current.Depatman || req.Shift != current.Shift))
                return "اجازه‌ی تغییر واحد و شیفت را ندارید؛ فقط با واحد و شیفتِ پیش‌فرضِ خودتان می‌توانید وارد شوید.";
            if (!depExists)
                return canChange ? "واحدِ انتخاب‌شده در سیستم وجود ندارد."
                                 : "واحدِ پیش‌فرضِ شما در سیستم وجود ندارد؛ به مدیر سیستم اطلاع دهید.";
            if (!shiftExists)
                return canChange ? "شیفتِ انتخاب‌شده در سیستم وجود ندارد."
                                 : "شیفتِ پیش‌فرضِ شما در سیستم وجود ندارد؛ به مدیر سیستم اطلاع دهید.";
            return null;
        }

        /// <summary>بررسی و ذخیره در DEFAULTDEP. خطا = پیامِ فارسی برای کاربر.</summary>
        public async Task<string?> SaveAsync(int userCo, WorkspaceSaveRequest req)
        {
            var current = await DefaultOfAsync(userCo);
            var canChange = await CanChangeAsync(userCo);
            var depExists = req.Depatman is not null && await _db.DoGetDataSQLAsyncSingle<int?>(
                "SELECT TOP 1 1 FROM dbo.DEPART WHERE DEPATMAN = @d", new { d = req.Depatman }) is not null;
            var shiftExists = req.Shift is not null && await _db.DoGetDataSQLAsyncSingle<int?>(
                "SELECT TOP 1 1 FROM dbo.SHIFT WHERE SHIFT_ID = @s", new { s = req.Shift }) is not null;

            var error = Validate(req, current, canChange, depExists, shiftExists);
            if (error is not null) return error;

            // DEFAULT.Window_Loaded ردیفِ نبوده را درج و DEFAULT_VSH_SAVE_Click آن را به‌روز می‌کند
            await _db.DoExecuteSQLAsync(@"
                UPDATE dbo.DEFAULTDEP SET TFSAZMAN = @dep, SHIFT = @shift WHERE USERID = @userCo;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.DEFAULTDEP (USERID, TFSAZMAN, SHIFT) VALUES (@userCo, @dep, @shift);",
                new { userCo, dep = req.Depatman, shift = req.Shift });
            return null;
        }
    }
}
