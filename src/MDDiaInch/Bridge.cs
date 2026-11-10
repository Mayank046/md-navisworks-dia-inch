// MD Dia-Inch - Navisworks bridge: tree adapter, property reading, scope memory, selection
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DiaInch.Core;
using Nw = Autodesk.Navisworks.Api;

namespace MDDiaInch
{
    /// <summary>ModelItem view for the Core walker. Every call is guarded: a bad node never stops a scan.</summary>
    internal sealed class NwDiaTree : IDiaTree<Nw.ModelItem>
    {
        private readonly double _mm;
        public NwDiaTree(double mmPerUnit) { _mm = mmPerUnit > 0 ? mmPerUnit : 1; }

        public IEnumerable<Nw.ModelItem> Children(Nw.ModelItem n)
        {
            var kids = new List<Nw.ModelItem>();
            try { foreach (Nw.ModelItem c in n.Children) if (c != null) kids.Add(c); }
            catch (Exception ex) { Diagnostics.Log("Children: " + ex.Message); }
            return kids;
        }

        public string Name(Nw.ModelItem n) { return Bridge.SafeName(n); }
        public bool Hidden(Nw.ModelItem n) { try { return n.IsHidden; } catch { return false; } }

        public PropBag Props(Nw.ModelItem n)
        {
            var bag = new PropBag();
            try
            {
                foreach (Nw.PropertyCategory cat in n.PropertyCategories)
                {
                    if (cat == null) continue;
                    string cn = cat.DisplayName ?? cat.Name ?? "";
                    foreach (Nw.DataProperty p in cat.Properties)
                    {
                        if (p == null) continue;
                        string v = Bridge.VariantToString(p.Value, _mm);
                        if (string.IsNullOrEmpty(v)) continue;
                        string dn = p.DisplayName ?? "", inn = p.Name ?? "";
                        if (dn.Length > 0) bag.Add(cn, dn, v);
                        if (inn.Length > 0 && !string.Equals(inn, dn, StringComparison.OrdinalIgnoreCase)) bag.Add(cn, inn, v);
                    }
                }
            }
            catch (Exception ex) { Diagnostics.Log("Props: " + ex.Message); }
            return bag;
        }

        public double[] BoxMm(Nw.ModelItem n)
        {
            try
            {
                Nw.BoundingBox3D b = n.BoundingBox();
                if (b == null || b.IsEmpty) return null;
                return new[] { b.Min.X * _mm, b.Min.Y * _mm, b.Min.Z * _mm, b.Max.X * _mm, b.Max.Y * _mm, b.Max.Z * _mm };
            }
            catch { return null; }
        }
    }

    internal sealed class ItemComparer : IEqualityComparer<Nw.ModelItem>
    {
        public bool Equals(Nw.ModelItem a, Nw.ModelItem b) { return ReferenceEquals(a, b) || (a != null && a.Equals(b)); }
        public int GetHashCode(Nw.ModelItem a) { return a == null ? 0 : a.GetHashCode(); }
    }

    internal sealed class ResolvedArea
    {
        public AreaRef Ref;
        public Nw.ModelItem Item;
        public string Text { get { return (Item == null ? "[missing] " : "") + Ref.Display; } }
    }

    internal static class Bridge
    {
        // ---------------- rules ----------------
        public static string RulesPath { get { return Path.Combine(Diagnostics.Folder, Names.RulesFile); } }

        /// <summary>Loads rules.json (writes defaults on first run). error != null when the file is bad (defaults used, file kept).</summary>
        public static Rules LoadRules(out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(RulesPath))
                {
                    Directory.CreateDirectory(Diagnostics.Folder);
                    File.WriteAllText(RulesPath, Rules.Default().ToJson(), new UTF8Encoding(false));
                    Diagnostics.Log("Wrote default rules: " + RulesPath);
                }
                return Rules.FromJson(File.ReadAllText(RulesPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Diagnostics.Log("Rules load: " + ex.Message);
                return Rules.Default();
            }
        }

        // ---------------- units / values ----------------
        public static double MmPerUnit(Nw.Document doc)
        {
            string u = "";
            try { u = doc.Units.ToString(); } catch (Exception ex) { Diagnostics.Log("Units: " + ex.Message); }
            switch ((u ?? "").ToLowerInvariant())
            {
                case "millimeters": return 1;
                case "centimeters": return 10;
                case "meters": return 1000;
                case "kilometers": return 1e6;
                case "micrometers": return 0.001;
                case "inches": return 25.4;
                case "feet": return 304.8;
                case "yards": return 914.4;
                case "miles": return 1609344;
                case "mils": return 0.0254;
                case "microinches": return 0.0000254;
                default: return 1;
            }
        }

        internal static string VariantToString(Nw.VariantData v, double mmPerUnit)
        {
            if (v == null) return null;
            try
            {
                if (v.IsDisplayString) return v.ToDisplayString();
                if (v.IsIdentifierString) return v.ToIdentifierString();
                if (v.IsDoubleLength) return (v.ToDoubleLength() * mmPerUnit).ToString("0.###", CultureInfo.InvariantCulture) + "mm";
                if (v.IsDouble) return v.ToDouble().ToString(CultureInfo.InvariantCulture);
                if (v.IsInt32) return v.ToInt32().ToString(CultureInfo.InvariantCulture);
                if (v.IsBoolean) return v.ToBoolean() ? "TRUE" : "FALSE";
                if (v.IsNamedConstant) { var c = v.ToNamedConstant(); return c != null ? (c.DisplayName ?? c.Name) : null; }
                try { return v.ToDisplayString(); } catch { return v.ToString(); }
            }
            catch { try { return v.ToString(); } catch { return null; } }
        }

        internal static string SafeName(Nw.ModelItem item)
        {
            try
            {
                string n = item == null ? "" : item.DisplayName;
                if (!string.IsNullOrEmpty(n)) return n;
                string c = SafeClass(item);
                return string.IsNullOrEmpty(c) ? "(unnamed)" : "<" + c + ">";
            }
            catch { return "(unnamed)"; }
        }

        internal static string SafeClass(Nw.ModelItem item)
        {
            try
            {
                if (item == null) return "";
                if (!string.IsNullOrEmpty(item.ClassDisplayName)) return item.ClassDisplayName;
                return item.ClassName ?? "";
            }
            catch { try { return item.ClassName ?? ""; } catch { return ""; } }
        }

        internal static string SafeGuid(Nw.ModelItem item)
        {
            try { return Names.FormatGuid(item.InstanceGuid); } catch { return ""; }
        }

        internal static List<string> PathOf(Nw.ModelItem n)
        {
            var l = new List<string>();
            int guard = 0;
            try
            {
                for (Nw.ModelItem c = n; c != null && guard++ < 256; c = c.Parent) l.Add(SafeName(c));
            }
            catch { }
            l.Reverse();
            return l;
        }

        /// <summary>Selected items, dropping any whose ancestor is also selected (no double counting).</summary>
        internal static List<Nw.ModelItem> SelectedRoots(Nw.Document doc)
        {
            var sel = new List<Nw.ModelItem>();
            var set = new HashSet<Nw.ModelItem>();
            try
            {
                foreach (Nw.ModelItem it in doc.CurrentSelection.SelectedItems)
                    if (it != null && set.Add(it)) sel.Add(it);
            }
            catch (Exception ex) { Diagnostics.Log("SelectedRoots: " + ex.Message); }
            var roots = new List<Nw.ModelItem>();
            foreach (Nw.ModelItem it in sel)
            {
                bool covered = false;
                try
                {
                    Nw.ModelItem cur = it.Parent;
                    int guard = 0;
                    while (cur != null && guard++ < 256)
                    {
                        if (set.Contains(cur)) { covered = true; break; }
                        cur = cur.Parent;
                    }
                }
                catch { }
                if (!covered) roots.Add(it);
            }
            return roots;
        }

        internal static List<Nw.ModelItem> ModelRoots(Nw.Document doc)
        {
            var models = new List<Nw.ModelItem>();
            try { if (doc != null) foreach (Nw.ModelItem it in doc.Models.RootItems) if (it != null) models.Add(it); }
            catch (Exception ex) { Diagnostics.Log("RootItems: " + ex.Message); }
            return models;
        }

        // ---------------- scope memory (areas.txt) ----------------
        private static Dictionary<string, AreaState> _all;
        private static string StorePath { get { return Path.Combine(Diagnostics.Folder, "areas.txt"); } }
        private static string HierarchyScanStore
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDHierarchyScan", "areas.txt"); }
        }

        public static string DocKey(Nw.Document doc)
        {
            if (doc == null) return "";
            try { if (!string.IsNullOrEmpty(doc.FileName)) return doc.FileName; } catch { }
            try { if (!string.IsNullOrEmpty(doc.CurrentFileName)) return doc.CurrentFileName; } catch { }
            try { return doc.IsClear ? "" : (doc.Title ?? ""); } catch { return ""; }
        }

        public static AreaState StateFor(Nw.Document doc)
        {
            if (_all == null)
            {
                try { _all = File.Exists(StorePath) ? AreaScope.Deserialize(File.ReadAllText(StorePath, Encoding.UTF8)) : null; }
                catch (Exception ex) { Diagnostics.Log("Area load: " + ex.Message); }
                if (_all == null) _all = new Dictionary<string, AreaState>(StringComparer.OrdinalIgnoreCase);
            }
            string key = DocKey(doc);
            AreaState st;
            if (!_all.TryGetValue(key, out st)) { st = new AreaState(); _all[key] = st; }
            return st;
        }

        public static void SaveAreas()
        {
            if (_all == null) return;
            try
            {
                var keep = _all.Where(kv => kv.Key.Length > 0 && (kv.Value.Limit || kv.Value.Parents.Count > 0)).ToDictionary(kv => kv.Key, kv => kv.Value);
                Directory.CreateDirectory(Diagnostics.Folder);
                File.WriteAllText(StorePath, AreaScope.Serialize(keep), new UTF8Encoding(false));
            }
            catch (Exception ex) { Diagnostics.Log("Area save: " + ex.Message); }
        }

        /// <summary>Pipe parents saved by MD Hierarchy Scan for this model (its "Pipe parent(s)").</summary>
        public static List<AreaRef> HierarchyScanPipeParents(Nw.Document doc)
        {
            try
            {
                if (!File.Exists(HierarchyScanStore)) return new List<AreaRef>();
                var all = AreaScope.Deserialize(File.ReadAllText(HierarchyScanStore, Encoding.UTF8));
                AreaState st;
                return all.TryGetValue(DocKey(doc), out st) ? st.Parents.ToList() : new List<AreaRef>();
            }
            catch (Exception ex) { Diagnostics.Log("HS import: " + ex.Message); return new List<AreaRef>(); }
        }

        public static List<AreaRef> RefsFromSelection(Nw.Document doc)
        {
            return SelectedRoots(doc).Select(i => new AreaRef { Path = PathOf(i), Guid = SafeGuid(i) }).ToList();
        }

        public static List<ResolvedArea> Resolve(Nw.Document doc, IList<AreaRef> refs)
        {
            var models = ModelRoots(doc);
            var r = new List<ResolvedArea>();
            foreach (AreaRef a in refs ?? new List<AreaRef>())
            {
                Nw.ModelItem it = null;
                try
                {
                    it = AreaScope.Resolve<Nw.ModelItem>(models, n => { var k = new List<Nw.ModelItem>(); try { foreach (Nw.ModelItem c in n.Children) k.Add(c); } catch { } return k; },
                        SafeName, SafeGuid, a);
                }
                catch (Exception ex) { Diagnostics.Log("Resolve: " + ex.Message); }
                r.Add(new ResolvedArea { Ref = a, Item = it });
            }
            return r;
        }

        // ---------------- selection ----------------
        public static int Select(Nw.Document doc, IEnumerable<object> items)
        {
            var coll = new Nw.ModelItemCollection();
            foreach (object o in items ?? Enumerable.Empty<object>())
            {
                var mi = o as Nw.ModelItem;
                if (mi != null) coll.Add(mi);
            }
            doc.CurrentSelection.CopyFrom(coll);
            return coll.Count;
        }

        /// <summary>Text report of what Dia-Inch sees on the first selected item (to tune rules.json aliases).</summary>
        public static string Inspect(Nw.Document doc, Rules rules)
        {
            var sel = SelectedRoots(doc);
            if (sel.Count == 0) return "Select one item (a branch, tube or component) in the Selection Tree first.";
            var it = sel[0];
            var tree = new NwDiaTree(MmPerUnit(doc));
            var walk = new TreeWalk<Nw.ModelItem>(tree, rules, new ItemComparer());
            var sb = new StringBuilder();
            sb.AppendLine(Names.Product + " v" + Names.Version + " - inspect");
            sb.AppendLine("Item: " + SafeName(it) + "   (class " + SafeClass(it) + ")");
            sb.AppendLine("Path: " + string.Join(" / ", PathOf(it)));
            sb.AppendLine("Detected type: " + Show(walk.TypeOf(it)));
            var bag = tree.Props(it);
            sb.AppendLine();
            sb.AppendLine("Rule attributes found on this item:");
            foreach (var kv in rules.Aliases)
            {
                string v = bag.Get(kv.Value);
                if (v != null) sb.AppendLine("  " + kv.Key.PadRight(11) + " = " + v);
            }
            var box = tree.BoxMm(it);
            if (box != null)
                sb.AppendLine("  box mm      = " + Fmt.N(box[3] - box[0], 1) + " x " + Fmt.N(box[4] - box[1], 1) + " x " + Fmt.N(box[5] - box[2], 1));
            int k = 0;
            sb.AppendLine();
            sb.AppendLine("Children (first 30) with detected type:");
            try
            {
                foreach (Nw.ModelItem c in it.Children)
                {
                    if (++k > 30) { sb.AppendLine("  ..."); break; }
                    sb.AppendLine("  " + Show(walk.TypeOf(c)).PadRight(8) + " " + SafeName(c));
                }
            }
            catch { }
            sb.AppendLine();
            sb.AppendLine("All properties (Category > Name = value):");
            foreach (var p in bag.Items.Take(400)) sb.AppendLine("  " + p[0] + " > " + p[1] + " = " + p[2]);
            sb.AppendLine();
            sb.AppendLine("If TYPE / ACON / LCON / bores use other names in your model, add them to propertyAliases in:");
            sb.AppendLine("  " + RulesPath);
            return sb.ToString();
        }

        private static string Show(string t) { return string.IsNullOrEmpty(t) ? "(none)" : t; }
    }
}
