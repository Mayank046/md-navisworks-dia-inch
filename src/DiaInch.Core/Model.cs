// MD Dia-Inch - data model (no Navisworks types)
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiaInch.Core
{
    /// <summary>Properties of one model item as (category, name, value). Lookup by alias list, case/space-insensitive.</summary>
    public sealed class PropBag
    {
        private readonly List<string[]> _items = new List<string[]>();
        private Dictionary<string, string> _byName, _byFull;

        public void Add(string category, string name, string value)
        {
            _items.Add(new[] { category ?? "", name ?? "", value ?? "" });
            _byName = null; _byFull = null;
        }

        public int Count { get { return _items.Count; } }
        public IEnumerable<string[]> Items { get { return _items; } }

        private void Index()
        {
            if (_byName != null) return;
            _byName = new Dictionary<string, string>(StringComparer.Ordinal);
            _byFull = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in _items)
            {
                if (string.IsNullOrWhiteSpace(t[2])) continue;
                string n = Rules.Norm(t[1]);
                string f = Rules.Norm(t[0]) + ">" + n;
                if (!_byName.ContainsKey(n)) _byName[n] = t[2].Trim();
                if (!_byFull.ContainsKey(f)) _byFull[f] = t[2].Trim();
            }
        }

        /// <summary>First non-empty value for any alias ("NAME" or "Category > NAME").</summary>
        public string Get(IEnumerable<string> aliases)
        {
            if (aliases == null || _items.Count == 0) return null;
            Index();
            string v;
            foreach (string a in aliases)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                int gt = a.IndexOf('>');
                if (gt > 0)
                {
                    string key = Rules.Norm(a.Substring(0, gt)) + ">" + Rules.Norm(a.Substring(gt + 1));
                    if (_byFull.TryGetValue(key, out v)) return v;
                }
                else if (_byName.TryGetValue(Rules.Norm(a), out v)) return v;
            }
            return null;
        }

        public string Get(Rules r, string logical)
        {
            List<string> al;
            return r.Aliases.TryGetValue(logical, out al) ? Get(al) : null;
        }
    }

    public sealed class Member
    {
        public string Name = "";
        /// <summary>Resolved type code (TUBE, ELBO, TEE ...); "" when unknown.</summary>
        public string Type = "";
        public PropBag Props = new PropBag();
        /// <summary>Axis-aligned box in mm: minX,minY,minZ,maxX,maxY,maxZ. Null when no geometry.</summary>
        public double[] BoxMm;
        public object Item;
    }

    public sealed class BranchInput
    {
        public string Name = "";
        public PropBag Props = new PropBag();
        public List<Member> Members = new List<Member>();
        public object Item;
        public object LineItem;
        public string LineNo = "";
        public string Area = "";
        public string Scope = "";
        public string Spec = "";
    }

    public sealed class Joint
    {
        public string LineNo = "";
        public string Branch = "";
        public JointKind Kind;
        /// <summary>NB inch, 0 when unknown (BoreMissing).</summary>
        public double Inch;
        public bool BoreMissing;
        /// <summary>true = field, false = shop, null = no shop/field attribute.</summary>
        public bool? Field;
        public bool Assumed;
        public string Where = "";
        public string Note = "";
    }

    public sealed class BranchResult
    {
        public BranchInput Input;
        public List<Joint> Joints = new List<Joint>();
        public double LengthM;
        public double InchMeter;
        public double MaxInch;
        public HashSet<double> Sizes = new HashSet<double>();
        public SortedSet<string> Notes = new SortedSet<string>(StringComparer.Ordinal);
        public bool Check;
        public bool HasShopField;
        /// <summary>Tube NB inch → metres, for the By Size sheet.</summary>
        public Dictionary<double, double> LengthBySize = new Dictionary<double, double>();
    }

    public sealed class LineResult
    {
        public string LineNo = "";
        public string Area = "";
        public string Scope = "";
        public string Spec = "";
        public double MaxInch;
        public List<double> Sizes = new List<double>();
        public int BW, SW, SCR, FL;
        public int Total;
        public double InchDia;
        public double LengthM;
        public double InchMeter;
        public int? Shop, FieldWelds;
        public int Branches;
        public bool Check;
        public List<string> Notes = new List<string>();
        public List<object> Items = new List<object>();
        public List<BranchResult> BranchResults = new List<BranchResult>();
        public string Status { get { return Check ? "CHECK" : "OK"; } }
        public string SizeText { get { return Nb.Format(MaxInch); } }
        public string NotesText { get { return string.Join("; ", Notes); } }
    }

    public static class Fmt
    {
        public static string N(double v, int dp)
        {
            return Math.Round(v, dp).ToString("0." + new string('#', dp), CultureInfo.InvariantCulture);
        }

        /// <summary>Length text → mm. "1234", "1234mm", "1.234 m", "48in", "4 ft". Bare numbers use bareUnitMm (1 = mm).</summary>
        public static double? LengthMm(string text, double bareUnitMm)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Match(text.Trim(), @"^([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?\d+)?)\s*(mm|cm|m|in|inch|inches|""|ft|feet|')?\s*$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            double v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string u = m.Groups[2].Value.ToLowerInvariant();
            double f = u == "" ? bareUnitMm : u == "mm" ? 1 : u == "cm" ? 10 : u == "m" ? 1000 :
                       (u == "ft" || u == "feet" || u == "'") ? 304.8 : 25.4;
            double mm = v * f;
            return mm < 0 ? (double?)null : mm;
        }
    }
}
