// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Common;

/// <summary>
/// The metric coordinate system this installation measures in.
///
/// <para>
/// Everything is stored in WGS 84, whose units are degrees. That is the right thing to store — it
/// is unambiguous anywhere on earth — but it cannot be measured in: a degree of longitude is about
/// 111 km at the equator and about 74 km at the latitude of the Carpathians, so a distance
/// computed in degrees is not a distance. Plane and geodesic distances have a way around this
/// (the geography type answers in metres), but three-dimensional distance does not: the 3D
/// functions are cartesian and expect all three ordinates in the same unit. A cave that comes
/// within twenty metres of another one is a fact about metres, so the answer needs a projected
/// system to be computed in.
/// </para>
/// <para>
/// It is per-installation rather than derived per query because a projected system is only honest
/// over the region it was made for, and an installation serves one group working one area. Picking
/// a zone per row would also mean two caves in different zones could not be compared at all, which
/// is exactly the question that most wants answering near a zone boundary.
/// </para>
/// </summary>
public sealed class SpatialOptions
{
    public const string SectionName = "Spatial";

    /// <summary>
    /// The EPSG code of the projected system distances are computed in.
    ///
    /// <para>
    /// The default is UTM zone 35 north on WGS 84, which covers the eastern Carpathians. An
    /// installation elsewhere sets its own zone; the value is validated when the application
    /// starts, so a wrong one is a refusal to boot rather than a wrong number in a report.
    /// </para>
    /// <para>
    /// A national grid is a tempting choice and is deliberately not the default. Romania's own
    /// (EPSG:3844) is defined on a different datum, so a transform into it carries a datum shift
    /// whose horizontal result depends on the altitude supplied with the point — which means the
    /// same cave passage yields slightly different plan coordinates depending on whether its Z is
    /// present. That is tolerable for mapping and wrong for a measurement this application then
    /// compares against another measurement.
    /// </para>
    /// </summary>
    public int WorkingSrid { get; set; } = 32635;
}

/// <summary>
/// Refuses to start on a working SRID the projection library cannot resolve.
///
/// <para>
/// The alternative is that the first query needing metres fails, deep inside a request, with a
/// message about a projection rather than about a setting — and only for whoever happened to ask
/// the first distance question, which may be weeks after the typo was made.
/// </para>
/// </summary>
public sealed class SpatialOptionsValidator(ICrsRegistry crs) : IValidateOptions<SpatialOptions>
{
    public ValidateOptionsResult Validate(string? name, SpatialOptions options)
    {
        if (options.WorkingSrid <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{SpatialOptions.SectionName}:{nameof(SpatialOptions.WorkingSrid)} must be a positive EPSG code; "
                + $"got {options.WorkingSrid}.");
        }

        if (crs.Proj4(options.WorkingSrid) is null)
        {
            return ValidateOptionsResult.Fail(
                $"{SpatialOptions.SectionName}:{nameof(SpatialOptions.WorkingSrid)} is EPSG:{options.WorkingSrid}, "
                + "which the bundled projection database does not know. Set a projected system covering this "
                + "installation's area — a UTM zone on WGS 84 is the usual choice.");
        }

        return ValidateOptionsResult.Success;
    }
}
