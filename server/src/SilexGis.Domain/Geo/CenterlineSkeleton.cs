// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;

namespace SilexGis.Domain.Geo;

/// <summary>
/// Builds a display skeleton of a surveyed centerline: the traverse network, with the splays
/// removed and the surviving shots sewn back into long polylines.
/// <para>
/// Why this exists. A modern survey export is dominated by splays — the short wall/LRUD shots
/// fired from each station to measure passage shape. On a real 15.7 km cave the export holds
/// 82,337 line components totalling 244 km of shots, of which ~98% are splays; a 3.1 km cave
/// measured 71,792 components and 237 km. Drawing them costs one canvas path each, which is
/// what makes the map overlay unusable, and below roughly 0.4 ground metres per pixel they are
/// not even visible — they only thicken the passage lines.
/// </para>
/// <para>
/// How a splay is recognised. The interchange formats a centerline is uploaded in — GeoJSON, GPX,
/// KML — carry no flag for it, but a splay is by construction a single shot from a station to a
/// point nothing else touches, and splays come in bunches from the same station. So: explode the
/// geometry into individual shots, build a graph on exact coordinate identity, and drop a shot
/// whose far end is touched by nothing else — unless it is the only such shot at its station,
/// which is how a genuine dead-end passage tip looks.
/// </para>
/// <para>
/// This is a guess, and it is only made where there is nothing better. A compiled survey export
/// states per leg whether it is a splay, so a centerline read out of one says which shots are
/// passage instead of inferring it, and asks for <see cref="Sew"/> rather than <see cref="Build"/>.
/// The guess remains correct for everything else, and remains what the uploaded formats get.
/// </para>
/// <para>
/// Three details are load-bearing, each established by measurement against real exports:
/// exploding first is essential, because survey tools merge consecutive traverse legs into
/// multi-vertex polylines and the splay-bearing station is then an interior vertex that an
/// endpoint-only test never inspects (testing whole components deleted 7.7 km of real passage
/// on the 15.7 km cave); identity must include altitude, because a deep cave has distinct
/// stations sharing a plan position (ignoring Z inflated one cave's skeleton to 143% of its
/// surveyed length); and the pass must not be repeated, because each further pass eats the new
/// dead ends the previous one exposed (six passes took retention from 88% down to 71%).
/// </para>
/// <para>
/// Measured on those two exports: 82,337 components collapse to 894 polylines while retaining
/// 92.3% of the surveyed length as a single connected network, and 71,792 to 328 retaining
/// 95.0%. A survey with no splays is left alone apart from the chain merging.
/// </para>
/// <para>
/// Known and accepted loss: where a passage ends at a station that also carries splays, its
/// final shot is one of the loose shots at that station and goes with them. That is most of the
/// 5–8% length shortfall above — a couple of metres at the tip of each dead end, invisible at
/// any zoom where the skeleton is what gets drawn.
/// </para>
/// </summary>
public static class CenterlineSkeleton
{
    /// <summary>
    /// The skeleton of <paramref name="lines"/>, flattened to 2D (the overlay draws on a
    /// surface map; the stored survey keeps its altitudes). Returns an empty geometry for
    /// empty input.
    /// </summary>
    public static MultiLineString Build(MultiLineString lines) => Reduce(lines, keepAltitude: false);

    /// <summary>
    /// The same skeleton as <see cref="Build"/>, with the altitude of every station kept on the
    /// output. Plan geometry is identical — the reduction is the same one, and the two differ only
    /// in whether the coordinates that come out carry Z.
    /// </summary>
    /// <remarks>
    /// The 2D form is what the map overlay draws and what the stored skeleton column can hold. A
    /// measurement over passage — how steep it is, how deep it goes, how its bearings distribute
    /// with height — needs the third coordinate, and for a cave whose centerline arrived as an
    /// uploaded file there is no other source of it. That is what this exists for; nothing draws it.
    /// </remarks>
    public static MultiLineString Build3D(MultiLineString lines) => Reduce(lines, keepAltitude: true);

    /// <summary>
    /// The skeleton of a centerline whose splays are already known: the shots given, sewn into
    /// maximal polylines, with nothing pruned. Flattened to 2D, like <see cref="Build"/>, because
    /// this is the same overlay geometry and the same column holds it.
    /// </summary>
    /// <remarks>
    /// For a survey read out of a compiled export, which states per leg whether it is a splay.
    /// The caller passes the legs that are passage and this only does the sewing, because guessing
    /// again on top of an answer that is already correct would drop the tip of every dead end for
    /// nothing.
    /// </remarks>
    public static MultiLineString Sew(MultiLineString lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var (segments, nodes) = Explode(lines);
        return Merge(segments, nodes, keepAltitude: false);
    }

    private static MultiLineString Reduce(MultiLineString lines, bool keepAltitude)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var (segments, nodes) = Explode(lines);
        if (segments.Count == 0)
        {
            return Empty();
        }

        var degree = new int[nodes.Count];
        foreach (var (a, b) in segments)
        {
            degree[a]++;
            degree[b]++;
        }

        // A shot with exactly one loose end is a stub; count how many hang off the same station.
        var stubsAtStation = new Dictionary<int, int>();
        foreach (var (a, b) in segments)
        {
            if (StationOfStub(a, b, degree) is int station)
            {
                stubsAtStation[station] = stubsAtStation.GetValueOrDefault(station) + 1;
            }
        }

        var kept = new List<(int A, int B)>(segments.Count);
        foreach (var segment in segments)
        {
            var station = StationOfStub(segment.A, segment.B, degree);
            // Keep the traverse, keep a shot that is loose at both ends (it carries no evidence
            // either way), and keep a lone stub — a station with a single loose shot is a
            // passage that ends there, not a splay fan.
            if (station is null || stubsAtStation[station.Value] == 1)
            {
                kept.Add(segment);
            }
        }

        // A centerline drawn as one short line — a couple of shots, no junctions — is all stubs
        // hanging off one station, indistinguishable from a fan, so the rule would erase it
        // outright. Nothing is a better answer than everything here: fall back to the unpruned
        // network, still sewn into polylines. Large surveys never reach this branch.
        return Merge(kept.Count == 0 ? segments : kept, nodes, keepAltitude);
    }

    /// <summary>Number of line components in a geometry (0 when null or empty).</summary>
    public static int PathCount(Geometry? geometry) =>
        geometry is null || geometry.IsEmpty ? 0 : geometry.NumGeometries;

    /// <summary>
    /// True when the skeleton is worth storing — it is not when the rule changed nothing,
    /// which is the case for a small hand-drawn centerline with no splays and no chains to sew.
    /// </summary>
    public static bool IsWorthStoring(MultiLineString source, MultiLineString skeleton) =>
        !skeleton.IsEmpty
        && (skeleton.NumGeometries < source.NumGeometries || skeleton.NumPoints < source.NumPoints);

    /// <summary>
    /// The station end of a stub, or null when the shot is not a stub (both ends carry other
    /// shots, or neither does).
    /// </summary>
    private static int? StationOfStub(int a, int b, int[] degree)
    {
        var looseA = degree[a] == 1;
        var looseB = degree[b] == 1;
        return looseA == looseB ? null : looseA ? b : a;
    }

    /// <summary>
    /// Splits every component into single shots and interns their endpoints on exact 3D
    /// identity. Zero-length shots are dropped — survey exports contain them (a station
    /// written twice), they carry no line work, and they would otherwise register as a
    /// second shot at their own station and so mask a splay as traverse.
    /// </summary>
    private static (List<(int A, int B)> Segments, List<Coordinate> Nodes) Explode(MultiLineString lines)
    {
        var ids = new Dictionary<(double X, double Y, double Z), int>();
        var nodes = new List<Coordinate>();
        var segments = new List<(int A, int B)>();

        int Intern(Coordinate coordinate)
        {
            var z = double.IsNaN(coordinate.Z) ? 0 : coordinate.Z;
            var key = (coordinate.X, coordinate.Y, z);
            if (ids.TryGetValue(key, out var existing))
            {
                return existing;
            }

            ids[key] = nodes.Count;
            nodes.Add(coordinate);
            return nodes.Count - 1;
        }

        for (var g = 0; g < lines.NumGeometries; g++)
        {
            if (lines.GetGeometryN(g) is not LineString line || line.IsEmpty)
            {
                continue;
            }

            var coordinates = line.Coordinates;
            for (var i = 1; i < coordinates.Length; i++)
            {
                var a = Intern(coordinates[i - 1]);
                var b = Intern(coordinates[i]);
                if (a != b)
                {
                    segments.Add((a, b));
                }
            }
        }

        return (segments, nodes);
    }

    /// <summary>
    /// Sews the retained shots into maximal polylines, running through every station where
    /// exactly two of them meet. Degrees are recomputed over the retained set — before
    /// pruning, a station carrying thirty splays is a junction and nothing would merge.
    /// </summary>
    private static MultiLineString Merge(
        List<(int A, int B)> kept, List<Coordinate> nodes, bool keepAltitude)
    {
        if (kept.Count == 0)
        {
            return Empty();
        }

        var degree = new int[nodes.Count];
        var incident = new List<int>?[nodes.Count];
        for (var i = 0; i < kept.Count; i++)
        {
            foreach (var node in new[] { kept[i].A, kept[i].B })
            {
                degree[node]++;
                (incident[node] ??= []).Add(i);
            }
        }

        var used = new bool[kept.Count];
        var lines = new List<LineString>();

        // The nodes carry whatever altitude the input had; a source with none reads back as NaN,
        // which PostGIS and every consumer would rather see as a flat zero than as a hole.
        Coordinate Vertex(int node) =>
            keepAltitude
                ? new CoordinateZ(
                    nodes[node].X, nodes[node].Y, double.IsNaN(nodes[node].Z) ? 0 : nodes[node].Z)
                : new Coordinate(nodes[node].X, nodes[node].Y);

        void Walk(int start)
        {
            used[start] = true;
            var chain = new LinkedList<int>();
            chain.AddLast(kept[start].A);
            chain.AddLast(kept[start].B);

            for (var forward = 0; forward < 2; forward++)
            {
                while (true)
                {
                    var tip = forward == 0 ? chain.Last!.Value : chain.First!.Value;
                    if (degree[tip] != 2)
                    {
                        break;
                    }

                    var next = incident[tip]?.FirstOrDefault(i => !used[i], -1) ?? -1;
                    if (next < 0)
                    {
                        break;
                    }

                    used[next] = true;
                    var other = kept[next].A == tip ? kept[next].B : kept[next].A;
                    if (forward == 0)
                    {
                        chain.AddLast(other);
                    }
                    else
                    {
                        chain.AddFirst(other);
                    }
                }
            }

            lines.Add(new LineString([.. chain.Select(Vertex)]) { SRID = 4326 });
        }

        // Start where a chain has to end, so the walk produces maximal polylines rather than
        // stopping in the middle of one; anything left after that is a closed loop.
        for (var i = 0; i < kept.Count; i++)
        {
            if (!used[i] && (degree[kept[i].A] != 2 || degree[kept[i].B] != 2))
            {
                Walk(i);
            }
        }

        for (var i = 0; i < kept.Count; i++)
        {
            if (!used[i])
            {
                Walk(i);
            }
        }

        return new MultiLineString([.. lines]) { SRID = 4326 };
    }

    private static MultiLineString Empty() => new([]) { SRID = 4326 };
}
