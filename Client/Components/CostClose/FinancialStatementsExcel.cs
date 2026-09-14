using System.Globalization;
using System.IO.Compression;
using System.Text;
using Safir.Shared.Models.CostClose;

namespace Safir.Client.Components.CostClose
{
    /// <summary>
    /// خروجی اکسلِ گزارش چنددوره‌ای — جدول به‌علاوهٔ نمودارهای واقعیِ اکسل.
    ///
    /// ── چرا سازنده‌ی جدا و نه GenericExcelExporter ──
    /// آن یکی یک جدولِ تخت می‌سازد و بس. اینجا فایل باید بخش‌های تازه‌ای
    /// داشته باشد (chart، drawing و رابطه‌هایشان) و نمودار باید به سلول‌های
    /// همان شیت ارجاع بدهد، پس باید بدانیم هر رقم در کدام سطر نشسته.
    ///
    /// ── چرا نمودارِ بومیِ اکسل و نه تصویر ──
    /// نمودارِ تصویری در اکسل مرده است: نه بزرگ‌نمایی درست دارد، نه با
    /// تغییر عدد به‌روز می‌شود، نه می‌شود رنگ و محورش را عوض کرد. نمودارِ
    /// بومی به سلول‌ها گره می‌خورد، پس اگر کسی رقمی را در فایل اصلاح کند
    /// نمودار هم با آن حرکت می‌کند — و همان کاری است که کاربرِ اکسل انتظار
    /// دارد. ClosedXML نمودار نمی‌سازد، پس XMLـش دستی نوشته شده.
    /// </summary>
    public static class FinancialStatementsExcel
    {
        // شماره‌ی ردیف‌های صورت سود و زیان، عیناً از رویه‌ی صورت‌های مالی
        private const int IncSales = 10, IncGross = 30, IncOperating = 90;

        private sealed class Row
        {
            public string    Section { get; init; } = "";
            public string    Text    { get; init; } = "";
            public int       SrcRow  { get; init; }
            public double?[] Values  { get; init; } = Array.Empty<double?>();
            public double?   Total   { get; init; }
            public double?   Consol  { get; init; }
        }

        public static byte[] Build(MultiPeriodStatementsDto data)
        {
            var periods = data.Periods;
            var hasConsol = data.Consolidated is not null;

            // ── ۱) سطرها ──
            var rows = new List<Row>();

            void Section(string name, Func<FinancialStatementsDto, List<FinLineDto>> pick)
            {
                rows.Add(new Row { Section = name, Text = "", SrcRow = -1 });   // سطر عنوان

                foreach (var line in pick(periods[0].Statements))
                {
                    var vals = periods
                        .Select(p => pick(p.Statements).FirstOrDefault(x => x.Row == line.Row)?.Amount)
                        .ToArray();

                    rows.Add(new Row
                    {
                        Section = name,
                        Text    = line.Text ?? "",
                        SrcRow  = line.Row,
                        Values  = vals,
                        Total   = Summable(line.Text) ? vals.Sum(v => v ?? 0) : null,
                        Consol  = data.Consolidated is null
                            ? null
                            : pick(data.Consolidated).FirstOrDefault(x => x.Row == line.Row)?.Amount
                    });
                }

                rows.Add(new Row { SrcRow = -2 });   // فاصله
            }

            Section("سود و زیان", st => st.Income);
            Section("بهای تمام‌شده کالای ساخته‌شده", st => st.Cogm);
            Section("بهای تمام‌شده کالای فروش‌رفته", st => st.Cogs);

            // ── ۲) شیت ──
            var colCount = 2 + periods.Count + 1 + (hasConsol ? 1 : 0);
            var sd = new StringBuilder();
            sd.Append("<sheetData>");

            // سرستون
            sd.Append("<row r=\"1\" s=\"3\" customFormat=\"1\">");
            sd.Append(Text("A1", "بخش", 3)).Append(Text("B1", "شرح", 3));
            for (var i = 0; i < periods.Count; i++)
                sd.Append(Text(Col(2 + i) + "1", periods[i].Label, 3));
            sd.Append(Text(Col(2 + periods.Count) + "1", "جمع", 3));
            if (hasConsol)
                sd.Append(Text(Col(3 + periods.Count) + "1", "تجمیعی — " + data.ConsolidatedLabel, 3));
            sd.Append("</row>");

            // سطرهای کلیدیِ سود و زیان را برای نمودار نگه می‌داریم
            var chartRows = new Dictionary<int, int>();

            var r = 2;
            foreach (var row in rows)
            {
                if (row.SrcRow == -2) { r++; continue; }             // فاصله

                sd.Append("<row r=\"").Append(r).Append("\">");

                if (row.SrcRow == -1)
                {
                    sd.Append(Text("A" + r, row.Section, 3));
                }
                else
                {
                    sd.Append(Text("A" + r, row.Section));
                    sd.Append(Text("B" + r, row.Text));

                    for (var i = 0; i < periods.Count; i++)
                        if (i < row.Values.Length && row.Values[i] is { } v)
                            sd.Append(Num(Col(2 + i) + r, v));

                    if (row.Total is { } t)  sd.Append(Num(Col(2 + periods.Count) + r, t));
                    if (hasConsol && row.Consol is { } cv)
                        sd.Append(Num(Col(3 + periods.Count) + r, cv));

                    if (row.Section == "سود و زیان" &&
                        row.SrcRow is IncSales or IncGross or IncOperating)
                        chartRows[row.SrcRow] = r;
                }

                sd.Append("</row>");
                r++;
            }

            sd.Append("</sheetData>");

            var cols =
                "<cols>" +
                "<col min=\"1\" max=\"1\" width=\"30\" customWidth=\"1\"/>" +
                "<col min=\"2\" max=\"2\" width=\"38\" customWidth=\"1\"/>" +
                "<col min=\"3\" max=\"" + colCount + "\" width=\"20\" customWidth=\"1\"/>" +
                "</cols>";

            // نمودار فقط وقتی معنی دارد که هر سه رقم و دست‌کم یک دوره باشد
            var withChart = chartRows.Count == 3 && periods.Count >= 1;

            var sheetXml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheetViews><sheetView rightToLeft=\"1\" tabSelected=\"1\" workbookViewId=\"0\">" +
                "<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>" +
                "</sheetView></sheetViews>" +
                cols + sd +
                (withChart ? "<drawing r:id=\"rId1\"/>" : "") +
                "</worksheet>";

            // ── ۳) بسته‌بندی ──
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                Add(zip, "[Content_Types].xml", ContentTypes(withChart));
                Add(zip, "_rels/.rels", RootRels);
                Add(zip, "xl/workbook.xml", Workbook);
                Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
                Add(zip, "xl/styles.xml", Styles);
                Add(zip, "xl/worksheets/sheet1.xml", sheetXml);

                if (withChart)
                {
                    Add(zip, "xl/worksheets/_rels/sheet1.xml.rels", SheetRels);
                    Add(zip, "xl/drawings/drawing1.xml", Drawing(rows.Count + 4));
                    Add(zip, "xl/drawings/_rels/drawing1.xml.rels", DrawingRels);
                    Add(zip, "xl/charts/chart1.xml", Chart(chartRows, periods.Count));
                }
            }
            return ms.ToArray();
        }

        /// <summary>
        /// جمعِ افقی برای موجودی و درصد معنا ندارد — همان قاعده‌ی صفحه.
        /// </summary>
        private static bool Summable(string? text) =>
            text?.Contains("درصد") != true &&
            text?.Contains("موجودی") != true &&
            text?.Contains('٪') != true;

        // ═══════════════════════ نمودار ═══════════════════════

        /// <summary>
        /// نمودار ستونیِ سه‌سری: فروش، سود ناخالص، سود عملیاتی — به‌ازای هر
        /// دوره. به سلول‌های همان شیت ارجاع می‌دهد، پس با ویرایشِ ارقام
        /// خودش به‌روز می‌شود.
        /// </summary>
        private static string Chart(Dictionary<int, int> rows, int periodCount)
        {
            var firstCol = Col(2);
            var lastCol  = Col(1 + periodCount);
            var cats     = $"Sheet1!${firstCol}$1:${lastCol}$1";

            string Series(int idx, int srcRow, string color)
            {
                var row = rows[srcRow];
                return
                    "<c:ser>" +
                    $"<c:idx val=\"{idx}\"/><c:order val=\"{idx}\"/>" +
                    $"<c:tx><c:strRef><c:f>Sheet1!$B${row}</c:f></c:strRef></c:tx>" +
                    "<c:spPr><a:solidFill><a:srgbClr val=\"" + color + "\"/></a:solidFill></c:spPr>" +
                    $"<c:cat><c:strRef><c:f>{cats}</c:f></c:strRef></c:cat>" +
                    $"<c:val><c:numRef><c:f>Sheet1!${firstCol}${row}:${lastCol}${row}</c:f></c:numRef></c:val>" +
                    "</c:ser>";
            }

            return
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<c:roundedCorners val=\"0\"/>" +
                "<c:chart>" +
                "<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r>" +
                "<a:t>روند فروش و سود</a:t></a:r></a:p></c:rich></c:tx>" +
                "<c:overlay val=\"0\"/></c:title>" +
                "<c:autoTitleDeleted val=\"0\"/>" +
                "<c:plotArea><c:layout/>" +
                "<c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/>" +
                "<c:varyColors val=\"0\"/>" +
                Series(0, IncSales,     "2563EB") +
                Series(1, IncGross,     "10B981") +
                Series(2, IncOperating, "F59E0B") +
                "<c:gapWidth val=\"60\"/>" +
                "<c:axId val=\"111111111\"/><c:axId val=\"222222222\"/>" +
                "</c:barChart>" +
                "<c:catAx><c:axId val=\"111111111\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling>" +
                "<c:delete val=\"0\"/><c:axPos val=\"b\"/><c:crossAx val=\"222222222\"/></c:catAx>" +
                "<c:valAx><c:axId val=\"222222222\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling>" +
                "<c:delete val=\"0\"/><c:axPos val=\"l\"/>" +
                "<c:numFmt formatCode=\"#,##0\" sourceLinked=\"0\"/>" +
                "<c:majorGridlines/><c:crossAx val=\"111111111\"/></c:valAx>" +
                "</c:plotArea>" +
                "<c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend>" +
                "<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/>" +
                "</c:chart></c:chartSpace>";
        }

        /// <summary>نمودار زیر جدول لنگر می‌اندازد تا ارقام را نپوشاند.</summary>
        private static string Drawing(int topRow) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<xdr:twoCellAnchor>" +
            $"<xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{topRow}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>" +
            $"<xdr:to><xdr:col>8</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{topRow + 20}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" +
            "<xdr:graphicFrame macro=\"\">" +
            "<xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"نمودار\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>" +
            "<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm>" +
            "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
            "<c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId1\"/>" +
            "</a:graphicData></a:graphic>" +
            "</xdr:graphicFrame><xdr:clientData/>" +
            "</xdr:twoCellAnchor></xdr:wsDr>";

        // ═══════════════════════ بخش‌های ثابت ═══════════════════════

        private static string ContentTypes(bool withChart) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            (withChart
                ? "<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>" +
                  "<Override PartName=\"/xl/charts/chart1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>"
                : "") +
            "</Types>";

        private const string RootRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private const string Workbook =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            "</workbook>";

        private const string WorkbookRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        private const string SheetRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/>" +
            "</Relationships>";

        private const string DrawingRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart\" Target=\"../charts/chart1.xml\"/>" +
            "</Relationships>";

        /// <summary>۰ عادی، ۱ ریالی، ۲ بی‌استفاده، ۳ سرستونِ پررنگ.</summary>
        private const string Styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0;#,##0-\"/></numFmts>" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE0E7FF\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"4\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";

        // ═══════════════════════ کمکی ═══════════════════════

        private static string Text(string cellRef, string? value, int style = 0)
        {
            var s = style > 0 ? $" s=\"{style}\"" : "";
            if (string.IsNullOrEmpty(value)) return $"<c r=\"{cellRef}\"{s}/>";
            return $"<c r=\"{cellRef}\"{s} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(value)}</t></is></c>";
        }

        /// <summary>
        /// ⚠️ بی‌نهایت و NaN در اکسل عدد نیستند؛ نوشتنشان فایل را خراب
        /// می‌کند. همان درسی که در گزارش هیئت‌مدیره گرفتیم.
        /// </summary>
        private static string Num(string cellRef, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return Text(cellRef, double.IsNaN(value) ? "نامعتبر" : "بی‌نهایت");

            return $"<c r=\"{cellRef}\" s=\"1\"><v>{value.ToString("0.############", CultureInfo.InvariantCulture)}</v></c>";
        }

        private static string Col(int index)
        {
            var s = "";
            index++;
            while (index > 0)
            {
                var m = (index - 1) % 26;
                s = (char)('A' + m) + s;
                index = (index - 1) / 26;
            }
            return s;
        }

        private static string Esc(string v) =>
            v.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;");

        private static void Add(ZipArchive zip, string path, string content)
        {
            var e = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var s = e.Open();
            var b = Encoding.UTF8.GetBytes(content);
            s.Write(b, 0, b.Length);
        }
    }
}
