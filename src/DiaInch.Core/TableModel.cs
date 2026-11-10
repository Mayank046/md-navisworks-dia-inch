// MD Dia-Inch - in-memory sheet tables
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;

namespace DiaInch.Core
{
    public sealed class TableRow
    {
        public readonly Dictionary<string, string> Cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Excel outline level (0 = none, max 7) for tree-style collapse.</summary>
        public int OutlineLevel;
        /// <summary>0 = normal; 2 = group header (bold, accent fill, full width); 3 = block column header (bold, light fill).</summary>
        public int Style;
        public string Get(string col)
        {
            if (col == null) return "";
            string v;
            return Cells.TryGetValue(col, out v) && v != null ? v : "";
        }
        public void Set(string col, string value)
        {
            if (string.IsNullOrEmpty(col)) return;
            Cells[col] = value ?? "";
        }
    }

    public sealed class DataTable
    {
        public string SheetName = "Sheet1";
        public readonly List<string> Columns = new List<string>();
        public readonly List<TableRow> Rows = new List<TableRow>();
        /// <summary>Columns written as numbers when the value parses.</summary>
        /// <summary>Write Columns as row 1 (bold header). False for block-style sheets.</summary>
        public bool HeaderRow = true;
        /// <summary>Note lines written in column A above the header (e.g. scan mode). Freeze/filter move down with them.</summary>
        public readonly List<string> Preamble = new List<string>();
        public bool AutoFilter = true;
        public bool FreezeHeader = true;
        public readonly HashSet<string> NumericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void EnsureColumn(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            foreach (string c in Columns)
                if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) return;
            Columns.Add(name);
        }

        public TableRow AddRow()
        {
            var r = new TableRow();
            Rows.Add(r);
            return r;
        }
    }
}
