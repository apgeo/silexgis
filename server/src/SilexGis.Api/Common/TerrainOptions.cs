// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Common;

/// <summary>
/// The elevation model the 3D scene draws its ground from, if this installation has one.
///
/// <para>
/// There is no default and no fallback: with nothing configured the scene draws the smooth
/// reference ellipsoid, which needs no elevation server, no download and no pre-baking, and that
/// is the shipped behaviour. Terrain is an operator's deliberate step — bake a tile pyramid,
/// serve it, name it here — and everything in this class is inert until <see cref="Url"/> is set.
/// </para>
///
/// <para>
/// A deployment fact rather than an administrator's setting, so it lives in configuration
/// alongside the file store and the key path rather than in the admin pages: it names a directory
/// somebody put on a disk, and it changes when that changes.
/// </para>
/// </summary>
public sealed class TerrainOptions
{
    public const string SectionName = "Terrain";

    /// <summary>
    /// Where the tile pyramid is served from — the directory holding <c>layer.json</c>, with a
    /// trailing slash. Usually a path on this same installation (<c>/terrain/</c>); an absolute
    /// URL works too, at the cost of the property that nothing outside this installation is
    /// contacted while a cave is being looked at. Empty means no terrain.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// What the heights in those tiles are measured from. Wrong here means every cave sits about
    /// forty metres off its hillside, in one direction or the other, with nothing on screen to
    /// say so — which is why it is declared per source rather than assumed.
    /// </summary>
    public TerrainHeightDatum HeightDatum { get; set; } = TerrainHeightDatum.Orthometric;

    /// <summary>
    /// The local geoid undulation in metres — how far the geoid sits above the WGS84 ellipsoid
    /// here. Used only when <see cref="HeightDatum"/> is ellipsoidal, because that is the only
    /// case in which a surveyed altitude has to be moved before it will meet the drawn ground.
    /// Measured EGM2008 values over Romanian karst run +39 m to +45 m; the default of zero is the
    /// right answer for an orthometric source and a visible error for an ellipsoidal one, which
    /// is the way round that gets noticed.
    /// </summary>
    public double GeoidHeightM { get; set; }

    /// <summary>
    /// Credit line for the elevation data, shown by the scene. Copernicus GLO-30 and most other
    /// free models require attribution, and the pre-baker writes a placeholder into the pyramid's
    /// own metadata rather than a real one, so this is the place an installation states it.
    /// </summary>
    public string? Attribution { get; set; }

    /// <summary>True when an operator has actually named a pyramid to draw.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);

    /// <summary>
    /// <see cref="Url"/> with a trailing slash, which is what a tile client needs in order to
    /// resolve <c>layer.json</c> underneath it rather than beside it. Without one, a pyramid at
    /// <c>/terrain</c> is looked for at <c>/layer.json</c>, the single-page fallback answers with
    /// the application's own HTML and a 200 status, and the only symptom is a globe with no
    /// ground on it.
    /// </summary>
    public string ResolvedUrl
    {
        get
        {
            var url = (Url ?? string.Empty).Trim();
            return url.EndsWith('/') ? url : url + "/";
        }
    }

    /// <summary>
    /// Metres to add to a surveyed altitude before drawing it against this source's ground.
    /// Resolved here so that the sign lives in one place and the client is handed an answer
    /// rather than a datum it could interpret backwards.
    /// </summary>
    public double SurveyHeightOffsetM => GeoidOffset.SurveyToSceneOffsetM(HeightDatum, GeoidHeightM);

    /// <summary>
    /// Configurations that are accepted but are almost certainly not what was meant, as sentences
    /// for the log. Empty for an installation with no terrain and for a coherently described one.
    ///
    /// <para>
    /// None of these can be made an error. An installation whose elevation model is described
    /// oddly still runs, still draws every cave, and is better off saying so at startup than
    /// refusing to start — but each of these is silent on screen, and the whole point of stating
    /// the datum per source is that nobody discovers a forty-metre error months later.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ConfigurationWarnings()
    {
        var warnings = new List<string>();
        if (!IsConfigured)
        {
            // Values without a source are the state an operator reaches half way through setting
            // one up, and calling that out would be noise rather than a warning.
            return warnings;
        }

        if (HeightDatum == TerrainHeightDatum.Ellipsoidal && GeoidHeightM == 0)
        {
            warnings.Add(
                "Terrain:HeightDatum is Ellipsoidal but Terrain:GeoidHeightM is 0, so no correction "
                + "is applied. Surveyed altitudes will sit about one geoid undulation — roughly 40 m "
                + "over Romanian karst — below the ground drawn for them.");
        }

        if (HeightDatum != TerrainHeightDatum.Ellipsoidal && GeoidHeightM != 0)
        {
            warnings.Add(
                "Terrain:GeoidHeightM is set but Terrain:HeightDatum is Orthometric, which needs no "
                + "correction, so the value is ignored. Set the datum the tiles were baked with.");
        }

        if (string.IsNullOrWhiteSpace(Attribution))
        {
            warnings.Add(
                "Terrain:Attribution is empty. Freely available elevation models generally require "
                + "a credit line, and the scene has nothing to show without one.");
        }

        if (Uri.TryCreate(ResolvedUrl, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            warnings.Add(
                $"Terrain:Url points at another host ({absolute.Host}). Viewers' browsers will "
                + "request elevation tiles from it directly, so this installation is no longer "
                + "self-contained and that host learns which ground is being looked at.");
        }

        return warnings;
    }
}
