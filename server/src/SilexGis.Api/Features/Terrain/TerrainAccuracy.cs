// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Terrain;

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
/// What an operator states about the accuracy of the elevation they build from, at submit or
/// afterwards. Sending nothing in every member clears the record.
/// </summary>
public sealed record TerrainAccuracyRequest(
    double? SourceResolutionM,
    double? VerticalAccuracyM,
    string? VerticalAccuracyStatistic,
    string? AccuracySource);

/// <summary>Checks that an accuracy record is a statement somebody can interpret.</summary>
public sealed class TerrainAccuracyRequestValidator : AbstractValidator<TerrainAccuracyRequest>
{
    public TerrainAccuracyRequestValidator()
    {
        RuleFor(x => x.SourceResolutionM)
            .GreaterThan(0d).LessThanOrEqualTo(100_000d)
            .When(x => x.SourceResolutionM.HasValue)
            .WithMessage("The ground spacing must be a positive number of metres.");
        RuleFor(x => x.VerticalAccuracyM)
            .GreaterThan(0d).LessThanOrEqualTo(10_000d)
            .When(x => x.VerticalAccuracyM.HasValue)
            .WithMessage("The vertical error must be a positive number of metres.");
        RuleFor(x => x.VerticalAccuracyStatistic)
            .Must(s => s is not null && TerrainAccuracyStatistics.All.Contains(s))
            .When(x => x.VerticalAccuracyM.HasValue || x.VerticalAccuracyStatistic is not null)
            .WithMessage("A vertical error is stated as RMSE, LE90 or LE95, and a statistic needs its figure.");
        RuleFor(x => x.VerticalAccuracyM)
            .NotNull()
            .When(x => x.VerticalAccuracyStatistic is not null)
            .WithMessage("A statistic needs the vertical error it describes.");
        RuleFor(x => x.AccuracySource).MaximumLength(500);
    }
}

/// <summary>Reading and writing the accuracy record of builds, in one place.</summary>
public static class TerrainAccuracy
{
    /// <summary>True when the request states nothing at all.</summary>
    public static bool IsEmpty(TerrainAccuracyRequest? r) =>
        r is null
        || (r.SourceResolutionM is null
            && r.VerticalAccuracyM is null
            && string.IsNullOrWhiteSpace(r.VerticalAccuracyStatistic)
            && string.IsNullOrWhiteSpace(r.AccuracySource));

    /// <summary>Writes a request onto a build, replacing whatever was recorded.</summary>
    public static void Apply(TerrainBuild build, TerrainAccuracyRequest? r)
    {
        var empty = IsEmpty(r);
        build.SourceResolutionM = empty ? null : r!.SourceResolutionM;
        build.VerticalAccuracyM = empty ? null : r!.VerticalAccuracyM;
        build.VerticalAccuracyStatistic = empty || r!.VerticalAccuracyM is null
            ? null
            : r.VerticalAccuracyStatistic;
        build.AccuracySource = empty || string.IsNullOrWhiteSpace(r!.AccuracySource)
            ? null
            : r.AccuracySource.Trim();
    }

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
