// SPDX-License-Identifier: AGPL-3.0-or-later
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
    /// A simple graph over the edges of a reduced passage network, which the shared network
    /// routine has already made simple: loops and parallel passages cut at stations along them so
    /// that they survive as loops. Nodes are numbered in the order the edges first mention them.
    /// </summary>
    public static SimpleGraph FromEdges(IReadOnlyList<(int A, int B)> reducedEdges)
    {
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
        foreach (var (a, b) in reducedEdges) { edges.Add(Key(Node(a), Node(b))); }

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

        // Allocated once and cleared per source rather than rebuilt inside the loop. The search
        // runs once per node, so a fresh list per node per source is n² lists — for a reduced
        // network of ten thousand junctions that is a hundred million allocations to hold the
        // same shape each time, and this runs while a survey file is being read.
        var stack = new Stack<int>();
        var predecessors = new List<int>[n];
        for (int i = 0; i < n; i++) { predecessors[i] = []; }
        var routes = new double[n];
        var distance = new int[n];
        var dependency = new double[n];
        var queue = new Queue<int>();

        for (int s = 0; s < n; s++)
        {
            stack.Clear();
            queue.Clear();
            for (int i = 0; i < n; i++) { predecessors[i].Clear(); }
            Array.Clear(routes);
            Array.Clear(dependency);

            Array.Fill(distance, -1);
            routes[s] = 1.0;
            distance[s] = 0;

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
