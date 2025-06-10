namespace Safir.Shared.Models.Kala
{
    public class HistoricalSearchRequestDto
    {
        public int AnbarCode { get; set; }
        public string? SearchTerm { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public int? PriceListId { get; set; }
        public int? CustomerTypeCode { get; set; }
        public int? PaymentTermId { get; set; }
        public int? DiscountListId { get; set; }
    }
}