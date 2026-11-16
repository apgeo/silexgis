// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// The counts that say what shape a passage network is, read off the legs themselves.
/// </summary>
/// <param name="NodeCount">Distinct stations the legs join.</param>
/// <param name="EdgeCount">Distinct connections between two stations. Two legs surveyed between the
/// same pair are one connection: the network is the same shape whether it was walked once or
/// twice.</param>
/// <param name="ComponentCount">How many separate pieces the network falls into.</param>
/// <param name="ReducedNodeCount">Nodes of the reduced network: the places passages meet or end,
/// plus the stations a loop or a pair of parallel passages is cut at so that it survives as a loop
/// in a simple graph (see <see cref="ReducedPassageNetwork"/>). This is the denominator a loop count
/// is meaningful against; the raw station count is a measure of how densely the cave was
/// surveyed.</param>
/// <param name="CyclomaticNumber">Independent loops, edges minus nodes plus components, taken on the
/// reduced network. The contraction and the cuts add as many nodes as edges, so it equals the count
/// on the raw stations.</param>
/// <param name="ExtremityCount">Stations with exactly one passage — where the survey stopped.</param>
/// <param name="Clustering">How often two passages leaving one junction are themselves joined,
/// averaged over the junctions — nought when no junction rings, one when every pair does. Measured
/// on the contracted network and not on the raw stations, because a survey that put stations along
/// its passages subdivides every ring into a path and would report nought for a cave made entirely
/// of them.
/// <para>
/// Null when no junction has two <i>distinct</i> places to reach, which is not a clustering of
/// nought: there are no pairs to be joined or not joined, and nought would claim the passages were
/// checked and found unconnected. A single corridor is the ordinary case. The other one is a
/// junction whose passages all lead to the same far junction — several routes between the same two
/// places — where the contraction leaves one distinct neighbour and so no pair, even though the
/// cave plainly has junctions and rings. Those rings are still counted by the cyclomatic number,
/// which is what anything reasoning about loops should read.
/// </para></param>
public sealed record PassageNetworkFigures(
    int NodeCount,
    int EdgeCount,
    int ComponentCount,
    int ReducedNodeCount,
    int CyclomaticNumber,
    int ExtremityCount,
    double? Clustering);

/// <summary>
/// The passage network with every run of through-stations contracted into one branch, and then
/// made a simple graph — the graph the karst literature's network figures are defined over, and
/// the one place in the application that says what a node, an edge, a component and an independent
/// loop of a cave are.
/// </summary>
/// <remarks>
/// <para>
/// A branch cannot become an edge unchanged without losing what the reduction exists to show.
/// A branch that leaves a junction and returns to it is one loop, but as a single edge it is a
/// node joined to itself; two branches joining the same pair of junctions are two passages, but as
/// two edges between one pair they collapse into one and take a loop with them. So a branch that
/// closes on itself is cut into three at stations a third and two thirds of the way along it, and
/// each of several branches sharing both ends is cut into two at the station halfway along — and
/// because the cuts fall on stations the passage really runs through, the reduced graph is still a
/// graph of real places. A closed ring with no junction therefore reduces to three nodes and three
/// edges, one loop.
/// </para>
/// <para>
/// A branch too short to be cut is kept whole: one that is a single leg has nowhere to be cut. A
/// network of distinct legs cannot contain a loop of one or two legs, so in practice every loop is
/// cut.
/// </para>
/// </remarks>
public sealed class ReducedPassageNetwork
{
    private readonly int[] _degree;

    internal ReducedPassageNetwork(
        IReadOnlyList<(int A, int B)> edges, int componentCount, int branchCount)
    {
        Edges = edges;
        ComponentCount = componentCount;
        BranchCount = branchCount;

        var degree = new Dictionary<int, int>();
        foreach (var (a, b) in edges)
        {
            degree[a] = degree.GetValueOrDefault(a) + 1;
            degree[b] = degree.GetValueOrDefault(b) + 1;
        }

        _degree = [.. degree.Values];
    }

    /// <summary>The edges of the simple reduced graph, each once, in a stable order. Nodes are the
    /// caller's own station ids; the ones introduced by the reduction are stations the passages
    /// run through.</summary>
    public IReadOnlyList<(int A, int B)> Edges { get; }

    /// <summary>Connected pieces of the network. Unchanged by the reduction.</summary>
    public int ComponentCount { get; }

    /// <summary>Runs of passage between two places where the network does something, before any
    /// cutting: one per loop, per dead-end passage, per junction-to-junction passage.</summary>
    public int BranchCount { get; }

    public int NodeCount => _degree.Length;

    public int EdgeCount => Edges.Count;

    /// <summary>Independent loops: edges minus nodes plus components.</summary>
    public int CyclomaticNumber => EdgeCount - NodeCount + ComponentCount;

    /// <summary>Nodes with exactly one passage.</summary>
    public int ExtremityCount => _degree.Count(d => d == 1);

    /// <summary>Nodes where three or more passages meet.</summary>
    public int JunctionCount => _degree.Count(d => d > 2);

    /// <summary>Edge ends at each node, one entry per node in no particular order.</summary>
    public IReadOnlyList<int> Degrees => _degree;
}

/// <summary>
/// The shape of a passage network, counted from the legs that were surveyed.
///
/// <para>
/// <b>One definition.</b> Every count of nodes, edges, components and independent loops that the
/// application reports — the pattern suggestion's and the survey topology's — comes from
/// <see cref="Reduce"/>, which follows the reference implementation of the published method; the
/// topology figures are held to its committed output, so this is the definition both features
/// share. What differs between the two consumers is only which legs they hand in.
/// </para>
/// <para>
/// <b>One figure is not part of that definition and is measured here for the pattern suggestion
/// alone.</b> How often two passages leaving one junction are themselves joined depends entirely
/// on the contraction: a triangle of passage surveyed with stations along its sides is a path
/// between junctions rather than a ring between neighbours, and reading it off the raw stations
/// would report nought for a cave whose every junction rings. So the runs of through-stations are
/// walked out into direct connections between the places passages meet or end, and the figure is
/// measured there.
/// </para>
/// <para>
/// <b>A leg with an unnamed end joins nothing.</b> Line work that was never a survey file has no
/// station names, and inventing an identity per coordinate would fuse every passage that happens to
/// pass through one point and split every one surveyed twice. Such legs are dropped, and a network
/// where none survive is not measured at all rather than measured as empty.
/// </para>
/// </summary>
public static class PassageNetwork
{
    /// <summary>
    /// The network figures for a set of legs, or null when no leg joined two named stations.
    /// </summary>
    /// <remarks>
    /// A leg from a station to itself is not a connection between two places and is dropped; it
    /// would otherwise add a loop the cave does not have.
    /// </remarks>
    public static PassageNetworkFigures? Measure(IEnumerable<(string? From, string? To)> legs)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string name)
        {
            if (!ids.TryGetValue(name, out var id))
            {
                id = ids.Count;
                ids[name] = id;
            }

            return id;
        }

        var stationLegs = new List<(int, int)>();
        foreach (var (from, to) in legs)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                continue;
            }

            stationLegs.Add((Id(from), Id(to)));
        }

        return MeasureStations(stationLegs);
    }

    /// <summary>
    /// The same figures for legs already identified by station number, or null when there is no
    /// leg that joins two different stations.
    /// </summary>
    public static PassageNetworkFigures? MeasureStations(IEnumerable<(int From, int To)> legs)
    {
        var adjacency = Adjacency(legs);
        var reduced = Reduce(adjacency);
        if (reduced is null)
        {
            return null;
        }

        return new PassageNetworkFigures(
            adjacency.Count,
            adjacency.Values.Sum(n => n.Count) / 2,
            reduced.ComponentCount,
            reduced.NodeCount,
            reduced.CyclomaticNumber,
            reduced.ExtremityCount,
            Clustering(adjacency));
    }

    /// <summary>
    /// The reduced network for a set of legs, or null when no leg joins two different stations.
    /// Legs repeated between one pair of stations, in either direction, are one connection, and a
    /// leg from a station to itself is dropped.
    /// </summary>
    public static ReducedPassageNetwork? Reduce(IEnumerable<(int From, int To)> legs) =>
        Reduce(Adjacency(legs));

    private static Dictionary<int, HashSet<int>> Adjacency(IEnumerable<(int From, int To)> legs)
    {
        var adjacency = new Dictionary<int, HashSet<int>>();
        foreach (var (from, to) in legs)
        {
            if (from == to)
            {
                continue;
            }

            Neighbours(adjacency, from).Add(to);
            Neighbours(adjacency, to).Add(from);
        }

        return adjacency;
    }

    private static ReducedPassageNetwork? Reduce(Dictionary<int, HashSet<int>> adjacency)
    {
        if (adjacency.Count == 0)
        {
            return null;
        }

        var sorted = adjacency.ToDictionary(kv => kv.Key, kv => kv.Value.Order().ToArray());

        // A branch is found by walking from a station that is not a through-station along one of
        // its passages. The same branch is found again from its far end, so both of its ends are
        // recorded as walked the moment it is found.
        var branches = new List<List<int>>();
        var walked = new HashSet<(int Start, int Next)>();
        var seen = new HashSet<int>();
        var componentCount = 0;
        foreach (var seed in sorted.Keys.Order())
        {
            if (!seen.Add(seed))
            {
                continue;
            }

            componentCount++;
            var members = new List<int> { seed };
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                foreach (var next in sorted[queue.Dequeue()])
                {
                    if (seen.Add(next))
                    {
                        members.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }

            members.Sort();
            var starts = members.Where(m => sorted[m].Length != 2).ToList();

            // A component every station of which has two passages is one closed ring with nothing
            // to anchor it. Starting at its lowest station gives the one branch it contains; the
            // choice is arbitrary but has to be made or the ring is lost.
            if (starts.Count == 0)
            {
                starts.Add(members[0]);
            }

            foreach (var start in starts)
            {
                foreach (var first in sorted[start])
                {
                    if (walked.Contains((start, first)))
                    {
                        continue;
                    }

                    var path = new List<int> { start, first };
                    while (path[^1] != start && sorted[path[^1]].Length == 2)
                    {
                        var onwards = sorted[path[^1]];
                        path.Add(onwards[0] == path[^2] ? onwards[1] : onwards[0]);
                    }

                    walked.Add((path[0], path[1]));
                    walked.Add((path[^1], path[^2]));
                    branches.Add(path);
                }
            }
        }

        // Ordered by the station each starts from, then the one it ends at; the order only fixes
        // the order of the edges, not any count.
        var ordered = branches
            .Select((path, index) => (path, index))
            .OrderBy(b => b.path[0]).ThenBy(b => b.path[^1]).ThenBy(b => b.index)
            .Select(b => b.path)
            .ToList();

        var siblings = new Dictionary<(int, int), int>();
        foreach (var path in ordered)
        {
            var key = Key(path[0], path[^1]);
            siblings[key] = siblings.GetValueOrDefault(key) + 1;
        }

        var pairs = new List<(int, int)>();
        foreach (var path in ordered)
        {
            if (path[0] == path[^1])
            {
                CutInThree(path, pairs);
            }
            else if (siblings[Key(path[0], path[^1])] > 1)
            {
                CutInTwo(path, pairs);
            }
            else
            {
                pairs.Add((path[0], path[^1]));
            }
        }

        var edges = new List<(int A, int B)>();
        var edgeSet = new HashSet<(int, int)>();
        foreach (var (a, b) in pairs)
        {
            if (edgeSet.Add(Key(a, b)))
            {
                edges.Add(Key(a, b));
            }
        }

        return new ReducedPassageNetwork(edges, componentCount, ordered.Count);
    }

    /// <summary>Cuts a ring branch into three at the stations a third and two thirds of the way
    /// along it, into two when it is only three stations long, and leaves a single leg alone.</summary>
    private static void CutInThree(List<int> path, List<(int, int)> pairs)
    {
        if (path.Count == 2)
        {
            pairs.Add((path[0], path[1]));
            return;
        }

        if (path.Count == 3)
        {
            CutInTwo(path, pairs);
            return;
        }

        var third = path.Count / 3;
        pairs.Add((path[0], path[third]));
        pairs.Add((path[third], path[2 * third]));
        pairs.Add((path[2 * third], path[^1]));
    }

    /// <summary>Cuts a branch in two at the station halfway along it, or leaves it alone when it is
    /// a single leg.</summary>
    private static void CutInTwo(List<int> path, List<(int, int)> pairs)
    {
        if (path.Count == 2)
        {
            pairs.Add((path[0], path[1]));
            return;
        }

        var middle = path.Count / 2;
        pairs.Add((path[0], path[middle]));
        pairs.Add((path[middle], path[^1]));
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>
    /// How often two passages leaving one junction are themselves joined, averaged over the
    /// junctions, or null when the network holds no junction at all.
    /// </summary>
    /// <remarks>
    /// The average is taken over junctions rather than over pairs so that one very busy junction
    /// does not decide the figure for the whole cave, which is the usual reading of a local
    /// clustering coefficient. Dead ends have no pair of passages to compare and take no part —
    /// they are not junctions that fail to ring.
    /// </remarks>
    private static double? Clustering(Dictionary<int, HashSet<int>> adjacency)
    {
        var reduced = Contract(adjacency);

        var total = 0d;
        var junctions = 0;
        foreach (var neighbours in reduced.Values)
        {
            if (neighbours.Count < 2)
            {
                continue;
            }

            var list = neighbours.ToList();
            var pairs = 0;
            var joined = 0;
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    pairs++;
                    if (reduced[list[i]].Contains(list[j]))
                    {
                        joined++;
                    }
                }
            }

            total += (double)joined / pairs;
            junctions++;
        }

        return junctions > 0 ? total / junctions : null;
    }

    /// <summary>
    /// The network with every run of through-stations walked out, leaving only the places passages
    /// meet or end, joined where a run leads from one to the other.
    /// </summary>
    /// <remarks>
    /// A run that leaves a junction and comes back to it is a loop hanging off one place. It joins
    /// that junction to nothing else, so it is dropped rather than recorded as a station joined to
    /// itself, which would otherwise read as a ring between two passages that do not exist.
    /// </remarks>
    private static Dictionary<int, HashSet<int>> Contract(Dictionary<int, HashSet<int>> adjacency)
    {
        var reduced = new Dictionary<int, HashSet<int>>();
        foreach (var (node, neighbours) in adjacency)
        {
            if (neighbours.Count == 2)
            {
                continue;
            }

            var joined = Neighbours(reduced, node);
            foreach (var neighbour in neighbours)
            {
                var far = WalkThrough(adjacency, node, neighbour);
                if (far != node)
                {
                    joined.Add(far);
                }
            }
        }

        return reduced;
    }

    /// <summary>
    /// Follow a run of through-stations from <paramref name="from"/> by way of
    /// <paramref name="first"/> and return the place it arrives at.
    /// </summary>
    private static int WalkThrough(Dictionary<int, HashSet<int>> adjacency, int from, int first)
    {
        var previous = from;
        var current = first;

        // A run visits each station at most once, so it cannot be longer than the network. The
        // bound is a guarantee of termination rather than a real case: the walk stops at the first
        // station that is not a through-station, and every run reaches one.
        for (var step = 0; step < adjacency.Count && adjacency[current].Count == 2; step++)
        {
            var next = adjacency[current].First(n => n != previous);
            previous = current;
            current = next;
        }

        return current;
    }

    private static HashSet<int> Neighbours(Dictionary<int, HashSet<int>> adjacency, int station)
    {
        if (!adjacency.TryGetValue(station, out var set))
        {
            set = [];
            adjacency[station] = set;
        }

        return set;
    }
}
