// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilexGis.Domain.Scans;

/// <summary>
/// One pair a scan was placed by, as it is kept: the point picked in the scan, the station it was
/// paired with, and how far apart the two ended up under the fitted placement.
/// </summary>
/// <param name="ScanX">The picked point, metres from the tiles' zero point along the tiles' +X.</param>
/// <param name="ScanY">The same, along +Y.</param>
/// <param name="ScanZ">The same, along +Z.</param>
/// <param name="Station">The station's name as the line plot's rows hold it.</param>
/// <param name="EastM">How far east of the station the placed point landed, metres.</param>
/// <param name="NorthM">How far north, metres.</param>
/// <param name="UpM">How far above, metres.</param>
/// <param name="HorizontalM">The residual on the plan, metres.</param>
/// <param name="DistanceM">The residual in space, metres — the figure the flag is judged on.</param>
/// <param name="Flagged">Whether the residual is over the installation's warning distance.</param>
public sealed record ScanControlRecordPair(
    double ScanX,
    double ScanY,
    double ScanZ,
    string Station,
    double EastM,
    double NorthM,
    double UpM,
    double HorizontalM,
    double DistanceM,
    bool Flagged);

/// <summary>
/// The control points a scan's placement was fitted to, kept beside the placement.
/// </summary>
/// <param name="LinePlotId">The line plot whose stations the pairs name.</param>
/// <param name="Pairs">The pairs, in the order they were given.</param>
/// <param name="RmsM">The root mean square of the residuals, metres.</param>
/// <param name="WarnM">The warning distance the flags were judged against, metres.</param>
/// <param name="TiltFitted">Whether a small tilt was fitted as well as the heading.</param>
public sealed record ScanControlRecord(
    Guid LinePlotId,
    IReadOnlyList<ScanControlRecordPair> Pairs,
    double RmsM,
    double WarnM,
    bool TiltFitted)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The record a stored document holds, or null for no document or one that does not read.</summary>
    public static ScanControlRecord? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScanControlRecord>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The record of a fit, pairing each residual with the station its pair named.</summary>
    public static ScanControlRecord Of(
        Guid linePlotId, IReadOnlyList<ScanControlPair> pairs, IReadOnlyList<string> stations, ScanControlFit fit)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(fit);
        return new(
            linePlotId,
            pairs.Select((p, i) =>
            {
                var r = fit.Residuals[i];
                return new ScanControlRecordPair(
                    p.ScanX, p.ScanY, p.ScanZ, stations[i], r.EastM, r.NorthM, r.UpM, r.HorizontalM, r.DistanceM, r.Flagged);
            }).ToList(),
            fit.RmsM,
            fit.WarnM,
            fit.TiltFitted);
    }
}
