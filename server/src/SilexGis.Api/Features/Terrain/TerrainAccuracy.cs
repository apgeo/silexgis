// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Terrain;

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
}
