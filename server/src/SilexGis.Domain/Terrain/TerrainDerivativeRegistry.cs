// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SilexGis.Domain.Terrain;

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
            ComputeEdges = settings.ComputeEdges,
            ColourRamp = settings.Derivative == TerrainDerivative.ColourRelief
                ? settings.ColourRamp
                : [],
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
    /// Whether a picture computed from <paramref name="sourceBuildId"/> is out of date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is, whenever the elevation it was drawn from is not the elevation the installation now
    /// serves — including when there is no active build at all, because then nothing on screen
    /// agrees with the ground and saying so is the honest answer.
    /// </para>
    /// <para>
    /// A stale picture is still shown. Withdrawing it would leave a reader with nothing where
    /// there was something, over ground that has probably not changed much; what must never happen
    /// is showing it as though it were current, because a shaded relief that disagrees with the
    /// elevation beneath it looks like a fault in the cave data rather than in the tile.
    /// </para>
    /// </remarks>
    public static bool IsStale(Guid sourceBuildId, Guid? activeBuildId) =>
        activeBuildId is null || activeBuildId.Value != sourceBuildId;
}
