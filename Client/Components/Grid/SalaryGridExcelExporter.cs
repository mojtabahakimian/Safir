using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Safir.Shared.Models.Salary;

namespace Safir.Client.Components.Grid
{
    /// <summary>
    /// سازندهٔ فایل واقعی XLSX (OpenXML) برای گرید محاسبهٔ حقوق (Syncfusion).
    /// مستقل از هر کتابخانهٔ خارجی (ZIP + XML خام) تا اعداد بزرگ بدون نماد علمی،
    /// بدون مثلث سبزِ خطا، جمع‌پذیر و فارسیِ درست خروجی شوند.
    /// خروجی شامل ستون‌های پویای آیتم‌های حقوقی + ستون‌های مبلغیِ ثابت + ردیف جمع است.
    /// منطق پایه از کامپوننت عمومی Pay2DataGrid اقتباس و برای ساختار فیش حقوقی تخصصی شده است.
    /// </summary>
    public static class SalaryGridExcelExporter
    {
        private enum Kind { Text, TextSafe, Number }

        private sealed class XCol
        {
            public string Title = "";
            public Kind Kind;
            public bool Money;
            public bool Sum;
            public Func<Pay2RunLineDto, string>? Text;
            public Func<Pay2RunLineDto, double>? Number;
        }

        /// <summary>
        /// تولید بایت‌های فایل XLSX از نتیجهٔ یک اجرای محاسبه.
        /// </summary>
        public static byte[] Build(Pay2RunResultDto data, string footerLabel = "جمع کل دوره")
        {
            data ??= new Pay2RunResultDto();
            var lines = data.Lines ?? new List<Pay2RunLineDto>();

            // ── ساخت لیست ستون‌های خروجی (هم‌ترتیب با گرید) ──
            var cols = new List<XCol>
            {
                new XCol { Title = "کد پرسنلی", Kind = Kind.TextSafe, Text = l => l.EMP_CODE ?? "" },
                new XCol { Title = "نام و نام خانوادگی", Kind = Kind.Text, Text = l => l.FULL_NAME ?? "" },
                new XCol { Title = "کارکرد", Kind = Kind.Number, Money = false, Number = l => (double)l.WORK_DAYS },
            };

            // ستون‌های پویای آیتم‌های حقوقی
            if (data.Columns != null)
            {
                foreach (var c in data.Columns)
                {
                    var code = c.ITEM_CODE;
                    cols.Add(new XCol
                    {
                        Title = c.ITEM_NAME,
                        Kind = Kind.Number,
                        Money = true,
                        Sum = true,
                        Number = l => l.Details != null && l.Details.TryGetValue(code, out var v) ? v : 0d
                    });
                }
            }

            // ستون‌های مبلغیِ ثابت
            cols.Add(new XCol { Title = "ناخالص حقوق", Kind = Kind.Number, Money = true, Sum = true, Number = l => l.GROSS_PAY });
            cols.Add(new XCol { Title = "مبنای بیمه", Kind = Kind.Number, Money = true, Sum = true, Number = l => l.INS_BASE });
            cols.Add(new XCol { Title = "بیمه کارگر", Kind = Kind.Number, Money = true, Sum = true, Number = l => l.INS_WORKER });
            cols.Add(new XCol { Title = "مالیات", Kind = Kind.Number, Money = true, Sum = true, Number = l => l.TAX_AMOUNT });
            cols.Add(new XCol { Title = "مساعده/وام/سایر کسورات", Kind = Kind.Number, Money = true, Sum = true, Number = l => l.TOTAL_DED });
            cols.Add(new XCol { Title = "خالص پرداختی", Kind = Kind.Number, Money = true, Sum = true, Number = l => l.NET_PAY });

            int colCount = cols.Count;
            bool hasFooter = lines.Count > 0;
            int totalRows = 1 + lines.Count + (hasFooter ? 1 : 0);

            // ── بدنهٔ worksheet ──
            var sd = new StringBuilder();
            sd.Append("<sheetData>");

            // هدر (ردیف ۱)
            sd.Append("<row r=\"1\">");
            for (int j = 0; j < colCount; j++)
                sd.Append(TextCell(ColLetter(j) + "1", cols[j].Title));
            sd.Append("</row>");

            // ردیف‌های داده + محاسبهٔ جمع‌ها
            var sums = new double[colCount];
            int rowNum = 2;
            foreach (var line in lines)
            {
                sd.Append("<row r=\"").Append(rowNum).Append("\">");
                for (int j = 0; j < colCount; j++)
                {
                    var xc = cols[j];
                    string cellRef = ColLetter(j) + rowNum;
                    if (xc.Kind == Kind.Number)
                    {
                        double val = xc.Number?.Invoke(line) ?? 0d;
                        if (xc.Sum) sums[j] += val;
                        sd.Append(NumberCell(cellRef, val, xc.Money));
                    }
                    else
                    {
                        sd.Append(TextCell(cellRef, xc.Text?.Invoke(line) ?? ""));
                    }
                }
                sd.Append("</row>");
                rowNum++;
            }

            // ردیف جمع
            if (hasFooter)
            {
                sd.Append("<row r=\"").Append(rowNum).Append("\">");
                for (int j = 0; j < colCount; j++)
                {
                    var xc = cols[j];
                    string cellRef = ColLetter(j) + rowNum;
                    if (xc.Kind == Kind.Number && xc.Sum)
                        sd.Append(NumberCell(cellRef, sums[j], xc.Money));
                    else if (j == 1) // برچسب جمع زیر ستون «نام»
                        sd.Append(TextCell(cellRef, footerLabel));
                    else
                        sd.Append("<c r=\"").Append(cellRef).Append("\"/>");
                }
                sd.Append("</row>");
                rowNum++;
            }

            sd.Append("</sheetData>");

            // عرضِ کافی تا اعداد بزرگ به نماد علمی نروند + سرکوبِ مثلثِ «متنِ عددی» روی کدها
            string lastCellRef = ColLetter(colCount - 1) + Math.Max(totalRows, 1);
            string colsXml = $"<cols><col min=\"1\" max=\"{colCount}\" width=\"18\" customWidth=\"1\"/></cols>";
            string ignoredXml = $"<ignoredErrors><ignoredError sqref=\"A1:{lastCellRef}\" numberStoredAsText=\"1\"/></ignoredErrors>";

            string sheetXml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<sheetViews><sheetView rightToLeft=\"1\" workbookViewId=\"0\"/></sheetViews>" +
                colsXml +
                sd.ToString() +
                ignoredXml +
                "</worksheet>";

            // ── ساخت فایل XLSX (ZIP از parts) ──
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

        private static string NumberCell(string cellRef, double value, bool money)
        {
            string styleAttr = money ? " s=\"1\"" : "";
            return "<c r=\"" + cellRef + "\"" + styleAttr + "><v>" + value.ToString(CultureInfo.InvariantCulture) + "</v></c>";
        }

        private static void AddZipEntry(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var s = entry.Open();
            var b = Encoding.UTF8.GetBytes(content);
            s.Write(b, 0, b.Length);
        }

        // index صفر‌مبنا → حرف ستون اکسل (0→A، 25→Z، 26→AA ...)
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
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0\"/></numFmts>" +
            "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border/></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";
    }
}
