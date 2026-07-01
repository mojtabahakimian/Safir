using Safir.Shared.Interfaces;
using Safir.Shared.Models.Salary;

namespace Safir.Server.Reports
{
    /// <summary>
    /// بارگذاریِ ۱۰۰٪ Read-Only داده‌های موردنیاز موتور اکسلِ تحلیلی برای یک اجرا (Run)
    /// و ساختِ فایل XLSX فرمول‌دار. هیچ نوشتنی روی دیتابیس انجام نمی‌شود.
    /// </summary>
    public class Pay2ExcelAuditService
    {
        private readonly IDatabaseService _db;
        public Pay2ExcelAuditService(IDatabaseService db) => _db = db;

        private static readonly string[] PersianMonthNames =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
            "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
        };

        public async Task<(byte[] Bytes, string FileName)?> BuildAsync(int runId)
        {
            var data = await LoadAsync(runId);
            if (data == null) return null;

            var bytes = Pay2ExcelAuditWorkbook.Build(data);
            return (bytes, $"PayrollAudit_{data.PERIOD_DATE}.xlsx");
        }

        private async Task<Pay2ExcelAuditData?> LoadAsync(int runId)
        {
            // ── هدر اجرا: دوره/کارگاه ──
            const string headSql = @"
                SELECT P.PERIOD_DATE, P.WS_ID, W.WS_NAME, W.SHIFT_MODE AS WS_SHIFT_MODE
                FROM PAY2_RUN R WITH (NOLOCK)
                INNER JOIN PAY2_PERIOD   P WITH (NOLOCK) ON R.PER_ID = P.PER_ID
                INNER JOIN PAY2_WORKSHOP W WITH (NOLOCK) ON P.WS_ID  = W.WS_ID
                WHERE R.RUN_ID = @runId";

            var head = await _db.DoGetDataSQLAsyncSingle<HeadRow>(headSql, new { runId });
            if (head == null) return null;

            var data = new Pay2ExcelAuditData
            {
                RUN_ID = runId,
                PERIOD_DATE = head.PERIOD_DATE,
                WORKSHOP_NAME = head.WS_NAME ?? "",
                PERIOD_TITLE = BuildPeriodTitle(head.PERIOD_DATE)
            };

            // ── تنظیمات (PAY2_CONFIG) ──
            data.Config = (await _db.DoGetDataSQLAsync<Pay2CfgRow>(
                "SELECT CFG_KEY, CFG_VALUE FROM PAY2_CONFIG WITH (NOLOCK)")).ToList();

            var cfg = data.Config.ToDictionary(c => c.CFG_KEY, c => c.CFG_VALUE, StringComparer.OrdinalIgnoreCase);
            string monthDaysMode = GetStr(cfg, "MONTH_DAYS_MODE", "30");
            string cfgShiftMode = GetStr(cfg, "SHIFT_MODE", "PCT");
            data.TAX_YEAR = (short)GetInt(cfg, "TAX_YEAR", 1403);
            data.MONTH_DAYS = ComputeMonthDays(head.PERIOD_DATE, monthDaysMode);

            long perStart = head.PERIOD_DATE + 1;
            long perEnd = head.PERIOD_DATE + data.MONTH_DAYS;

            // ── پله‌های مالیات ──
            data.TaxBrackets = (await _db.DoGetDataSQLAsync<Pay2TaxBracketRow>(
                @"SELECT SORT_ORDER, UPPER_LIMIT, RATE_PCT, FIXED_TAX
                  FROM PAY2_TAX_BRACKET WITH (NOLOCK)
                  WHERE TAX_YEAR = @taxYear ORDER BY SORT_ORDER",
                new { taxYear = data.TAX_YEAR })).ToList();

            // ── پرسنلِ همین اجرا + کارکرد خام + صفات ──
            data.Employees = (await _db.DoGetDataSQLAsync<Pay2AuditEmpRow>(
                @"SELECT
                    E.EMP_ID, E.EMP_CODE, (E.LAST_NAME + N' ' + E.FIRST_NAME) AS FULL_NAME,
                    A.WORK_DAYS, A.DAYS, A.DAYSB, A.FRID_COUNT, A.TDAYS,
                    A.OT_NORMAL_H, A.OT_HOLIDAY_H, A.OT_ADMIN_H, A.LEAVE_DAYS,
                    A.PERF_AMOUNT, A.TRANSP_AMOUNT, A.KASR_OTHER,
                    E.INS_TYPE, E.TAX_EXEMPT, E.IS_MANAGER, E.IS_JANBAZ, E.REGION_DEPRIVATION
                  FROM PAY2_RUN_LINE RL WITH (NOLOCK)
                  INNER JOIN PAY2_EMPLOYEE E WITH (NOLOCK) ON RL.EMP_ID = E.EMP_ID
                  INNER JOIN PAY2_RUN R WITH (NOLOCK) ON RL.RUN_ID = R.RUN_ID
                  LEFT  JOIN PAY2_ATTENDANCE A WITH (NOLOCK) ON A.PER_ID = R.PER_ID AND A.EMP_ID = RL.EMP_ID
                  WHERE RL.RUN_ID = @runId
                  ORDER BY E.LAST_NAME, E.FIRST_NAME",
                new { runId })).ToList();

            // ── خطوط احکامِ فعال در بازهٔ دوره (با basis/مشمولیت مؤثر + روزهای فعال + پایهٔ روزانه) ──
            data.DecreeLines = (await _db.DoGetDataSQLAsync<Pay2AuditDecreeLineRow>(
                @"SELECT
                    D.EMP_ID, D.DEC_ID, D.EFF_FROM, ISNULL(D.EFF_TO, 99991231) AS EFF_TO,
                    DL.ITEM_ID, ID.ITEM_CODE, ID.ITEM_NAME, ID.ITEM_TYPE, ID.SORT_ORDER,
                    ISNULL(DL.AMOUNT, 0) AS RAW_AMOUNT,
                    COALESCE(OV.BASIS_OV, DL.BASIS_OV, ID.CALC_BASIS) AS EFF_BASIS,
                    COALESCE(OV.INS_OV,   DL.INS_OV,   ID.INS_SUBJECT) AS EFF_INS,
                    COALESCE(OV.TAX_OV,   DL.TAX_OV,   ID.TAX_SUBJECT) AS EFF_TAX,
                    ID.PAY_BASE_DAYS, ID.INS_BASE_DAYS,
                    COALESCE(NULLIF(DL.SHIFT_MODE_OV, N''), NULLIF(D.SHIFT_MODE, N''), NULLIF(@wsShiftMode, N''), @cfgShiftMode, 'PCT') AS EFF_SHIFT_MODE,
                    CASE
                        WHEN (CASE WHEN D.EFF_FROM > @perStart THEN D.EFF_FROM ELSE @perStart END)
                           <= (CASE WHEN ISNULL(D.EFF_TO, 99991231) < @perEnd THEN ISNULL(D.EFF_TO, 99991231) ELSE @perEnd END)
                        THEN ((CASE WHEN ISNULL(D.EFF_TO, 99991231) < @perEnd THEN ISNULL(D.EFF_TO, 99991231) ELSE @perEnd END) % 100)
                           - ((CASE WHEN D.EFF_FROM > @perStart THEN D.EFF_FROM ELSE @perStart END) % 100) + 1
                        ELSE 0
                    END AS ACTIVE_DAYS,
                    ISNULL(DB.DAILY_BASE, 0) AS DEC_DAILY_BASE
                  FROM PAY2_DECREE D WITH (NOLOCK)
                  INNER JOIN PAY2_DECREE_LINE DL WITH (NOLOCK) ON D.DEC_ID = DL.DEC_ID
                  INNER JOIN PAY2_ITEM_DEF ID WITH (NOLOCK) ON DL.ITEM_ID = ID.ITEM_ID
                  INNER JOIN PAY2_RUN_LINE RL WITH (NOLOCK) ON RL.RUN_ID = @runId AND RL.EMP_ID = D.EMP_ID
                  OUTER APPLY (
                      SELECT TOP 1 O.INS_OV, O.TAX_OV, O.BASIS_OV
                      FROM PAY2_OVERRIDE O WITH (NOLOCK)
                      WHERE O.EMP_ID = D.EMP_ID AND O.ITEM_ID = DL.ITEM_ID
                        AND O.VALID_FROM <= @periodDate AND (O.VALID_TO IS NULL OR O.VALID_TO >= @periodDate)
                      ORDER BY O.VALID_FROM DESC
                  ) OV
                  OUTER APPLY (
                      SELECT TOP 1 DL2.AMOUNT AS DAILY_BASE
                      FROM PAY2_DECREE_LINE DL2 WITH (NOLOCK)
                      INNER JOIN PAY2_ITEM_DEF ID2 WITH (NOLOCK) ON DL2.ITEM_ID = ID2.ITEM_ID
                      WHERE DL2.DEC_ID = D.DEC_ID AND ID2.ITEM_CODE IN ('BASE_SAL', 'BASE_SAL_B')
                      ORDER BY CASE WHEN ID2.ITEM_CODE = 'BASE_SAL_B' THEN 1 ELSE 2 END
                  ) DB
                  WHERE D.IS_CONFIRMED = 1 AND ID.IS_ACTIVE = 1
                    AND ID.ITEM_CODE NOT IN ('INS_DED','TAX_DED','LOAN_DED','ADVANCE_DED')
                    AND D.EFF_FROM <= @perEnd
                    AND (D.EFF_TO IS NULL OR D.EFF_TO >= @perStart)
                  ORDER BY D.EMP_ID, D.EFF_FROM, ID.SORT_ORDER",
                new { runId, perStart, perEnd, periodDate = head.PERIOD_DATE, wsShiftMode = head.WS_SHIFT_MODE, cfgShiftMode })).ToList();

            // ── مقادیر دستیِ آیتم‌ها (PAY2_ATT_VALUE) که در حکم نیستند ──
            data.AttValues = (await _db.DoGetDataSQLAsync<Pay2AuditAttValueRow>(
                @"SELECT AV.EMP_ID, AV.ITEM_ID, ID.ITEM_CODE, ID.ITEM_NAME, ID.ITEM_TYPE, ID.SORT_ORDER,
                         AV.VALUE, ID.INS_SUBJECT, ID.TAX_SUBJECT
                  FROM PAY2_ATT_VALUE AV WITH (NOLOCK)
                  INNER JOIN PAY2_ITEM_DEF ID WITH (NOLOCK) ON AV.ITEM_ID = ID.ITEM_ID
                  INNER JOIN PAY2_RUN R WITH (NOLOCK) ON R.RUN_ID = @runId
                  INNER JOIN PAY2_RUN_LINE RL WITH (NOLOCK) ON RL.RUN_ID = @runId AND RL.EMP_ID = AV.EMP_ID
                  WHERE AV.PER_ID = R.PER_ID AND AV.VALUE <> 0",
                new { runId })).ToList();

            // ── نتیجهٔ قطعیِ موتور C# (برای شیت کنترل تطابق) ──
            data.Results = (await _db.DoGetDataSQLAsync<Pay2AuditResultRow>(
                @"SELECT EMP_ID, GROSS_PAY, INS_BASE, INS_WORKER, TAX_BASE, TAX_AMOUNT,
                         LOAN_DED, ADVANCE_DED, OTHER_DED, TOTAL_DED, NET_PAY
                  FROM PAY2_RUN_LINE WITH (NOLOCK) WHERE RUN_ID = @runId",
                new { runId })).ToList();

            // ── مبالغ ریزِ قطعیِ هر آیتم (برای تطبیق ستون‌ها) ──
            data.Details = (await _db.DoGetDataSQLAsync<Pay2AuditDetailRow>(
                @"SELECT D.EMP_ID, I.ITEM_CODE, D.AMOUNT
                  FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                  INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                  WHERE D.RUN_ID = @runId",
                new { runId })).ToList();

            // ── تعریف آیتم‌ها (برای آیتم‌های خودکار: اضافه‌کار/پاداش/ناقل) ──
            data.ItemDefs = (await _db.DoGetDataSQLAsync<Pay2AuditItemDefRow>(
                @"SELECT ITEM_ID, ITEM_CODE, ITEM_NAME, ITEM_TYPE, INS_SUBJECT, TAX_SUBJECT, SORT_ORDER
                  FROM PAY2_ITEM_DEF WITH (NOLOCK)")).ToList();

            // ── ستون‌های پویا (هم‌ترتیب با گرید): آیتم‌هایی که در این اجرا مقدار دارند ──
            data.Columns = (await _db.DoGetDataSQLAsync<Pay2AuditColumn>(
                @"SELECT DISTINCT I.ITEM_CODE, I.ITEM_NAME, I.SORT_ORDER
                  FROM PAY2_RUN_DETAIL D WITH (NOLOCK)
                  INNER JOIN PAY2_ITEM_DEF I WITH (NOLOCK) ON D.ITEM_ID = I.ITEM_ID
                  WHERE D.RUN_ID = @runId
                  ORDER BY I.SORT_ORDER",
                new { runId })).ToList();

            return data;
        }

        // ── محاسبهٔ طول ماه شمسی (عیناً مطابق منطق SP_PAY2_CALC_RUN) ──
        private static int ComputeMonthDays(long periodDate, string monthDaysMode)
        {
            int month = (int)((periodDate / 100) % 100);
            long year = periodDate / 10000;
            bool isLeap = ((25 * year + 11) % 33) < 8;

            if (string.Equals(monthDaysMode, "30", StringComparison.OrdinalIgnoreCase)) return 30;
            if (month <= 6) return 31;
            if (month >= 7 && month <= 11) return 30;
            if (month == 12 && isLeap) return 30;
            return 29;
        }

        private static string BuildPeriodTitle(long periodDate)
        {
            long year = periodDate / 10000;
            int month = (int)((periodDate / 100) % 100);
            string monthName = (month >= 1 && month <= 12) ? PersianMonthNames[month - 1] : "";
            return string.IsNullOrEmpty(monthName) ? year.ToString() : $"{monthName} {year}";
        }

        private static string GetStr(Dictionary<string, string?> cfg, string key, string def)
            => cfg.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : def;

        private static int GetInt(Dictionary<string, string?> cfg, string key, int def)
            => cfg.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : def;

        private class HeadRow
        {
            public long PERIOD_DATE { get; set; }
            public int WS_ID { get; set; }
            public string? WS_NAME { get; set; }
            public string? WS_SHIFT_MODE { get; set; }
        }
    }
}
