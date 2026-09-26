using Safir.Server.Ai;
using Xunit;

namespace Safir.Server.Tests
{
    public class AiNameMaskerTests
    {
        [Fact]
        public void MaskJson_ReplacesNameFields_KeepsCodesAndNumbers()
        {
            var m = new AiNameMasker();
            var json = m.MaskJson("{\"Top\":[{\"Code\":3365,\"Name\":\"پودر شیر\",\"NetSales\":241740},{\"CUSTNAME\":\"آقای الف\",\"TAFZIL\":\"رفاه\"}]}");

            Assert.DoesNotContain("پودر شیر", json);
            Assert.DoesNotContain("آقای الف", json);
            Assert.DoesNotContain("رفاه", json);
            Assert.Contains("3365", json);
            Assert.Contains("241740", json);
            Assert.Equal(3, m.Count);
        }

        // فارسی به همان شکل به مدل برسد، نه کدِ یونیکدِ فرارداده (هر حرف ۶ نویسه، توکنِ چند برابر)
        [Fact]
        public void MaskJson_KeepsPersianUnescaped()
        {
            var json = new AiNameMasker().MaskJson("{\"Metric\":\"فروش خالص\",\"Note\":\"a \\\"q\\\" b\"}");
            Assert.Contains("فروش خالص", json);
            Assert.DoesNotContain("\\u06", json);
            Assert.Contains("\\\"q\\\"", json);   // گیومه همچنان فرار داده می‌شود و JSON معتبر می‌ماند
        }

        // run_sql ستونِ تکراری را NAME_2 می‌کند؛ نامِ دوم بدون پوشش به مدل می‌رفت
        [Fact]
        public void MaskJson_DuplicateColumnSuffix_IsStillMasked()
        {
            var json = new AiNameMasker().MaskJson("[{\"NAME\":\"مشتری الف\",\"NAME_2\":\"مشتری ب\"}]");
            Assert.DoesNotContain("مشتری ب", json);
        }

        // برچسب‌ها نام شخص نیستند؛ پوشاندنشان «کامل‌شده/آزمایشی» و نام ستون‌ها را از مدل می‌گرفت
        [Fact]
        public void MaskJson_KeepsStructuralLabels()
        {
            var json = System.Text.RegularExpressions.Regex.Unescape(new AiNameMasker().MaskJson(
                "[{\"StatusName\":\"کامل‌شده\",\"KindName\":\"آزمایشی\",\"UnitName\":\"کیلوگرم\",\"ObjectName\":\"DEED_DTL\",\"ColumnName\":\"BED\"}]"));
            foreach (var s in new[] { "کامل‌شده", "آزمایشی", "کیلوگرم", "DEED_DTL", "BED" })
                Assert.Contains(s, json);
        }

        [Fact]
        public void SameName_SameToken()
        {
            var m = new AiNameMasker();
            Assert.Equal(m.Token("الف"), m.Token("الف"));
            Assert.NotEqual(m.Token("الف"), m.Token("ب"));
        }

        [Fact]
        public void Unmask_AcceptsPersianDigits_LeavesUnknownTokens()
        {
            var m = new AiNameMasker();
            var t = m.Token("مشتری نمونه");                 // N-0001
            Assert.Equal("مشتری نمونه و N-0099", m.Unmask("N-۰۰۰۱ و N-0099"));
            Assert.Equal("مشتری نمونه", m.Unmask(t));
        }

        [Fact]
        public void UnmaskArgs_EscapesQuotesInRealName()
        {
            var m = new AiNameMasker();
            m.Token("شرکت \"نمونه\"");
            var args = System.Text.Json.JsonDocument.Parse("{\"name\":\"N-0001\"}").RootElement;
            var un = m.UnmaskArgs(args);
            Assert.Equal("شرکت \"نمونه\"", un.GetProperty("name").GetString());
        }

        [Fact]
        public void MaskKnown_HidesNamesFromEarlierTurns()
        {
            var m = new AiNameMasker();
            m.Token("فروشگاه رفاه");
            Assert.Equal("بدهی N-0001 زیاد است", m.MaskKnown("بدهی فروشگاه رفاه زیاد است"));
        }

        [Fact]
        public void BrokenJson_ReturnedAsIs()
            => Assert.Equal("⚠ not json", new AiNameMasker().MaskJson("⚠ not json"));
    }
}
