using ClosedXML.Excel;
using Dapper;
using Safir.Shared.Interfaces;
using System.Data;

namespace Safir.Server.CostClose
{
    /// <summary>
    /// سازنده گزارش اکسل هیئت‌مدیره.
    ///
    /// شیت‌های موجود گزارش فعلی، به‌علاوه دو شیت جدید که امروز
    /// وجود ندارند: «خلاصه اجرا» و «بیشترین تغییر نرخ».
    ///
    /// شیت دوم پاسخ سؤالی است که تا امروز قابل جواب نبود:
    /// «چرا قیمت تمام‌شده این کالا نسبت به ماه قبل عوض شد؟»
    /// </summary>
    public interface IBoardReportBuilder
    {
        Task<byte[]> BuildAsync(int runId);
    }

    public sealed class BoardReportBuilder : IBoardReportBuilder
    {
        private readonly IDatabaseService _db;
        public BoardReportBuilder(IDatabaseService db) => _db = db;

        public async Task<byte[]> BuildAsync(int runId)
        {
            using var grid = await _db.DoGetDataSQLAsyncMultiple(
                "EXEC dbo.CC_sp_S13_ReportData @RunId=@r", new { r = runId });

            var margins = (await grid.ReadAsync()).ToList();
            var summary = (await grid.ReadAsync()).ToList();
            var conv    = (await grid.ReadAsync()).ToList();
            var changes = (await grid.ReadAsync()).ToList();

            using var wb = new XLWorkbook();

            AddSheet(wb, "سود کالا به کالا", margins, freezeTop: true);
            AddSheet(wb, "هزینه تبدیل",      conv);
            AddSheet(wb, "بیشترین تغییر نرخ", changes, freezeTop: true);
            AddSheet(wb, "خلاصه اجرا",       summary);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void AddSheet(
            XLWorkbook wb, string name, List<dynamic> rows, bool freezeTop = false)
        {
            var ws = wb.Worksheets.Add(name);
            ws.RightToLeft = true;

            if (rows.Count == 0)
            {
                ws.Cell(1, 1).Value = "داده‌ای وجود ندارد";
                return;
            }

            var cols = ((IDictionary<string, object>)rows[0]).Keys.ToList();

            // سرستون‌ها
            for (int c = 0; c < cols.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = cols[c].Replace('_', ' ');
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // داده
            for (int r = 0; r < rows.Count; r++)
            {
                var dict = (IDictionary<string, object>)rows[r];

                for (int c = 0; c < cols.Count; c++)
                {
                    var v = dict[cols[c]];
                    var cell = ws.Cell(r + 2, c + 1);

                    if (v is null) { cell.Value = ""; continue; }

                    switch (v)
                    {
                        case double or decimal or float:
                            var d = Convert.ToDecimal(v);
                            cell.Value = d;
                            cell.Style.NumberFormat.Format = "#,##0;#,##0-";
                            if (d < 0) cell.Style.Font.FontColor = XLColor.FromHtml("#B4342F");
                            break;

                        case int or long or short:
                            cell.Value = Convert.ToInt64(v);
                            cell.Style.NumberFormat.Format = "#,##0;#,##0-";
                            break;

                        case DateTime dt:
                            cell.Value = dt;
                            break;

                        default:
                            cell.Value = v.ToString();
                            break;
                    }
                }
            }

            ws.Columns().AdjustToContents(10.0, 45.0);
            ws.SheetView.FreezeRows(freezeTop ? 1 : 0);

            if (rows.Count > 1)
                ws.Range(1, 1, rows.Count + 1, cols.Count).SetAutoFilter();
        }
    }
}
