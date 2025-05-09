namespace Safir.Shared.Models
{
    // مدل عمومی برای lookup های ساده (Id, Name)
    public class LookupDto<TValue>
    {
        public TValue Id { get; set; }
        public string Name { get; set; }
        // سازنده بدون پارامتر برای deserialization
        public LookupDto() { }
        public LookupDto(TValue id, string name) { Id = id; Name = name; }
    }

    // مدل برای شهرها که شامل ParentId (کد استان) است
    public class CityLookupDto : LookupDto<int?> // Assuming CityCode and OstanCode are int?
    {
        public int? ParentId { get; set; } // OstanCode
    }

    // مدل برای مسیرها که متن نمایشی متفاوتی دارد
    public class RouteLookupDto
    {
        public string RouteName { get; set; } // Value Member (Id)
        public string DisplayName { get; set; } // Display Member (Expr1 from WPF)
    }
}