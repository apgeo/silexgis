// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;

namespace SilexGis.Api.Features.Import;

/// <summary>The nearest thing already in the registry, among what this caller may locate.</summary>
public sealed record ImportDuplicateDto(
    Guid FeatureId,
    string? Name,
    FeatureKind Kind,
    double DistanceMeters,
    double NameSimilarity,
    Guid? CaveFeatureId,
    string? CaveName);

/// <summary>
/// One row of the review table.
///
/// <para>
/// <c>geom</c> is the candidate's own point, and null for a track or an area: those are drawn
/// by the file's own map layer, which already exists, and a page of tracks would otherwise send
/// tens of thousands of vertices to draw a preview of a table.
/// </para>
/// </summary>
public sealed record ImportCandidateDto(
    long SourceId,
    CandidateGeometry Geometry,
    string? SourceName,
    string? SourceDescription,
    string? SourceCode,
    double? SourceElevation,
    string? RuleId,
    string? RuleName,
    IReadOnlyList<string> ConflictingRuleNames,
    ImportTargetKind? ProposedKind,
    string? ProposedCaveTypeCode,
    string? ProposedEntranceTypeCode,
    string? ProposedFeatureTypeCode,
    string? ProposedName,
    GeoJsonGeometry? Geom,
    ImportDuplicateDto? Duplicate,
    ImportDecision? Decision);

/// <summary>How many candidates one rule claimed — the dry run, rule by rule.</summary>
public sealed record RuleHitDto(string RuleId, string RuleName, int Count);

/// <summary>
/// What the file amounts to under a rule set, before anything is created.
///
/// <para>
/// <c>filteredSourceIds</c> is every row matching the current filter. <c>selectableSourceIds</c>
/// is the subset something says what to become — a proposal from a rule, or a decision the
/// reviewer has already made — and it is what "select all" selects. The two are separate
/// because they disagree: a row nobody has said anything about cannot be created, and a bulk
/// selector that took it anyway would produce a failure line per row at confirmation. The
/// server answers this rather than the browser, because a decision made on page one has to
/// count for a row on page nine.
/// </para>
/// <para>
/// <c>truncated</c> says the file is larger than one review reads, and the two row counts say
/// by how much; a silently shortened candidate list reads exactly like a complete one.
/// </para>
/// </summary>
public sealed record ImportPreviewDto(
    IReadOnlyList<ImportCandidateDto> Items,
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<long> FilteredSourceIds,
    IReadOnlyList<long> SelectableSourceIds,
    int CandidateCount,
    int MatchedCount,
    int UnmatchedCount,
    int TrackCount,
    int AreaCount,
    bool Truncated,
    int FileRowCount,
    int ScannedRowCount,
    IReadOnlyList<RuleHitDto> RuleHits,
    TermRuleSetDto? RuleSet);

/// <summary>
/// Runs a rule set over a loaded file. A computation over a body, so a POST.
/// <c>rule</c> filters to what one rule claimed, or to <c>none</c> for the rows no rule did;
/// <c>hasDuplicate</c> filters to candidates with, or without, something already nearby.
/// </summary>
public sealed record ImportPreviewRequest(
    ImportOptions Options,
    int? Page,
    int? PageSize,
    string? Rule,
    ImportTargetKind? Kind,
    CandidateGeometry? Geometry,
    string? Search,
    bool? HasDuplicate);

/// <summary>
/// A review in progress, and what the installation lets this caller do with it —
/// <c>allowCreateWithoutReview</c> is the installation's setting, not a permission.
/// </summary>
public sealed record ImportSessionDto(
    Guid GeofileId,
    ImportOptions Options,
    IReadOnlyDictionary<string, ImportDecision> Decisions,
    bool AllowCreateWithoutReview,
    DateTimeOffset? UpdatedAt);

/// <summary>Saves the review. Sent as the reviewer works, so a closed tab costs nothing.</summary>
public sealed record ImportSessionWriteRequest(
    ImportOptions Options,
    IReadOnlyDictionary<string, ImportDecision> Decisions);

/// <summary>
/// Confirms a review: everything selected is created, as one revertible unit.
/// <c>withoutReview</c> creates everything the rules claimed instead of a selection — refused
/// unless the installation allows it, and the caller still needs the right to create features.
/// </summary>
public sealed record ImportCommitRequest(
    ImportOptions Options,
    IReadOnlyList<long> Selection,
    IReadOnlyDictionary<string, ImportDecision> Decisions,
    bool WithoutReview);

/// <summary>A candidate that could not be created, and why.</summary>
public sealed record ImportFailureDto(long SourceId, string? Name, string Code, string Reason);

/// <summary>What one confirmation did.</summary>
public sealed record ImportCommitResultDto(ImportBatchDto Batch, IReadOnlyList<ImportFailureDto> Failures);

/// <summary>One confirmation, as the history lists it.</summary>
public sealed record ImportBatchDto(
    Guid Id,
    Guid? GeofileId,
    string? GeofileName,
    Guid? TermRuleSetId,
    string? TermRuleSetName,
    Guid ConfirmedByUserId,
    ImportBatchMode Mode,
    int CreatedCount,
    int AttachedCount,
    int SkippedCount,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset? RevertedAt,
    Guid? RevertedByUserId,
    bool CanRevert);

/// <summary>One line of a batch: what a source row became, under which rule.</summary>
public sealed record ImportBatchItemDto(
    long Id,
    Guid? FeatureId,
    string? FeatureName,
    FeatureKind? FeatureKind,
    bool FeatureDeleted,
    Guid? AttachedToFeatureId,
    long? SourceFeatureId,
    string? RuleId,
    string? RuleName,
    ImportDecisionAction Action);

/// <summary>
/// Where an object came from: which file, which rule, who confirmed it, when. The source
/// properties are the row's own attributes verbatim — what makes a bad mapping recoverable
/// months later without the original file.
/// </summary>
public sealed record ImportProvenanceDto(
    ImportBatchDto Batch,
    ImportBatchItemDto Item,
    System.Text.Json.JsonElement SourceProperties);

public sealed class ImportPreviewRequestValidator : AbstractValidator<ImportPreviewRequest>
{
    public ImportPreviewRequestValidator()
    {
        RuleFor(x => x.Options).NotNull().SetValidator(new ImportOptionsValidator()!);
    }
}

public sealed class ImportSessionWriteRequestValidator : AbstractValidator<ImportSessionWriteRequest>
{
    public ImportSessionWriteRequestValidator()
    {
        RuleFor(x => x.Options).NotNull().SetValidator(new ImportOptionsValidator()!);
        RuleFor(x => x.Decisions).NotNull();
    }
}

public sealed class ImportCommitRequestValidator : AbstractValidator<ImportCommitRequest>
{
    public ImportCommitRequestValidator()
    {
        RuleFor(x => x.Options).NotNull().SetValidator(new ImportOptionsValidator()!);
        RuleFor(x => x.Selection).NotNull();
    }
}

/// <summary>
/// Bounds on the whole-file choices. The duplicate radius has a ceiling because it decides how
/// wide a proximity read runs, and an unbounded one over a page of candidates spread across a
/// massif would sweep the installation.
/// </summary>
public sealed class ImportOptionsValidator : AbstractValidator<ImportOptions>
{
    public ImportOptionsValidator()
    {
        RuleFor(x => x.Elevation).IsInEnum();
        RuleFor(x => x.Tracks).IsInEnum();
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.NamePrefix).MaximumLength(100);
        RuleFor(x => x.DuplicateRadiusMeters)
            .InclusiveBetween(0, ImportOptions.MaxDuplicateRadiusMeters);
        RuleFor(x => x.TrackFeatureTypeCode)
            .NotEmpty()
            .When(x => x.Tracks == ImportTrackHandling.ImportAsLine)
            .WithMessage("Say which kind of line feature a track becomes.");
        RuleFor(x => x.Languages).NotNull();
        RuleFor(x => x.TagIds).NotNull();
    }
}
