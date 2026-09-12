using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Safir.Client.Components.Grid
{
    /// <summary>
    /// سازندهٔ عمومیِ فایل واقعی XLSX (OpenXML خام، بدون کتابخانهٔ خارجی) برای
    /// هر گرید/لیستی در سراسر برنامه — نسخهٔ عمومی‌شدهٔ همان روشِ
    /// SalaryGridExcelExporter، اینجا به‌جای مدل ثابتِ فیش حقوقی، ستون‌ها را
    /// خودِ صفحه با ExcelCol&lt;T&gt; مشخص می‌کند. مستقل از هر پکیج (کار می‌کند
    /// چه Blazor Server باشد چه WASM)، راست‌به‌چپ، بدون نمادِ علمی روی اعداد
    /// بزرگ، و بدون مثلثِ خطای «عدد به‌صورت متن» روی ستون‌های کدی.
    /// </summary>
    public sealed class ExcelCol<T>
    {
        public string Title { get; init; } = "";
        public bool IsNumber { get; init; }
        public bool Money { get; init; }

        /// <summary>
        /// ستونی که هم ریال دارد هم مقدارِ کسری (مثل «مبلغ/مقدار» در
        /// مغایرت‌ها). با Money تنها، ۰٫۰۰۲۹ واحد در فایل «۰» دیده می‌شد.
        /// این پرچم قالبِ #,##0.###### را می‌دهد که هر دو را درست نشان
        /// می‌دهد. اگر هر دو ست شوند، همین برنده است.
        /// </summary>
        public bool Fractional { get; init; }
        public bool Sum { get; init; }
        public Func<T, string>? Text { get; init; }
        public Func<T, double?>? Number { get; init; }
    }

    public static class GenericExcelExporter
    {
        /// <summary>
        /// تولید بایت‌های XLSX از یک لیست و تعریفِ ستون‌ها. اگر footerLabelColumnIndex
        /// داده شود و حداقل یک ستونِ Sum وجود داشته باشد، یک ردیفِ جمع در انتها اضافه می‌شود.
        /// </summary>
        public static byte[] Build<T>(
            IEnumerable<T> rows,
            IReadOnlyList<ExcelCol<T>> columns,
            string? footerLabel = "جمع",
            int footerLabelColumnIndex = 0)
        {
            var list = rows?.ToList() ?? new List<T>();
            int colCount = columns.Count;
            bool hasSum = columns.Any(c => c.Sum);
            bool hasFooter = hasSum && list.Count > 0 && footerLabel is not null;
            int totalRows = 1 + list.Count + (hasFooter ? 1 : 0);

            var sd = new StringBuilder();
            sd.Append("<sheetData>");

            sd.Append("<row r=\"1\">");
            for (int j = 0; j < colCount; j++)
                sd.Append(TextCell(ColLetter(j) + "1", columns[j].Title));
            sd.Append("</row>");

            var sums = new double[colCount];
            int rowNum = 2;
            foreach (var row in list)
            {
                sd.Append("<row r=\"").Append(rowNum).Append("\">");
                for (int j = 0; j < colCount; j++)
                {
                    var c = columns[j];
                    string cellRef = ColLetter(j) + rowNum;
                    if (c.IsNumber)
                    {
                        double val = c.Number?.Invoke(row) ?? 0d;
                        if (c.Sum) sums[j] += val;
                        sd.Append(NumberCell(cellRef, val, c.Money, c.Fractional));
                    }
                    else
                    {
                        sd.Append(TextCell(cellRef, c.Text?.Invoke(row) ?? ""));
                    }
                }
                sd.Append("</row>");
                rowNum++;
            }

            if (hasFooter)
            {
                sd.Append("<row r=\"").Append(rowNum).Append("\">");
                for (int j = 0; j < colCount; j++)
                {
                    var c = columns[j];
                    string cellRef = ColLetter(j) + rowNum;
                    if (c.IsNumber && c.Sum)
                        sd.Append(NumberCell(cellRef, sums[j], c.Money, c.Fractional));
                    else if (j == footerLabelColumnIndex)
                        sd.Append(TextCell(cellRef, footerLabel));
                    else
                        sd.Append("<c r=\"").Append(cellRef).Append("\"/>");
                }
                sd.Append("</row>");
                rowNum++;
            }

            sd.Append("</sheetData>");

            string lastCellRef = ColLetter(Math.Max(colCount - 1, 0)) + Math.Max(totalRows, 1);
            string colsXml = $"<cols><col min=\"1\" max=\"{Math.Max(colCount, 1)}\" width=\"18\" customWidth=\"1\"/></cols>";
            string ignoredXml = $"<ignoredErrors><ignoredError sqref=\"A1:{lastCellRef}\" numberStoredAsText=\"1\"/></ignoredErrors>";

            string sheetXml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<sheetViews><sheetView rightToLeft=\"1\" workbookViewId=\"0\"/></sheetViews>" +
                colsXml +
                sd.ToString() +
                ignoredXml +
                "</worksheet>";

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                AddZipEntry(zip, "[Content_Types].xml", ContentTypesXml);
                AddZipEntry(zip, "_rels/.rels", RootRelsXml);
                AddZipEntry(zip, "xl/workbook.xml", WorkbookXml);
                AddZipEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
                AddZipEntry(zip, "xl/styles.xml", StylesXml);
                AddZipEntry(zip, "xl/worksheets/sheet1.xml", sheetXml);
            }
            return ms.ToArray();
        }

        private static string TextCell(string cellRef, string? value)
        {
            if (string.IsNullOrEmpty(value)) return "<c r=\"" + cellRef + "\"/>";
            return "<c r=\"" + cellRef + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + XmlEsc(value) + "</t></is></c>";
        }

        private static string NumberCell(string cellRef, double value, bool money, bool fractional = false)
        {
            string styleAttr = fractional ? " s=\"2\"" : money ? " s=\"1\"" : "";
            return "<c r=\"" + cellRef + "\"" + styleAttr + "><v>" + value.ToString(CultureInfo.InvariantCulture) + "</v></c>";
        }

        private static void AddZipEntry(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var s = entry.Open();
            var b = Encoding.UTF8.GetBytes(content);
            s.Write(b, 0, b.Length);
        }

        private static string ColLetter(int index)
        {
            var sb = new StringBuilder();
            index++;
            while (index > 0)
            {
                int rem = (index - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                index = (index - 1) / 26;
            }
            return sb.ToString();
        }

        private static string XmlEsc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if ((c < 0x20 && c != '\t' && c != '\n' && c != '\r') || c == '￾' || c == '￿')
                    continue;
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private const string ContentTypesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>";

        private const string RootRelsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private const string WorkbookXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            "</workbook>";

        private const string WorkbookRelsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        private const string StylesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"2\">" +
            "<numFmt numFmtId=\"164\" formatCode=\"#,##0\"/>" +
            // ⚠️ یک قالب برای هر دو: «#» رقمِ نبودِ معنا را نشان نمی‌دهد،
            //    پس ۲۰۷۳۰۷۴ می‌شود «2,073,074» و ۰٫۰۰۲۹۲۶ می‌شود «0.002926».
            //    ستونی که هم ریال دارد هم مقدار، با همین یکی درست در می‌آید.
            "<numFmt numFmtId=\"165\" formatCode=\"#,##0.######\"/>" +
            "</numFmts>" +
            "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border/></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"3\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";
    }
}
