using System.Collections.Generic;
using System.Linq;
using DiaInch.Core;

namespace DiaInch.Tests
{
    public sealed class F
    {
        public string Name;
        public bool Hidden;
        public PropBag Props = new PropBag();
        public double[] Box;
        public List<F> Kids = new List<F>();
        public F(string name, params string[] kv)
        {
            Name = name;
            for (int i = 0; i + 1 < kv.Length; i += 2) Props.Add("AVEVA", kv[i], kv[i + 1]);
        }
        public F Add(params F[] k) { Kids.AddRange(k); return this; }
        public F WithBox(double x, double y, double z) { Box = new[] { 0, 0, 0, x, y, z }; return this; }
    }

    public sealed class FTree : IDiaTree<F>
    {
        public IEnumerable<F> Children(F n) { return n.Kids; }
        public string Name(F n) { return n.Name; }
        public bool Hidden(F n) { return n.Hidden; }
        public PropBag Props(F n) { return n.Props; }
        public double[] BoxMm(F n) { return n.Box; }
    }

    public static class H
    {
        public static ScanResult Run(F root, Rules r = null, string scope = "")
        {
            return Scan.Run(new FTree(), new[] { new ScanRoot<F>(root, scope) }, r ?? Rules.Default(), true, null, null);
        }

        public static F Tube(string mm, string len) { return new F("TUBE", "TYPE", "TUBE", "LBOR", mm, "LTLE", len); }
        public static F C(string type, string acon, string lcon, string bore, params string[] more)
        {
            var d = new Dictionary<string, string> { { "TYPE", type }, { "ACON", acon }, { "LCON", lcon }, { "ABOR", bore }, { "LBOR", bore } };
            for (int i = 0; i + 1 < more.Length; i += 2) d[more[i]] = more[i + 1];
            return new F(type + " 1", d.SelectMany(x => new[] { x.Key, x.Value }).ToArray());
        }
        public static F Bran(string name, params string[] kv)
        {
            var l = new List<string> { "TYPE", "BRAN" };
            l.AddRange(kv);
            return new F(name, l.ToArray());
        }
        public static F Pipe(string name, params F[] br) { return new F(name, "TYPE", "PIPE").Add(br); }
        public static F Zone(string name, params F[] p) { return new F(name, "TYPE", "ZONE").Add(p); }
    }
}
