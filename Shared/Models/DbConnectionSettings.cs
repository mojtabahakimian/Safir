using System;

namespace Safir.Shared.Models
{
    public class DbConnectionSettings
    {
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsWindowsAuthentication { get; set; } = true;
    }
}
