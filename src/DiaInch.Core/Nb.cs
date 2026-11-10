// MD Dia-Inch - nominal bore parsing and mm → inch conversion
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DiaInch.Core
{
    public struct NbValue
    {
        public double Inch;
        /// <summary>True when the value was not on the NB table (nearest / raw conversion) - row should be checked.</summary>
        public bool OffTable;
        public NbValue(double inch, bool off) { Inch = inch; OffTable = off; }
    }

    public static class Nb
    {
        private static readonly Regex Frac = new Regex(@"^\s*(?:(\d+)\s+)?(\d+)\s*/\s*(\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex NumUnit = new Regex(@"^\s*([0-9]*\.?[0-9]+(?:[eE][-+]?\d+)?)\s*(mm|millimet(?:er|re)s?|in|inch|inches|""|''|nb|dn)?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Lead = new Regex(@"^\s*(?:dn|nb)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Parses "100", "100mm", "100 mm", "DN100", "4in", "4\"", "1 1/2in", "3/4\"", "1-1/2\"". Null when not a bore.</summary>
        public static NbValue? Parse(string text, Rules rules)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string s = text.Trim();
            bool dn = Lead.IsMatch(s);
            s = Lead.Replace(s, "");
            // PDMS often writes "100mm" or "4in"; E3D can write "100.0mm"
            string unit = null;
            string lower = s.ToLowerInvariant();
            foreach (string u in new[] { "inches", "inch", "in", "\"", "''", "millimetres", "millimeters", "millimetre", "millimeter", "mm" })
            {
                if (lower.EndsWith(u, StringComparison.Ordinal)) { unit = u; s = s.Substring(0, s.Length - u.Length).Trim(); break; }
            }
            bool inch = unit != null && !unit.StartsWith("m", StringComparison.Ordinal);
            if (unit == null && !dn && string.Equals(rules != null ? rules.BareBoreUnit : "mm", "in", StringComparison.OrdinalIgnoreCase)) inch = true;

            double val;
            var f = Frac.Match(s.Replace("-", " "));
            if (f.Success)
            {
                double whole = f.Groups[1].Success ? double.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                double den = double.Parse(f.Groups[3].Value, CultureInfo.InvariantCulture);
                if (den == 0) return null;
                val = whole + double.Parse(f.Groups[2].Value, CultureInfo.InvariantCulture) / den;
                if (unit == null) inch = true; // fractions are always inches
            }
            else
            {
                var m = NumUnit.Match(s);
                if (!m.Success) return null;
                val = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            if (val <= 0 || double.IsNaN(val) || double.IsInfinity(val)) return null;
            return inch ? FromInch(val, rules) : FromMm(val, rules);
        }

        /// <summary>NB mm → NB inch using the table. Off-table: nearest entry within 2 mm, else mm / 25.4 (flagged).</summary>
        public static NbValue FromMm(double mm, Rules rules)
        {
            var table = rules != null ? rules.NbTable : Rules.Default().NbTable;
            double bestKey = double.NaN, bestDiff = double.MaxValue;
            foreach (var kv in table)
            {
                double d = Math.Abs(kv.Key - mm);
                if (d < bestDiff) { bestDiff = d; bestKey = kv.Key; }
            }
            if (!double.IsNaN(bestKey) && bestDiff < 1e-6) return new NbValue(table[bestKey], false);
            if (!double.IsNaN(bestKey) && bestDiff <= 2.0) return new NbValue(table[bestKey], true);
            return new NbValue(Math.Round(mm / 25.4, 3), true);
        }

        /// <summary>Inch value as given; flagged when it is not one of the table's inch sizes.</summary>
        public static NbValue FromInch(double inch, Rules rules)
        {
            var table = rules != null ? rules.NbTable : Rules.Default().NbTable;
            foreach (var kv in table) if (Math.Abs(kv.Value - inch) < 1e-6) return new NbValue(kv.Value, false);
            return new NbValue(Math.Round(inch, 3), true);
        }

        /// <summary>NB inch → table NB mm (for the Size column). 0 when not in the table.</summary>
        public static double ToMm(double inch, Rules rules)
        {
            foreach (var kv in rules.NbTable) if (Math.Abs(kv.Value - inch) < 1e-6) return kv.Key;
            return Math.Round(inch * 25.4);
        }

        // ASME B36.10 outside diameters (mm) by NB mm.
        private static readonly double[,] OdTable =
        {
            { 6, 10.3 }, { 8, 13.7 }, { 10, 17.1 }, { 15, 21.3 }, { 20, 26.7 }, { 25, 33.4 }, { 32, 42.2 }, { 40, 48.3 },
            { 50, 60.3 }, { 65, 73.0 }, { 80, 88.9 }, { 90, 101.6 }, { 100, 114.3 }, { 125, 141.3 }, { 150, 168.3 },
            { 200, 219.1 }, { 250, 273.0 }, { 300, 323.8 }
        };

        /// <summary>Pipe OD (mm) for an NB inch. 14" and up OD = NB inch × 25.4.</summary>
        public static double OdMm(double nbInch, Rules rules)
        {
            if (nbInch >= 14) return nbInch * 25.4;
            double mm = ToMm(nbInch, rules);
            for (int i = 0; i < OdTable.GetLength(0); i++) if (Math.Abs(OdTable[i, 0] - mm) < 1e-6) return OdTable[i, 1];
            return nbInch * 25.4 + 10;
        }

        /// <summary>NB inch from a measured outside diameter (mm): table OD within 4%, else null.</summary>
        public static double? FromOd(double odMm, Rules rules)
        {
            if (odMm <= 5) return null;
            double best = double.MaxValue; double? inch = null;
            for (int i = 0; i < OdTable.GetLength(0); i++)
            {
                double d = Math.Abs(OdTable[i, 1] - odMm) / OdTable[i, 1];
                if (d < best) { best = d; inch = FromMm(OdTable[i, 0], rules).Inch; }
            }
            if (best <= 0.04) return inch;
            if (odMm > 340)
            {
                double nominal = Math.Round(odMm / 25.4 / 2) * 2; // big bore sizes go in 2" steps
                if (nominal >= 14 && Math.Abs(nominal * 25.4 - odMm) / odMm <= 0.04) return nominal;
            }
            return null;
        }

        public static string Format(double inch)
        {
            if (inch <= 0) return "";
            double whole = Math.Floor(inch + 1e-9);
            double frac = inch - whole;
            string[] names = { "", "1/8", "1/4", "3/8", "1/2", "5/8", "3/4", "7/8" };
            int eighths = (int)Math.Round(frac * 8);
            if (Math.Abs(frac * 8 - eighths) < 1e-3 && eighths > 0 && eighths < 8)
                return (whole > 0 ? whole.ToString("0", CultureInfo.InvariantCulture) + " " : "") + names[eighths] + "\"";
            if (Math.Abs(frac) < 1e-3) return whole.ToString("0", CultureInfo.InvariantCulture) + "\"";
            return inch.ToString("0.###", CultureInfo.InvariantCulture) + "\"";
        }
    }
}
