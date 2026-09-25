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

        // تاریخِ نامعتبر قبلاً بی‌صدا «امروز» می‌شد و مانده‌ی امروز جای مانده‌ی آن تاریخ برمی‌گشت
        [Theory]
        [InlineData("{\"asOf\":\"14050732\"}")]
        [InlineData("{\"asOf\":\"1405/7\"}")]
        public void AsOf_Invalid_IsRejected_NotToday(string json)
            => Assert.Null(FinArgs.AsOf(Call(json)));

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"asOf\":null}")]
        [InlineData("{\"asOf\":\"\"}")]
        public void AsOf_Missing_IsToday(string json)
            => Assert.Equal(FinArgs.Today(), FinArgs.AsOf(Call(json)));

        // Nemotron عدد را رشته می‌فرستاد و profit_and_loss هر بار «خطای نوع داده» می‌داد
        [Theory]
        [InlineData("{\"month\":1}", 1)]
        [InlineData("{\"month\":\"1\"}", 1)]
        [InlineData("{\"month\":\"۶\"}", 6)]
        [InlineData("{\"month\":\"x\"}", 0)]
        [InlineData("{\"month\":true}", 0)]
        [InlineData("{}", 0)]
        public void Int_AcceptsNumericStrings(string json, int expected)
            => Assert.Equal(expected, Call(json).Int("month"));

        [Fact]
        public void Pct_IsServerSide_AndSafeOnZero()
        {
            Assert.Equal(16.97m, FinArgs.Pct(1320561627337m, 1129003400407m));
            Assert.Null(FinArgs.Pct(5m, 0m));
        }
    }
}
