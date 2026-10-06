// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>
/// Installation-wide map rendering limits. The defaults come from measurements on real survey
/// exports: a cave's line work is dominated by splays, drawing costs one canvas path per line
/// component, and the splays only carry information below roughly 0.4 ground metres per pixel.
/// So overview zooms get the stored skeleton, and full detail arrives only when the viewport is
/// small enough to bound what it costs.
/// </summary>
public sealed class MapOptions
{
    public const string SectionName = "Map";

    /// <summary>
    /// The most point features any one map layer request answers with — entrances, surface
    /// features, photos, trip logs and the rows of an imported GPS file alike.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cap rather than a page: these endpoints answer a viewport, and a viewport holding more
    /// than this is one nobody can read anyway. What it protects is the browser, which draws every
    /// feature it is given whether or not two of them land on the same pixel.
    /// </para>
    /// <para>
    /// Configurable because the number that is generous for a map of caves is mean for a map of an
    /// imported track log: a single day's GPS recording is routinely tens of thousands of points,
    /// and an installation that imports those should be able to see all of them without a rebuild.
    /// Raising it costs browser memory and draw time, not server work — the query is bounded by
    /// the viewport long before it is bounded by this.
    /// </para>
    /// </remarks>
    public int MaxPoints { get; set; } = 10000;

    /// <summary>
    /// From this zoom up the map serves bbox-clipped full detail (splays included) instead of
    /// the skeleton — provided the clipped result fits <see cref="CenterlineMaxPaths"/>.
    /// The default is where the splays start to be visible at all.
    /// </summary>
    public int CenterlineDetailZoom { get; set; } = 18;

    /// <summary>
    /// Line components a single request may serve across all centerlines. Past it, remaining
    /// centerlines are reported as withheld rather than drawn. This, not the zoom, is what
    /// protects a cave whose whole footprint fits on one screen.
    /// </summary>
    public int CenterlineMaxPaths { get; set; } = 25000;

    /// <summary>Hard ceiling on a client-requested <see cref="CenterlineMaxPaths"/> override.</summary>
    public int CenterlineMaxPathsLimit { get; set; } = 100000;

    /// <summary>Below this zoom, centerlines bigger than <see cref="CenterlineGatePaths"/> are withheld.</summary>
    public int CenterlineGateZoom { get; set; } = 12;

    /// <summary>Skeleton size at which a centerline is too heavy for an overview view.</summary>
    public int CenterlineGatePaths { get; set; } = 20000;

    /// <summary>
    /// The zoom from which the 3D scene may ask for the wall meshes of every cave in its view
    /// rather than of the one selected cave.
    /// </summary>
    /// <remarks>
    /// A floor rather than a courtesy. A wall mesh is fetched and held whole — there is no coarser
    /// form of one to serve to a wide view — so the only thing that bounds how many a view can
    /// hold is how much ground the view covers. Below this a view is a district, not a hillside,
    /// and the walls in it would be specks that each cost their full size.
    /// </remarks>
    public int MeshesInViewMinZoom { get; set; } = 14;

    /// <summary>
    /// The most caves one request for the wall meshes in a view answers with, nearest the middle
    /// of the view first. The caves beyond it are counted in the answer and not described.
    /// </summary>
    /// <remarks>
    /// Where a person starts, not where they must stay: a request may name a count of its own, up
    /// to <see cref="MeshesInViewMaxCavesLimit"/>.
    /// </remarks>
    public int MeshesInViewMaxCaves { get; set; } = 12;

    /// <summary>Hard ceiling on a client-requested <see cref="MeshesInViewMaxCaves"/> override.</summary>
    /// <remarks>
    /// A person may raise the count for their own browser — a workstation can hold more walls
    /// than a phone — but every mesh is fetched from this installation, so the operator keeps
    /// the last word on how much one view may pull. A default configured above the ceiling is
    /// held to it.
    /// </remarks>
    public int MeshesInViewMaxCavesLimit { get; set; } = 60;

    /// <summary>
    /// How many bytes of wall mesh the 3D scene may hold at once for the caves in its view.
    /// </summary>
    /// <remarks>
    /// The number that actually protects a browser. Meshes are usually a few hundred kilobytes,
    /// but the largest measured one is 54.7 MB to fetch and roughly twice that once it is on the
    /// graphics card, so a count alone would let a dozen large caves ask for gigabytes. It is
    /// spent by the client, nearest cave first, against the sizes the answer states; the server
    /// publishes it so that every client of an installation starts from the same budget; a
    /// person may then set their own, up to <see cref="MeshesInViewMaxBytesLimit"/>.
    /// </remarks>
    public long MeshesInViewMaxBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Hard ceiling on a person's own <see cref="MeshesInViewMaxBytes"/>.</summary>
    /// <remarks>
    /// Published rather than enforced here, because the budget is spent in the browser. It is
    /// still the operator's number for the same reason the cave ceiling is: the bytes a raised
    /// budget buys are served by this installation, to every member who raises it. A default
    /// configured above the ceiling is held to it.
    /// </remarks>
    public long MeshesInViewMaxBytesLimit { get; set; } = 512L * 1024 * 1024;

    /// <summary>
    /// Simplification tolerance in screen pixels. Nearly a no-op on splay-heavy surveys, whose
    /// components are single shots with no interior vertices to drop, but it does thin ordinary
    /// imported line work.
    /// </summary>
    public double CenterlineSimplifyPixels { get; set; } = 1.0;

    /// <summary>
    /// Tolerance in degrees of longitude for a given zoom. Against a Web Mercator display a
    /// pixel spans 360/(256·2^zoom) degrees of longitude, with no latitude term.
    /// </summary>
    public double SimplifyToleranceDegrees(int zoom) =>
        CenterlineSimplifyPixels * 360d / (256d * Math.Pow(2, Math.Clamp(zoom, 0, 24)));

    /// <summary>
    /// How many caves one request for the walls in a view is answered with: the count the caller
    /// named, or the installation's default when they named none — never fewer than one cave and
    /// never more than the ceiling, which is what holds a default configured above it.
    /// </summary>
    public int MeshesInViewCaves(int? requested = null) =>
        Math.Clamp(requested ?? MeshesInViewMaxCaves, 1, Math.Max(1, MeshesInViewMaxCavesLimit));

    /// <summary>The byte budget a scene starts from: the configured default, held to its ceiling.</summary>
    public long MeshesInViewBytes() => Math.Min(MeshesInViewMaxBytes, MeshesInViewMaxBytesLimit);
}
