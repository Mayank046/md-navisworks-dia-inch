// MD Dia-Inch - minimal N-sheet .xlsx writer (no Excel install)
// Minimal OpenXML .xlsx writer. Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DiaInch.Core
{
    public static class XlsxWriter
    {
        public static void Save(string path, IList<DataTable> sheets, string author)
        {
            if (sheets == null || sheets.Count == 0) throw new ArgumentException("No sheets", "sheets");
            if (File.Exists(path)) File.Delete(path);
            int n = sheets.Count;
            using (var fs = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var sst = new SharedStrings();
                var ct = new StringBuilder();
                ct.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                for (int i = 1; i <= n; i++)
                    ct.Append("<Override PartName=\"/xl/worksheets/sheet" + i + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
                    "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/></Types>");
                Put(zip, "[Content_Types].xml", ct.ToString());

                Put(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");

                Put(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    "<dc:creator>" + X(author ?? Names.Author) + "</dc:creator><dc:title>" + X(Names.Product) + "</dc:title>" +
                    "<dcterms:created xsi:type=\"dcterms:W3CDTF\">" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "</dcterms:created></cp:coreProperties>");

                var wb = new StringBuilder();
                wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < n; i++)
                {
                    string nm = SafeSheetName(sheets[i] != null ? sheets[i].SheetName : null, "Sheet" + (i + 1));
                    string baseNm = nm; int k = 2;
                    while (!used.Add(nm)) { string suf = " (" + (k++) + ")"; nm = (baseNm.Length + suf.Length > 31 ? baseNm.Substring(0, 31 - suf.Length) : baseNm) + suf; }
                    wb.Append("<sheet name=\"" + X(nm) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
                }
                wb.Append("</sheets></workbook>");
                Put(zip, "xl/workbook.xml", wb.ToString());

                var rels = new StringBuilder();
                rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                for (int i = 1; i <= n; i++)
                    rels.Append("<Relationship Id=\"rId" + i + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + i + ".xml\"/>");
                rels.Append("<Relationship Id=\"rId" + (n + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                rels.Append("<Relationship Id=\"rId" + (n + 2) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>");
                Put(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

                Put(zip, "xl/styles.xml", Styles());
                for (int i = 0; i < n; i++)
                    Put(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", BuildSheet(sheets[i] ?? new DataTable(), sst));
                Put(zip, "xl/sharedStrings.xml", sst.ToXml());
            }
        }

        private static double ColWidth(DataTable t, int c)
        {
            string name = t.Columns[c] ?? "";
            int max = name.Length;
            int sample = Math.Min(t.Rows.Count, 500);
            for (int r = 0; r < sample; r++) { int l = t.Rows[r].Get(name).Length; if (l > max) max = l; }
            return Math.Max(8, Math.Min(60, max + 2));
        }

        private static string BuildSheet(DataTable table, SharedStrings sst)
        {
            int cols = table.Columns.Count;
            int pre = table.Preamble.Count;
            int off = pre + (table.HeaderRow ? 1 : 0);
            int rows = table.Rows.Count + off;
            bool outline = false;
            foreach (TableRow tr in table.Rows) if (tr.OutlineLevel > 0) { outline = true; break; }
            var sb = new StringBuilder(Math.Max(4096, rows * Math.Max(1, cols) * 24));
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            if (outline) sb.Append("<sheetPr><outlinePr summaryBelow=\"0\"/></sheetPr>");
            if (table.HeaderRow && table.FreezeHeader)
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"" + (pre + 1).ToString(CultureInfo.InvariantCulture) + "\" topLeftCell=\"A" + (pre + 2).ToString(CultureInfo.InvariantCulture) + "\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            else
                sb.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
            if (outline)
            {
                int maxLvl = 0;
                foreach (TableRow tr in table.Rows) if (tr.OutlineLevel > maxLvl) maxLvl = tr.OutlineLevel;
                sb.Append("<sheetFormatPr defaultRowHeight=\"15\" outlineLevelRow=\"").Append(Math.Min(7, maxLvl)).Append("\"/>");
            }
            if (cols > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < cols; c++)
                    sb.Append("<col min=\"").Append(c + 1).Append("\" max=\"").Append(c + 1)
                      .Append("\" width=\"").Append(ColWidth(table, c).ToString(CultureInfo.InvariantCulture)).Append("\" customWidth=\"1\"/>");
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            for (int p = 0; p < pre; p++)
            {
                int si = sst.Add(table.Preamble[p] ?? "");
                sb.Append("<row r=\"").Append(p + 1).Append("\"><c r=\"").Append(CellRef(0, p)).Append("\" t=\"s\" s=\"3\"><v>").Append(si).Append("</v></c></row>");
            }
            if (table.HeaderRow)
            {
                sb.Append("<row r=\"").Append(pre + 1).Append("\">");
                for (int c = 0; c < cols; c++)
                {
                    int si = sst.Add(table.Columns[c] ?? "");
                    sb.Append("<c r=\"").Append(CellRef(c, pre)).Append("\" t=\"s\" s=\"1\"><v>").Append(si).Append("</v></c>");
                }
                sb.Append("</row>");
            }
            for (int r = 0; r < table.Rows.Count; r++)
            {
                TableRow row = table.Rows[r];
                int rr = r + off; // 0-based sheet row
                string st = row.Style > 0 ? " s=\"" + row.Style.ToString(CultureInfo.InvariantCulture) + "\"" : "";
                sb.Append("<row r=\"").Append(rr + 1).Append("\"");
                if (row.OutlineLevel > 0) sb.Append(" outlineLevel=\"").Append(Math.Min(7, row.OutlineLevel)).Append("\"");
                sb.Append(">");
                for (int c = 0; c < cols; c++)
                {
                    string col = table.Columns[c];
                    string val = row.Get(col);
                    if (val.Length == 0)
                    {
                        if (row.Style > 0) sb.Append("<c r=\"").Append(CellRef(c, rr)).Append("\"").Append(st).Append("/>");
                        continue; // sparse: keeps tree-style level columns light
                    }
                    double num;
                    if (table.NumericColumns.Contains(col) &&
                        double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out num))
                    {
                        sb.Append("<c r=\"").Append(CellRef(c, rr)).Append("\"").Append(st).Append("><v>")
                          .Append(num.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
                        continue;
                    }
                    int si = sst.Add(val);
                    sb.Append("<c r=\"").Append(CellRef(c, rr)).Append("\" t=\"s\"").Append(st).Append("><v>").Append(si).Append("</v></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            if (table.AutoFilter && table.HeaderRow && cols > 0 && rows > 0)
                sb.Append("<autoFilter ref=\"A" + (pre + 1).ToString(CultureInfo.InvariantCulture) + ":").Append(CellRef(cols - 1, rows - 1)).Append("\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"3\"><font><sz val=\"10\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"10\"/><name val=\"Calibri\"/></font>" +
                "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFCCE8EC\"/></patternFill></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF00647D\"/></patternFill></fill></fills>" +
                "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                "<cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";
        }

        private sealed class SharedStrings
        {
            private readonly Dictionary<string, int> _map = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<string> _list = new List<string>();
            public int Add(string s)
            {
                if (s == null) s = "";
                int i;
                if (_map.TryGetValue(s, out i)) return i;
                i = _list.Count;
                _list.Add(s);
                _map[s] = i;
                return i;
            }
            public string ToXml()
            {
                var sb = new StringBuilder(Math.Max(256, _list.Count * 32));
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                sb.Append("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"")
                  .Append(_list.Count).Append("\" uniqueCount=\"").Append(_list.Count).Append("\">");
                foreach (string s in _list)
                    sb.Append("<si><t xml:space=\"preserve\">").Append(X(s)).Append("</t></si>");
                sb.Append("</sst>");
                return sb.ToString();
            }
        }

        private static string CellRef(int col0, int row0)
        {
            return ColumnLetter(col0) + (row0 + 1).ToString(CultureInfo.InvariantCulture);
        }

        public static string ColumnLetter(int col0)
        {
            int n = col0 + 1;
            var sb = new StringBuilder();
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                sb.Insert(0, (char)('A' + rem));
                n = (n - 1) / 26;
            }
            return sb.ToString();
        }

        public static string SafeSheetName(string n, string fallback)
        {
            n = string.IsNullOrWhiteSpace(n) ? fallback : n.Trim();
            foreach (char c in "[]:*?/\\") n = n.Replace(c, '_');
            return n.Length > 31 ? n.Substring(0, 31) : n;
        }

        private static void Put(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                w.Write(content);
        }

        private static string X(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var clean = new StringBuilder(s.Length);
            foreach (char ch in s) if (ch >= 32 || ch == '\t' || ch == '\n' || ch == '\r') clean.Append(ch);
            s = clean.ToString();
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
