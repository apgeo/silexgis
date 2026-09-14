// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain.Import;

namespace SilexGis.Api.Features.Import;

// ---------- reads ----------

/// <summary>
/// One recording the archive holds, as the device wrote it. The document count is a count: what
/// somebody photographed underground stays in the archive, and this import never opens it.
/// </summary>
public sealed record SpeleolocRecordingDto(
    string Id,
    string Title,
    string? CaveTitle,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? DeviceUserId,
    int PointCount,
    int DocumentCount);

/// <summary>The archive's recordings, newest first.</summary>
public sealed record SpeleolocRecordingsDto(
    Guid FileId,
    string? FileName,
    IReadOnlyList<SpeleolocRecordingDto> Recordings);

/// <summary>
/// The caller's review of one archive, resumed where they left it.
/// </summary>
/// <param name="PositionsWithheld">
/// True when decisions were stored and are not being handed back, because the caller can no longer
/// place the cave the chosen model belongs to. A decision names a station, so a review read back
/// is a position read back, and it follows exactly the rule the tracking log follows.
/// </param>
public sealed record SpeleolocImportSessionDto(
    Guid FileId,
    string? FileName,
    SpeleolocImportOptions Options,
    IReadOnlyDictionary<string, SpeleolocPointDecision> Decisions,
    bool PositionsWithheld,
    DateTimeOffset? UpdatedAt);

/// <summary>One scan of the recording with what it would become.</summary>
public sealed record SpeleolocPointDto(
    string PointId,
    DateTimeOffset ScannedAt,
    string? Notes,
    Guid? PlaceId,
    string? PlaceTitle,
    double? PlaceDepthM,
    string? DeviceUserId,
    Guid? CaverId,
    SpeleolocPointState State,
    IReadOnlyList<SpeleolocStationCandidate> Candidates,
    SpeleolocPointDecision? Decision);

/// <summary>
/// The dry run: what the whole recording would do to the trip's timeline.
/// </summary>
/// <param name="UnmappedDeviceUsers">
/// Device accounts nobody has said are anybody. Stated at the top rather than left to be counted
/// off the rows, because one unmapped account costs every scan it made and a reviewer has to be
/// able to see that before they work through two hundred rows that will all be refused.
/// </param>
/// <param name="CaversNotOnRoster">
/// People the mapping names who are not on the chosen trip's roster. The other half of the same
/// warning: a mapping can be complete and still cost every scan it covers, because putting somebody
/// on a trip is a statement about who went and this import will not make it. Empty when the
/// confirmation is to create the trip, which it creates with exactly these people on it.
/// </param>
public sealed record SpeleolocImportPreviewDto(
    IReadOnlyList<SpeleolocPointDto> Points,
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<string> SelectablePointIds,
    IReadOnlyList<string> DeviceUsers,
    IReadOnlyList<string> UnmappedDeviceUsers,
    IReadOnlyList<Guid> CaversNotOnRoster,
    bool ModelUsable,
    int ProposedCount,
    int UnresolvedCount,
    string RecordingTitle,
    DateTimeOffset RecordingStartedAt,
    DateTimeOffset? RecordingEndedAt,
    int DocumentCount);

public sealed record SpeleolocImportFailureDto(string PointId, DateTimeOffset ScannedAt, string Code, string Reason);

public sealed record SpeleolocImportCommitResultDto(
    Guid BatchId,
    Guid TripLogId,
    bool CreatedTrip,
    int CreatedEventCount,
    int SkippedCount,
    IReadOnlyList<SpeleolocImportFailureDto> Failures);

// ---------- writes ----------

/// <summary>
/// Shared validation for the choices, so the save, the dry run and the confirmation cannot come to
/// disagree about what a well-formed set of them is.
/// </summary>
/// <remarks>
/// Every rule below that reaches inside a collection guards the collection itself first, and the
/// guard is not decoration. A property initialiser is not a null check: the serialiser assigns
/// whatever the body said, so a request whose <c>cavers</c> is the literal <c>null</c> leaves that
/// property null however it was initialised, and a rule that dereferenced it faulted inside the
/// validator — which is a 500 answering a malformed request, the one answer validation exists to
/// prevent. The same applies one level down, to a null *value* in a dictionary the body sent.
/// </remarks>
internal static class SpeleolocImportOptionRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, SpeleolocImportOptions?> options)
        where T : class
    {
        validator.RuleFor(x => options(x)).NotNull();

        validator.RuleFor(x => options(x)!.TripUuid)
            .Must(BeAnIdentifierOrAbsent)
            .WithMessage("The chosen recording is not an identifier.")
            .When(x => options(x) is not null);

        // Onto a trip, or into one this confirmation creates — never both, and the refusal is
        // here rather than a silent precedence rule, because a caller who sent both did not mean
        // either of them in particular.
        validator.RuleFor(x => options(x)!)
            .Must(o => !(o.CreateTrip && o.TripLogId is not null))
            .WithMessage("A recording is imported onto a trip or into a new one, not both.")
            .When(x => options(x) is not null);

        validator.RuleFor(x => options(x)!.CandidateCount)
            .InclusiveBetween(1, 25)
            .When(x => options(x) is not null);

        validator.RuleFor(x => options(x)!.Cavers)
            .NotNull()
            .Must(c => c is null || c.Count <= MaxMappedDeviceUsers)
            .WithMessage($"At most {MaxMappedDeviceUsers} device accounts are mapped at once.")
            .When(x => options(x) is not null);

        validator.RuleForEach(x => options(x)!.Cavers.Keys)
            .Must(k => Guid.TryParse(k, out _))
            .WithMessage("A device account is named by its identifier.")
            .When(x => options(x)?.Cavers is not null);
    }

    /// <summary>A phone's user table is small; a mapping larger than this is not a mapping.</summary>
    private const int MaxMappedDeviceUsers = 200;

    private static bool BeAnIdentifierOrAbsent(string? value) =>
        value is null || Guid.TryParse(value, out _);
}

public sealed record SpeleolocImportSessionWriteRequest(
    SpeleolocImportOptions? Options,
    IReadOnlyDictionary<string, SpeleolocPointDecision>? Decisions);

public sealed class SpeleolocImportSessionWriteRequestValidator
    : AbstractValidator<SpeleolocImportSessionWriteRequest>
{
    public SpeleolocImportSessionWriteRequestValidator()
    {
        SpeleolocImportOptionRules.Apply(this, x => x.Options);
        RuleFor(x => x.Decisions).NotNull();
        RuleFor(x => x.Decisions!.Count).LessThanOrEqualTo(SpeleolocImportLimits.MaxDecisions)
            .When(x => x.Decisions is not null);
        // A null value is tolerated and dropped on the way in rather than refused: a saved review is
        // a durable thing that a client may well have written a hole into, and one such hole must
        // not make the whole archive unreviewable. What it may not do is fault here.
        RuleForEach(x => x.Decisions!.Values)
            .Must(d => d?.StationName is null || d.StationName.Length <= SpeleolocImportLimits.MaxStationNameLength)
            .WithMessage("A station name is longer than the survey holds.")
            .When(x => x.Decisions is not null);
    }
}

public sealed record SpeleolocImportPreviewRequest(
    SpeleolocImportOptions? Options,
    int? Page,
    int? PageSize);

public sealed class SpeleolocImportPreviewRequestValidator : AbstractValidator<SpeleolocImportPreviewRequest>
{
    public SpeleolocImportPreviewRequestValidator() => SpeleolocImportOptionRules.Apply(this, x => x.Options);
}

/// <summary>
/// The confirmation. The chosen scans are named by their own identifiers rather than by their
/// place in a listing, because the listing is rebuilt from the archive each time it is asked for.
/// </summary>
public sealed record SpeleolocImportCommitRequest(
    SpeleolocImportOptions? Options,
    IReadOnlyList<string>? PointIds,
    IReadOnlyDictionary<string, SpeleolocPointDecision>? Decisions);

public sealed class SpeleolocImportCommitRequestValidator : AbstractValidator<SpeleolocImportCommitRequest>
{
    public SpeleolocImportCommitRequestValidator()
    {
        SpeleolocImportOptionRules.Apply(this, x => x.Options);
        RuleFor(x => x.PointIds).NotEmpty();
        RuleForEach(x => x.PointIds).Must(id => Guid.TryParse(id, out _))
            .WithMessage("A scan is named by its identifier.")
            .When(x => x.PointIds is not null);
        RuleFor(x => x.Decisions!.Count).LessThanOrEqualTo(SpeleolocImportLimits.MaxDecisions)
            .When(x => x.Decisions is not null);
    }
}

internal static class SpeleolocImportLimits
{
    /// <summary>As many decisions as one confirmation can carry scans, and no more.</summary>
    public const int MaxDecisions = 200000;

    public const int MaxStationNameLength = 400;
}
