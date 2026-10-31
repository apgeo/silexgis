// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Features.Caves;

/// <summary>One entrance's recorded altitude set beside the ground height under it. No position.</summary>
/// <param name="EntranceId">The entrance.</param>
/// <param name="CaveId">The cave it belongs to.</param>
/// <param name="CaveName">The cave's name.</param>
/// <param name="EntranceName">The entrance's own name, when it has one.</param>
/// <param name="RecordedAltitudeM">The altitude on record, or null when none was recorded.</param>
/// <param name="ModelledAltitudeM">The ground height the elevation data gives under it, or null when
/// it could not answer.</param>
/// <param name="DifferenceM">Recorded minus modelled; null unless both exist.</param>
/// <param name="Verdict">Not recorded, not modelled, agrees or disagrees.</param>
/// <param name="FillableFromTerrain">Whether the record is empty and the model has a value, so the
/// fill action would do something. Whether the caller may write the cave is decided by the action.</param>
public sealed record EntranceAltitudeRowDto(
    Guid EntranceId,
    Guid CaveId,
    string? CaveName,
    string? EntranceName,
    decimal? RecordedAltitudeM,
    double? ModelledAltitudeM,
    double? DifferenceM,
    EntranceAltitudeVerdict Verdict,
    bool FillableFromTerrain);

/// <summary>
/// The altitude-disagreement report over the entrances the caller may place exactly.
/// </summary>
/// <param name="ScopeId">The cave or area the report was asked over.</param>
/// <param name="HasTerrain">Whether this installation has any elevation data built at all.</param>
/// <param name="EntranceCount">How many entrances the caller may place, which is the set every figure
/// below is taken over. It is deliberately not a count of the entrances in the scope.</param>
/// <param name="ComparedCount">How many of them had both a recorded and a modelled altitude.</param>
/// <param name="DisagreeingCount">How many of those differ by more than <paramref name="ToleranceM"/>.</param>
/// <param name="NotRecordedCount">How many have no recorded altitude.</param>
/// <param name="NotModelledCount">How many have a recorded altitude but no ground height under them.</param>
/// <param name="ToleranceM">The gap beyond which a pair is called a disagreement.</param>
/// <param name="Truncated">Whether <paramref name="Entrances"/> is cut short of the whole set.</param>
/// <param name="Entrances">Compared entrances first, widest gap first; then those that could not be
/// compared. Cut to a fixed length; the counts above are over the whole set.</param>
public sealed record AltitudeDisagreementDto(
    Guid ScopeId,
    bool HasTerrain,
    int EntranceCount,
    int ComparedCount,
    int DisagreeingCount,
    int NotRecordedCount,
    int NotModelledCount,
    double ToleranceM,
    bool Truncated,
    IReadOnlyList<EntranceAltitudeRowDto> Entrances);

/// <summary>Which cave or area to report on. The routes take nothing else.</summary>
public sealed record AltitudeDisagreementRequest(Guid Id);

/// <summary>Refuses a request naming nothing before it reaches the access check.</summary>
public sealed class AltitudeDisagreementRequestValidator : AbstractValidator<AltitudeDisagreementRequest>
{
    public AltitudeDisagreementRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

/// <summary>Which entrance to fill. The action takes nothing else.</summary>
public sealed record FillEntranceAltitudeRequest(Guid Id);

/// <summary>Refuses a request naming no entrance.</summary>
public sealed class FillEntranceAltitudeRequestValidator : AbstractValidator<FillEntranceAltitudeRequest>
{
    public FillEntranceAltitudeRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
