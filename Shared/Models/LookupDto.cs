namespace Safir.Shared.Models
{
    // مدل عمومی برای lookup های ساده (Id, Name)
    public class LookupDto<TKey>
    {
        public TKey Id { get; set; }
        public string Name { get; set; }
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