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
/// <param name="ReducedNodeCount">Stations where passages meet or end — the network with every run
/// of through-stations contracted away. This is the denominator a loop count is meaningful against;
/// the raw station count is a measure of how densely the cave was surveyed.</param>
/// <param name="CyclomaticNumber">Independent loops, edges minus nodes plus components. Unchanged by
/// the contraction, which is why it can be counted here without reducing anything.</param>
/// <param name="ExtremityCount">Stations with exactly one passage — where the survey stopped.
/// Unchanged by the contraction for the same reason.</param>
/// <param name="Clustering">How often two passages leaving one junction are themselves joined,
/// averaged over the junctions — nought when no junction rings, one when every pair does. Null when
/// the network holds no place where two passages meet, which is not a clustering of nought: a
/// single corridor has no pairs to be joined or not joined. Measured on the contracted network and
/// not on the raw stations, because a survey that put stations along its passages subdivides every
/// ring into a path and would report nought for a cave made entirely of them.</param>
public sealed record PassageNetworkFigures(
    int NodeCount,
    int EdgeCount,
    int ComponentCount,
    int ReducedNodeCount,
    int CyclomaticNumber,
    int ExtremityCount,
    double? Clustering);

/// <summary>
/// The shape of a passage network, counted from the legs that were surveyed.
///
/// <para>
/// <b>Only the figures the contraction leaves alone are counted here.</b> Contracting every run of
/// through-stations into a single branch changes neither the number of independent loops nor the
/// number of places the survey stopped — a chain of stations between two junctions adds one node and
/// one edge for each station it holds, and those cancel — so both can be read off the unreduced legs
/// exactly. The count of junctions and dead ends is likewise just the stations that are not
/// through-stations.
/// </para>
/// <para>
/// <b>One figure is not invariant and so the contraction is actually performed for it.</b> How
/// often two passages leaving one junction are themselves joined depends entirely on the
/// contraction: a triangle of passage surveyed with stations along its sides is a path between
/// junctions rather than a ring between neighbours, and reading it off the raw stations would
/// report nought for a cave whose every junction rings. So the runs of through-stations are walked
/// out into direct connections between the places passages meet or end, and the figure is measured
/// there.
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
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (from, to) in legs)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || StringComparer.Ordinal.Equals(from, to))
            {
                continue;
            }

            Neighbours(adjacency, from).Add(to);
            Neighbours(adjacency, to).Add(from);
        }

        if (adjacency.Count == 0)
        {
            return null;
        }

        var nodeCount = adjacency.Count;
        var edgeCount = adjacency.Values.Sum(n => n.Count) / 2;
        var extremityCount = adjacency.Values.Count(n => n.Count == 1);

        // A component made entirely of through-stations is a closed loop with no junction and no
        // end. It contracts to a single node rather than to none, and counting it as none would
        // divide a real loop count by zero.
        var componentCount = 0;
        var reducedNodeCount = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in adjacency.Keys)
        {
            if (!seen.Add(start))
            {
                continue;
            }

            componentCount++;
            var corners = 0;
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (adjacency[node].Count != 2)
                {
                    corners++;
                }

                foreach (var neighbour in adjacency[node])
                {
                    if (seen.Add(neighbour))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }

            reducedNodeCount += corners == 0 ? 1 : corners;
        }

        return new PassageNetworkFigures(
            nodeCount,
            edgeCount,
            componentCount,
            reducedNodeCount,
            edgeCount - nodeCount + componentCount,
            extremityCount,
            Clustering(adjacency));
    }

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
    private static double? Clustering(Dictionary<string, HashSet<string>> adjacency)
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
    private static Dictionary<string, HashSet<string>> Contract(Dictionary<string, HashSet<string>> adjacency)
    {
        var reduced = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
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
                if (!StringComparer.Ordinal.Equals(far, node))
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
    private static string WalkThrough(
        Dictionary<string, HashSet<string>> adjacency, string from, string first)
    {
        var previous = from;
        var current = first;

        // A run visits each station at most once, so it cannot be longer than the network. The
        // bound is a guarantee of termination rather than a real case: the walk stops at the first
        // station that is not a through-station, and every run reaches one.
        for (var step = 0; step < adjacency.Count && adjacency[current].Count == 2; step++)
        {
            var next = adjacency[current].First(n => !StringComparer.Ordinal.Equals(n, previous));
            previous = current;
            current = next;
        }

        return current;
    }

    private static HashSet<string> Neighbours(Dictionary<string, HashSet<string>> adjacency, string station)
    {
        if (!adjacency.TryGetValue(station, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            adjacency[station] = set;
        }

        return set;
    }
}
