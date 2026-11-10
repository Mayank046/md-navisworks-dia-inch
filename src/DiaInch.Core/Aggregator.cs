// MD Dia-Inch - per line / area / hierarchy / size totals
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Linq;

namespace DiaInch.Core
{
    public sealed class Totals
    {
        public string Key = "";
        public string Key2 = "";
        public int Lines;
        public int BW, SW, SCR, FL, Total;
        public double InchDia, LengthM, InchMeter;
        public int? Shop, Field;
        public int CheckLines;
    }

    public sealed class ScanResult
    {
        public List<LineResult> Lines = new List<LineResult>();
        public List<Totals> ByArea = new List<Totals>();
        public List<Totals> ByScope = new List<Totals>();
        public List<Totals> BySize = new List<Totals>();
        public Totals Grand = new Totals { Key = "TOTAL" };
        public List<Joint> Joints = new List<Joint>();
        public int BranchCount;
        public int NodesVisited;
        public bool ShopFieldAvailable;
    }

    public static class Aggregator
    {
        public static bool CountsAsWeld(JointKind k, Rules r)
        {
            return k == JointKind.BW || k == JointKind.SW || (k == JointKind.SCR && r.CountScrewedJoints);
        }

        public static bool InInchDia(JointKind k, Rules r)
        {
            return CountsAsWeld(k, r) || (k == JointKind.FL && r.FlangeJointsInInchDia);
        }

        /// <summary>Groups branch results into lines (same Line No) and builds the totals.</summary>
        public static ScanResult Build(IEnumerable<BranchResult> branches, Rules r)
        {
            var res = new ScanResult();
            var map = new Dictionary<string, LineResult>(StringComparer.OrdinalIgnoreCase);
            var order = new List<LineResult>();
            foreach (var br in branches)
            {
                res.BranchCount++;
                var b = br.Input;
                LineResult lr;
                if (!map.TryGetValue(b.LineNo, out lr))
                {
                    lr = new LineResult { LineNo = b.LineNo };
                    map[b.LineNo] = lr;
                    order.Add(lr);
                }
                lr.Branches++;
                lr.BranchResults.Add(br);
                lr.Area = Join(lr.Area, b.Area);
                lr.Scope = Join(lr.Scope, b.Scope);
                lr.Spec = Join(lr.Spec, b.Spec);
                object sel = b.LineItem ?? b.Item;
                if (sel != null && !lr.Items.Contains(sel)) lr.Items.Add(sel);
                foreach (var j in br.Joints)
                {
                    res.Joints.Add(j);
                    switch (j.Kind)
                    {
                        case JointKind.BW: lr.BW++; break;
                        case JointKind.SW: lr.SW++; break;
                        case JointKind.SCR: lr.SCR++; break;
                        case JointKind.FL: lr.FL++; break;
                    }
                    if (CountsAsWeld(j.Kind, r))
                    {
                        lr.Total++;
                        if (j.Field.HasValue)
                        {
                            if (j.Field.Value) lr.FieldWelds = (lr.FieldWelds ?? 0) + 1;
                            else lr.Shop = (lr.Shop ?? 0) + 1;
                        }
                    }
                    if (InInchDia(j.Kind, r)) lr.InchDia += j.Inch;
                }
                lr.LengthM += br.LengthM;
                lr.InchMeter += br.InchMeter;
                lr.MaxInch = Math.Max(lr.MaxInch, br.MaxInch);
                foreach (double s in br.Sizes) if (!lr.Sizes.Contains(s)) lr.Sizes.Add(s);
                if (br.Check) lr.Check = true;
                foreach (string n in br.Notes) if (!lr.Notes.Contains(n)) lr.Notes.Add(n);
            }
            foreach (var lr in order)
            {
                lr.Sizes.Sort();
                lr.Sizes.Reverse();
                // shop/field only reported when every weld of the line carries the attribute
                if ((lr.Shop ?? 0) + (lr.FieldWelds ?? 0) != lr.Total) { lr.Shop = null; lr.FieldWelds = null; }
                if (lr.Total == 0 && lr.FL == 0 && lr.LengthM == 0) { lr.Check = true; if (!lr.Notes.Contains("nothing counted")) lr.Notes.Add("nothing counted"); }
                lr.Notes.Sort(StringComparer.Ordinal);
            }
            res.Lines = order.OrderBy(l => l.Area, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.LineNo, StringComparer.OrdinalIgnoreCase).ToList();
            res.ShopFieldAvailable = res.Lines.Any(l => l.Shop.HasValue);

            res.ByArea = Group(res.Lines, l => l.Area, l => l.Scope);
            res.ByScope = Group(res.Lines, l => l.Scope.Length == 0 ? "Whole model" : l.Scope, l => "");
            var all = Group(res.Lines, l => "TOTAL", l => "");
            res.Grand = all.Count > 0 ? all[0] : new Totals { Key = "TOTAL" };

            // by size: joints by joint NB, length by tube NB
            var sizes = new SortedDictionary<double, Totals>();
            Func<double, Totals> get = s =>
            {
                Totals t;
                if (!sizes.TryGetValue(s, out t)) { t = new Totals { Key = s > 0 ? Nb.Format(s) : "unknown" }; sizes[s] = t; }
                return t;
            };
            foreach (var j in res.Joints)
            {
                var t = get(j.Inch);
                Add(t, j.Kind, r, j.Inch);
            }
            foreach (var lr in res.Lines)
                foreach (var br in lr.BranchResults)
                    foreach (var kv in br.LengthBySize)
                    {
                        var t = get(kv.Key);
                        t.LengthM += kv.Value;
                        t.InchMeter += kv.Key * kv.Value;
                    }
            res.BySize = sizes.Reverse().Select(kv => kv.Value).ToList();
            return res;
        }

        private static void Add(Totals t, JointKind k, Rules r, double inch)
        {
            switch (k)
            {
                case JointKind.BW: t.BW++; break;
                case JointKind.SW: t.SW++; break;
                case JointKind.SCR: t.SCR++; break;
                case JointKind.FL: t.FL++; break;
            }
            if (CountsAsWeld(k, r)) t.Total++;
            if (InInchDia(k, r)) t.InchDia += inch;
        }

        private static List<Totals> Group(List<LineResult> lines, Func<LineResult, string> key, Func<LineResult, string> key2)
        {
            var d = new Dictionary<string, Totals>(StringComparer.OrdinalIgnoreCase);
            var order = new List<Totals>();
            foreach (var l in lines)
            {
                string k = key(l) ?? "";
                Totals t;
                if (!d.TryGetValue(k, out t)) { t = new Totals { Key = k, Key2 = key2(l) ?? "" }; d[k] = t; order.Add(t); }
                t.Lines++;
                t.BW += l.BW; t.SW += l.SW; t.SCR += l.SCR; t.FL += l.FL; t.Total += l.Total;
                t.InchDia += l.InchDia; t.LengthM += l.LengthM; t.InchMeter += l.InchMeter;
                if (l.Shop.HasValue) t.Shop = (t.Shop ?? 0) + l.Shop.Value;
                if (l.FieldWelds.HasValue) t.Field = (t.Field ?? 0) + l.FieldWelds.Value;
                if (l.Check) t.CheckLines++;
            }
            return order.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string Join(string cur, string add)
        {
            if (string.IsNullOrWhiteSpace(add)) return cur ?? "";
            if (string.IsNullOrEmpty(cur)) return add;
            foreach (string p in cur.Split(new[] { "; " }, StringSplitOptions.None))
                if (string.Equals(p, add, StringComparison.OrdinalIgnoreCase)) return cur;
            return cur + "; " + add;
        }
    }
}
