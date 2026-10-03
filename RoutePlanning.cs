using System;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    // Kept independent of Unity so topology and release rules can be exercised without a running game.
    public sealed class RouteEdge
    {
        public string Target = "";
        public int Junction = -1;
        public byte Branch;
    }

    public sealed class RouteNode
    {
        public string Id = "";
        public string Track = "";
        public double Length;
        public readonly List<RouteEdge> Edges = new List<RouteEdge>();
    }

    public sealed class RoutePath
    {
        public readonly List<string> Nodes = new List<string>();
        public readonly Dictionary<int, byte> Switches = new Dictionary<int, byte>();
        public double Length;
        public string Key => string.Join(">", Nodes);
    }

    public static class RoutePlanning
    {
        public static List<RoutePath> FindPaths(IReadOnlyDictionary<string, RouteNode> graph,
            string start, string destination, double startSpan, double destinationSpan, out bool truncated)
        {
            var results = new List<RoutePath>();
            truncated = false;
            if (!graph.ContainsKey(start) || !graph.ContainsKey(destination)) return results;
            var queue = new Queue<RoutePath>();
            var first = new RoutePath();
            first.Nodes.Add(start);
            queue.Enqueue(first);
            int examined = 0;
            while (queue.Count > 0 && examined++ < 20000 && results.Count < 32)
            {
                var path = queue.Dequeue();
                var last = path.Nodes[path.Nodes.Count - 1];
                if (last == destination)
                {
                    if (path.Nodes.Count > 1 || destinationSpan > startSpan + 1)
                    {
                        path.Length = path.Nodes.Sum(id => graph[id].Length) - startSpan -
                            (graph[destination].Length - destinationSpan);
                        results.Add(path);
                    }
                    continue;
                }
                if (path.Nodes.Count >= 128) { truncated = true; continue; }
                foreach (var edge in graph[last].Edges)
                {
                    // Disallow loops, reversals and contradictory requirements at the same switch.
                    if (!graph.TryGetValue(edge.Target, out var target) ||
                        path.Nodes.Any(id => graph[id].Track == target.Track) ||
                        (edge.Junction >= 0 && path.Switches.TryGetValue(edge.Junction, out var branch) && branch != edge.Branch))
                        continue;
                    var next = new RoutePath();
                    next.Nodes.AddRange(path.Nodes);
                    next.Nodes.Add(edge.Target);
                    foreach (var item in path.Switches) next.Switches.Add(item.Key, item.Value);
                    if (edge.Junction >= 0) next.Switches[edge.Junction] = edge.Branch;
                    queue.Enqueue(next);
                }
            }
            truncated |= queue.Count > 0;
            return results.OrderBy(p => p.Length).ToList();
        }
    }

    public sealed class RouteReleaseTracker
    {
        private readonly Dictionary<string, bool> passed = new Dictionary<string, bool>();
        public bool Entered { get; private set; }
        public void Capture(IEnumerable<string> bogieIds)
        {
            Entered = true;
            foreach (var id in bogieIds) if (!passed.ContainsKey(id)) passed.Add(id, false);
        }
        public void Observe(string bogieId, bool passedTrigger, bool valid)
        {
            if (passed.ContainsKey(bogieId)) passed[bogieId] = valid && passedTrigger;
        }
        public bool CanRelease(bool routeClear) => Entered && passed.Count > 0 && routeClear && passed.Values.All(p => p);
        public IEnumerable<string> BogieIds => passed.Keys;
    }
}
