// MD Dia-Inch - dock pane UI
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DiaInch.Core;
using Nw = Autodesk.Navisworks.Api;
using NavisApp = Autodesk.Navisworks.Api.Application;

namespace MDDiaInch
{
    internal static class Theme
    {
        internal static readonly Color Surface = Color.FromArgb(243, 244, 246);
        internal static readonly Color Text = Color.FromArgb(32, 33, 36);
        internal static readonly Color Subtle = Color.FromArgb(96, 100, 108);
        internal static readonly Color Accent = Color.FromArgb(0, 100, 125);
        internal static readonly Color CheckBack = Color.FromArgb(255, 242, 204);
        internal static Font Ui(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size, style); }
    }

    public sealed class DiaInchPaneControl : UserControl
    {
        private readonly RadioButton _whole = new RadioButton { Text = "Scan whole model", AutoSize = true, Checked = true };
        private readonly RadioButton _limit = new RadioButton { Text = "Limit to chosen hierarchies (pipe parents)", AutoSize = true };
        private readonly ListBox _parents = new ListBox { Height = 70, Dock = DockStyle.Top, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
        private readonly CheckBox _skipHidden = new CheckBox { Text = "Skip hidden items", AutoSize = true, Checked = true };
        private readonly ComboBox _view = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _status = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 52, ForeColor = Theme.Subtle };
        private readonly Label _totals = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 36, ForeColor = Theme.Text };
        private Button _scan, _cancel, _xlsx, _csv;

        private Rules _rules;
        private ScanResult _last;
        private string _lastScope = "";
        private string _lastModel = "";
        private DateTime _lastWhen;
        private bool _busy, _cancelRequested;
        private List<object> _rowItems = new List<object>();
        private List<List<object>> _rowSel = new List<List<object>>();

        public DiaInchPaneControl()
        {
            BackColor = Theme.Surface;
            Font = Theme.Ui(9F);
            Padding = new Padding(8);
            _view.Items.AddRange(new object[] { "Lines", "Area totals", "Hierarchy totals", "By size" });
            _view.SelectedIndex = 0;

            var title = new Label { Text = Names.Product + "  v" + Names.Version, Font = Theme.Ui(12F, FontStyle.Bold), ForeColor = Theme.Accent, Dock = DockStyle.Top, Height = 26 };
            var sub = new Label { Text = "Weld joints, inch-dia and inch-meter per pipeline  |  " + Names.Author, ForeColor = Theme.Subtle, Dock = DockStyle.Top, Height = 20 };

            // scope
            var scopeBox = new GroupBox { Text = "Scope", Dock = DockStyle.Top, Height = 180, Padding = new Padding(6) };
            var radios = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            radios.Controls.Add(_whole); radios.Controls.Add(_limit);
            var pbtn = Row(Btn("Add selected", AddSelected), Btn("Set selected", SetSelected), Btn("Remove", RemoveParents),
                           Btn("Clear", ClearParents), Btn("Import from Hierarchy Scan", ImportHs));
            scopeBox.Controls.Add(pbtn); scopeBox.Controls.Add(_parents); scopeBox.Controls.Add(radios);
            _whole.CheckedChanged += (s, e) => ScopeChanged();

            // actions
            _scan = Btn("Scan", RunScan); _scan.Font = Theme.Ui(9F, FontStyle.Bold);
            _cancel = Btn("Cancel", (s, e) => _cancelRequested = true); _cancel.Enabled = false;
            _xlsx = Btn("Export Excel", ExportXlsx); _csv = Btn("Export CSV", ExportCsv);
            _xlsx.Enabled = _csv.Enabled = false;
            var act = Row(_scan, _cancel, _xlsx, _csv, _skipHidden);
            var tools = Row(Btn("Open rules", OpenRules), Btn("Reload rules", (s, e) => ReloadRules(true)), Btn("Inspect selection", InspectSel),
                            Btn("Open log folder", (s, e) => OpenPath(Diagnostics.Folder)));
            var viewRow = Row(new Label { Text = "Show:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _view,
                              new Label { Text = "click a row to select it in the model", AutoSize = true, ForeColor = Theme.Subtle, Padding = new Padding(0, 6, 0, 0) });
            _view.SelectedIndexChanged += (s, e) => FillGrid();

            // grid
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            _grid.CellClick += (s, e) => { if (e.RowIndex >= 0) SelectRows(); };
            _grid.KeyUp += (s, e) => { if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) SelectRows(); };

            Controls.Add(_grid);
            foreach (Control c in new Control[] { viewRow, _totals, _status, tools, act, scopeBox, sub, title }) Controls.Add(c);

            ReloadRules(false);
            LoadScopeState();
            try { NavisApp.ActiveDocumentChanged += (s, e) => { LoadScopeState(); }; } catch { }
            SetStatus("Ready. Pick a scope and press Scan. Rules: " + Bridge.RulesPath);
        }

        // ---------------- small UI helpers ----------------
        private static Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 2, 6, 2), FlatStyle = FlatStyle.System };
            b.Click += (s, e) =>
            {
                try { click(s, e); }
                catch (Exception ex) { Diagnostics.Log(text + ": " + ex); MessageBox.Show(text + " failed:\r\n" + ex.Message, Names.Product, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            return b;
        }

        private static FlowLayoutPanel Row(params Control[] cs)
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 2, 0, 2) };
            p.Controls.AddRange(cs);
            return p;
        }

        private void SetStatus(string s) { _status.Text = s ?? ""; _status.Refresh(); }

        private static Nw.Document Doc { get { try { return NavisApp.ActiveDocument; } catch { return null; } } }

        private static void OpenPath(string p)
        {
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch (Exception ex) { Diagnostics.Log("Open " + p + ": " + ex.Message); }
        }

        // ---------------- rules ----------------
        private void ReloadRules(bool tell)
        {
            string err;
            _rules = Bridge.LoadRules(out err);
            if (err != null)
                MessageBox.Show("rules.json could not be read, built-in defaults are used for now:\r\n\r\n" + err + "\r\n\r\n" + Bridge.RulesPath,
                    Names.Product, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (tell) SetStatus("Rules reloaded from " + Bridge.RulesPath + ". Press Scan to apply.");
        }

        private void OpenRules(object s, EventArgs e)
        {
            string err; Bridge.LoadRules(out err); // makes sure the file exists
            try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Bridge.RulesPath + "\"") { UseShellExecute = true }); }
            catch { OpenPath(Bridge.RulesPath); }
            SetStatus("Edit rules.json, save, then press Reload rules and Scan.");
        }

        private void InspectSel(object s, EventArgs e)
        {
            var doc = Doc; if (doc == null) return;
            string text = Bridge.Inspect(doc, _rules);
            using (var f = new Form { Text = Names.Product + " - inspect selection", Width = 760, Height = 620, StartPosition = FormStartPosition.CenterParent })
            {
                var tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F), Text = text.Replace("\n", "\r\n").Replace("\r\r", "\r") };
                f.Controls.Add(tb);
                f.ShowDialog(this);
            }
        }

        // ---------------- scope ----------------
        private AreaState State { get { return Bridge.StateFor(Doc); } }

        private void LoadScopeState()
        {
            try
            {
                var st = State;
                _limit.Checked = st.Limit; _whole.Checked = !st.Limit;
                RefreshParents();
            }
            catch (Exception ex) { Diagnostics.Log("LoadScopeState: " + ex.Message); }
        }

        private void ScopeChanged()
        {
            State.Limit = _limit.Checked;
            Bridge.SaveAreas();
            _parents.Enabled = _limit.Checked;
        }

        private void RefreshParents()
        {
            _parents.Items.Clear();
            var doc = Doc;
            foreach (var r in Bridge.Resolve(doc, State.Parents)) _parents.Items.Add(r.Text);
            _parents.Enabled = _limit.Checked;
        }

        private void AddSelected(object s, EventArgs e) { AddRefs(Bridge.RefsFromSelection(Doc), false, "selection"); }
        private void SetSelected(object s, EventArgs e) { AddRefs(Bridge.RefsFromSelection(Doc), true, "selection"); }

        private void ImportHs(object s, EventArgs e)
        {
            var refs = Bridge.HierarchyScanPipeParents(Doc);
            if (refs.Count == 0) { SetStatus("No pipe parents saved by MD Hierarchy Scan for this model."); return; }
            AddRefs(refs, false, "MD Hierarchy Scan");
        }

        private void AddRefs(List<AreaRef> refs, bool replace, string from)
        {
            if (refs.Count == 0) { SetStatus("Select one or more pipe parents (zones, sites, pipes...) in the Selection Tree first."); return; }
            var st = State;
            if (replace) st.Parents.Clear();
            int added = AreaState.Merge(st.Parents, refs);
            st.Limit = true; _limit.Checked = true;
            Bridge.SaveAreas();
            RefreshParents();
            SetStatus("Added " + added + " pipe parent(s) from " + from + ". Total " + st.Parents.Count + ".");
        }

        private void RemoveParents(object s, EventArgs e)
        {
            var st = State;
            foreach (int i in _parents.SelectedIndices.Cast<int>().OrderByDescending(i => i))
                if (i < st.Parents.Count) st.Parents.RemoveAt(i);
            Bridge.SaveAreas(); RefreshParents();
        }

        private void ClearParents(object s, EventArgs e) { State.Parents.Clear(); Bridge.SaveAreas(); RefreshParents(); }

        // ---------------- scan ----------------
        private void RunScan(object s, EventArgs e)
        {
            if (_busy) return;
            var doc = Doc;
            if (doc == null || doc.IsClear) { SetStatus("Open a model first."); return; }
            var roots = new List<ScanRoot<Nw.ModelItem>>();
            string scopeText;
            if (_limit.Checked)
            {
                var res = Bridge.Resolve(doc, State.Parents);
                var found = res.Where(r => r.Item != null).ToList();
                if (found.Count == 0) { SetStatus("No chosen hierarchy was found in this model. Select pipe parents and press Add selected."); return; }
                foreach (var r in found) roots.Add(new ScanRoot<Nw.ModelItem>(r.Item, r.Ref.Leaf));
                int missing = res.Count - found.Count;
                scopeText = "Chosen hierarchies: " + string.Join("; ", found.Select(r => r.Ref.Display)) + (missing > 0 ? "  (" + missing + " missing)" : "");
            }
            else
            {
                foreach (var m in Bridge.ModelRoots(doc)) roots.Add(new ScanRoot<Nw.ModelItem>(m, Bridge.SafeName(m)));
                scopeText = "Whole model";
            }

            _busy = true; _cancelRequested = false;
            _scan.Enabled = _xlsx.Enabled = _csv.Enabled = false; _cancel.Enabled = true;
            Cursor = Cursors.WaitCursor;
            var sw = Stopwatch.StartNew();
            try
            {
                var tree = new NwDiaTree(Bridge.MmPerUnit(doc));
                long lastUi = 0;
                _last = Scan.Run(tree, roots, _rules, _skipHidden.Checked,
                    (n, what) =>
                    {
                        if (sw.ElapsedMilliseconds - lastUi < 250) return;
                        lastUi = sw.ElapsedMilliseconds;
                        SetStatus("Scanning... " + n.ToString("N0", CultureInfo.InvariantCulture) + " nodes " + (what ?? ""));
                        Application.DoEvents();
                    },
                    () => _cancelRequested, new ItemComparer());
                _lastScope = scopeText + (_cancelRequested ? "  (CANCELLED - partial)" : "");
                _lastModel = ModelName(doc);
                _lastWhen = DateTime.Now;
                var g = _last.Grand;
                string msg = string.Format(CultureInfo.InvariantCulture,
                    "{0}{1} lines, {2} branches, {3:N0} nodes in {4:0.0}s. {5} line(s) marked CHECK.",
                    _cancelRequested ? "CANCELLED - partial result. " : "", _last.Lines.Count, _last.BranchCount, _last.NodesVisited,
                    sw.Elapsed.TotalSeconds, g.CheckLines);
                if (_last.Lines.Count == 0) msg += " No pipe branches found: use Inspect selection on a branch to see its attributes.";
                SetStatus(msg);
                Diagnostics.Log("Scan: " + msg + " scope=" + _lastScope);
                _totals.Text = string.Format(CultureInfo.InvariantCulture,
                    "TOTAL  BW {0}  SW {1}  Flange {2}  Joints {3}   Inch-dia {4}   Pipe {5} m   Inch-meter {6}{7}",
                    g.BW, g.SW, g.FL, g.Total, Fmt.N(g.InchDia, 2), Fmt.N(g.LengthM, 2), Fmt.N(g.InchMeter, 2),
                    g.Shop.HasValue ? "   Shop " + g.Shop + " / Field " + g.Field : "");
                FillGrid();
                _xlsx.Enabled = _csv.Enabled = _last.Lines.Count > 0;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Scan failed: " + ex);
                SetStatus("Scan failed: " + ex.Message);
            }
            finally
            {
                _busy = false; _scan.Enabled = true; _cancel.Enabled = false;
                Cursor = Cursors.Default;
            }
        }

        private static string ModelName(Nw.Document doc)
        {
            try
            {
                string f = Bridge.DocKey(doc);
                return string.IsNullOrEmpty(f) ? "model" : Path.GetFileNameWithoutExtension(f);
            }
            catch { return "model"; }
        }

        // ---------------- grid ----------------
        private void FillGrid()
        {
            _grid.SuspendLayout();
            _grid.Rows.Clear(); _grid.Columns.Clear(); _rowSel.Clear();
            if (_last == null) { _grid.ResumeLayout(); return; }
            int view = _view.SelectedIndex;
            if (view == 0)
            {
                string[] cols = { "Line No", "Area", "Size", "Spec", "BW", "SW", "Flange", "Total", "Inch-dia", "Pipe m", "Inch-meter", "Status", "Notes" };
                AddCols(cols, 4);
                foreach (var l in _last.Lines)
                {
                    int i = _grid.Rows.Add(l.LineNo, l.Area, l.SizeText, l.Spec, l.BW, l.SW, l.FL, l.Total,
                        Fmt.N(l.InchDia, 2), Fmt.N(l.LengthM, 2), Fmt.N(l.InchMeter, 2), l.Status, l.NotesText);
                    if (l.Check) _grid.Rows[i].DefaultCellStyle.BackColor = Theme.CheckBack;
                    _rowSel.Add(l.Items);
                }
            }
            else if (view == 3)
            {
                AddCols(new[] { "Size", "Joints", "Inch-dia", "Pipe m", "Inch-meter" }, 1);
                foreach (var t in _last.BySize)
                {
                    _grid.Rows.Add(t.Key, t.Total, Fmt.N(t.InchDia, 2), Fmt.N(t.LengthM, 2), Fmt.N(t.InchMeter, 2));
                    _rowSel.Add(new List<object>());
                }
            }
            else
            {
                bool area = view == 1;
                AddCols(new[] { area ? "Area" : "Hierarchy", "Lines", "BW", "SW", "Flange", "Total", "Inch-dia", "Pipe m", "Inch-meter", "CHECK lines" }, 1);
                foreach (var t in area ? _last.ByArea : _last.ByScope)
                {
                    int i = _grid.Rows.Add(t.Key, t.Lines, t.BW, t.SW, t.FL, t.Total, Fmt.N(t.InchDia, 2), Fmt.N(t.LengthM, 2), Fmt.N(t.InchMeter, 2), t.CheckLines);
                    if (t.CheckLines > 0) _grid.Rows[i].Cells[9].Style.BackColor = Theme.CheckBack;
                    string key = t.Key;
                    _rowSel.Add(_last.Lines.Where(l => string.Equals(area ? l.Area : l.Scope, key, StringComparison.OrdinalIgnoreCase)).SelectMany(l => l.Items).ToList());
                }
            }
            var gr = _last.Grand;
            if (view != 3)
            {
                int ti = view == 0
                    ? _grid.Rows.Add("TOTAL", "", "", "", gr.BW, gr.SW, gr.FL, gr.Total, Fmt.N(gr.InchDia, 2), Fmt.N(gr.LengthM, 2), Fmt.N(gr.InchMeter, 2), gr.CheckLines > 0 ? gr.CheckLines + " CHECK" : "OK", "")
                    : _grid.Rows.Add("TOTAL", gr.Lines, gr.BW, gr.SW, gr.FL, gr.Total, Fmt.N(gr.InchDia, 2), Fmt.N(gr.LengthM, 2), Fmt.N(gr.InchMeter, 2), gr.CheckLines);
                _grid.Rows[ti].DefaultCellStyle.Font = Theme.Ui(9F, FontStyle.Bold);
                _rowSel.Add(_last.Lines.SelectMany(l => l.Items).ToList());
            }
            _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
            foreach (DataGridViewColumn c in _grid.Columns) if (c.Width > 260) c.Width = 260;
            _grid.ResumeLayout();
        }

        private void AddCols(string[] names, int firstNumeric)
        {
            for (int i = 0; i < names.Length; i++)
            {
                int k = _grid.Columns.Add("c" + i, names[i]);
                var col = _grid.Columns[k];
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                bool num = i >= firstNumeric && names[i] != "Status" && names[i] != "Notes" && names[i] != "Area" && names[i] != "Size" && names[i] != "Spec";
                if (num) col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
        }

        private void SelectRows()
        {
            var doc = Doc;
            if (doc == null || _last == null) return;
            var items = new List<object>();
            foreach (DataGridViewRow r in _grid.SelectedRows)
                if (r.Index >= 0 && r.Index < _rowSel.Count) items.AddRange(_rowSel[r.Index]);
            if (items.Count == 0) return;
            try
            {
                int n = Bridge.Select(doc, items.Distinct());
                SetStatus("Selected " + n + " item(s) in the model.");
            }
            catch (Exception ex) { Diagnostics.Log("Select: " + ex.Message); SetStatus("Could not select: " + ex.Message); }
        }

        // ---------------- export ----------------
        private void ExportXlsx(object s, EventArgs e)
        {
            if (_last == null) return;
            string path = AskPath("Excel workbook (*.xlsx)|*.xlsx", "xlsx");
            if (path == null) return;
            XlsxWriter.Save(path, Report.Build(_last, _rules, _lastModel, _lastScope, _lastWhen), Names.Author);
            Done(path);
        }

        private void ExportCsv(object s, EventArgs e)
        {
            if (_last == null) return;
            string path = AskPath("CSV (*.csv)|*.csv", "csv");
            if (path == null) return;
            var lines = Report.Build(_last, _rules, _lastModel, _lastScope, _lastWhen).First(t => t.SheetName == "Lines");
            Report.SaveCsv(path, lines);
            Done(path);
        }

        private string AskPath(string filter, string ext)
        {
            using (var d = new SaveFileDialog { Filter = filter, FileName = Names.DefaultFileName(_lastModel, _lastWhen, ext), OverwritePrompt = true })
            {
                try { d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch { }
                return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
            }
        }

        private void Done(string path)
        {
            Diagnostics.Log("Exported " + path);
            SetStatus("Saved " + path);
            if (MessageBox.Show("Saved:\r\n" + path + "\r\n\r\nOpen it now?", Names.Product, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                OpenPath(path);
        }
    }
}
