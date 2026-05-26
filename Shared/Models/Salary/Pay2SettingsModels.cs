namespace Safir.Shared.Models.Salary
{
    public class Pay2ConfigDto
    {
        public string CFG_KEY { get; set; } = "";
        public string CFG_VALUE { get; set; } = "";
        public string? CFG_OPTIONS { get; set; }
        public string CFG_DEFAULT { get; set; } = "";
        public string CFG_SECTION { get; set; } = "";
        public string LABEL_FA { get; set; } = "";
        public string? DESC_FA { get; set; }
        public string? OPT_LABELS { get; set; }
        public string DATA_TYPE { get; set; } = "TEXT";
        public byte ACCESS_LEVEL { get; set; }
    }

    public class Pay2ConfigSaveRequest
    {
        public List<Pay2ConfigDto> Items { get; set; } = new();
        public string? ChangeNote { get; set; }
    }

    public class Pay2TaxBracketDto
    {
        public int BRK_ID { get; set; }
        public short TAX_YEAR { get; set; }
        public long UPPER_LIMIT { get; set; }
        public decimal RATE_PCT { get; set; }
        public long FIXED_TAX { get; set; }
        public short SORT_ORDER { get; set; }

        public string UPPER_LIMIT_STR { get; set; } = "";
        public string RATE_PCT_STR { get; set; } = "";
        public string FIXED_TAX_STR { get; set; } = "";
    }

    public class Pay2TaxBracketSaveRequest
    {
        public short TAX_YEAR { get; set; }
        public List<Pay2TaxBracketDto> Items { get; set; } = new();
    }

    public class Pay2TaxBracketCopyRequest
    {
        public short SourceYear { get; set; }
        public short TargetYear { get; set; }
    }
}