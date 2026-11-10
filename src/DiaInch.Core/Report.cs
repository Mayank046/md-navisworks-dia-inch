// MD Dia-Inch - Excel / CSV report tables
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DiaInch.Core
{
    public static class Report
    {
        public static readonly string[] LineColumns =
        {
            "Line No", "Area", "Size", "Spec", "BW joints", "SW joints", "Flange joints", "Total joints",
            "Inch-dia", "Pipe length m", "Inch-meter", "Status", "Notes",
            "Screwed joints", "Shop welds", "Field welds", "All sizes", "Hierarchy", "Branches"
        };

        public static string Kind(JointKind k)
        {
            switch (k)
            {
                case JointKind.BW: return "BW";
                case JointKind.SW: return "SW";
                case JointKind.SCR: return "SCR";
                case JointKind.FL: return "FL";
                default: return k.ToString().ToUpperInvariant();
            }
        }

        private static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        private static string O(int? v) { return v.HasValue ? I(v.Value) : ""; }

        public static void FillLineRow(TableRow row, LineResult l)
        {
            row.Set("Line No", l.LineNo);
            row.Set("Area", l.Area);
            row.Set("Size", l.SizeText);
            row.Set("Spec", l.Spec);
            row.Set("BW joints", I(l.BW));
            row.Set("SW joints", I(l.SW));
            row.Set("Flange joints", I(l.FL));
            row.Set("Total joints", I(l.Total));
            row.Set("Inch-dia", Fmt.N(l.InchDia, 2));
            row.Set("Pipe length m", Fmt.N(l.LengthM, 3));
            row.Set("Inch-meter", Fmt.N(l.InchMeter, 2));
            row.Set("Status", l.Status);
            row.Set("Notes", l.NotesText);
            row.Set("Screwed joints", I(l.SCR));
            row.Set("Shop welds", O(l.Shop));
            row.Set("Field welds", O(l.FieldWelds));
            row.Set("All sizes", string.Join(", ", l.Sizes.Select(Nb.Format)));
            row.Set("Hierarchy", l.Scope);
            row.Set("Branches", I(l.Branches));
        }

        private static DataTable Table(string name, IEnumerable<string> cols, params string[] numeric)
        {
            var t = new DataTable { SheetName = name };
            foreach (string c in cols) t.EnsureColumn(c);
            foreach (string n in numeric) t.NumericColumns.Add(n);
            return t;
        }

        private static readonly string[] Num = { "BW joints", "SW joints", "Screwed joints", "Flange joints", "Total joints", "Inch-dia", "Pipe length m", "Inch-meter", "Shop welds", "Field welds", "Lines", "Lines to CHECK", "Branches" };

        private static DataTable TotalsTable(string name, string keyCol, string key2Col, List<Totals> rows, Totals grand)
        {
            var cols = new List<string> { keyCol };
            if (key2Col != null) cols.Add(key2Col);
            cols.AddRange(new[] { "Lines", "BW joints", "SW joints", "Screwed joints", "Flange joints", "Total joints", "Inch-dia", "Pipe length m", "Inch-meter", "Shop welds", "Field welds", "Lines to CHECK" });
            var t = Table(name, cols, Num);
            foreach (var x in rows.Concat(grand != null ? new[] { grand } : new Totals[0]))
            {
                var r = t.AddRow();
                r.Set(keyCol, x.Key);
                if (key2Col != null) r.Set(key2Col, x.Key2);
                r.Set("Lines", I(x.Lines)); r.Set("BW joints", I(x.BW)); r.Set("SW joints", I(x.SW)); r.Set("Screwed joints", I(x.SCR));
                r.Set("Flange joints", I(x.FL)); r.Set("Total joints", I(x.Total)); r.Set("Inch-dia", Fmt.N(x.InchDia, 2));
                r.Set("Pipe length m", Fmt.N(x.LengthM, 3)); r.Set("Inch-meter", Fmt.N(x.InchMeter, 2));
                r.Set("Shop welds", O(x.Shop)); r.Set("Field welds", O(x.Field)); r.Set("Lines to CHECK", I(x.CheckLines));
                if (ReferenceEquals(x, grand)) r.Style = 3;
            }
            return t;
        }

        public static List<DataTable> Build(ScanResult s, Rules r, string modelName, string scopeText, DateTime when)
        {
            var tables = new List<DataTable>();

            var sum = Table("Summary", new[] { "Item", "Value" }, "Value");
            Action<string, string> kv = (a, b) => { var row = sum.AddRow(); row.Set("Item", a); row.Set("Value", b); };
            sum.AutoFilter = false;
            kv("Report", Names.Product + " v" + Names.Version);
            kv("Author", Names.Author);
            kv("Model", modelName);
            kv("Scope", scopeText);
            kv("Generated", when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            kv("Lines", I(s.Lines.Count));
            kv("Branches", I(s.BranchCount));
            kv("Lines to CHECK", I(s.Lines.Count(l => l.Check)));
            kv("BW joints", I(s.Grand.BW));
            kv("SW joints", I(s.Grand.SW));
            kv("Screwed joints" + (r.CountScrewedJoints ? " (in total)" : " (not in total)"), I(s.Grand.SCR));
            kv("Flange joints (not in total)", I(s.Grand.FL));
            kv("Total weld joints", I(s.Grand.Total));
            kv("Inch-dia", Fmt.N(s.Grand.InchDia, 2));
            kv("Pipe length m", Fmt.N(s.Grand.LengthM, 3));
            kv("Inch-meter", Fmt.N(s.Grand.InchMeter, 2));
            kv("Shop / field split", s.ShopFieldAvailable ? "from SHOP / field-weld attribute" : "not available (no attribute) - totals only");
            kv("Note", "CHECK = a property was missing or assumed; see Notes and the Joints sheet. Numbers are never invented.");
            tables.Add(sum);

            var lines = Table("Lines", LineColumns, Num);
            lines.Preamble.Add(Names.Product + " v" + Names.Version + " | " + modelName + " | " + scopeText);
            foreach (var l in s.Lines) FillLineRow(lines.AddRow(), l);
            tables.Add(lines);

            tables.Add(TotalsTable("Area Totals", "Area", "Hierarchy", s.ByArea, s.Grand));
            tables.Add(TotalsTable("Hierarchy Totals", "Hierarchy", null, s.ByScope, s.Grand));
            tables.Add(TotalsTable("By Size", "Size", null, s.BySize, null));

            var j = JointsTable(s);
            tables.Add(j);

            var rt = Table("Rules", new[] { "Rule", "Value" });
            rt.AutoFilter = false;
            foreach (var x in RuleLines(r)) { var row = rt.AddRow(); row.Set("Rule", x.Key); row.Set("Value", x.Value); }
            tables.Add(rt);
            return tables;
        }

        public static DataTable JointsTable(ScanResult s)
        {
            var j = Table("Joints", new[] { "Line No", "Branch", "#", "Kind", "NB inch", "Size", "Shop/Field", "Where", "Assumed", "Bore missing" }, "#", "NB inch");
            foreach (var l in s.Lines)
                foreach (var br in l.BranchResults)
                {
                    int n = 0;
                    foreach (var x in br.Joints)
                    {
                        var row = j.AddRow();
                        row.Set("Line No", l.LineNo); row.Set("Branch", x.Branch); row.Set("#", I(++n));
                        row.Set("Kind", Kind(x.Kind)); row.Set("NB inch", Fmt.N(x.Inch, 3)); row.Set("Size", Nb.Format(x.Inch));
                        row.Set("Shop/Field", x.Field.HasValue ? (x.Field.Value ? "FIELD" : "SHOP") : "");
                        row.Set("Where", x.Where); row.Set("Assumed", x.Assumed ? "yes" : ""); row.Set("Bore missing", x.BoreMissing ? "yes" : "");
                    }
                }
            return j;
        }

        public static List<KeyValuePair<string, string>> RuleLines(Rules r)
        {
            var l = new List<KeyValuePair<string, string>>();
            Action<string, string> a = (k, v) => l.Add(new KeyValuePair<string, string>(k, v));
            a("Total joints", "BW + SW" + (r.CountScrewedJoints ? " + screwed" : "") + " (flange joints listed separately)");
            a("Inch-dia", "sum of joint NB inch over BW + SW" + (r.CountScrewedJoints ? " + screwed" : "") + (r.FlangeJointsInInchDia ? " + flange" : "") + " joints");
            a("Inch-meter", "sum over tubes of NB inch x length m");
            a("Small bore", "NB <= " + Fmt.N(r.SmallBoreMaxNbMm, 1) + " mm: default weld = SW, default valve end = SW");
            a("Random pipe length", r.RandomPipeLengthM > 0 ? Fmt.N(r.RandomPipeLengthM, 2) + " m (extra pipe-to-pipe welds)" : "off");
            a("Bare bore unit", r.BareBoreUnit);
            a("Area mode", r.AreaMode);
            a("Line regex", string.IsNullOrEmpty(r.LineRegex) ? "(none: PIPE name)" : r.LineRegex);
            foreach (var kv in r.ConnCodes.OrderBy(k => (int)k.Key)) a("Connection " + kv.Key, string.Join(", ", kv.Value));
            a("Connection NONE", string.Join(", ", r.NoJointCodes));
            foreach (var kv in r.TypeRules)
            {
                var t = kv.Value;
                string v = t.Ignore ? "ignored" : (t.Joint ? "is one joint" : "ends " + string.Join("/", t.Ends) +
                           (t.Outlets.Count > 0 ? ", outlets " + string.Join("/", t.Outlets) : "") + (t.Extra > 0 ? ", +" + t.Extra + " header weld" : ""));
                a("Type " + kv.Key, v);
            }
            a("NB table", string.Join("  ", r.NbTable.Select(k => Fmt.N(k.Key, 0) + "=" + Fmt.N(k.Value, 3))));
            return l;
        }

        // ---------------- CSV ----------------

        public static void SaveCsv(string path, DataTable t)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", t.Columns.Select(Csv)));
            foreach (var r in t.Rows) sb.AppendLine(string.Join(",", t.Columns.Select(c => Csv(r.Get(c)))));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static string Csv(string v)
        {
            v = v ?? "";
            if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + v.Replace("\"", "\"\"") + "\"";
            // keep Excel from turning 4" or =... into formulas / dates
            if (v.Length > 0 && "=+-@".IndexOf(v[0]) >= 0) return "'" + v;
            return v;
        }
    }
}
