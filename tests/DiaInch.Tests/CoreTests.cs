using System;
using System.IO;
using System.Linq;
using DiaInch.Core;
using Xunit;
using static DiaInch.Tests.H;

namespace DiaInch.Tests
{
    public class NbTests
    {
        private readonly Rules R = Rules.Default();

        [Theory]
        [InlineData("15", 0.5)] [InlineData("20", 0.75)] [InlineData("25", 1)] [InlineData("40", 1.5)] [InlineData("50", 2)]
        [InlineData("80", 3)] [InlineData("100", 4)] [InlineData("150", 6)] [InlineData("600", 24)] [InlineData("1200", 48)]
        [InlineData("100mm", 4)] [InlineData("100 mm", 4)] [InlineData("100.0mm", 4)] [InlineData("DN80", 3)]
        [InlineData("4in", 4)] [InlineData("4\"", 4)] [InlineData("1 1/2in", 1.5)] [InlineData("3/4\"", 0.75)]
        [InlineData("1-1/2\"", 1.5)] [InlineData("2 inch", 2)]
        public void Converts_on_table(string text, double inch)
        {
            var v = Nb.Parse(text, R);
            Assert.True(v.HasValue);
            Assert.Equal(inch, v.Value.Inch, 6);
            Assert.False(v.Value.OffTable);
        }

        [Fact]
        public void Off_table_is_flagged_never_silent()
        {
            var near = Nb.Parse("101", R).Value;
            Assert.Equal(4, near.Inch, 6); Assert.True(near.OffTable);
            var far = Nb.Parse("37", R).Value;
            Assert.Equal(Math.Round(37 / 25.4, 3), far.Inch, 6); Assert.True(far.OffTable);
        }

        [Theory] [InlineData("")] [InlineData(null)] [InlineData("abc")] [InlineData("0")] [InlineData("-5")]
        public void Junk_is_not_a_bore(string t) { Assert.False(Nb.Parse(t, R).HasValue); }

        [Fact]
        public void Bare_unit_inch_rule()
        {
            var r = Rules.Default(); r.BareBoreUnit = "in";
            Assert.Equal(4, Nb.Parse("4", r).Value.Inch, 6);
            Assert.Equal(4, Nb.Parse("100mm", r).Value.Inch, 6);
        }

        [Fact]
        public void Od_round_trip()
        {
            Assert.Equal(4, Nb.FromOd(114.3, R).Value, 6);
            Assert.Equal(2, Nb.FromOd(60.3, R).Value, 6);
            Assert.Equal(16, Nb.FromOd(406.4, R).Value, 6);
            Assert.False(Nb.FromOd(300, R).HasValue);
            Assert.Equal(114.3, Nb.OdMm(4, R), 6);
        }

        [Fact]
        public void Formats_sizes()
        {
            Assert.Equal("4\"", Nb.Format(4)); Assert.Equal("1 1/2\"", Nb.Format(1.5)); Assert.Equal("3/4\"", Nb.Format(0.75));
        }
    }

    public class RulesTests
    {
        [Theory]
        [InlineData("BWD", JointKind.BW)] [InlineData("bwd", JointKind.BW)] [InlineData("SWF", JointKind.SW)]
        [InlineData("SCF", JointKind.SCR)] [InlineData("NPT", JointKind.SCR)] [InlineData("FBD", JointKind.FL)]
        [InlineData("RF", JointKind.FL)] [InlineData("FGD150", JointKind.FL)] [InlineData("OPEN", JointKind.None)]
        [InlineData("CLOS", JointKind.None)] [InlineData("XYZ", JointKind.Other)]
        public void Classifies_connection_codes(string code, JointKind k) { Assert.Equal(k, Rules.Default().ClassifyConn(code)); }

        [Fact] public void Empty_code_is_unknown() { Assert.Null(Rules.Default().ClassifyConn("  ")); }

        [Theory]
        [InlineData("ELBOW 1 of BRANCH /P-1/B1", "ELBO")] [InlineData("FLANGE 2 of BRANCH /X", "FLAN")] [InlineData("TUBE 3 of BRANCH /X", "TUBE")]
        [InlineData("ELBO", "ELBO")] [InlineData("REDUCER 1 of", "REDU")] [InlineData("BRAN", "BRAN")] [InlineData("/150-P-1001", "")]
        public void Types_from_names(string n, string t) { Assert.Equal(t, Rules.Default().TypeFromWord(n)); }

        [Fact]
        public void Json_round_trip_keeps_everything()
        {
            var a = Rules.Default();
            a.SmallBoreMaxNbMm = 50; a.CountScrewedJoints = true; a.RandomPipeLengthM = 6; a.LineRegex = "\\d+-[A-Z]+";
            a.TypeRules["VALV"].Ends = new System.Collections.Generic.List<string> { "BW", "BW" };
            string json = a.ToJson();
            var b = Rules.FromJson(json);
            Assert.Equal(50, b.SmallBoreMaxNbMm); Assert.True(b.CountScrewedJoints); Assert.Equal(6, b.RandomPipeLengthM);
            Assert.Equal("\\d+-[A-Z]+", b.LineRegex);
            Assert.Equal(new[] { "BW", "BW" }, b.TypeRules["VALV"].Ends);
            Assert.Equal(a.NbTable.Count, b.NbTable.Count);
            Assert.Equal(json, b.ToJson());
        }

        [Fact]
        public void Partial_json_keeps_defaults_and_allows_comments()
        {
            var r = Rules.FromJson("{ // my overrides\n \"countScrewedJoints\": true, \"typeRules\": { \"GASK\": { \"ignore\": true } }, }");
            Assert.True(r.CountScrewedJoints);
            Assert.Equal(40, r.SmallBoreMaxNbMm);
            Assert.True(r.TypeRules.ContainsKey("ELBO"));
        }

        [Fact]
        public void Bad_json_says_where()
        {
            var ex = Assert.Throws<FormatException>(() => Rules.FromJson("{\n \"a\": tru }"));
            Assert.Contains("line 2", ex.Message);
        }
    }

    public class JointTests
    {
        private static ScanResult One(F bran, Rules r = null) { return Run(Zone("/ZONE-A", Pipe("/P-100", bran)), r); }

        [Fact]
        public void Full_property_branch_counts_each_interface_once()
        {
            var b = Bran("/P-100/B1", "HBOR", "100mm", "HCON", "BWD", "HREF", "/E-101/N1", "TBOR", "100mm", "TCON", "BWD", "TREF", "/P-200/B1")
                .Add(Tube("100mm", "1000mm"), C("ELBO", "BWD", "BWD", "100mm"), Tube("100mm", "2000mm"),
                     C("FLAN", "BWD", "FBD", "100mm"), new F("GASK 1", "TYPE", "GASK"), C("VALV", "FBD", "FBD", "100mm"),
                     new F("GASK 2", "TYPE", "GASK"), C("FLAN", "FBD", "BWD", "100mm"), Tube("100mm", "500mm"));
            var s = One(b);
            var l = Assert.Single(s.Lines);
            Assert.Equal("P-100", l.LineNo);
            Assert.Equal("ZONE-A", l.Area);
            Assert.Equal(6, l.BW);   // head, elbow x2, flange weld x2, tail
            Assert.Equal(2, l.FL);   // flange/valve both sides
            Assert.Equal(0, l.SW);
            Assert.Equal(6, l.Total);
            Assert.Equal(24, l.InchDia, 6);
            Assert.Equal(3.5, l.LengthM, 6);
            Assert.Equal(14, l.InchMeter, 6);
            Assert.False(l.Check, l.NotesText);
            Assert.Equal("4\"", l.SizeText);
        }

        [Fact]
        public void Tee_outlet_counts_and_branch_off_tee_does_not_double_count()
        {
            var main = Bran("/P-100/B1", "HCON", "OPEN", "TCON", "OPEN", "HREF", "", "TREF", "")
                .Add(Tube("100", "1000"), C("TEE", "BWD", "BWD", "100", "P3CONN", "BWD", "P3BORE", "50"), Tube("100", "1000"));
            var br = Bran("/P-100/B2", "HCON", "BWD", "HREF", "TEE 1 of BRANCH /P-100/B1", "HBOR", "50", "TCON", "FBD", "TREF", "/E-1/N2", "TBOR", "50")
                .Add(Tube("50", "800"));
            var s = Run(Zone("/Z", Pipe("/P-100", main, br)));
            var l = Assert.Single(s.Lines);
            Assert.Equal(2, l.Branches);
            Assert.Equal(3, l.BW);           // tee run x2 + tee outlet (B2 head is the outlet weld)
            Assert.Equal(1, l.FL);           // B2 tail on tube with TCON FBD → nozzle flange joint
            Assert.Equal(4 + 4 + 2, l.InchDia, 6);
            Assert.Equal(2.8, l.LengthM, 6);
            Assert.Equal(4 * 2 + 2 * 0.8, l.InchMeter, 6);
        }

        [Fact]
        public void Olet_counts_outlet_plus_header_weld_only()
        {
            var b = Bran("/P-1/B1", "HCON", "OPEN", "TCON", "OPEN")
                .Add(Tube("150", "3000"), C("OLET", "BWD", "BWD", "150", "P3CONN", "SWF", "P3BORE", "20"), Tube("150", "3000"));
            var l = One(b).Lines[0];
            Assert.Equal(1, l.SW);
            Assert.Equal(1, l.BW);
            Assert.Equal(1.5, l.InchDia, 6);
        }

        [Fact]
        public void Small_bore_defaults_to_socket_weld_and_flags_check()
        {
            var b = Bran("/P-2/B1", "HBOR", "25", "TBOR", "25", "HCON", "OPEN", "TCON", "OPEN")
                .Add(Tube("25", "500"), new F("ELBO 1", "TYPE", "ELBO"), Tube("25", "500"));
            var l = One(b).Lines[0];
            Assert.Equal(2, l.SW);
            Assert.Equal(0, l.BW);
            Assert.Equal(2, l.InchDia, 6);
            Assert.True(l.Check);
            Assert.Contains(l.Notes, n => n.Contains("assumed"));
        }

        [Fact]
        public void Name_only_rvm_export_uses_geometry_and_defaults()
        {
            var b = new F("BRANCH 1 of PIPE /P-3")
                .Add(new F("TUBE 1 of BRANCH /P-3/B1").WithBox(1000, 114.3, 114.3),
                     new F("ELBOW 1 of BRANCH /P-3/B1").WithBox(200, 200, 114.3),
                     new F("TUBE 2 of BRANCH /P-3/B1").WithBox(114.3, 114.3, 2000));
            var s = Run(new F("/ZONE-X").Add(new F("/P-3").Add(b)));
            var l = Assert.Single(s.Lines);
            Assert.Equal("P-3", l.LineNo);
            Assert.Equal(2, l.BW);
            Assert.Equal(8, l.InchDia, 6);
            Assert.Equal(3.0, l.LengthM, 3);
            Assert.True(l.Check);
            Assert.Contains(l.Notes, n => n.Contains("length from geometry"));
            Assert.Contains(l.Notes, n => n.Contains("tail on tube"));
        }

        [Fact]
        public void Flange_orientation_guessed_from_neighbours()
        {
            var b = Bran("/P-4/B1", "HBOR", "100", "TBOR", "100", "HREF", "/E-1/N1", "TREF", "/E-2/N1")
                .Add(new F("FLAN 1", "TYPE", "FLAN"), Tube("100", "1000"), new F("ELBO 1", "TYPE", "ELBO"), Tube("100", "1000"),
                     new F("FLAN 2", "TYPE", "FLAN"), new F("GASK 1", "TYPE", "GASK"), new F("FLAN 3", "TYPE", "FLAN"), Tube("100", "1000"));
            var l = One(b).Lines[0];
            Assert.Equal(2, l.FL);  // nozzle flange at head + flange pair
            Assert.Equal(5, l.BW);  // flange1 weld, elbow x2, flange2 weld, flange3 weld (tail on tube, no TCON: not counted)
        }

        [Fact]
        public void Screwed_excluded_unless_configured()
        {
            var b = Bran("/P-5/B1", "HCON", "OPEN", "TCON", "OPEN")
                .Add(Tube("15", "100"), C("ELBO", "SCF", "SCF", "15"), Tube("15", "100"));
            var l = One(b).Lines[0];
            Assert.Equal(2, l.SCR); Assert.Equal(0, l.Total); Assert.Equal(0, l.InchDia, 6);
            var r = Rules.Default(); r.CountScrewedJoints = true;
            var l2 = One(b, r).Lines[0];
            Assert.Equal(2, l2.Total); Assert.Equal(1, l2.InchDia, 6);
        }

        [Fact]
        public void Flange_in_inch_dia_option()
        {
            var b = Bran("/P-6/B1", "HCON", "OPEN", "TCON", "OPEN")
                .Add(Tube("50", "100"), C("FLAN", "BWD", "FBD", "50"), C("FLAN", "FBD", "BWD", "50"), Tube("50", "100"));
            var l = One(b).Lines[0];
            Assert.Equal(1, l.FL); Assert.Equal(2, l.BW); Assert.Equal(4, l.InchDia, 6);
            var r = Rules.Default(); r.FlangeJointsInInchDia = true;
            Assert.Equal(6, One(b, r).Lines[0].InchDia, 6);
        }

        [Fact]
        public void Shop_field_split_only_when_attribute_exists()
        {
            var b = Bran("/P-7/B1", "HCON", "OPEN", "TCON", "OPEN")
                .Add(Tube("100", "1000"), C("ELBO", "BWD", "BWD", "100", "SHOP", "TRUE"), Tube("100", "1000"),
                     C("WELD", "BWD", "BWD", "100", "SHOP", "FALSE"), Tube("100", "1000"));
            var l = One(b).Lines[0];
            Assert.Equal(3, l.BW);
            Assert.Equal(2, l.Shop); Assert.Equal(1, l.FieldWelds);

            var b2 = Bran("/P-8/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("100", "1000"), C("ELBO", "BWD", "BWD", "100"), Tube("100", "1000"));
            var l2 = One(b2).Lines[0];
            Assert.Null(l2.Shop); Assert.Null(l2.FieldWelds);
        }

        [Fact]
        public void Weld_element_between_components_is_one_joint()
        {
            var b = Bran("/P-9/B1", "HCON", "OPEN", "TCON", "OPEN")
                .Add(Tube("100", "500"), C("ELBO", "BWD", "BWD", "100"), C("WELD", "BWD", "BWD", "100"), C("ELBO", "BWD", "BWD", "100"), Tube("100", "500"));
            Assert.Equal(3, One(b).Lines[0].BW);
        }

        [Fact]
        public void Elbow_to_elbow_merges()
        {
            var b = Bran("/P-10/B1", "HCON", "OPEN", "TCON", "OPEN")
                .Add(Tube("100", "500"), C("ELBO", "BWD", "BWD", "100"), C("ELBO", "BWD", "BWD", "100"), Tube("100", "500"));
            Assert.Equal(3, One(b).Lines[0].BW);
        }

        [Fact]
        public void Random_length_adds_pipe_to_pipe_welds()
        {
            var b = Bran("/P-11/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("100", "13000mm"));
            var r = Rules.Default(); r.RandomPipeLengthM = 6;
            var l = One(b, r).Lines[0];
            Assert.Equal(2, l.BW);
            Assert.Equal(0, One(b).Lines[0].BW);
        }

        [Fact]
        public void Missing_bore_counts_joint_at_zero_and_flags()
        {
            var b = Bran("/P-12/B1", "HCON", "OPEN", "TCON", "OPEN")
                .Add(new F("TUBE", "TYPE", "TUBE", "LTLE", "1000"), new F("ELBO 1", "TYPE", "ELBO", "ACON", "BWD", "LCON", "BWD"), new F("TUBE", "TYPE", "TUBE", "LTLE", "1000"));
            var l = One(b).Lines[0];
            Assert.Equal(2, l.BW);
            Assert.Equal(0, l.InchDia, 6);
            Assert.Equal(0, l.InchMeter, 6);
            Assert.True(l.Check);
            Assert.Contains(l.Notes, n => n.Contains("bore missing"));
        }

        [Fact]
        public void Reducer_uses_larger_end_for_merge_and_each_end_for_tube_joints()
        {
            var b = Bran("/P-13/B1", "HCON", "OPEN", "TCON", "OPEN")
                .Add(Tube("100", "500"), C("REDU", "BWD", "BWD", "100", "LBOR", "80"), Tube("80", "500"));
            var l = One(b).Lines[0];
            Assert.Equal(2, l.BW);
            Assert.Equal(4 + 3, l.InchDia, 6);
            Assert.Equal(2 + 1.5, l.InchMeter, 6);
        }
    }

    public class LineTests
    {
        [Fact]
        public void Branches_of_one_pipe_group_into_one_line_and_branch_ref_counts_once()
        {
            var b1 = Bran("/150-P-1001-A1A/B1", "HCON", "OPEN", "TCON", "BWD", "TREF", "/150-P-1001-A1A/B2")
                .Add(Tube("150", "1000"));
            var b2 = Bran("/150-P-1001-A1A/B2", "HCON", "BWD", "HREF", "/150-P-1001-A1A/B1", "TCON", "OPEN")
                .Add(Tube("150", "1000"));
            var s = Run(Zone("/AREA-10", Pipe("/150-P-1001-A1A", b1, b2)));
            var l = Assert.Single(s.Lines);
            Assert.Equal(1, l.BW);
            Assert.Equal(6, l.InchDia, 6);
            Assert.Equal(2, l.Branches);
            Assert.Equal("AREA-10", l.Area);
        }

        [Fact]
        public void Line_regex_and_area_totals()
        {
            var r = Rules.Default(); r.LineRegex = @"\d+-[A-Z]+-\d+";
            var p1 = Pipe("/150-P-1001-A1A-IH", Bran("/150-P-1001-A1A-IH/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("150", "1000"), C("ELBO", "BWD", "BWD", "150"), Tube("150", "1000")));
            var p2 = Pipe("/50-P-1002-B1A", Bran("/50-P-1002-B1A/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("50", "1000"), C("ELBO", "BWD", "BWD", "50"), Tube("50", "1000")));
            var p3 = Pipe("/80-CW-2001-C1", Bran("/80-CW-2001-C1/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("80", "2000")));
            var site = new F("/SITE-1", "TYPE", "SITE").Add(Zone("/ZONE-A", p1, p2), Zone("/ZONE-B", p3));
            var s = Run(site, r, "SITE-1");
            Assert.Equal(new[] { "150-P-1001", "50-P-1002", "80-CW-2001" }, s.Lines.Select(x => x.LineNo).ToArray());
            var a = s.ByArea.Single(x => x.Key == "ZONE-A");
            Assert.Equal(2, a.Lines); Assert.Equal(4, a.BW); Assert.Equal(12 + 4, a.InchDia, 6);
            Assert.Equal(6 * 2 + 2 * 2, a.InchMeter, 6);
            var bz = s.ByArea.Single(x => x.Key == "ZONE-B");
            Assert.Equal(6, bz.InchMeter, 6);
            Assert.Equal(16, s.Grand.InchDia, 6);
            Assert.Equal(22, s.Grand.InchMeter, 6);
            Assert.Single(s.ByScope);
            Assert.Equal("SITE-1", s.ByScope[0].Key);
            Assert.Equal(3, s.BySize.Count(x => x.LengthM > 0));
        }

        [Fact]
        public void Hidden_items_are_skipped_and_stray_items_ignored()
        {
            var b = Bran("/P-1/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("100", "1000"));
            var hidden = Pipe("/P-2", Bran("/P-2/B1").Add(Tube("100", "1000")));
            hidden.Hidden = true;
            var s = Run(Zone("/Z", Pipe("/P-1", b), hidden, new F("/STRU-1", "TYPE", "STRU")));
            Assert.Single(s.Lines);
        }

        [Fact]
        public void Spec_from_pipe_or_component_spref()
        {
            var b = Bran("/P-1/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("100", "1000"), C("ELBO", "BWD", "BWD", "100", "SPRE", "/A1A/ELBOW/100"), Tube("100", "1000"));
            Assert.Equal("A1A", Run(Zone("/Z", Pipe("/P-1", b))).Lines[0].Spec);
            var p = new F("/P-2", "TYPE", "PIPE", "PSPE", "/B2B").Add(Bran("/P-2/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("100", "1000")));
            Assert.Equal("B2B", Run(Zone("/Z", p)).Lines[0].Spec);
        }
    }

    public class GeometryTests
    {
        [Fact]
        public void Straight_and_diagonal_tube_lengths()
        {
            Assert.Equal(1000, TubeGeometry.LengthFromBox(new double[] { 1000, 114.3, 114.3 }, 114.3), 1);
            double L = 1000, od = 114.3, c = Math.Sqrt(0.5);
            double e = L * c + od * c;
            Assert.Equal(1000, TubeGeometry.LengthFromBox(new[] { e, e, od }, od), 0);
            // fully skewed (1,1,1)/√3
            double u = 1 / Math.Sqrt(3), s = Math.Sqrt(1 - u * u);
            double e3 = L * u + od * s;
            Assert.Equal(1000, TubeGeometry.LengthFromBox(new[] { e3, e3, e3 }, od), 0);
        }

        [Fact]
        public void Short_tube_length_is_small_extent()
        {
            var b = Bran("/P-1/B1", "HCON", "OPEN", "TCON", "OPEN").Add(new F("TUBE 1 of BRANCH /P-1/B1").WithBox(60, 114.3, 114.3));
            var l = Run(Zone("/Z", Pipe("/P-1", b))).Lines[0];
            Assert.Equal(0.06, l.LengthM, 3);
            Assert.Equal(0.24, l.InchMeter, 3);
        }
    }

    public class ReportTests
    {
        [Fact]
        public void Csv_escapes()
        {
            Assert.Equal("\"4\"\"\"", Report.Csv("4\""));
            Assert.Equal("\"a,b\"", Report.Csv("a,b"));
            Assert.Equal("'=1+1", Report.Csv("=1+1"));
        }

        [Fact]
        public void Excel_and_csv_are_written()
        {
            var b = Bran("/P-1/B1", "HCON", "OPEN", "TCON", "OPEN").Add(Tube("100", "1000"), C("ELBO", "BWD", "BWD", "100"), Tube("100", "1000"));
            var s = Run(Zone("/Z", Pipe("/P-1", b)));
            var tables = Report.Build(s, Rules.Default(), "test.nwd", "Whole model", new DateTime(2026, 11, 3));
            Assert.Equal(new[] { "Summary", "Lines", "Area Totals", "Hierarchy Totals", "By Size", "Joints", "Rules" }, tables.Select(t => t.SheetName).ToArray());
            string dir = Path.Combine(Path.GetTempPath(), "diainch-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string x = Path.Combine(dir, "r.xlsx");
            XlsxWriter.Save(x, tables, Names.Author);
            using (var z = System.IO.Compression.ZipFile.OpenRead(x))
                Assert.Contains(z.Entries, e => e.FullName.StartsWith("xl/worksheets/sheet"));
            string c = Path.Combine(dir, "l.csv");
            Report.SaveCsv(c, tables[1]);
            var lines = File.ReadAllLines(c);
            Assert.StartsWith("\uFEFFLine No,Area,Size", lines[0].StartsWith("\uFEFF") ? lines[0] : "\uFEFF" + lines[0]);
            Assert.Contains("P-1,Z,\"4\"\"\"", lines[1]);
            Directory.Delete(dir, true);
        }
    }

    public class AreaScopeTests
    {
        [Fact]
        public void Serialize_roundtrip_and_nested_removed()
        {
            var st = new AreaState { Limit = true };
            st.Parents.Add(new AreaRef { Path = { "m.nwd", "SITE", "ZONE-A" }, Guid = "g1" });
            st.Parents.Add(new AreaRef { Path = { "m.nwd", "SITE", "ZONE-A", "P-1" } });
            var all = new System.Collections.Generic.Dictionary<string, AreaState> { { "m.nwd", st } };
            var back = AreaScope.Deserialize(AreaScope.Serialize(all));
            Assert.True(back["M.NWD"].Limit);
            Assert.Equal(2, back["m.nwd"].Parents.Count);
            Assert.Single(AreaScope.RemoveNested(back["m.nwd"].Parents));
        }

        [Fact]
        public void Resolve_by_path()
        {
            var root = new F("m.nwd").Add(new F("SITE").Add(new F("ZONE-A"), new F("ZONE-B")));
            var hit = AreaScope.Resolve(new[] { root }, n => n.Kids, n => n.Name, n => "", new AreaRef { Path = { "m.nwd", "SITE", "ZONE-B" } });
            Assert.Equal("ZONE-B", hit.Name);
        }
    }
}
