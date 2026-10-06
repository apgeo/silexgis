// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SilexGis.Domain.Terrain;

/// <summary>Why a stored picture of the ground is out of date.</summary>
/// <remarks>
/// Two different faults with two different remedies, which is why the reason travels with the
/// answer: a picture of replaced elevation is put right by asking for one over the elevation now
/// served, and a picture computed by superseded arithmetic by asking for the same picture again.
/// </remarks>
public enum TerrainDerivativeStaleness : short
{
    /// <summary>The elevation it was drawn from is not the elevation the installation serves.</summary>
    ElevationReplaced = 0,

    /// <summary>It was computed by arithmetic that has since been corrected.</summary>
    MethodRevised = 1,
}

/// <summary>
/// What makes two requests for a picture of the ground the same request, and what makes a stored
/// picture out of date.
/// </summary>
/// <remarks>
/// Both rules live here rather than at the places that ask them, because both are asked from more
/// than one place — the fingerprint by whatever accepts a request and by whatever stores the
/// result, the staleness by whatever lists layers and by whatever draws one — and two spellings of
/// either is how the same picture ends up stored twice, or shown as current after the ground under
/// it was replaced.
/// </remarks>
public static class TerrainDerivativeRegistry
{
    /// <summary>
    /// The settings with everything the chosen picture does not read set back to its default.
    /// </summary>
    /// <remarks>
    /// A hillshade computes the same file whatever the slope unit says, so two requests differing
    /// only in that are one request. Without this they would fingerprint differently, and the
    /// installation would compute, store and serve two identical rasters under two names, each
    /// claiming to answer a different question.
    /// </remarks>
    public static TerrainDerivativeSettings Normalise(TerrainDerivativeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var lit = settings.Derivative == TerrainDerivative.Hillshade;
        var sloped = settings.Derivative is TerrainDerivative.Slope or TerrainDerivative.Aspect
            or TerrainDerivative.Hillshade;

        // Only shaded relief is drawn with a height exaggeration, and only a single light has a
        // direction to come from: the four lights of a multidirectional shading are at fixed
        // compass points, so a direction given alongside it changes nothing about the file. Keeping
        // either of those would split one picture into several rows holding byte-identical rasters
        // on a volume that is neither swept nor backed up — and, worse, would let the settings
        // stored beside a picture state a light direction that picture never had.
        var lightDirected = lit && settings.Lighting == TerrainHillshadeLighting.Single;

        return new TerrainDerivativeSettings
        {
            Derivative = settings.Derivative,
            Lighting = lit ? settings.Lighting : TerrainHillshadeLighting.Single,
            AzimuthDegrees = lightDirected ? settings.AzimuthDegrees : 315d,
            AltitudeDegrees = lit ? settings.AltitudeDegrees : 45d,
            ZFactor = lit ? settings.ZFactor : 1d,
            SurfaceFit = sloped ? settings.SurfaceFit : TerrainSurfaceFit.Horn,
            SlopeUnit = settings.Derivative == TerrainDerivative.Slope
                ? settings.SlopeUnit
                : TerrainSlopeUnit.Degrees,
            RuggednessFit = settings.Derivative == TerrainDerivative.RuggednessIndex
                ? settings.RuggednessFit
                : TerrainRuggednessFit.Riley,
            // Contour lines are traced through the heights rather than computed from a cell's
            // neighbours, so they have no outermost ring to compute or leave out.
            ComputeEdges = settings.Derivative == TerrainDerivative.Contours || settings.ComputeEdges,
            ColourRamp = settings.Derivative == TerrainDerivative.ColourRelief
                ? settings.ColourRamp
                : [],

            // The spacing asked for, or the usual one written out: two requests for contours, one
            // naming twenty metres and one naming nothing, are one picture.
            ContourIntervalMetres = settings.Derivative == TerrainDerivative.Contours
                ? settings.ContourIntervalMetres ?? TerrainContourLines.DefaultIntervalMetres
                : null,
        };
    }

    /// <summary>The settings written down the one way they are ever written down.</summary>
    public static string Describe(TerrainDerivativeSettings settings) =>
        JsonSerializer.Serialize(Normalise(settings), JsonSerializerOptions.Web);

    /// <summary>
    /// The fingerprint of a request: lower-case hex, and the same for any two requests that would
    /// produce the same file.
    /// </summary>
    public static string Fingerprint(TerrainDerivativeSettings settings) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Describe(settings))));

    /// <summary>
    /// Which revision of its arithmetic each kind of picture is computed by today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded beside a picture's files when they are written, and compared with this when the
    /// picture is read. A picture is a file that outlives the code that drew it, and when that
    /// code is corrected nothing about the file changes: it goes on being served, looking exactly
    /// as right as it did before. The number is what lets the register say otherwise.
    /// </para>
    /// <para>
    /// Counted per kind, so that correcting one leaves the others current. Raised by one — never
    /// reused, never lowered — whenever the same request over the same elevation would no longer
    /// produce the same raster. Zero is how each kind was first computed.
    /// </para>
    /// <para>
    /// Shaded relief, steepness and facing are at one: they were first computed with a single
    /// distance for a degree in both directions, and are now computed with the two distances a
    /// degree really spans at each row's latitude.
    /// </para>
    /// </remarks>
    public static int MethodRevision(TerrainDerivative derivative) => derivative switch
    {
        TerrainDerivative.Hillshade or TerrainDerivative.Slope or TerrainDerivative.Aspect => 1,
        _ => 0,
    };

    /// <summary>
    /// Whether rasters computed at <paramref name="methodRevision"/> are what this kind of
    /// picture would no longer be computed as.
    /// </summary>
    /// <param name="derivative">Which picture it is.</param>
    /// <param name="version">
    /// How many times its rasters have been computed. A picture that has never been computed has
    /// no rasters to be wrong, whatever revision its row carries.
    /// </param>
    /// <param name="methodRevision">The revision recorded when its rasters were written.</param>
    public static bool IsMethodRevised(TerrainDerivative derivative, int version, int methodRevision) =>
        version > 0 && methodRevision != MethodRevision(derivative);

    /// <summary>
    /// Why a picture computed from <paramref name="sourceBuildId"/> is out of date, or null while
    /// it is current.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is, whenever the elevation it was drawn from is not the elevation the installation now
    /// serves — including when there is no active build at all, because then nothing on screen
    /// agrees with the ground and saying so is the honest answer. And it is, over elevation that
    /// has not changed at all, when the arithmetic that drew it has since been corrected: the
    /// picture then disagrees with the ground itself rather than with the tile beneath it.
    /// </para>
    /// <para>
    /// Replaced elevation is named first when both hold, because it is the fault that asking for
    /// the same picture again would not cure.
    /// </para>
    /// <para>
    /// A stale picture is still shown. Withdrawing it would leave a reader with nothing where
    /// there was something, over ground that has probably not changed much; what must never happen
    /// is showing it as though it were current, because a shaded relief that disagrees with the
    /// elevation beneath it looks like a fault in the cave data rather than in the tile.
    /// </para>
    /// </remarks>
    public static TerrainDerivativeStaleness? Staleness(
        Guid sourceBuildId,
        Guid? activeBuildId,
        TerrainDerivative derivative,
        int version,
        int methodRevision)
    {
        if (activeBuildId is null || activeBuildId.Value != sourceBuildId)
        {
            return TerrainDerivativeStaleness.ElevationReplaced;
        }

        return IsMethodRevised(derivative, version, methodRevision)
            ? TerrainDerivativeStaleness.MethodRevised
            : null;
    }
}
