// MD Dia-Inch - configurable joint rules (rules.json). Every default is written to the file so it can be edited.
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiaInch.Core
{
    /// <summary>Weld / joint kinds after classification.</summary>
    public enum JointKind { None = 0, BW, SW, SCR, FL, Other }

    /// <summary>Default end behaviour for one component type, used only when the connection-type property is missing.</summary>
    public sealed class TypeRule
    {
        /// <summary>Arrive/leave (P1/P2) default end codes: WELD, BW, SW, SCR, FL, AUTO_FL, NONE. Prefix "=" forces the code even if a property exists.</summary>
        public List<string> Ends = new List<string> { "WELD", "WELD" };
        /// <summary>Branch outlets (P3, P4) default end codes.</summary>
        public List<string> Outlets = new List<string>();
        /// <summary>Extra weld joints for this component (e.g. OLET-to-header weld), sized at the first outlet bore.</summary>
        public int Extra;
        /// <summary>Component is itself one joint (PDMS/E3D WELD element).</summary>
        public bool Joint;
        /// <summary>Skip completely (gasket, bolt, attachment): not a joint, does not break adjacency.</summary>
        public bool Ignore;
    }

    public sealed class Rules
    {
        public const int SchemaVersion = 1;

        /// <summary>Logical attribute → property names (bare name or "Category > Property"), first found wins. Case and spaces ignored.</summary>
        public Dictionary<string, List<string>> Aliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Connection-type code → kind. Exact match first, then the longest matching prefix.</summary>
        public Dictionary<JointKind, List<string>> ConnCodes = new Dictionary<JointKind, List<string>>();
        /// <summary>Codes that mean "no joint" (open end, closed end, null).</summary>
        public List<string> NoJointCodes = new List<string>();
        /// <summary>Component type code → words that identify it in the TYPE property or as the first word of the item name.</summary>
        public Dictionary<string, List<string>> TypeWords = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TypeRule> TypeRules = new Dictionary<string, TypeRule>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Types whose outlet (P3/P4) carries the branch weld; a branch whose HREF/TREF points at one of these does not count that joint again.</summary>
        public List<string> BranchOutletTypes = new List<string>();
        /// <summary>Structure node types (not components).</summary>
        public List<string> PipeTypes = new List<string>();
        public List<string> BranchTypes = new List<string>();
        public List<string> ZoneTypes = new List<string>();
        public List<string> SiteTypes = new List<string>();

        /// <summary>"WELD" default resolves to SW at or below this NB (mm), BW above. "AUTO_FL" resolves to SW at or below, FL above.</summary>
        public double SmallBoreMaxNbMm = 40;
        public bool CountScrewedJoints;
        public bool FlangeJointsInInchDia;
        /// <summary>&gt; 0 adds pipe-to-pipe butt welds every N metres of straight tube (random length). 0 = off.</summary>
        public double RandomPipeLengthM;
        /// <summary>Unit for a bare bore number with no unit text: "mm" or "in".</summary>
        public string BareBoreUnit = "mm";
        /// <summary>Nominal bore mm → NB inch.</summary>
        public SortedDictionary<double, double> NbTable = new SortedDictionary<double, double>();
        /// <summary>Optional regex to cut the Line No out of the PIPE name. Empty = whole PIPE name (leading '/' removed).</summary>
        public string LineRegex = "";
        /// <summary>Area column: "zone" (nearest ZONE), "site", "scope" (chosen hierarchy), "parent" (node above the line).</summary>
        public string AreaMode = "zone";
        /// <summary>A component with SHOP = false makes its welds field welds.</summary>
        public bool FieldWhenShopFalse = true;
        /// <summary>Flag the row CHECK when any end connection type had to be assumed from the type default.</summary>
        public bool CheckWhenConnAssumed = true;
        /// <summary>Accept the NB derived from tube outside diameter when no bore property exists.</summary>
        public bool NbFromGeometry = true;

        public static Rules Default()
        {
            var r = new Rules();
            Action<string, string[]> a = (k, v) => r.Aliases[k] = v.ToList();
            a("type", new[] { "TYPE", "Type", "Element Type", "AVEVA Type", "PDMS Type", "E3D Type" });
            a("name", new[] { "NAME", "Name", "Element Name", "Full Name" });
            a("p1Conn", new[] { "ACON", "Arrive Connection", "Arrive Conn", "Arrive Connection Type", "P1CONN", "P1 CONN", "P1 Connection", "P1 Connection Type", "PPCON1" });
            a("p2Conn", new[] { "LCON", "Leave Connection", "Leave Conn", "Leave Connection Type", "P2CONN", "P2 CONN", "P2 Connection", "P2 Connection Type", "PPCON2" });
            a("p3Conn", new[] { "P3CONN", "P3 CONN", "P3 Connection", "P3 Connection Type", "PPCON3", "Branch Connection" });
            a("p4Conn", new[] { "P4CONN", "P4 CONN", "P4 Connection", "P4 Connection Type", "PPCON4" });
            a("p1Bore", new[] { "ABOR", "Arrive Bore", "P1BORE", "P1 BORE", "P1 Bore", "PPBO1" });
            a("p2Bore", new[] { "LBOR", "Leave Bore", "P2BORE", "P2 BORE", "P2 Bore", "PPBO2" });
            a("p3Bore", new[] { "P3BORE", "P3 BORE", "P3 Bore", "PPBO3", "Branch Bore" });
            a("p4Bore", new[] { "P4BORE", "P4 BORE", "P4 Bore", "PPBO4" });
            a("bore", new[] { "BORE", "NB", "Nominal Bore", "Nominal Size", "Size", "DN" });
            a("headBore", new[] { "HBOR", "Head Bore" });
            a("tailBore", new[] { "TBOR", "Tail Bore" });
            a("headConn", new[] { "HCON", "HCONN", "Head Connection", "Head Connection Type" });
            a("tailConn", new[] { "TCON", "TCONN", "Tail Connection", "Tail Connection Type" });
            a("headRef", new[] { "HREF", "Head Reference", "Head Ref" });
            a("tailRef", new[] { "TREF", "Tail Reference", "Tail Ref" });
            a("spec", new[] { "PSPE", "PSPEC", "Pipe Spec", "Piping Spec", "Specification", "Spec" });
            a("compSpec", new[] { "SPRE", "Spec Reference", "Spref" });
            a("tubeLength", new[] { "LTLE", "Tube Length", "TUBE LENGTH", "ITLE", "Length" });
            a("shop", new[] { "SHOP", "Shop", "Shop Flag" });
            a("fieldWeld", new[] { "FIELD WELD", "FIELDWELD", "FWELD", "Field Weld", ":FIELDWELD" });
            a("lineNo", new[] { "Line No", "LINE NO", "Line Number", "LINENUMBER", ":LINENO", ":LINE_NO", ":LINENUMBER" });
            a("area", new[] { ":AREA", "Area" });

            r.ConnCodes[JointKind.BW] = new List<string> { "BW", "BWD", "BWE", "BV", "BUTT" };
            r.ConnCodes[JointKind.SW] = new List<string> { "SW", "SWF", "SWM", "SWD", "SKT", "SOCK" };
            r.ConnCodes[JointKind.SCR] = new List<string> { "SC", "SCR", "SCF", "SCM", "NPT", "TH", "THD", "BSP" };
            r.ConnCodes[JointKind.FL] = new List<string> { "F", "FL", "FBD", "FGD", "FF", "RF", "RTJ", "FLG", "LAP" };
            r.NoJointCodes = new List<string> { "OPEN", "OPE", "CL", "CLOS", "CLOSED", "NUL", "NULL", "NONE", "NOC", "DRAN", "VENT", "PLN", "-", "UNSET" };

            Action<string, string[]> w = (k, v) => r.TypeWords[k] = v.ToList();
            w("TUBE", new[] { "TUBE", "TUBI", "TUBING" });
            w("ELBO", new[] { "ELBO", "ELBOW" });
            w("BEND", new[] { "BEND" });
            w("TEE", new[] { "TEE" });
            w("CROS", new[] { "CROS", "CROSS" });
            w("OLET", new[] { "OLET", "WELDOLET", "SOCKOLET", "THREDOLET", "NIPOLET" });
            w("REDU", new[] { "REDU", "REDUCER" });
            w("FLAN", new[] { "FLAN", "FLANGE" });
            w("LJSE", new[] { "LJSE" });
            w("FBLI", new[] { "FBLI", "BLIND" });
            w("VALV", new[] { "VALV", "VALVE" });
            w("VTWA", new[] { "VTWA" });
            w("VFWA", new[] { "VFWA" });
            w("INST", new[] { "INST", "INSTRUMENT" });
            w("PCOM", new[] { "PCOM", "PCOMPONENT" });
            w("FILT", new[] { "FILT", "FILTER" });
            w("TRAP", new[] { "TRAP" });
            w("CAP", new[] { "CAP" });
            w("CLOS", new[] { "CLOS", "CLOSURE" });
            w("COUP", new[] { "COUP", "COUPLING" });
            w("UNIO", new[] { "UNIO", "UNION" });
            w("WELD", new[] { "WELD" });
            w("GASK", new[] { "GASK", "GASKET" });
            w("BOLT", new[] { "BOLT" });
            w("ATTA", new[] { "ATTA", "ATTACHMENT" });
            w("SHU", new[] { "SHU" });

            Func<string[], string[], TypeRule> tr = (e, o) => new TypeRule { Ends = e.ToList(), Outlets = (o ?? new string[0]).ToList() };
            r.TypeRules["ELBO"] = tr(new[] { "WELD", "WELD" }, null);
            r.TypeRules["BEND"] = tr(new[] { "WELD", "WELD" }, null);
            r.TypeRules["TEE"] = tr(new[] { "WELD", "WELD" }, new[] { "WELD" });
            r.TypeRules["CROS"] = tr(new[] { "WELD", "WELD" }, new[] { "WELD", "WELD" });
            var olet = tr(new[] { "=NONE", "=NONE" }, new[] { "WELD" }); olet.Extra = 1; r.TypeRules["OLET"] = olet;
            r.TypeRules["REDU"] = tr(new[] { "WELD", "WELD" }, null);
            r.TypeRules["FLAN"] = tr(new[] { "WELD", "FL" }, null);
            r.TypeRules["LJSE"] = tr(new[] { "WELD", "FL" }, null);
            r.TypeRules["FBLI"] = tr(new[] { "FL", "NONE" }, null);
            r.TypeRules["VALV"] = tr(new[] { "AUTO_FL", "AUTO_FL" }, null);
            r.TypeRules["VTWA"] = tr(new[] { "AUTO_FL", "AUTO_FL" }, new[] { "AUTO_FL" });
            r.TypeRules["VFWA"] = tr(new[] { "AUTO_FL", "AUTO_FL" }, new[] { "AUTO_FL", "AUTO_FL" });
            r.TypeRules["INST"] = tr(new[] { "AUTO_FL", "AUTO_FL" }, null);
            r.TypeRules["PCOM"] = tr(new[] { "AUTO_FL", "AUTO_FL" }, null);
            r.TypeRules["FILT"] = tr(new[] { "AUTO_FL", "AUTO_FL" }, null);
            r.TypeRules["TRAP"] = tr(new[] { "AUTO_FL", "AUTO_FL" }, null);
            r.TypeRules["CAP"] = tr(new[] { "WELD", "NONE" }, null);
            r.TypeRules["CLOS"] = tr(new[] { "WELD", "NONE" }, null);
            r.TypeRules["COUP"] = tr(new[] { "WELD", "WELD" }, null);
            r.TypeRules["UNIO"] = tr(new[] { "WELD", "WELD" }, null);
            r.TypeRules["WELD"] = new TypeRule { Ends = new List<string> { "WELD", "WELD" }, Joint = true };
            r.TypeRules["GASK"] = new TypeRule { Ignore = true };
            r.TypeRules["BOLT"] = new TypeRule { Ignore = true };
            r.TypeRules["ATTA"] = new TypeRule { Ignore = true };
            r.TypeRules["SHU"] = new TypeRule { Ignore = true };

            r.BranchOutletTypes = new List<string> { "TEE", "CROS", "OLET", "VTWA", "VFWA" };
            r.PipeTypes = new List<string> { "PIPE" };
            r.BranchTypes = new List<string> { "BRAN", "BRANCH" };
            r.ZoneTypes = new List<string> { "ZONE" };
            r.SiteTypes = new List<string> { "SITE" };

            double[,] nb =
            {
                { 6, 0.125 }, { 8, 0.25 }, { 10, 0.375 }, { 15, 0.5 }, { 20, 0.75 }, { 25, 1 }, { 32, 1.25 }, { 40, 1.5 },
                { 50, 2 }, { 65, 2.5 }, { 80, 3 }, { 90, 3.5 }, { 100, 4 }, { 125, 5 }, { 150, 6 }, { 200, 8 }, { 250, 10 },
                { 300, 12 }, { 350, 14 }, { 400, 16 }, { 450, 18 }, { 500, 20 }, { 550, 22 }, { 600, 24 }, { 650, 26 },
                { 700, 28 }, { 750, 30 }, { 800, 32 }, { 850, 34 }, { 900, 36 }, { 950, 38 }, { 1000, 40 }, { 1050, 42 },
                { 1100, 44 }, { 1150, 46 }, { 1200, 48 }, { 1300, 52 }, { 1400, 56 }, { 1500, 60 }, { 1600, 64 }, { 1800, 72 },
                { 2000, 80 }
            };
            for (int i = 0; i < nb.GetLength(0); i++) r.NbTable[nb[i, 0]] = nb[i, 1];
            return r;
        }

        // ---------------- helpers used by the counter ----------------

        public static string Norm(string s)
        {
            if (s == null) return "";
            var c = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s) if (!char.IsWhiteSpace(ch) && ch != '_' && ch != '-') c.Append(char.ToUpperInvariant(ch));
            return c.ToString();
        }

        /// <summary>Classifies a connection-type code (BWD, SWF, FBD ...). Returns null when the code is empty (= unknown).</summary>
        public JointKind? ClassifyConn(string code)
        {
            string c = (code ?? "").Trim().ToUpperInvariant();
            if (c.Length == 0) return null;
            if (NoJointCodes.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))) return JointKind.None;
            foreach (var kv in ConnCodes)
                if (kv.Value.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase))) return kv.Key;
            JointKind best = JointKind.Other; int bestLen = 0;
            foreach (var kv in ConnCodes)
                foreach (string p in kv.Value)
                    if (p.Length > bestLen && c.StartsWith(p.ToUpperInvariant(), StringComparison.Ordinal)) { best = kv.Key; bestLen = p.Length; }
            foreach (string p in NoJointCodes)
                if (p.Length > bestLen && p.Length >= 2 && c.StartsWith(p.ToUpperInvariant(), StringComparison.Ordinal)) { best = JointKind.None; bestLen = p.Length; }
            return best;
        }

        /// <summary>Type code from a TYPE value or an item name ("ELBOW 1 of BRANCH /X/B1" → ELBO). "" = not a known type.</summary>
        public string TypeFromWord(string text)
        {
            string t = (text ?? "").Trim().TrimStart('/');
            if (t.Length == 0) return "";
            string first = t.Split(new[] { ' ', '\t', ':', '#' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            // strip trailing digits ("ELBOW1")
            string bare = first.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-', '_');
            foreach (string cand in new[] { first, bare })
            {
                if (cand.Length == 0) continue;
                foreach (var kv in TypeWords)
                    if (kv.Value.Any(x => string.Equals(x, cand, StringComparison.OrdinalIgnoreCase))) return kv.Key.ToUpperInvariant();
                if (Is(BranchTypes, cand)) return "BRAN";
                if (Is(PipeTypes, cand)) return "PIPE";
                if (Is(ZoneTypes, cand)) return "ZONE";
                if (Is(SiteTypes, cand)) return "SITE";
            }
            return "";
        }

        public static bool Is(IEnumerable<string> list, string v)
        {
            return list != null && list.Any(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
        }

        public TypeRule RuleFor(string type)
        {
            TypeRule t;
            return type != null && TypeRules.TryGetValue(type, out t) ? t : null;
        }

        /// <summary>Known component type (has a rule or a type word) other than TUBE.</summary>
        public bool IsComponentType(string type)
        {
            if (string.IsNullOrEmpty(type) || type == "TUBE" || type == "BRAN" || type == "PIPE" || type == "ZONE" || type == "SITE") return false;
            return TypeRules.ContainsKey(type) || TypeWords.ContainsKey(type);
        }

        // ---------------- JSON ----------------

        public string ToJson()
        {
            var root = new List<KeyValuePair<string, object>>();
            Action<string, object> add = (k, v) => root.Add(new KeyValuePair<string, object>(k, v));
            add("_about", "MD Dia-Inch rules. Edit, save, then press 'Reload rules' in the panel. Author: " + Names.Author);
            add("schemaVersion", SchemaVersion);
            add("smallBoreMaxNbMm", SmallBoreMaxNbMm);
            add("countScrewedJoints", CountScrewedJoints);
            add("flangeJointsInInchDia", FlangeJointsInInchDia);
            add("randomPipeLengthM", RandomPipeLengthM);
            add("bareBoreUnit", BareBoreUnit);
            add("lineRegex", LineRegex);
            add("areaMode", AreaMode);
            add("fieldWhenShopFalse", FieldWhenShopFalse);
            add("checkWhenConnAssumed", CheckWhenConnAssumed);
            add("nbFromGeometry", NbFromGeometry);
            add("propertyAliases", Aliases.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)).ToList());
            add("connectionCodes", ConnCodes.OrderBy(kv => (int)kv.Key).Select(kv => new KeyValuePair<string, object>(kv.Key.ToString(), kv.Value))
                .Concat(new[] { new KeyValuePair<string, object>("NONE", NoJointCodes) }).ToList());
            add("typeWords", TypeWords.Select(kv => new KeyValuePair<string, object>(kv.Key, kv.Value)).ToList());
            add("typeRules", TypeRules.Select(kv => new KeyValuePair<string, object>(kv.Key, RuleJson(kv.Value))).ToList());
            add("branchOutletTypes", BranchOutletTypes);
            add("structureTypes", new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("PIPE", PipeTypes), new KeyValuePair<string, object>("BRAN", BranchTypes),
                new KeyValuePair<string, object>("ZONE", ZoneTypes), new KeyValuePair<string, object>("SITE", SiteTypes)
            });
            add("nbTableMmToInch", NbTable.Select(kv => new KeyValuePair<string, object>(kv.Key.ToString(CultureInfo.InvariantCulture), kv.Value)).ToList());
            return Json.Write(root);
        }

        private static object RuleJson(TypeRule t)
        {
            var d = new List<KeyValuePair<string, object>>();
            if (t.Ignore) { d.Add(new KeyValuePair<string, object>("ignore", true)); return d; }
            if (t.Joint) d.Add(new KeyValuePair<string, object>("joint", true));
            d.Add(new KeyValuePair<string, object>("ends", t.Ends));
            if (t.Outlets.Count > 0) d.Add(new KeyValuePair<string, object>("outlets", t.Outlets));
            if (t.Extra > 0) d.Add(new KeyValuePair<string, object>("extraJoints", t.Extra));
            return d;
        }

        /// <summary>Defaults, overlaid with whatever the JSON sets. Unknown keys ignored.</summary>
        public static Rules FromJson(string text)
        {
            var r = Default();
            var root = Json.Parse(text) as Dictionary<string, object>;
            if (root == null) throw new FormatException("rules.json must be a JSON object { ... }");
            object v;
            if (root.TryGetValue("smallBoreMaxNbMm", out v)) r.SmallBoreMaxNbMm = D(v, r.SmallBoreMaxNbMm);
            if (root.TryGetValue("countScrewedJoints", out v)) r.CountScrewedJoints = B(v);
            if (root.TryGetValue("flangeJointsInInchDia", out v)) r.FlangeJointsInInchDia = B(v);
            if (root.TryGetValue("randomPipeLengthM", out v)) r.RandomPipeLengthM = D(v, 0);
            if (root.TryGetValue("bareBoreUnit", out v)) r.BareBoreUnit = S(v) ?? "mm";
            if (root.TryGetValue("lineRegex", out v)) r.LineRegex = S(v) ?? "";
            if (root.TryGetValue("areaMode", out v)) r.AreaMode = S(v) ?? "zone";
            if (root.TryGetValue("fieldWhenShopFalse", out v)) r.FieldWhenShopFalse = B(v);
            if (root.TryGetValue("checkWhenConnAssumed", out v)) r.CheckWhenConnAssumed = B(v);
            if (root.TryGetValue("nbFromGeometry", out v)) r.NbFromGeometry = B(v);
            Dictionary<string, object> o;
            if ((o = Obj(root, "propertyAliases")) != null)
                foreach (var kv in o) r.Aliases[kv.Key] = L(kv.Value);
            if ((o = Obj(root, "connectionCodes")) != null)
                foreach (var kv in o)
                {
                    if (string.Equals(kv.Key, "NONE", StringComparison.OrdinalIgnoreCase)) { r.NoJointCodes = L(kv.Value); continue; }
                    JointKind k;
                    if (Enum.TryParse(kv.Key, true, out k)) r.ConnCodes[k] = L(kv.Value);
                }
            if ((o = Obj(root, "typeWords")) != null)
                foreach (var kv in o) r.TypeWords[kv.Key.ToUpperInvariant()] = L(kv.Value);
            if ((o = Obj(root, "typeRules")) != null)
                foreach (var kv in o)
                {
                    var ro = kv.Value as Dictionary<string, object>;
                    if (ro == null) { r.TypeRules.Remove(kv.Key); continue; }
                    var t = new TypeRule();
                    object x;
                    if (ro.TryGetValue("ignore", out x)) t.Ignore = B(x);
                    if (ro.TryGetValue("joint", out x)) t.Joint = B(x);
                    if (ro.TryGetValue("ends", out x)) t.Ends = L(x);
                    if (ro.TryGetValue("outlets", out x)) t.Outlets = L(x);
                    if (ro.TryGetValue("extraJoints", out x)) t.Extra = (int)D(x, 0);
                    while (t.Ends.Count < 2) t.Ends.Add("NONE");
                    r.TypeRules[kv.Key.ToUpperInvariant()] = t;
                }
            if (root.TryGetValue("branchOutletTypes", out v)) r.BranchOutletTypes = L(v);
            if ((o = Obj(root, "structureTypes")) != null)
            {
                object x;
                if (o.TryGetValue("PIPE", out x)) r.PipeTypes = L(x);
                if (o.TryGetValue("BRAN", out x)) r.BranchTypes = L(x);
                if (o.TryGetValue("ZONE", out x)) r.ZoneTypes = L(x);
                if (o.TryGetValue("SITE", out x)) r.SiteTypes = L(x);
            }
            if ((o = Obj(root, "nbTableMmToInch")) != null)
            {
                r.NbTable.Clear();
                foreach (var kv in o)
                {
                    double mm;
                    if (double.TryParse(kv.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out mm)) r.NbTable[mm] = D(kv.Value, mm / 25.4);
                }
            }
            return r;
        }

        private static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }
        private static double D(object v, double def)
        {
            if (v is double) return (double)v;
            double d;
            return v is string && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def;
        }
        private static bool B(object v)
        {
            if (v is bool) return (bool)v;
            var s = (v as string ?? "").Trim().ToLowerInvariant();
            return s == "true" || s == "yes" || s == "1";
        }
        private static string S(object v) { return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture); }
        private static List<string> L(object v)
        {
            var l = v as List<object>;
            if (l != null) return l.Where(x => x != null).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList();
            string s = S(v);
            return string.IsNullOrEmpty(s) ? new List<string>() : s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }
    }
}
