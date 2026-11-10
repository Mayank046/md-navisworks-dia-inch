// MD Dia-Inch - finds PDMS/E3D branches in a model tree and builds BranchInput objects
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiaInch.Core
{
    /// <summary>Thin view of the Navisworks tree so the walker can be unit tested.</summary>
    public interface IDiaTree<T>
    {
        IEnumerable<T> Children(T node);
        string Name(T node);
        bool Hidden(T node);
        PropBag Props(T node);
        /// <summary>minX,minY,minZ,maxX,maxY,maxZ in mm, or null.</summary>
        double[] BoxMm(T node);
    }

    public sealed class ScanRoot<T>
    {
        public T Node;
        public string Scope = "";
        public ScanRoot(T node, string scope) { Node = node; Scope = scope ?? ""; }
    }

    public sealed class TreeWalk<T>
    {
        private readonly IDiaTree<T> _t;
        private readonly Rules _r;
        private readonly Dictionary<T, string> _type;
        private readonly Dictionary<T, PropBag> _props;
        private static readonly Regex RvmName = new Regex(@"^\s*[A-Za-z]+\s+\d+\s+of\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BranchSuffix = new Regex(@"/B\d+\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RvmOwner = new Regex(@"\bof\s+[A-Za-z]+\s+(\S.*?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private Regex _lineRx;

        public bool SkipHidden = true;
        public Func<bool> Cancelled = () => false;
        public Action<int, string> Progress = (n, s) => { };
        public int Visited { get; private set; }

        public TreeWalk(IDiaTree<T> tree, Rules rules, IEqualityComparer<T> cmp = null)
        {
            _t = tree; _r = rules;
            _type = new Dictionary<T, string>(cmp ?? EqualityComparer<T>.Default);
            _props = new Dictionary<T, PropBag>(cmp ?? EqualityComparer<T>.Default);
            if (!string.IsNullOrWhiteSpace(rules.LineRegex))
            {
                try { _lineRx = new Regex(rules.LineRegex, RegexOptions.IgnoreCase); } catch (ArgumentException) { _lineRx = null; }
            }
        }

        public List<BranchInput> Collect(IEnumerable<ScanRoot<T>> roots)
        {
            var list = new List<BranchInput>();
            foreach (var root in roots)
            {
                var chain = new List<T>();
                Visit(root.Node, chain, root.Scope, list);
                if (Cancelled()) break;
            }
            return list;
        }

        private PropBag P(T n)
        {
            PropBag p;
            if (!_props.TryGetValue(n, out p)) { p = _t.Props(n) ?? new PropBag(); _props[n] = p; }
            return p;
        }

        public string TypeOf(T n)
        {
            string t;
            if (_type.TryGetValue(n, out t)) return t;
            string name = _t.Name(n) ?? "";
            t = "";
            if (RvmName.IsMatch(name)) t = _r.TypeFromWord(name); // "ELBOW 1 of BRANCH ..." - no need to read properties
            if (t.Length == 0)
            {
                string tp = P(n).Get(_r, "type");
                if (!string.IsNullOrEmpty(tp)) t = _r.TypeFromWord(tp);
            }
            if (t.Length == 0) t = _r.TypeFromWord(name);
            _type[n] = t;
            return t;
        }

        private bool IsStructure(string t) { return t == "SITE" || t == "ZONE" || t == "PIPE"; }

        private bool LooksLikeBranch(T n)
        {
            int comps = 0, tubes = 0, k = 0;
            foreach (T c in _t.Children(n))
            {
                if (++k > 60) break;
                string t = TypeOf(c);
                if (t == "TUBE") tubes++;
                else if (_r.IsComponentType(t)) comps++;
                else if (t == "BRAN" || IsStructure(t)) return false;
            }
            return tubes >= 1 || comps >= 2;
        }

        private void Visit(T n, List<T> chain, string scope, List<BranchInput> outList)
        {
            if (Cancelled()) return;
            if (SkipHidden && _t.Hidden(n)) return;
            Visited++;
            if ((Visited & 1023) == 0) Progress(Visited, _t.Name(n));
            string t = TypeOf(n);
            if (t == "BRAN" || (!IsStructure(t) && t != "TUBE" && !_r.IsComponentType(t) && LooksLikeBranch(n)))
            {
                outList.Add(Build(n, chain, scope));
                return;
            }
            if (t == "TUBE" || _r.IsComponentType(t)) return; // stray component outside a branch
            chain.Add(n);
            foreach (T c in _t.Children(n)) Visit(c, chain, scope, outList);
            chain.RemoveAt(chain.Count - 1);
        }

        private BranchInput Build(T br, List<T> chain, string scope)
        {
            var b = new BranchInput { Name = Clean(_t.Name(br)), Props = P(br), Item = br, Scope = scope };
            foreach (T c in _t.Children(br))
            {
                if (SkipHidden && _t.Hidden(c)) continue;
                b.Members.Add(new Member { Name = _t.Name(c) ?? "", Type = TypeOf(c), Props = P(c), BoxMm = _t.BoxMm(c), Item = c });
            }
            // line = the PIPE above the branch
            T line = br; string lineName = null; bool lineIsParent = false;
            var own = RvmOwner.Match(b.Name); // "BRANCH 1 of PIPE /P-3" (unnamed branch in an RVM export)
            string owner = own.Success ? own.Groups[1].Value : null;
            if (chain.Count > 0)
            {
                T par = chain[chain.Count - 1];
                string pt = TypeOf(par), pn = Clean(_t.Name(par));
                if (pt == "PIPE" || (pt.Length == 0 && pn.Length > 0 &&
                    (b.Name.StartsWith(pn, StringComparison.OrdinalIgnoreCase) || string.Equals(owner, pn, StringComparison.OrdinalIgnoreCase))))
                { line = par; lineName = pn; lineIsParent = true; }
            }
            if (lineName == null) lineName = owner ?? BranchSuffix.Replace(b.Name, "");
            b.LineItem = line;
            string ln = P(line).Get(_r, "lineNo") ?? b.Props.Get(_r, "lineNo");
            if (string.IsNullOrWhiteSpace(ln) && _lineRx != null)
            {
                var m = _lineRx.Match(lineName);
                if (m.Success) ln = m.Value;
            }
            if (string.IsNullOrWhiteSpace(ln)) ln = lineName;
            b.LineNo = ln.Trim().TrimStart('/');
            if (b.LineNo.Length == 0) b.LineNo = "(unnamed)";

            // area
            string area = P(line).Get(_r, "area");
            if (string.IsNullOrWhiteSpace(area))
            {
                string mode = (_r.AreaMode ?? "zone").ToLowerInvariant();
                int lineIdx = lineIsParent ? chain.Count - 1 : chain.Count; // chain[lineIdx - 1] = node above the line
                if (mode == "scope") area = scope;
                else if (mode == "parent") area = lineIdx > 0 ? Clean(_t.Name(chain[lineIdx - 1])) : scope;
                else
                {
                    string want = mode == "site" ? "SITE" : "ZONE";
                    for (int i = chain.Count - 1; i >= 0 && string.IsNullOrEmpty(area); i--)
                        if (TypeOf(chain[i]) == want) area = Clean(_t.Name(chain[i]));
                    if (string.IsNullOrEmpty(area)) area = lineIdx > 0 ? Clean(_t.Name(chain[lineIdx - 1])) : scope;
                }
            }
            b.Area = (area ?? "").Trim().TrimStart('/');

            // spec
            string spec = P(line).Get(_r, "spec") ?? b.Props.Get(_r, "spec");
            if (string.IsNullOrWhiteSpace(spec))
                foreach (var m in b.Members)
                {
                    string s = m.Props.Get(_r, "compSpec");
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var parts = s.Trim().TrimStart('/').Split('/');
                    spec = parts[0];
                    break;
                }
            b.Spec = (spec ?? "").Trim().TrimStart('/');
            return b;
        }

        private static string Clean(string s) { return (s ?? "").Trim(); }
    }
}
