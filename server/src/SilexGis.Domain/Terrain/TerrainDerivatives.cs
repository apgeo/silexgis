// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Terrain;

/// <summary>
/// The shaded and coloured pictures of the ground that can be computed from an elevation raster.
/// </summary>
/// <remarks>
/// <para>
/// Stored as a small integer on the row that records a computed raster, so the numbers are part of
/// the schema and are never renumbered. Adding a member is appending one.
/// </para>
/// <para>
/// This list is exactly what the bundled raster library can do in this process, and it is
/// deliberately shorter than the list of things a geomorphologist would ask for. Three families are
/// missing and are missing on purpose rather than by oversight: <b>geomorphons</b>, which the
/// library has no equivalent of at all and which would need a separate tool and a process boundary
/// this application does not have; <b>curvature</b>, for which there is no mode — it is a
/// second-derivative convolution that could be written here, but it would be new arithmetic with
/// nothing to check it against; and <b>flow direction, accumulation and wetness index</b>, which are
/// a different body of work entirely. None of them is approximated by anything in this list, and
/// none of the members below may be relabelled as one of them: a slope map presented as curvature is
/// a lie the reader has no way to detect.
/// </para>
/// </remarks>
public enum TerrainDerivative : short
{
    /// <summary>Shaded relief: how brightly the ground would be lit from a chosen direction.</summary>
    Hillshade = 0,

    /// <summary>How steep the ground is, in degrees or as a percentage.</summary>
    Slope = 1,

    /// <summary>Which compass direction the ground faces, in degrees clockwise from north.</summary>
    Aspect = 2,

    /// <summary>Terrain ruggedness: how different a cell is from the eight around it.</summary>
    RuggednessIndex = 3,

    /// <summary>Topographic position: how far a cell stands above or below its neighbourhood.</summary>
    PositionIndex = 4,

    /// <summary>The spread between the highest and lowest of a cell and its neighbours.</summary>
    Roughness = 5,

    /// <summary>Elevation painted with a colour ramp the caller supplies.</summary>
    ColourRelief = 6,
}

/// <summary>Which arithmetic is used to work out the slope of the ground at a cell.</summary>
/// <remarks>
/// Horn weights the eight neighbours and is the usual choice for real, noisy elevation data.
/// Zevenbergen–Thorne uses only the four cardinal neighbours: sharper on clean synthetic or
/// lidar-derived surfaces, noisier on anything sampled from photogrammetry.
/// </remarks>
public enum TerrainSurfaceFit : short
{
    /// <summary>Horn's eight-neighbour weighting.</summary>
    Horn = 0,

    /// <summary>Zevenbergen and Thorne's four-neighbour formulation.</summary>
    ZevenbergenThorne = 1,
}

/// <summary>What a slope raster's numbers mean.</summary>
public enum TerrainSlopeUnit : short
{
    /// <summary>Degrees from horizontal, 0 to 90.</summary>
    Degrees = 0,

    /// <summary>Rise over run as a percentage, unbounded above.</summary>
    Percent = 1,
}

/// <summary>Which ruggedness definition a terrain ruggedness raster uses.</summary>
/// <remarks>
/// Riley's is the mean of the squared differences to the eight neighbours, square-rooted — the
/// original definition, and the one published ruggedness classes are stated against. Wilson's is
/// the plain mean of the absolute differences, which is cheaper to reason about and gives different
/// numbers, so the two are never comparable and which was used has to be recorded.
/// </remarks>
public enum TerrainRuggednessFit : short
{
    /// <summary>Riley's root-mean-square of the differences to the eight neighbours.</summary>
    Riley = 0,

    /// <summary>Wilson's mean of the absolute differences.</summary>
    Wilson = 1,
}

/// <summary>How a hillshade is lit.</summary>
public enum TerrainHillshadeLighting : short
{
    /// <summary>One light, from a chosen compass direction and height.</summary>
    Single = 0,

    /// <summary>
    /// Four lights combined, so that ground facing away from a single sun is still readable.
    /// </summary>
    /// <remarks>
    /// The reason to prefer it for anything being interpreted rather than merely looked at: under a
    /// single light every slope facing away from it is flat black, and a doline sitting in that
    /// shadow is invisible in a picture that otherwise looks correct.
    /// </remarks>
    Multidirectional = 1,
}

/// <summary>One stop on an elevation colour ramp.</summary>
/// <param name="Elevation">The height, in metres, this colour is exactly at.</param>
public readonly record struct TerrainColourStop(
    double Elevation, byte Red, byte Green, byte Blue, byte Alpha = 255);

/// <summary>Everything that decides what one computed raster looks like.</summary>
/// <remarks>
/// Written down as one record because it is also what gets stored beside the finished raster: two
/// hillshades of the same ground lit from different directions are different pictures answering
/// different questions, and a registry that records only "hillshade" cannot tell them apart or say
/// whether the one on screen is the one that was asked for.
/// </remarks>
public sealed record TerrainDerivativeSettings
{
    /// <summary>Which picture to compute.</summary>
    public required TerrainDerivative Derivative { get; init; }

    /// <summary>How a hillshade is lit. Ignored by everything else.</summary>
    public TerrainHillshadeLighting Lighting { get; init; } = TerrainHillshadeLighting.Single;

    /// <summary>Where the light comes from, in degrees clockwise from north.</summary>
    /// <remarks>
    /// The conventional default is from the north-west. It is a convention worth keeping rather than
    /// a physical fact — lighting a northern-hemisphere landscape from the south-east, where the sun
    /// actually is in the morning, makes most readers see the valleys as ridges.
    /// </remarks>
    public double AzimuthDegrees { get; init; } = 315d;

    /// <summary>How high the light stands above the horizon, in degrees.</summary>
    public double AltitudeDegrees { get; init; } = 45d;

    /// <summary>How much to exaggerate height before the picture is computed.</summary>
    public double ZFactor { get; init; } = 1d;

    /// <summary>Which arithmetic works out the slope of the ground, where the picture needs one.</summary>
    public TerrainSurfaceFit SurfaceFit { get; init; } = TerrainSurfaceFit.Horn;

    /// <summary>What a slope raster's numbers mean.</summary>
    public TerrainSlopeUnit SlopeUnit { get; init; } = TerrainSlopeUnit.Degrees;

    /// <summary>Which ruggedness definition a ruggedness raster uses.</summary>
    public TerrainRuggednessFit RuggednessFit { get; init; } = TerrainRuggednessFit.Riley;

    /// <summary>
    /// Whether the outermost ring of cells is computed from the neighbours it does have.
    /// </summary>
    /// <remarks>
    /// Off, the edge is a one-cell band of nothing, and every raster of a build meets its neighbour
    /// with a hairline gap through the picture. On, the edge is computed from a partial
    /// neighbourhood and is therefore slightly wrong — but wrong by less than a missing line is,
    /// and the rasters of one build are separate files precisely because they are not resampled onto
    /// a common grid, so those seams are everywhere.
    /// </remarks>
    public bool ComputeEdges { get; init; } = true;

    /// <summary>The elevation ramp, for a colour relief and for nothing else.</summary>
    public IReadOnlyList<TerrainColourStop> ColourRamp { get; init; } = [];
}

/// <summary>What the settings have to satisfy before anything is computed from them.</summary>
public static class TerrainDerivativeRules
{
    /// <summary>The steepest a light may stand, and the shallowest.</summary>
    private const double MinAltitude = 0d;
    private const double MaxAltitude = 90d;

    /// <summary>How exaggerated a height may be asked to be.</summary>
    /// <remarks>
    /// Bounded at all because the factor multiplies elevations before any arithmetic runs, so a
    /// large one turns a gentle landscape into a wall of saturated pixels — a picture that is
    /// finished, valid and says nothing. The upper bound is generous; twenty times is already far
    /// past what anybody reads.
    /// </remarks>
    private const double MaxZFactor = 100d;

    /// <summary>Why these settings cannot be computed, or null when they can.</summary>
    public static string? Problem(TerrainDerivativeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Enum.IsDefined(settings.Derivative))
        {
            return "That is not a picture this installation knows how to compute.";
        }

        if (!double.IsFinite(settings.ZFactor) || settings.ZFactor <= 0d
            || settings.ZFactor > MaxZFactor)
        {
            return $"Height exaggeration is a number above zero and no more than {MaxZFactor}.";
        }

        if (settings.Derivative == TerrainDerivative.Hillshade)
        {
            if (!double.IsFinite(settings.AzimuthDegrees)
                || settings.AzimuthDegrees < 0d || settings.AzimuthDegrees >= 360d)
            {
                return "The light's direction is measured clockwise from north, from 0 up to 360.";
            }

            if (!double.IsFinite(settings.AltitudeDegrees)
                || settings.AltitudeDegrees <= MinAltitude || settings.AltitudeDegrees > MaxAltitude)
            {
                return "The light stands above the horizon, so its height is above 0 and at most 90.";
            }
        }

        if (settings.Derivative == TerrainDerivative.ColourRelief)
        {
            if (settings.ColourRamp.Count < 2)
            {
                return "A colour relief needs at least two heights to put colours between.";
            }

            if (settings.ColourRamp.Any(stop => !double.IsFinite(stop.Elevation)))
            {
                return "Every stop on a colour ramp sits at a real height.";
            }

            // Two stops at one height are two answers to the same question, and which one wins is
            // decided by the order they happen to be written in rather than by anybody's intent.
            if (settings.ColourRamp.Select(stop => stop.Elevation).Distinct().Count()
                != settings.ColourRamp.Count)
            {
                return "No two stops on a colour ramp sit at the same height.";
            }
        }
        else if (settings.ColourRamp.Count > 0)
        {
            return "A colour ramp only means something for a colour relief.";
        }

        return null;
    }
}

/// <summary>One raster to compute, and where the result goes.</summary>
/// <param name="SourcePath">
/// The elevation raster to read, as an absolute path. One of a build's prepared rasters: they are
/// each a file of their own at their own pixel size, deliberately never merged, so a derivative over
/// a build is one of these per raster rather than one for the build.
/// </param>
/// <param name="OutputPath">Where to write the finished raster, as an absolute path.</param>
public sealed record TerrainDerivativeRequest(
    string SourcePath, string OutputPath, TerrainDerivativeSettings Settings);

/// <summary>What a computed raster turned out to be.</summary>
/// <remarks>
/// Deliberately not the record that describes a prepared elevation raster, which carries the one
/// value standing for a hole and is checked against a single fixed number. These rasters do not all
/// have one: a shaded relief is whole bytes with nothing to spare for a hole marker, and the library
/// marks its edge with zero, which is also a legitimate darkness. Reusing that record would have
/// every hillshade judged unusable by a check written about elevation.
/// </remarks>
public sealed record ComputedTerrainRaster(
    string Path,
    int Width,
    int Height,
    double PixelSizeDegrees,
    double West,
    double South,
    double East,
    double North,
    long SizeBytes);

/// <summary>Computes the shaded and coloured pictures of the ground from an elevation raster.</summary>
public interface ITerrainDerivativeComputer
{
    /// <summary>Computes one raster and answers what it wrote.</summary>
    /// <exception cref="TerrainBuildException">
    /// The source could not be read, or the picture could not be computed. Carries the short code
    /// and, separately, whatever the raster library said.
    /// </exception>
    ComputedTerrainRaster Compute(TerrainDerivativeRequest request, CancellationToken ct);
}
