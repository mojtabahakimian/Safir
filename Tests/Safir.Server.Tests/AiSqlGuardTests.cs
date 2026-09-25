using Safir.Server.Ai;
using Xunit;

namespace Safir.Server.Tests
{
    /// <summary>
    /// نگهبانِ run_sql. قبلاً فقط Regex بود و این‌ها از آن رد می‌شدند:
    /// OPENQUERY، جدول موقت، نام سه‌بخشی، sys، و خواندن رمز کاربران و کلید API.
    /// </summary>
    public class AiSqlGuardTests
    {
        [Theory]
        [InlineData("SELECT TOP 10 N_S, DATE_S FROM DEED_HED")]
        [InlineData("select top 5 d.HES_K, sum(d.BED - d.BES) as m from dbo.DEED_DTL d join dbo.DEED_HED h on h.N_S = d.N_S group by d.HES_K")]
        [InlineData("WITH x AS (SELECT CODE, SUM(KHFR) s FROM KALAS WHERE TAGCODE = 2 GROUP BY CODE) SELECT TOP 3 * FROM x ORDER BY s DESC")]
        [InlineData("SELECT TOP 3 * FROM dbo.QDAFTARTAFZIL2(14050101, 14050131) t")]
        [InlineData("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES")]
        [InlineData("SELECT TOP 1 Updated, CreatedAt FROM CC_Run")]
        public void Allows_ReadOnlySelects(string sql)
        {
            var (ok, error) = AiSqlGuard.Validate(sql);
            Assert.True(ok, error);
        }

        [Theory]
        [InlineData("SELECT * FROM OPENQUERY(srv, 'select 1')")]
        [InlineData("SELECT * FROM #tmp")]
        [InlineData("SELECT * FROM otherdb.dbo.DEED_HED")]
        [InlineData("SELECT * FROM srv.otherdb.dbo.DEED_HED")]
        [InlineData("SELECT name, password_hash FROM sys.sql_logins")]
        [InlineData("SELECT SAL_NAME, PSAL_NAME FROM SALA_DTL")]
        [InlineData("SELECT * FROM dbo.sala_dtl")]
        [InlineData("SELECT ApiKey FROM AI_Config")]
        [InlineData("SELECT * FROM PAY2_RUN_LINE")]
        [InlineData("SELECT h.N_S FROM DEED_HED h WHERE EXISTS (SELECT 1 FROM SALA_DTL s)")]
        [InlineData("WITH u AS (SELECT * FROM SALA_DTL) SELECT * FROM u")]
        [InlineData("SELECT 1 UNION ALL SELECT ApiKey FROM AI_Config")]
        [InlineData("SELECT * FROM OPENXML(@h, '/r')")]
        [InlineData("SELECT SAL_NAME FROM SALS")]
        [InlineData("SELECT * FROM dbo.V_PAY2_BIMEH")]
        public void Rejects_DangerousOrSensitive(string sql)
        {
            var (ok, _) = AiSqlGuard.Validate(sql);
            Assert.False(ok);
        }

        // «SELECT COUNT(*), SUM(BED)»: دو ستونِ بی‌نام نباید یکی شوند
        [Fact]
        public void ColumnKey_UnnamedAndDuplicateColumns_StayDistinct()
        {
            var row = new System.Collections.Generic.Dictionary<string, object?>();
            row[RunSqlTool.ColumnKey("", 0, row)]     = 1;
            row[RunSqlTool.ColumnKey("", 1, row)]     = 2;
            row[RunSqlTool.ColumnKey("NAME", 2, row)] = "a";
            row[RunSqlTool.ColumnKey("NAME", 3, row)] = "b";

            Assert.Equal(4, row.Count);
            Assert.Equal(1, row["Column1"]);
            Assert.Equal(2, row["Column2"]);
            Assert.Equal("b", row["NAME_2"]);
        }

        [Theory]
        [InlineData("PAY2_EMPLOYEE", true)]
        [InlineData("dbo.[SALA_DTL]", true)]
        [InlineData("v_pay2_bimeh", true)]
        [InlineData("AI_Config", true)]
        [InlineData("DEED_DTL", false)]
        [InlineData("KALAS", false)]
        [InlineData(null, false)]
        public void IsDenied_CoversSchemaAndDocTools(string? name, bool denied)
            => Assert.Equal(denied, AiSqlGuard.IsDenied(name));

        [Theory]
        [InlineData("SELECT 1; DROP TABLE X")]
        [InlineData("UPDATE DEED_HED SET OKF = 1")]
        [InlineData("SELECT * INTO NewT FROM DEED_HED")]
        [InlineData("SELECT 1 -- x")]
        [InlineData("EXEC sp_who")]
        [InlineData("SELECT FROM WHERE")]
        public void Rejects_NonSelectOrBroken(string sql)
        {
            var (ok, _) = AiSqlGuard.Validate(sql);
            Assert.False(ok);
        }
    }
}
