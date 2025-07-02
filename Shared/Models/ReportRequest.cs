namespace Safir.Shared.Models
{
    public class ReportRequest
    {
        public string ReportName { get; set; } = default!; // e.g. "R_DAFTAR_TAFZILY_2_2.mrt"
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>(); // any number of named parameters
    }
}