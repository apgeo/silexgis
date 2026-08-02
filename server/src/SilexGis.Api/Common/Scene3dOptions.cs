// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common;

/// <summary>
/// Installation-wide settings for the three-dimensional scene.
///
/// <para>
/// This is also where the installation <b>declares its vertical datum</b>, because nothing in
/// the data can be asked. Cave altitudes reach the system by hand (an entrance's altitude field)
/// or inside an uploaded survey, and neither GPX, KML, GeoJSON nor a Survex header states which
/// height system its elevations are on. Recording a per-import datum would therefore mean
/// recording a guess, so the assumption is stated once, here, where an operator can correct it.
/// </para>
///
/// <para>
/// The assumption: stored altitudes are orthometric — for Romania, normal heights on the Black
/// Sea 1975 system. A globe places points by ellipsoidal WGS84 height, and elevation models such
/// as Copernicus GLO-30 are on EGM2008, so both have to be reconciled with the stored value
/// before anything is drawn on terrain.
/// </para>
/// </summary>
public sealed class Scene3dOptions
{
    public const string SectionName = "Scene3d";

    /// <summary>
    /// Geoid undulation in metres to add to a stored altitude to obtain an ellipsoidal WGS84
    /// height (and to subtract going the other way). The default is the mid-range of measured
    /// EGM2008 undulations over Romanian karst — +44.46 m in Banat, +43.50 m in the Apuseni,
    /// +39.39 m in Piatra Craiului — so no Romanian installation is more than about three metres
    /// out, which is inside the accuracy of a hand-recorded cave altitude. Left uncorrected the
    /// error is roughly forty metres, enough to float every entrance visibly off its hillside.
    /// An installation outside that region must set its own value; zero disables the correction.
    /// </summary>
    public double GeoidOffsetM { get; set; } = 41.5;
}
