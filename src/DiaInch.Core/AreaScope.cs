// MD Dia-Inch - "Limit to chosen hierarchies": remembered pipe parent nodes per model
// Same storage format as MD Hierarchy Scan (areas.txt), so its pipe parents can be imported.
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DiaInch.Core
{
    /// <summary>A remembered parent node: tree path of names from the model root down, plus InstanceGuid if any.</summary>
    public sealed class AreaRef
    {
        public List<string> Path = new List<string>();
        public string Guid = "";
        public string Display { get { return string.Join(" / ", Path); } }
        public string Leaf { get { return Path.Count > 0 ? Path[Path.Count - 1] : ""; } }

        public bool SameAs(AreaRef o)
        {
            if (o == null) return false;
            if (Guid.Length > 0 && o.Guid.Length > 0) return string.Equals(Guid, o.Guid, StringComparison.OrdinalIgnoreCase) && Path.SequenceEqual(o.Path);
            return Path.SequenceEqual(o.Path);
        }
    }

    public sealed class AreaState
    {
        public bool Limit;
        public readonly List<AreaRef> Parents = new List<AreaRef>();

        public static int Merge(List<AreaRef> into, IEnumerable<AreaRef> add)
        {
            int n = 0;
            foreach (AreaRef r in add)
                if (r != null && r.Path.Count > 0 && !into.Any(x => x.SameAs(r))) { into.Add(r); n++; }
            return n;
        }
    }

    public static class AreaScope
    {
        /// <summary>Finds the node for a ref by walking names down from the model roots. Guid breaks ties between same-name siblings.</summary>
        public static T Resolve<T>(IList<T> roots, Func<T, IEnumerable<T>> children, Func<T, string> name, Func<T, string> guid, AreaRef r) where T : class
        {
            if (r == null || r.Path.Count == 0 || roots == null) return null;
            IEnumerable<T> level = roots;
            for (int d = 0; d < r.Path.Count; d++)
            {
                var matches = level.Where(x => x != null && string.Equals(name(x), r.Path[d], StringComparison.Ordinal)).ToList();
                if (matches.Count == 0) return null;
                if (d == r.Path.Count - 1)
                {
                    if (matches.Count > 1 && r.Guid.Length > 0)
                    {
                        T g = matches.FirstOrDefault(x => string.Equals(guid(x), r.Guid, StringComparison.OrdinalIgnoreCase));
                        if (g != null) return g;
                    }
                    return matches[0];
                }
                if (matches.Count > 1)
                {
                    var rest = new AreaRef { Path = r.Path.Skip(d + 1).ToList(), Guid = r.Guid };
                    foreach (T m in matches)
                    {
                        T hit = Resolve(children(m).ToList(), children, name, guid, rest);
                        if (hit != null) return hit;
                    }
                    return null;
                }
                level = children(matches[0]) ?? Enumerable.Empty<T>();
            }
            return null;
        }

        /// <summary>Drops refs that sit inside another chosen ref (no double counting).</summary>
        public static List<AreaRef> RemoveNested(IEnumerable<AreaRef> refs)
        {
            var l = refs.Where(r => r != null && r.Path.Count > 0).ToList();
            return l.Where(r => !l.Any(o => !ReferenceEquals(o, r) && o.Path.Count < r.Path.Count && r.Path.Take(o.Path.Count).SequenceEqual(o.Path))).ToList();
        }

        private const char Sep = '\u001F';
        private const char PathSep = '\u001E';

        public static string Serialize(IDictionary<string, AreaState> all)
        {
            var sb = new StringBuilder();
            foreach (var kv in all)
            {
                sb.Append("D").Append(Sep).Append(kv.Key).Append(Sep).Append(kv.Value.Limit ? "1" : "0").Append('\n');
                foreach (AreaRef r in kv.Value.Parents) sb.Append("P").Append(Sep).Append(r.Guid).Append(Sep).Append(string.Join(PathSep.ToString(), r.Path)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Reads Dia-Inch areas.txt; also accepts MD Hierarchy Scan's file (its "P" = pipe parents).</summary>
        public static Dictionary<string, AreaState> Deserialize(string text)
        {
            var all = new Dictionary<string, AreaState>(StringComparer.OrdinalIgnoreCase);
            AreaState cur = null;
            foreach (string line in (text ?? "").Split('\n'))
            {
                string[] f = line.TrimEnd('\r').Split(Sep);
                if (f.Length < 3) continue;
                if (f[0] == "D") { cur = new AreaState { Limit = f[2] == "1" }; all[f[1]] = cur; continue; }
                if (cur == null || f[0] != "P") continue;
                cur.Parents.Add(new AreaRef { Guid = f[1], Path = f[2].Split(PathSep).ToList() });
            }
            return all;
        }
    }
}
