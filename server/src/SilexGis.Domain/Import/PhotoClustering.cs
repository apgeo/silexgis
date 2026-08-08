// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Import;

/// <summary>One positioned picture as clustering sees it: an identity, a place and a time.</summary>
public sealed record PhotoFix(Guid FileId, double Longitude, double Latitude, DateTimeOffset? CapturedAt);

/// <summary>
/// Pictures of one place, and where that place is.
/// </summary>
/// <param name="Key">
/// The candidate's identity, and the key a review's decisions are stored under. It is the
/// lowest file id among the members, so the same drop reviewed twice produces the same keys and
/// a decision made before lunch still names the same candidate after it.
/// </param>
public sealed record PhotoCluster(
    Guid Key,
    double Longitude,
    double Latitude,
    IReadOnlyList<Guid> FileIds);

/// <summary>
/// Groups a drop's pictures into places.
///
/// <para>
/// Twelve photographs of one entrance are one candidate with a gallery, not twelve points to
/// reject one at a time — which is the whole difference between a review somebody finishes and
/// one they abandon. The grouping is by distance alone: time would seem to help, but a return
/// visit to the same hole an hour later is the same place, and two different shafts
/// photographed a minute apart while walking a ridge are not.
/// </para>
/// <para>
/// A picture joins the nearest group whose centre is within the radius, and the centre then
/// moves. Nearest-centre rather than nearest-member deliberately: chaining along a line of
/// pictures — each one within the radius of the last — would walk a single candidate down a
/// whole valley, which is exactly what a reviewer would then have to undo by hand.
/// </para>
/// </summary>
public static class PhotoClustering
{
    /// <summary>
    /// The drop's pictures as places. The order of the result is the order the places were
    /// first seen, so it follows the order below rather than anything about the registry.
    /// </summary>
    /// <remarks>
    /// Input order decides which of two equally-good groupings comes out, so it is fixed here
    /// rather than left to the caller: by capture time, then by file id, with pictures that
    /// state no time last. Sorting by time makes the grouping match how the photographs were
    /// actually taken — a walk-up, the entrance, a few inside — instead of how a file system
    /// happened to list them.
    /// </remarks>
    public static IReadOnlyList<PhotoCluster> Group(IEnumerable<PhotoFix> fixes, double radiusMeters)
    {
        var ordered = fixes
            .OrderBy(f => f.CapturedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(f => f.FileId)
            .ToList();

        var clusters = new List<Builder>();
        foreach (var fix in ordered)
        {
            Builder? nearest = null;
            var nearestDistance = double.MaxValue;
            foreach (var cluster in clusters)
            {
                var distance = Geodesy.DistanceMeters(
                    fix.Longitude, fix.Latitude, cluster.Longitude, cluster.Latitude);
                if (distance <= radiusMeters && distance < nearestDistance)
                {
                    nearest = cluster;
                    nearestDistance = distance;
                }
            }

            if (nearest is null)
            {
                clusters.Add(new Builder(fix));
            }
            else
            {
                nearest.Add(fix);
            }
        }

        return [.. clusters.Select(c => c.Build())];
    }

    /// <summary>
    /// A group as it is being built: the members, and the running mean of their positions.
    /// </summary>
    /// <remarks>
    /// Longitudes are averaged as offsets from the first member's rather than directly. Over
    /// twenty-five metres the difference is nothing anywhere on earth except across the
    /// antimeridian, where averaging 179.9999 with -179.9999 gives zero — a group of pictures
    /// of one hole placed in the Gulf of Guinea. Cheap to do correctly, so it is done correctly.
    /// </remarks>
    private sealed class Builder(PhotoFix first)
    {
        private readonly List<Guid> members = [first.FileId];
        private readonly double originLongitude = first.Longitude;
        private double longitudeOffsetSum;
        private double latitudeSum = first.Latitude;

        public double Longitude { get; private set; } = first.Longitude;

        public double Latitude { get; private set; } = first.Latitude;

        public void Add(PhotoFix fix)
        {
            members.Add(fix.FileId);
            longitudeOffsetSum += Unwrap(fix.Longitude - originLongitude);
            latitudeSum += fix.Latitude;
            Longitude = Wrap(originLongitude + (longitudeOffsetSum / members.Count));
            Latitude = latitudeSum / members.Count;
        }

        public PhotoCluster Build() => new(
            members.Min(),
            Longitude,
            Latitude,
            [.. members]);

        /// <summary>A difference of longitudes as the short way round rather than the long way.</summary>
        private static double Unwrap(double delta) => delta switch
        {
            > 180 => delta - 360,
            < -180 => delta + 360,
            _ => delta,
        };

        private static double Wrap(double longitude) => longitude switch
        {
            > 180 => longitude - 360,
            < -180 => longitude + 360,
            _ => longitude,
        };
    }
}
