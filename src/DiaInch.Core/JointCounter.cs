// MD Dia-Inch - joint counting along one PDMS/E3D branch
// Author: MD (Mayank Dholariya)
//
// A branch is HEAD -> ordered members (components with TUBE between them) -> TAIL.
// One joint per interface:
//   component end next to a TUBE ............. 1 joint (kind = that end's connection type)
//   component end touching a component ....... 1 joint for the pair (FL+FL = flange joint, weld wins otherwise)
//   TEE/CROS/OLET outlet (P3/P4) .............. 1 joint each; OLET also +1 header weld (extraJoints)
//   WELD element ............................. 1 joint, neighbours do not add another
//   HEAD/TAIL ................................ counted unless HREF/TREF is an outlet fitting (TEE/OLET...) whose
//                                               outlet already counts it, or another branch (counted at its TAIL only)
// GASK/BOLT/ATTA are skipped. Unknown properties are never invented: defaults from the rules are used,
// marked as assumed, and the line is flagged CHECK.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiaInch.Core
{
    public sealed class JointCounter
    {
        private readonly Rules _r;
        public JointCounter(Rules rules) { _r = rules ?? Rules.Default(); }

        private sealed class End
        {
            public JointKind Kind;
            public double? Inch;
            public bool Assumed;
            public string Label = "";
            public string RawCode;
            public string DefaultCode;
            public bool HasProp;
        }

        private sealed class Node
        {
            public Member M;
            public string Type;
            public TypeRule Rule;
            public bool IsTube, IsWeld;
            public double? TubeInch;
            public bool TubeInchGeom;
            public double? TubeLenMm;
            public bool TubeLenGeom;
            public double OdEstMm;
            public End P1 = new End(), P2 = new End();
            public List<End> Outlets = new List<End>();
            public bool? Field;
        }

        public BranchResult Count(BranchInput b)
        {
            var res = new BranchResult { Input = b };
            var seq = new List<Node>();
            int unknown = 0;
            foreach (var m in b.Members)
            {
                string t = (m.Type ?? "").ToUpperInvariant();
                if (t == "TUBE") { seq.Add(new Node { M = m, Type = t, IsTube = true }); continue; }
                if (!_r.IsComponentType(t)) { unknown++; continue; }
                var rule = _r.RuleFor(t) ?? new TypeRule();
                if (rule.Ignore) continue;
                seq.Add(new Node { M = m, Type = t, Rule = rule, IsWeld = rule.Joint });
            }
            if (unknown > 0) res.Notes.Add(unknown + " item(s) of unknown type skipped");
            if (seq.Count == 0) { res.Notes.Add("no components or tubes found"); res.Check = true; return res; }

            double? headBore = Bore(b.Props.Get(_r, "headBore"), res);
            double? tailBore = Bore(b.Props.Get(_r, "tailBore"), res);

            // ---- pass 1: own properties ----
            foreach (var n in seq)
            {
                var p = n.M.Props;
                n.Field = FieldFlag(p, res);
                if (n.IsTube)
                {
                    n.TubeInch = Bore(p.Get(_r, "p1Bore"), res) ?? Bore(p.Get(_r, "p2Bore"), res) ?? Bore(p.Get(_r, "bore"), res);
                    double? len = Fmt.LengthMm(p.Get(_r, "tubeLength"), 1);
                    if (len.HasValue && len.Value > 0) n.TubeLenMm = len;
                    continue;
                }
                double? gen = Bore(p.Get(_r, "bore"), res);
                n.P1.Inch = Bore(p.Get(_r, "p1Bore"), res) ?? gen;
                n.P2.Inch = Bore(p.Get(_r, "p2Bore"), res) ?? gen;
                n.P1.RawCode = p.Get(_r, "p1Conn"); n.P1.Label = "P1";
                n.P2.RawCode = p.Get(_r, "p2Conn"); n.P2.Label = "P2";
                for (int k = 0; k < n.Rule.Outlets.Count; k++)
                {
                    string pk = "p" + (k + 3);
                    var e = new End { Label = "P" + (k + 3), RawCode = p.Get(_r, pk + "Conn"), Inch = Bore(p.Get(_r, pk + "Bore"), res) };
                    n.Outlets.Add(e);
                }
            }

            // ---- tube geometry (length, NB from OD) ----
            foreach (var n in seq.Where(x => x.IsTube)) Geometry(n, res);

            // ---- bore fallbacks through neighbours (never invented: only copied from what touches it) ----
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < seq.Count; i++)
                {
                    var n = seq[i];
                    Node prev = i > 0 ? seq[i - 1] : null, next = i + 1 < seq.Count ? seq[i + 1] : null;
                    if (n.IsTube)
                    {
                        if (!n.TubeInch.HasValue)
                            n.TubeInch = (prev != null && !prev.IsTube ? prev.P2.Inch : null)
                                         ?? (next != null && !next.IsTube ? next.P1.Inch : null)
                                         ?? (prev == null ? headBore : null) ?? (next == null ? tailBore : null);
                        continue;
                    }
                    if (!n.P1.Inch.HasValue)
                        n.P1.Inch = (prev != null && prev.IsTube ? prev.TubeInch : null) ?? (prev == null ? headBore : null) ?? n.P2.Inch;
                    if (!n.P2.Inch.HasValue)
                        n.P2.Inch = (next != null && next.IsTube ? next.TubeInch : null) ?? (next == null ? tailBore : null) ?? n.P1.Inch;
                    if (pass == 1)
                        foreach (var o in n.Outlets)
                            if (!o.Inch.HasValue && n.P1.Inch.HasValue)
                            {
                                o.Inch = n.P1.Inch; o.Assumed = true;
                                res.Notes.Add(n.Type + " outlet bore not found, header bore used");
                            }
                }
            }

            // ---- connection kinds ----
            for (int i = 0; i < seq.Count; i++)
            {
                var n = seq[i];
                if (n.IsTube) continue;
                n.P1.DefaultCode = n.Rule.Ends.Count > 0 ? n.Rule.Ends[0] : "NONE";
                n.P2.DefaultCode = n.Rule.Ends.Count > 1 ? n.Rule.Ends[1] : "NONE";
                for (int k = 0; k < n.Outlets.Count; k++) n.Outlets[k].DefaultCode = n.Rule.Outlets[k];
                n.P1.HasProp = !string.IsNullOrWhiteSpace(n.P1.RawCode);
                n.P2.HasProp = !string.IsNullOrWhiteSpace(n.P2.RawCode);
            }
            OrientAsymmetric(seq, b, res);
            foreach (var n in seq.Where(x => !x.IsTube))
            {
                Resolve(n.P1, n, res);
                Resolve(n.P2, n, res);
                foreach (var o in n.Outlets) Resolve(o, n, res);
            }

            // ---- walk ----
            string headRef = b.Props.Get(_r, "headRef"), tailRef = b.Props.Get(_r, "tailRef");
            string headConn = b.Props.Get(_r, "headConn"), tailConn = b.Props.Get(_r, "tailConn");
            Node prevN = null;
            End pending = null; Node pendingNode = null;
            for (int i = 0; i < seq.Count; i++)
            {
                var n = seq[i];
                if (n.IsTube)
                {
                    if (prevN == null) HeadTailTube(true, headConn, headRef, headBore ?? n.TubeInch, n, b, res);
                    else if (pending != null) Emit(res, b, pending, pending.Inch ?? n.TubeInch, pendingNode, null, pendingNode.Type + " " + pending.Label + " / TUBE");
                    pending = null; pendingNode = null;
                    TubeTotals(n, res);
                    prevN = n;
                    continue;
                }
                if (n.IsWeld)
                {
                    var w = new End { Kind = n.P1.Kind, Inch = n.P1.Inch ?? n.P2.Inch, Assumed = n.P1.Assumed, Label = "" };
                    if (w.Kind == JointKind.None || w.Kind == JointKind.Other) w.Kind = WeldKind(w.Inch);
                    Emit(res, b, w, w.Inch, n, null, "WELD element");
                    pending = null; pendingNode = null;
                    prevN = n;
                    continue;
                }
                // component arrive end
                if (prevN == null)
                {
                    string skip = RefSkips(headRef, true, res);
                    if (skip == null) Emit(res, b, n.P1, n.P1.Inch ?? headBore, n, null, "HEAD / " + n.Type + " P1");
                }
                else if (prevN.IsTube)
                    Emit(res, b, n.P1, n.P1.Inch ?? prevN.TubeInch, n, null, "TUBE / " + n.Type + " P1");
                else if (!prevN.IsWeld && pending != null)
                    EmitMerged(res, b, pending, pendingNode, n.P1, n);

                foreach (var o in n.Outlets)
                    Emit(res, b, o, o.Inch, n, null, n.Type + " " + o.Label + " (outlet)");
                for (int x = 0; x < n.Rule.Extra; x++)
                {
                    var o0 = n.Outlets.FirstOrDefault();
                    var e = new End { Kind = JointKind.BW, Inch = o0 != null ? o0.Inch : n.P1.Inch, Assumed = o0 != null && o0.Assumed };
                    Emit(res, b, e, e.Inch, n, null, n.Type + " to header weld");
                }
                pending = n.P2; pendingNode = n;
                prevN = n;
            }
            if (prevN != null && !prevN.IsTube && !prevN.IsWeld && pending != null)
            {
                string skip = RefSkips(tailRef, false, res);
                if (skip == null) Emit(res, b, pending, pending.Inch ?? tailBore, pendingNode, null, pendingNode.Type + " P2 / TAIL");
            }
            else if (prevN != null && prevN.IsTube)
                HeadTailTube(false, tailConn, tailRef, tailBore ?? prevN.TubeInch, prevN, b, res);

            // ---- result flags ----
            foreach (var j in res.Joints)
            {
                if (j.Inch > 0) { res.Sizes.Add(j.Inch); res.MaxInch = Math.Max(res.MaxInch, j.Inch); }
                if (j.BoreMissing) res.Check = true;
                if (j.Assumed && _r.CheckWhenConnAssumed) res.Check = true;
                if (j.Field.HasValue) res.HasShopField = true;
            }
            if (headBore.HasValue) res.MaxInch = Math.Max(res.MaxInch, headBore.Value);
            if (res.Joints.Any(j => j.BoreMissing)) res.Notes.Add("bore missing on " + res.Joints.Count(j => j.BoreMissing) + " joint(s), counted with 0 inch");
            if (res.Joints.Any(j => j.Assumed)) res.Notes.Add("connection type assumed from rules on " + res.Joints.Count(j => j.Assumed) + " joint(s)");
            if (unknown > 0) res.Check = true;
            return res;
        }

        // ---------------- helpers ----------------

        private double? Bore(string text, BranchResult res)
        {
            var v = Nb.Parse(text, _r);
            if (!v.HasValue) return null;
            if (v.Value.OffTable) { res.Notes.Add("bore '" + text.Trim() + "' not on NB table, converted " + Nb.Format(v.Value.Inch)); res.Check = true; }
            return v.Value.Inch;
        }

        private bool? FieldFlag(PropBag p, BranchResult res)
        {
            string fw = p.Get(_r, "fieldWeld");
            bool? b = Bool(fw);
            if (b.HasValue) return b.Value;
            if (_r.FieldWhenShopFalse)
            {
                bool? s = Bool(p.Get(_r, "shop"));
                if (s.HasValue) return !s.Value;
            }
            return null;
        }

        private static bool? Bool(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            string s = v.Trim().ToUpperInvariant();
            if (s == "TRUE" || s == "YES" || s == "Y" || s == "1" || s == "T" || s == "FIELD") return true;
            if (s == "FALSE" || s == "NO" || s == "N" || s == "0" || s == "F" || s == "SHOP") return false;
            return null;
        }

        private JointKind WeldKind(double? inch)
        {
            return IsSmall(inch) ? JointKind.SW : JointKind.BW;
        }

        private bool IsSmall(double? inch)
        {
            return inch.HasValue && inch.Value > 0 && Nb.ToMm(inch.Value, _r) <= _r.SmallBoreMaxNbMm + 1e-6;
        }

        private JointKind FromCode(string code, double? inch, out bool needsBore)
        {
            needsBore = false;
            string c = (code ?? "NONE").Trim().TrimStart('=').ToUpperInvariant();
            switch (c)
            {
                case "WELD": needsBore = !inch.HasValue; return IsSmall(inch) ? JointKind.SW : JointKind.BW;
                case "AUTO_FL": needsBore = !inch.HasValue; return IsSmall(inch) ? JointKind.SW : JointKind.FL;
                case "BW": return JointKind.BW;
                case "SW": return JointKind.SW;
                case "SCR": return JointKind.SCR;
                case "FL": return JointKind.FL;
                default: return JointKind.None;
            }
        }

        private void Resolve(End e, Node n, BranchResult res)
        {
            bool forced = (e.DefaultCode ?? "").StartsWith("=", StringComparison.Ordinal);
            if (!forced && !string.IsNullOrWhiteSpace(e.RawCode))
            {
                var k = _r.ClassifyConn(e.RawCode);
                if (k.HasValue)
                {
                    e.Kind = k.Value;
                    if (k.Value == JointKind.Other) res.Notes.Add("unknown connection code '" + e.RawCode.Trim() + "' (" + n.Type + "), not counted");
                    return;
                }
            }
            bool nb;
            e.Kind = FromCode(e.DefaultCode, e.Inch, out nb);
            e.Assumed = !forced && e.Kind != JointKind.None;
        }

        /// <summary>For FLAN-like defaults (WELD one side, FL the other) with no connection properties,
        /// turn the weld end towards a tube / welded neighbour and the flange face towards a flanged neighbour.</summary>
        private void OrientAsymmetric(List<Node> seq, BranchInput b, BranchResult res)
        {
            for (int i = 0; i < seq.Count; i++)
            {
                var n = seq[i];
                if (n.IsTube || n.IsWeld || n.P1.HasProp || n.P2.HasProp) continue;
                string a = Clean(n.P1.DefaultCode), z = Clean(n.P2.DefaultCode);
                if (a == z || n.P1.DefaultCode.StartsWith("=") || n.P2.DefaultCode.StartsWith("=")) continue;
                string before = Facing(i > 0 ? seq[i - 1] : null, false, b), after = Facing(i + 1 < seq.Count ? seq[i + 1] : null, true, b);
                int keep = Score(a, before) + Score(z, after), swap = Score(z, before) + Score(a, after);
                if (swap > keep)
                {
                    string t = n.P1.DefaultCode; n.P1.DefaultCode = n.P2.DefaultCode; n.P2.DefaultCode = t;
                }
            }
        }

        private static string Clean(string c) { return (c ?? "NONE").TrimStart('=').ToUpperInvariant(); }

        private static int Score(string mine, string facing)
        {
            if (facing == null) return 0;
            bool weld = mine == "WELD" || mine == "BW" || mine == "SW";
            if (facing == "WELD" && weld) return 2;
            if (facing == "FL" && mine == "FL") return 2;
            if (facing == "NONE" && mine == "NONE") return 1;
            return 0;
        }

        /// <summary>What the neighbour presents towards this node: WELD (tube, weld element, welded end), FL, NONE or null (unknown).</summary>
        private string Facing(Node nb, bool nbIsAfter, BranchInput b)
        {
            if (nb == null)
            {
                var k = _r.ClassifyConn(b.Props.Get(_r, nbIsAfter ? "tailConn" : "headConn"));
                if (!k.HasValue) return nbIsAfter ? null : "FL"; // unknown head: component at a nozzle is usually a flange
                return k.Value == JointKind.FL ? "FL" : (k.Value == JointKind.BW || k.Value == JointKind.SW) ? "WELD" : k.Value == JointKind.None ? "NONE" : null;
            }
            if (nb.IsTube || nb.IsWeld) return "WELD";
            var e = nbIsAfter ? nb.P1 : nb.P2;
            if (e.HasProp)
            {
                var k = _r.ClassifyConn(e.RawCode);
                if (!k.HasValue) return null;
                return k.Value == JointKind.FL ? "FL" : (k.Value == JointKind.BW || k.Value == JointKind.SW) ? "WELD" : null;
            }
            string a = Clean(nb.P1.DefaultCode), z = Clean(nb.P2.DefaultCode);
            if (a != z) return null; // the other one is asymmetric too, undecided
            if (a == "AUTO_FL") return IsSmall(e.Inch) ? "WELD" : "FL";
            if (a == "WELD" || a == "BW" || a == "SW") return "WELD";
            if (a == "FL") return "FL";
            return null;
        }

        private static readonly Regex BranchName = new Regex(@"/B\d+\s*$", RegexOptions.IgnoreCase);

        /// <summary>Null = count the head/tail joint. Otherwise the reason it is not counted here.</summary>
        private string RefSkips(string refText, bool head, BranchResult res)
        {
            if (string.IsNullOrWhiteSpace(refText))
            {
                res.Notes.Add((head ? "HREF" : "TREF") + " not in model, " + (head ? "head" : "tail") + " joint counted");
                return null;
            }
            string t = _r.TypeFromWord(refText);
            if (t.Length > 0 && Rules.Is(_r.BranchOutletTypes, t)) return "outlet";
            if (head && (t == "BRAN" || BranchName.IsMatch(refText.Trim()))) return "branch"; // counted at the other branch's tail
            return null;
        }

        private void HeadTailTube(bool head, string conn, string refText, double? inch, Node tube, BranchInput b, BranchResult res)
        {
            var k = _r.ClassifyConn(conn);
            if (!k.HasValue)
            {
                res.Notes.Add((head ? "head" : "tail") + " on tube with no " + (head ? "HCONN" : "TCONN") + ", not counted");
                return;
            }
            if (k.Value == JointKind.None || k.Value == JointKind.Other)
            {
                if (k.Value == JointKind.Other) res.Notes.Add("unknown connection code '" + conn.Trim() + "' at " + (head ? "head" : "tail"));
                return;
            }
            if (RefSkips(refText, head, res) != null) return;
            var e = new End { Kind = k.Value, Inch = inch };
            Emit(res, b, e, inch, tube, null, (head ? "HEAD" : "TAIL") + " / TUBE (" + conn.Trim() + ")");
        }

        private void EmitMerged(BranchResult res, BranchInput b, End a, Node an, End z, Node zn)
        {
            JointKind k = Merge(a.Kind, z.Kind);
            if (k == JointKind.None) return;
            var e = new End
            {
                Kind = k,
                Inch = Max(a.Inch, z.Inch),
                Assumed = (a.Assumed && a.Kind == k) || (z.Assumed && z.Kind == k) || (a.Assumed && z.Assumed)
            };
            if (a.Kind != z.Kind && a.Kind != JointKind.None && z.Kind != JointKind.None)
                res.Notes.Add("mixed connection " + a.Kind + "/" + z.Kind + " between " + an.Type + " and " + zn.Type + ", counted as " + k);
            Emit(res, b, e, e.Inch, an, zn, an.Type + " " + a.Label + " / " + zn.Type + " " + z.Label);
        }

        public static JointKind Merge(JointKind a, JointKind z)
        {
            if (a == JointKind.Other) a = JointKind.None;
            if (z == JointKind.Other) z = JointKind.None;
            if (a == JointKind.None || z == JointKind.None)
                return (a == JointKind.FL || z == JointKind.FL) ? JointKind.FL : JointKind.None;
            if (a == z) return a;
            if (a == JointKind.BW || z == JointKind.BW) return JointKind.BW;
            if (a == JointKind.SW || z == JointKind.SW) return JointKind.SW;
            if (a == JointKind.SCR || z == JointKind.SCR) return JointKind.SCR;
            return JointKind.FL;
        }

        private static double? Max(double? a, double? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return Math.Max(a.Value, b.Value);
        }

        private void Emit(BranchResult res, BranchInput b, End e, double? inch, Node n1, Node n2, string where)
        {
            if (e.Kind == JointKind.None || e.Kind == JointKind.Other) return;
            bool? field = null;
            foreach (var n in new[] { n1, n2 })
                if (n != null && n.Field.HasValue) field = (field ?? false) || n.Field.Value;
            res.Joints.Add(new Joint
            {
                LineNo = b.LineNo,
                Branch = b.Name,
                Kind = e.Kind,
                Inch = inch ?? 0,
                BoreMissing = !inch.HasValue,
                Field = field,
                Assumed = e.Assumed,
                Where = where
            });
        }

        private void TubeTotals(Node n, BranchResult res)
        {
            if (!n.TubeLenMm.HasValue) { res.Notes.Add("tube length not found"); res.Check = true; return; }
            double m = n.TubeLenMm.Value / 1000.0;
            res.LengthM += m;
            if (n.TubeInch.HasValue)
            {
                res.InchMeter += n.TubeInch.Value * m;
                double cur; res.LengthBySize.TryGetValue(n.TubeInch.Value, out cur);
                res.LengthBySize[n.TubeInch.Value] = cur + m;
                res.Sizes.Add(n.TubeInch.Value);
                res.MaxInch = Math.Max(res.MaxInch, n.TubeInch.Value);
            }
            else { res.Notes.Add("tube NB not found, inch-meter excludes it"); res.Check = true; }
            if (_r.RandomPipeLengthM > 0 && m > _r.RandomPipeLengthM)
            {
                int extra = (int)Math.Ceiling(m / _r.RandomPipeLengthM - 1e-9) - 1;
                for (int k = 0; k < extra; k++)
                    res.Joints.Add(new Joint
                    {
                        LineNo = res.Input.LineNo, Branch = res.Input.Name, Kind = WeldKind(n.TubeInch),
                        Inch = n.TubeInch ?? 0, BoreMissing = !n.TubeInch.HasValue, Field = n.Field,
                        Where = "pipe-to-pipe (random length " + Fmt.N(_r.RandomPipeLengthM, 2) + " m)"
                    });
            }
        }

        private void Geometry(Node n, BranchResult res)
        {
            var box = n.M.BoxMm;
            if (box == null || box.Length < 6)
            {
                if (!n.TubeLenMm.HasValue) res.Notes.Add("tube has no length property or geometry");
                return;
            }
            double[] d = { Math.Abs(box[3] - box[0]), Math.Abs(box[4] - box[1]), Math.Abs(box[5] - box[2]) };
            var srt = d.OrderBy(x => x).ToArray();
            // short tube (length below OD): the two large extents are the OD, the small one is the length
            bool shortTube = Near(srt[1], srt[2]) && !Near(srt[0], srt[1]);
            double od;
            if (n.TubeInch.HasValue) od = Nb.OdMm(n.TubeInch.Value, _r);
            else
            {
                var s = d.OrderBy(x => x).ToArray();
                if (Near(s[0], s[1])) od = (s[0] + s[1]) / 2;
                else if (Near(s[1], s[2])) od = (s[1] + s[2]) / 2;
                else od = s[0];
                n.OdEstMm = od;
                if (_r.NbFromGeometry)
                {
                    var g = Nb.FromOd(od, _r);
                    if (g.HasValue) { n.TubeInch = g; n.TubeInchGeom = true; res.Notes.Add("tube NB from outside diameter"); }
                }
            }
            if (!n.TubeLenMm.HasValue)
            {
                double len = shortTube ? srt[0] : TubeGeometry.LengthFromBox(d, od);
                if (len > 0) { n.TubeLenMm = len; n.TubeLenGeom = true; res.Notes.Add("length from geometry"); }
            }
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) <= 0.04 * Math.Max(a, b) + 0.5; }
    }

    public static class TubeGeometry
    {
        /// <summary>Centre-line length of a straight cylinder from its axis-aligned box extents and outside diameter.
        /// Box extent along axis k = L·|u_k| + OD·sqrt(1-u_k²). Solved by fixed-point iteration.</summary>
        public static double LengthFromBox(double[] extents, double od)
        {
            if (extents == null || extents.Length < 3) return 0;
            double[] dp = { extents[0], extents[1], extents[2] };
            for (int it = 0; it < 50; it++)
            {
                double len = Math.Sqrt(dp[0] * dp[0] + dp[1] * dp[1] + dp[2] * dp[2]);
                if (len < 1e-9) return 0;
                double[] nd = new double[3];
                double change = 0;
                for (int k = 0; k < 3; k++)
                {
                    double u = dp[k] / len;
                    nd[k] = Math.Max(0, extents[k] - od * Math.Sqrt(Math.Max(0, 1 - u * u)));
                    change += Math.Abs(nd[k] - dp[k]);
                }
                dp = nd;
                if (change < 1e-6) break;
            }
            return Math.Sqrt(dp[0] * dp[0] + dp[1] * dp[1] + dp[2] * dp[2]);
        }
    }
}
