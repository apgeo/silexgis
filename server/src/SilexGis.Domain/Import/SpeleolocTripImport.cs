// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Import;

/// <summary>
/// What one scanned point of a device recording was taken to mean, before anybody confirms it.
/// </summary>
/// <remarks>
/// A recording's point is a physical marker and a clock reading. A tracked trip's history is a
/// station of a survey. Those are different vocabularies, and every value here except
/// <see cref="Proposed"/> is a way in which the second cannot be derived from the first — stated
/// rather than guessed, because a station named wrongly is a claim about where somebody was.
/// </remarks>
public enum SpeleolocPointState
{
    /// <summary>The place's recorded depth resolved to one or more stations, nearest first.</summary>
    Proposed = 0,

    /// <summary>The point names no place at all; the recording says only that a scan happened.</summary>
    NoPlace = 1,

    /// <summary>
    /// The point names a place this installation does not hold, or holds and will not show this
    /// caller. Those two are one answer on purpose: an answer that told them apart would say
    /// whether a place exists to anybody who can upload an archive naming it.
    /// </summary>
    PlaceUnknown = 2,

    /// <summary>The place is here and carries no depth, so nothing can be proposed from it.</summary>
    DepthUnknown = 3,

    /// <summary>The depth is known and the survey reaches nothing at it.</summary>
    NoStationAtDepth = 4,

    /// <summary>
    /// No survey model was chosen, or the one chosen cannot be placed by this caller. The whole
    /// import answers this way rather than per point, and it is here so a row can say it.
    /// </summary>
    ModelUnavailable = 5,
}

/// <summary>A station the place's depth could mean, with how far off it is, metres positive down.</summary>
public sealed record SpeleolocStationCandidate(string StationName, string? SurveyName, double DepthM, double DeltaM);

/// <summary>What the reviewer decided about one point of the recording.</summary>
public enum SpeleolocPointAction
{
    /// <summary>Record the point as a position on the trip.</summary>
    Record = 0,

    /// <summary>Leave it alone. Nothing is written from it and it is not counted as a failure.</summary>
    Skip = 1,
}

/// <summary>
/// One point's overrides, every member optional so that agreeing with what is proposed costs
/// nothing to store.
/// </summary>
public sealed record SpeleolocPointDecision
{
    /// <summary>Null follows what the point proposes, which is to record it.</summary>
    public SpeleolocPointAction? Action { get; init; }

    /// <summary>
    /// The station this point is to be recorded at, in the model's own spelling. Absent takes the
    /// nearest candidate the resolver offered. A name given here is checked against the chosen
    /// model's stations before anything is written, exactly as a hand-typed station report is —
    /// so the only names that survive are names the model holds.
    /// </summary>
    public string? StationName { get; init; }

    /// <summary>
    /// Which person this point is about, overriding the recording-wide mapping for this one row.
    /// A device account is not a person here and is never resolved to one by inference; this and
    /// the mapping beside it are the only two ways a point acquires a caver at all.
    /// </summary>
    public Guid? CaverId { get; init; }
}

/// <summary>
/// The choices that apply to one whole import of one recording out of one archive.
///
/// <para>
/// Deliberately absent: the reference station and the depth filter. Those are station vocabulary
/// of the model's cave, they already have a home on the trip's own tracking configuration, and a
/// second copy here would be both a second answer to the same question and a set of station names
/// travelling in a snapshot that outlives the reviewer's right to read them. Where the trip has a
/// tracking configuration the import resolves depths under it; where it has none, the resolver's
/// own datum rule applies and the whole model is in scope.
/// </para>
/// </summary>
public sealed record SpeleolocImportOptions
{
    /// <summary>
    /// Which recording in the archive, as the canonical 36-character form of its identifier. An
    /// archive carries the device's whole database, so naming one is the first choice a reviewer
    /// makes and nothing is read row by row until they have.
    /// </summary>
    public string? TripUuid { get; init; }

    /// <summary>The trip the positions are recorded onto. Exclusive with <see cref="CreateTrip"/>.</summary>
    public Guid? TripLogId { get; init; }

    /// <summary>
    /// Create a trip from what the recording itself says — its title and its two timestamps — and
    /// record the positions onto that. The created trip is a line of the same batch, so one undo
    /// takes the whole import back.
    /// </summary>
    public bool CreateTrip { get; init; }

    /// <summary>
    /// The survey model the points are placed in. Required before anything can be proposed: a
    /// position in this application is a station reference, and a model is what has stations.
    /// </summary>
    public Guid? SurveyModelId { get; init; }

    /// <summary>
    /// Device account identifier (canonical form) to the person it is. Filled in by hand, never
    /// inferred: a device account is a login on a phone, and deciding that it stands for a named
    /// caver is a statement about who was underground that nothing in the archive supports.
    /// </summary>
    public IReadOnlyDictionary<string, Guid> Cavers { get; init; } = new Dictionary<string, Guid>();

    /// <summary>The group a created trip is organised by; ignored when recording onto an existing trip.</summary>
    public Guid? CavingGroupId { get; init; }

    /// <summary>
    /// Visibility a created trip starts with. The most restrictive value rather than the
    /// installation's usual default, for the reason every import uses it: an import is a bulk act.
    /// </summary>
    public Visibility Visibility { get; init; } = Visibility.Private;

    /// <summary>How many stations a point's depth is offered as, nearest first.</summary>
    public int CandidateCount { get; init; } = 5;
}

/// <summary>
/// What one point of the recording was taken to mean, for one caller, under one set of choices.
/// </summary>
/// <param name="PointId">
/// The point's own identifier as the device minted it, canonical form. This is the key a decision
/// is stored under: the archive is re-read on every preview and again at the confirmation, so the
/// key has to be something the row carries itself rather than a position in a listing that a
/// different choice of recording would renumber.
/// </param>
/// <param name="Candidates">
/// The stations the place's depth could mean. A choice is honoured among exactly these, or is a
/// station named outright and checked against the model — nothing is ever resolved by guessing
/// between two survey branches that share a depth.
/// </param>
public sealed record SpeleolocPointResolution(
    string PointId,
    DateTimeOffset ScannedAt,
    string? Notes,
    Guid? PlaceId,
    string? PlaceTitle,
    double? PlaceDepthM,
    string? DeviceUserId,
    Guid? CaverId,
    SpeleolocPointState State,
    IReadOnlyList<SpeleolocStationCandidate> Candidates);

/// <summary>
/// What a whole recording was taken to mean. The device accounts are listed apart from the points
/// because mapping them is one decision per account rather than one per scan, and because a
/// reviewer needs to see the whole list before they start: an account left unmapped costs every
/// point it scanned.
/// </summary>
public sealed record SpeleolocImportResolutionSet(
    IReadOnlyList<SpeleolocPointResolution> Points,
    IReadOnlyList<string> DeviceUsers,
    IReadOnlyList<string> UnmappedDeviceUsers,
    bool ModelUsable);

/// <summary>
/// Who one scan is about, in one place.
/// </summary>
/// <remarks>
/// There are two ways a scan acquires a person and they are ranked: the reviewer's choice for that
/// one row beats the recording-wide mapping, because the row-level choice is the later and more
/// specific statement and there would be no reason to make it otherwise. The ranking has to be
/// stated once. It is asked twice in the confirmation — once to decide who a created trip's roster
/// is, and again per scan to decide whose position is being written — and once more in the dry run,
/// to decide which scans "select all" may offer; and two of those asking it in opposite orders is
/// exactly the defect this replaces, where a per-row override left its person off the roster the
/// same confirmation then checked them against, and every overridden scan was refused.
/// </remarks>
public static class SpeleolocPointMeaning
{
    public static Guid? CaverOf(SpeleolocPointResolution? resolution, SpeleolocPointDecision? decision) =>
        decision?.CaverId ?? resolution?.CaverId;
}

/// <summary>
/// What a caller must hold before a recording may be confirmed, in one place, for the same reason
/// the trip spreadsheet keeps its list in one place: the answer is needed at the route, before the
/// archive is fetched, and again in the layer that knows what is being bound, and two copies drift
/// in one direction only.
/// </summary>
/// <remarks>
/// This covers only what the options themselves ask for. Recording onto an <em>existing</em> trip
/// is a write on that trip and is decided against the trip by the access service, not here; and
/// whether the caller may place the model's cave is decided by the exact-location rules, not here.
/// Each of those has its own single home and neither is restated.
/// </remarks>
public static class SpeleolocImportCreateRights
{
    public static (string Code, string Message)? Refusal(AccessContext? ctx, SpeleolocImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.CreateTrip)
        {
            return null;
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs, options.CavingGroupId))
        {
            return (CreateRules.ForbiddenCode, "You may not record trips here.");
        }

        if (options.CavingGroupId is { } group
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.TripLogs, group))
        {
            return (CavingGroupBindingRules.ForbiddenCode, "You may not organise trips under that group.");
        }

        return null;
    }
}

/// <summary>
/// The stable codes this import answers with. Held together so the route, the service and the
/// specification cannot come to disagree about what a refusal is called.
/// </summary>
public static class SpeleolocImportCodes
{
    /// <summary>The upload is not there any more, or this caller may not read it.</summary>
    public const string FileNotFound = "file.not_found";

    /// <summary>The upload is not a SpeleoLoc archive, or its database cannot be opened.</summary>
    public const string ArchiveUnreadable = "speleoloc_import.archive_unreadable";

    /// <summary>The upload, or the database inside it, is larger than one review reads.</summary>
    public const string ArchiveTooLarge = "speleoloc_import.archive_too_large";

    /// <summary>
    /// The chosen recording holds more scans than one review reads. Refused rather than truncated:
    /// a list cut off at a ceiling reads exactly like a complete one, and the whole point of the
    /// review is that somebody saw every scan before any of them became a position.
    /// </summary>
    public const string RecordingTooLarge = "speleoloc_import.recording_too_large";

    /// <summary>No recording was chosen, or the archive no longer holds the one that was.</summary>
    public const string RecordingNotFound = "speleoloc_import.recording_not_found";

    /// <summary>Neither an existing trip nor the switch that creates one.</summary>
    public const string TripMissing = "speleoloc_import.trip_missing";

    /// <summary>Both an existing trip and the switch that creates one.</summary>
    public const string TripAmbiguous = "speleoloc_import.trip_ambiguous";

    /// <summary>The trip named does not exist here, or this caller was never shown it.</summary>
    public const string TripNotFound = "trip_log.not_found";

    /// <summary>No survey model was chosen, and a position is a station of one.</summary>
    public const string ModelMissing = "speleoloc_import.model_missing";

    /// <summary>
    /// The model does not exist here, or its cave cannot be placed by this account. One answer for
    /// both, because telling them apart would say that a cave exists to somebody who may not place it.
    /// </summary>
    public const string ModelUnavailable = "speleoloc_import.model_unavailable";

    /// <summary>The depth datum cannot be established for the chosen model.</summary>
    public const string ReferenceUnknown = "speleoloc_import.reference_unknown";

    /// <summary>Nothing was chosen, so there is nothing to confirm.</summary>
    public const string SelectionEmpty = "speleoloc_import.selection_empty";

    /// <summary>More points than one confirmation records.</summary>
    public const string SelectionTooLarge = "speleoloc_import.selection_too_large";

    /// <summary>Every chosen point failed, so the batch would record only failures.</summary>
    public const string NothingCreated = "speleoloc_import.nothing_created";

    /// <summary>A chosen point is not one the archive reads as a point of this recording.</summary>
    public const string PointMissing = "speleoloc_import.point_missing";

    /// <summary>Nothing said which station this point is, and nothing could be proposed.</summary>
    public const string StationUnresolved = "speleoloc_import.station_unresolved";

    /// <summary>The station named is not one of the chosen model's stations.</summary>
    public const string StationUnknown = "speleoloc_import.station_unknown";

    /// <summary>The device account that scanned this point has not been said to be anybody.</summary>
    public const string CaverUnmapped = "speleoloc_import.caver_unmapped";

    /// <summary>The person this point was mapped to is not on the trip's roster.</summary>
    public const string CaverNotParticipant = "speleoloc_import.caver_not_participant";

    /// <summary>The recording claims a moment after the clock; a scan cannot be about the future.</summary>
    public const string RecordedInFuture = "speleoloc_import.recorded_in_future";

    /// <summary>The scan's own note is longer than a position row carries.</summary>
    public const string NoteTooLong = "speleoloc_import.note_too_long";
}
