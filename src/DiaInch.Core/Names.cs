// MD Dia-Inch - shared names
// Author: MD (Mayank Dholariya)
using System;
using System.Globalization;
using System.Text;

namespace DiaInch.Core
{
    public static class Names
    {
        public const string Product = "MD Dia-Inch";
        public const string Author = "MD (Mayank Dholariya)";
        public const string Version = "1.0.0";
        public const string LogFolder = "MDDiaInch";
        public const string LogFile = "mddiainch.log";
        public const string RulesFile = "rules.json";

        public static string SanitizeFileFragment(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Model";
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
            {
                if (c < 32 || "/\\:*?\"<>|".IndexOf(c) >= 0 || char.IsWhiteSpace(c)) sb.Append('_');
                else sb.Append(c);
                if (sb.Length >= 40) break;
            }
            string s = sb.ToString().Trim('_', '.', ' ');
            return s.Length == 0 ? "Model" : s;
        }

        public static string DefaultFileName(string modelName, DateTime when, string ext)
        {
            return "DiaInch_" + SanitizeFileFragment(modelName) + "_" +
                   when.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ext;
        }

        public static string FormatGuid(Guid g)
        {
            return g == Guid.Empty ? "" : g.ToString("D", CultureInfo.InvariantCulture);
        }
    }
}
