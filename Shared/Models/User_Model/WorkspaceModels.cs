namespace Safir.Shared.Models.User_Model
{
    /// <summary>
    /// واحد و شیفتِ کاریِ جلسه — همان VAHED_OF_USER / SHIFT_OF_USER که WPF بعد از ورود در
    /// پنجره‌ی DEFAULT می‌گیرد و در DEFAULTDEP نگه می‌دارد. در توکن با TFSAZMAN و SHIFT می‌آید.
    /// </summary>
    public class WorkspaceDto
    {
        public int? Depatman { get; set; }
        public string? DepName { get; set; }
        public int? Shift { get; set; }
        public string? ShiftName { get; set; }
        /// <summary>مجوزِ فرمِ DEFAULT (LETSGO("DEFA") در WPF)؛ بدونِ آن، همان پیش‌فرض قفل است.</summary>
        public bool CanChange { get; set; }
    }

    public class WorkspaceOptionsDto : WorkspaceDto
    {
        public List<WorkspaceLookupItem> Departments { get; set; } = new();
        public List<WorkspaceLookupItem> Shifts { get; set; } = new();
    }

    public class WorkspaceLookupItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        /// <summary>برای واحدها: واحدِ پیش‌فرضِ کاربری یا به‌کاررفته در خزانه — اولِ فهرست می‌آید.</summary>
        public bool Frequent { get; set; }
    }

    public class WorkspaceSaveRequest
    {
        public int? Depatman { get; set; }
        public int? Shift { get; set; }
    }

    public class WorkspaceSaveResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        /// <summary>توکنِ تازه با TFSAZMAN و SHIFT ِ انتخاب‌شده.</summary>
        public string? Token { get; set; }
        public WorkspaceDto? Workspace { get; set; }
    }
}
