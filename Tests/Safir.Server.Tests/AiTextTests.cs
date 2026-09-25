using Safir.Server.Ai;
using Xunit;

namespace Safir.Server.Tests
{
    /// <summary>ورودیِ جستجو با «ي/ك» عربی، ارقام عربی و فاصله‌ی تکراری.</summary>
    public class AiTextTests
    {
        [Theory]
        [InlineData("شير خشك", "شیر خشک")]
        [InlineData("Uپودر  شیر   خشک", "Uپودر شیر خشک")]
        [InlineData("مي\u200Cخواهم", "می خواهم")]
        [InlineData("كد ١٢٣", "کد ۱۲۳")]
        [InlineData("  پنير ", "پنیر")]
        public void NormalizeFa(string input, string expected)
            => Assert.Equal(expected, AiText.NormalizeFa(input));

        [Fact]
        public void SqlFa_ReplacesArabicYehKaf()
        {
            var sql = AiText.SqlFa("t.NAME");
            Assert.Contains("NCHAR(1610), NCHAR(1740)", sql);
            Assert.Contains("NCHAR(1603), NCHAR(1705)", sql);
        }
    }
}
