// SPDX-License-Identifier: AGPL-3.0-or-later
using Therion.Blender.Geometry;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// A small undirected graph over nodes numbered from zero, with no parallel edges and no node
/// joined to itself, and the handful of measures the topology figures are defined over.
/// </summary>
/// <remarks>
/// Written here rather than taken from a library because it is a few hundred lines of textbook
/// algorithm over graphs of a few thousand nodes, and because the alternative — a graph package —
/// would be a runtime dependency taken on for one screen of arithmetic.
/// </remarks>
internal sealed class SimpleGraph
{
    private readonly List<int>[] _adjacency;
    private readonly int[] _degree;
    private readonly HashSet<(int, int)> _edges;

    private SimpleGraph(List<int>[] adjacency, int[] degree, HashSet<(int, int)> edges)
    {
        _adjacency = adjacency;
        _degree = degree;
        _edges = edges;
    }

    public int NodeCount => _adjacency.Length;

    public int EdgeCount => _edges.Count;

    public IEnumerable<(int A, int B)> Edges => _edges;

    /// <summary>The other end of every edge at this node. A node joined to itself is not its own
    /// neighbour, so that walks and triangle counts are unaffected by a loop that leaves and
    /// returns without passing anywhere.</summary>
    public IReadOnlyList<int> Neighbours(int node) => _adjacency[node];

    public bool AreAdjacent(int a, int b) => _edges.Contains(a < b ? (a, b) : (b, a));

    /// <summary>Edge ends at each node. A node joined to itself has two ends there.</summary>
    public int[] Degrees() => _degree;

    /// <summary>
    /// The reduced graph as a simple graph, built from the branch list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A branch cannot become an edge unchanged without losing what the reduction exists to show.
    /// A branch that leaves a junction and returns to it is one loop, but as a single edge it is a
    /// node joined to itself; two branches joining the same pair of junctions are two passages,
    /// but as two edges between one pair they collapse into one and take a loop with them. So a
    /// branch that closes on itself is cut into three at two of the stations along it, and each of
    /// several branches sharing both ends is cut into two — and because the cuts fall on stations
    /// the passage really runs through, the reduced graph is still a graph of real places.
    /// </para>
    /// <para>
    /// A branch too short to be cut is kept whole: one that is a single leg has nowhere to be cut,
    /// and one of two legs can only be cut once. That leaves a node genuinely joined to itself, or
    /// two passages genuinely indistinguishable, in the rare case of a loop or a cycle surveyed in
    /// one or two shots — reported as it is rather than papered over with invented stations, which
    /// would make the network larger than the cave.
    /// </para>
    /// </remarks>
    public static SimpleGraph FromBranches(IReadOnlyList<CenterlineBranch> branches)
    {
        var siblings = new Dictionary<(int, int), int>();
        foreach (var branch in branches)
        {
            var key = Key(branch.First, branch.Last);
            siblings[key] = siblings.GetValueOrDefault(key) + 1;
        }

        var pairs = new List<(int, int)>();
        foreach (var branch in branches)
        {
            if (branch.IsLoop) { CutInThree(branch.StationIndices, pairs); }
            else if (siblings[Key(branch.First, branch.Last)] > 1) { CutInTwo(branch.StationIndices, pairs); }
            else { pairs.Add((branch.First, branch.Last)); }
        }

        var index = new Dictionary<int, int>();
        int Node(int station)
        {
            if (!index.TryGetValue(station, out int id))
            {
                id = index.Count;
                index[station] = id;
            }
            return id;
        }

        var edges = new HashSet<(int, int)>();
        foreach (var (a, b) in pairs) { edges.Add(Key(Node(a), Node(b))); }

        var adjacency = new List<int>[index.Count];
        var degree = new int[index.Count];
        for (int i = 0; i < adjacency.Length; i++) { adjacency[i] = []; }

        foreach (var (a, b) in edges)
        {
            if (a == b)
            {
                degree[a] += 2;
                continue;
            }
            adjacency[a].Add(b);
            adjacency[b].Add(a);
            degree[a]++;
            degree[b]++;
        }

        return new SimpleGraph(adjacency, degree, edges);
    }

    /// <summary>Cuts a branch into three at the stations a third and two thirds of the way along
    /// it, or into two when it is only three stations long, or leaves it alone when it is a single
    /// leg with nowhere to be cut.</summary>
    private static void CutInThree(IReadOnlyList<int> path, List<(int, int)> pairs)
    {
        if (path.Count == 2) { pairs.Add((path[0], path[1])); return; }
        if (path.Count == 3) { CutInTwo(path, pairs); return; }

        int third = path.Count / 3;
        pairs.Add((path[0], path[third]));
        pairs.Add((path[third], path[2 * third]));
        pairs.Add((path[2 * third], path[^1]));
    }

    /// <summary>Cuts a branch in two at the station halfway along it, or leaves it alone when it
    /// is a single leg.</summary>
    private static void CutInTwo(IReadOnlyList<int> path, List<(int, int)> pairs)
    {
        if (path.Count == 2) { pairs.Add((path[0], path[1])); return; }

        int middle = path.Count / 2;
        pairs.Add((path[0], path[middle]));
        pairs.Add((path[middle], path[^1]));
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    public int ComponentCount => ComponentMembers().Count;

    /// <summary>The nodes of each connected component, one list per component.</summary>
    public IReadOnlyList<List<int>> ComponentMembers()
    {
        var seen = new bool[NodeCount];
        var components = new List<List<int>>();
        for (int start = 0; start < NodeCount; start++)
        {
            if (seen[start]) { continue; }

            var members = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                members.Add(v);
                foreach (int w in _adjacency[v])
                {
                    if (seen[w]) { continue; }
                    seen[w] = true;
                    queue.Enqueue(w);
                }
            }
            components.Add(members);
        }
        return components;
    }

    /// <summary>Edges traversed from <paramref name="source"/> to every node, and -1 for the
    /// nodes it cannot reach.</summary>
    public int[] BreadthFirstDistances(int source)
    {
        var distance = new int[NodeCount];
        Array.Fill(distance, -1);
        distance[source] = 0;

        var queue = new Queue<int>();
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            int v = queue.Dequeue();
            foreach (int w in _adjacency[v])
            {
                if (distance[w] >= 0) { continue; }
                distance[w] = distance[v] + 1;
                queue.Enqueue(w);
            }
        }
        return distance;
    }

    /// <summary>
    /// The share of shortest routes between other pairs of nodes that pass through each node,
    /// scaled so that a node every route passes through scores 1. Brandes' accumulation: one
    /// breadth-first search per node, counting how many shortest routes reach each node and then
    /// unwinding the dependencies back along them.
    /// </summary>
    public double[] Betweenness()
    {
        int n = NodeCount;
        var betweenness = new double[n];

        for (int s = 0; s < n; s++)
        {
            var stack = new Stack<int>();
            var predecessors = new List<int>[n];
            for (int i = 0; i < n; i++) { predecessors[i] = []; }

            var routes = new double[n];
            var distance = new int[n];
            Array.Fill(distance, -1);
            routes[s] = 1.0;
            distance[s] = 0;

            var queue = new Queue<int>();
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                stack.Push(v);
                foreach (int w in _adjacency[v])
                {
                    if (distance[w] < 0)
                    {
                        distance[w] = distance[v] + 1;
                        queue.Enqueue(w);
                    }
                    if (distance[w] == distance[v] + 1)
                    {
                        routes[w] += routes[v];
                        predecessors[w].Add(v);
                    }
                }
            }

            var dependency = new double[n];
            while (stack.Count > 0)
            {
                int w = stack.Pop();
                foreach (int v in predecessors[w])
                {
                    dependency[v] += routes[v] / routes[w] * (1.0 + dependency[w]);
                }
                if (w != s) { betweenness[w] += dependency[w]; }
            }
        }

        // Each pair is walked from both ends, so the raw sums are twice the pair count; the
        // divisor turns them into a share of every ordered pair that could have passed through.
        if (n > 2)
        {
            double scale = 1.0 / ((n - 1.0) * (n - 2.0));
            for (int i = 0; i < n; i++) { betweenness[i] *= scale; }
        }
        return betweenness;
    }
}
