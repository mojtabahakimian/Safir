using System.Text.Json;
using Safir.Server.Ai;
using Xunit;

namespace Safir.Server.Tests
{
    /// <summary>
    /// تاریخِ ورودیِ ابزارهای مالی. مدل گاهی عدد، گاهی رشته، گاهی ارقام فارسی
    /// یا «/» می‌فرستد؛ تاریخِ نامعتبر باید رد شود، نه اینکه بازه‌ی غلط بسازد.
    /// </summary>
    public class AiFinanceArgsTests
    {
        private static AiToolCall Call(string json) => new() { Args = JsonDocument.Parse(json).RootElement };

        [Theory]
        [InlineData("{\"d\":14050101}", 14050101L)]
        [InlineData("{\"d\":\"14050631\"}", 14050631L)]
        [InlineData("{\"d\":\"1405/07/03\"}", 14050703L)]
        [InlineData("{\"d\":\"۱۴۰۵/۰۶/۳۱\"}", 14050631L)]
        [InlineData("{\"d\":\"14031230\"}", 14031230L)]   // اسفند ۱۴۰۳ کبیسه است
        public void Date_Accepts(string json, long expected)
            => Assert.Equal(expected, FinArgs.Date(Call(json), "d"));

        [Theory]
        [InlineData("{\"d\":\"14050732\"}")]   // مهر ۳۰ روز است
        [InlineData("{\"d\":\"14051230\"}")]   // اسفند ۱۴۰۵ کبیسه نیست
        [InlineData("{\"d\":\"14051301\"}")]
        [InlineData("{\"d\":\"1405071\"}")]
        [InlineData("{\"d\":\"\"}")]
        [InlineData("{}")]
        public void Date_Rejects(string json)
            => Assert.Null(FinArgs.Date(Call(json), "d"));

        [Fact]
        public void Pct_IsServerSide_AndSafeOnZero()
        {
            Assert.Equal(16.97m, FinArgs.Pct(1320561627337m, 1129003400407m));
            Assert.Null(FinArgs.Pct(5m, 0m));
        }
    }
}
