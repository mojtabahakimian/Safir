namespace Safir.Shared.Models.Permissions
{
    public class Pay2AuditEntry
    {
        public int UserCo { get; set; }
        public string? UserName { get; set; }
        public string? FormName { get; set; }
        public string? PermFlag { get; set; }
        public int? WsId { get; set; }
        public string? EntityKey { get; set; }
        public bool Allowed { get; set; }
        public string? HttpMethod { get; set; }
        public string? Path { get; set; }
        public string? Ip { get; set; }
        public string? Details { get; set; }
        public DateTime Crt { get; set; }
    }
}
