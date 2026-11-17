// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;

namespace SilexGis.Api.Features.Caves;

/// <summary>
/// One cave whose passage lies under, or beside, a surface feature.
/// </summary>
/// <param name="CaveId">The cave.</param>
/// <param name="CaveName">Its name.</param>
/// <param name="LateralOffsetM">Plan distance, metres, from the feature to the nearest part of the
/// cave's line work. Zero means the line work runs under the feature's outline.</param>
/// <param name="Basis">Which body of line work the figures below were read off. A depth over the
/// surveyor's own flags and one over a shape-based reduction are different claims.</param>
/// <param name="IsApproximation">True when <paramref name="Basis"/> is the shape-based reduction.</param>
/// <param name="HasAltitudes">Whether the line work carried altitudes at all. False means no
/// overburden could be worked out for this cave, and every figure below that depends on one is null.</param>
/// <param name="PassageLongitude">Where the measured station lies, degrees: the station of the
/// overburden profile nearest the point of the line work that is closest to the feature, so the
/// figure is the one that cave's own profile shows at that place. Null when there is no station.</param>
/// <param name="PassageLatitude">Latitude of the same station.</param>
/// <param name="PassageAltitudeM">Altitude of the passage at that station.</param>
/// <param name="Outcome">What the elevation reader said at that station; null when none was asked.</param>
/// <param name="GroundAltitudeM">Ground height there; null when unread.</param>
/// <param name="OverburdenM">Rock overhead there, ground less passage; null when either is unknown,
/// never nought. Negative where the survey reads above its own ground.</param>
public sealed record FeaturePassageDto(
    Guid CaveId,
    string? CaveName,
    double LateralOffsetM,
    SurveySegmentBasis Basis,
    bool IsApproximation,
    bool HasAltitudes,
    double? PassageLongitude,
    double? PassageLatitude,
    double? PassageAltitudeM,
    DemSampleOutcome? Outcome,
    double? GroundAltitudeM,
    double? OverburdenM);

/// <summary>
/// The caves with passage beneath a surface feature — a doline or sinkhole, polygon or point — or
/// beneath any outline, which is the same question the other way round.
///
/// <para>
/// <b>Every cave listed is one the caller could already place exactly</b>, and so is the feature.
/// A feature or cave the caller may not place is simply not here, and the refusal for a feature is
/// the same "no such feature" as for one that does not exist. The list, its order and whether it is
/// cut short are all computed after that gate, so none of them can say that a protected cave lies
/// near a place.
/// </para>
/// </summary>
/// <param name="FeatureId">The feature asked about.</param>
/// <param name="FeatureName">Its name.</param>
/// <param name="IsPolygon">True for an outline, false for a point.</param>
/// <param name="DistanceM">The reach used, metres; zero asks for passage under an outline only.</param>
/// <param name="HasTerrain">Whether this installation has any prepared elevation data. Separates
/// "no terrain built" from "terrain does not reach here".</param>
/// <param name="Truncated">True when more caves qualified than are listed.</param>
/// <param name="Passages">Nearest first.</param>
public sealed record FeaturePassagesDto(
    Guid FeatureId,
    string? FeatureName,
    bool IsPolygon,
    double DistanceM,
    bool HasTerrain,
    bool Truncated,
    IReadOnlyList<FeaturePassageDto> Passages);

/// <summary>What the route is asked: a feature and how far to look.</summary>
/// <param name="Id">The feature.</param>
/// <param name="DistanceM">How far from the feature, in plan, a passage may lie and still be
/// listed; defaults to <see cref="FeaturePassagesLimits.DefaultDistanceM"/>.</param>
public sealed record FeaturePassagesRequest(Guid Id, double? DistanceM);

/// <summary>Refuses a malformed request before the access check.</summary>
public sealed class FeaturePassagesRequestValidator : AbstractValidator<FeaturePassagesRequest>
{
    public FeaturePassagesRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DistanceM)
            .InclusiveBetween(0, FeaturePassagesLimits.MaxDistanceM)
            .When(x => x.DistanceM is not null);
    }
}

/// <summary>The bounds the route is held to, in one place so validator and handler agree.</summary>
public static class FeaturePassagesLimits
{
    /// <summary>Fifty metres: a doline's own reach, wide enough to take in the survey error of a
    /// passage under it.</summary>
    public const double DefaultDistanceM = 50d;

    /// <summary>Half a kilometre. Past this a passage is not "beneath" anything in the sense the
    /// question means.</summary>
    public const double MaxDistanceM = 500d;

    /// <summary>How many caves are listed. Each costs one profile read and one ground height, and
    /// the elevation reader answers one request at a time for the whole installation.</summary>
    public const int MaxCaves = 25;

    /// <summary>How many caves may be projected and measured at all.</summary>
    public const int MaxCandidates = 200;
}
