using System;
using Safir.Shared.Utility;
using Xunit;

namespace Safir.Server.Tests
{
    public class TarikhTimeConversionTests
    {
        [Theory]
        [InlineData(null, null, null)]
        [InlineData(-1, null, null)]
        [InlineData(2400, null, null)]
        [InlineData(960, null, null)]
        [InlineData(0, 0, 0)]
        [InlineData(930, 9, 30)]
        [InlineData(1430, 14, 30)]
        [InlineData(2359, 23, 59)]
        public void ConvertToTimeSpanFromTimeInt_ValidatesAndConvertsCorrectly(object? inputObj, object? expectedHourObj, object? expectedMinuteObj)
        {
            int? input = inputObj != null ? Convert.ToInt32(inputObj) : null;
            int? expectedHour = expectedHourObj != null ? Convert.ToInt32(expectedHourObj) : null;
            int? expectedMinute = expectedMinuteObj != null ? Convert.ToInt32(expectedMinuteObj) : null;

            TimeSpan? result = CL_Tarikh.ConvertToTimeSpanFromTimeInt(input);

            if (!expectedHour.HasValue || !expectedMinute.HasValue)
            {
                Assert.Null(result);
            }
            else
            {
                Assert.NotNull(result);
                Assert.Equal(expectedHour.Value, result.Value.Hours);
                Assert.Equal(expectedMinute.Value, result.Value.Minutes);
            }
        }
    }
}
