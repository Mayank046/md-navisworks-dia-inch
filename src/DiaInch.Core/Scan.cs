// MD Dia-Inch - one call: walk → count → aggregate
// Author: MD (Mayank Dholariya)
using System;
using System.Collections.Generic;

namespace DiaInch.Core
{
    public static class Scan
    {
        public static ScanResult Run<T>(IDiaTree<T> tree, IEnumerable<ScanRoot<T>> roots, Rules rules, bool skipHidden,
                                        Action<int, string> progress, Func<bool> cancelled, IEqualityComparer<T> cmp = null)
        {
            var walk = new TreeWalk<T>(tree, rules, cmp) { SkipHidden = skipHidden };
            if (progress != null) walk.Progress = progress;
            if (cancelled != null) walk.Cancelled = cancelled;
            var branches = walk.Collect(roots);
            var counter = new JointCounter(rules);
            var results = new List<BranchResult>();
            int i = 0;
            foreach (var b in branches)
            {
                if (cancelled != null && cancelled()) break;
                results.Add(counter.Count(b));
                if (progress != null && (++i % 200) == 0) progress(walk.Visited, "counting " + i + "/" + branches.Count);
            }
            var res = Aggregator.Build(results, rules);
            res.NodesVisited = walk.Visited;
            return res;
        }
    }
}
