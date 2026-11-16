// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// How good the elevation of one build is: how finely it samples the ground and how far off its
/// heights may be, with the statistic the error is stated as and where the figures come from.
/// </summary>
/// <remarks>
/// <para>
/// Carried on every answer that reads a build — the build itself, a ground-height probe and a
/// picture computed from it — because a height without its error is a number that looks more exact
/// than it is, and the reader of a probe has no other way to learn what the ground was built from.
/// </para>
/// <para>
/// Absent (null) when nothing is recorded. Each member is also null when that one figure is not
/// known: "not recorded" is never zero, which would read as a perfect elevation model.
/// </para>
/// </remarks>
/// <param name="SourceResolutionM">Ground spacing of the elevation, in metres.</param>
/// <param name="VerticalAccuracyM">Vertical error in metres, as the statistic that follows.</param>
/// <param name="VerticalAccuracyStatistic">RMSE, LE90 or LE95; null exactly when the error is.</param>
/// <param name="AccuracySource">Where the figures come from, in words.</param>
public sealed record TerrainAccuracyDto(
    double? SourceResolutionM,
    double? VerticalAccuracyM,
    string? VerticalAccuracyStatistic,
    string? AccuracySource);

/// <summary>
/// Reading the accuracy record of terrain builds, shared by every answer that reads heights from a
/// build — the terrain routes themselves and the cave answers (overburden, entrance altitude) that
/// sample the same ground.
/// </summary>
public static class TerrainAccuracyReader
{
    /// <summary>The record of a build, or null when it holds nothing.</summary>
    public static TerrainAccuracyDto? From(TerrainBuild build) =>
        From(build.SourceResolutionM, build.VerticalAccuracyM, build.VerticalAccuracyStatistic, build.AccuracySource);

    private static TerrainAccuracyDto? From(
        double? resolutionM, double? accuracyM, string? statistic, string? source) =>
        resolutionM is null && accuracyM is null && source is null
            ? null
            : new TerrainAccuracyDto(resolutionM, accuracyM, statistic, source);

    /// <summary>The record of the build the 3D scene draws, or null when there is none or it holds nothing.</summary>
    public static async Task<TerrainAccuracyDto?> OfActiveBuildAsync(SilexGisDbContext db, CancellationToken ct)
    {
        var row = await db.TerrainBuilds.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new
            {
                b.SourceResolutionM,
                b.VerticalAccuracyM,
                b.VerticalAccuracyStatistic,
                b.AccuracySource,
            })
            .FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : From(row.SourceResolutionM, row.VerticalAccuracyM, row.VerticalAccuracyStatistic, row.AccuracySource);
    }

    /// <summary>The record of each named build that has one, by build id.</summary>
    public static async Task<Dictionary<Guid, TerrainAccuracyDto>> OfBuildsAsync(
        SilexGisDbContext db, IReadOnlyCollection<Guid> buildIds, CancellationToken ct)
    {
        var rows = await db.TerrainBuilds.AsNoTracking()
            .Where(b => buildIds.Contains(b.Id))
            .Select(b => new
            {
                b.Id,
                b.SourceResolutionM,
                b.VerticalAccuracyM,
                b.VerticalAccuracyStatistic,
                b.AccuracySource,
            })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, TerrainAccuracyDto>();
        foreach (var r in rows)
        {
            if (From(r.SourceResolutionM, r.VerticalAccuracyM, r.VerticalAccuracyStatistic, r.AccuracySource) is { } dto)
            {
                result[r.Id] = dto;
            }
        }

        return result;
    }
}
