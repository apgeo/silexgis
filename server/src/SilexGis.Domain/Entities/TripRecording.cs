// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A trip as a phone recorded it: which cave, when, and the places scanned on the way, kept as
/// the phone sent it and beside any trip report made from it.
/// </summary>
/// <remarks>
/// <para>
/// The recording is not the trip report. A phone records what it saw — a cave, a start and an end,
/// a scan at each marked place — and the report is what the club writes up from that, with a
/// roster, positions on a survey and an audience of its own. Keeping the recording as its own row
/// is what lets other phones of the club carry the same recording, lets the report be made from it
/// (or not) without losing what the phone said, and lets the same recording arriving a second way —
/// in an exported archive — be recognised as the one already here.
/// </para>
/// <para>
/// Its identifier is the phone's own, adopted verbatim the first time it arrives, so a phone and
/// this server never have to translate each other's names for it. It is governed by the trip
/// domain's rules — a club's rules over its trips reach its recordings without a second set of
/// rules to keep in step — and its audience is decided by the same owner, club and visibility
/// columns every trip carries.
/// </para>
/// </remarks>
public class TripRecording : IProtectedEntity, ITimestamped, IAuditable
{
    /// <summary>The phone's identifier for the recording, adopted verbatim.</summary>
    public Guid Id { get; set; }

    /// <summary>The cave the recording was made in.</summary>
    public Guid CaveFeatureId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    /// <summary>The phone's running log of the trip, as the phone kept it.</summary>
    public string? Log { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the recording was closed; null while it was still open on the phone.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// The phone user who started the recording, by the phone's own identifier for them — resolved
    /// to a caver through the phone-user mapping, never stored as one here, so a link made later
    /// reaches recordings sent before it.
    /// </summary>
    public Guid? CreatedByDeviceUserId { get; set; }

    /// <summary>The account whose upload brought the recording.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>The club the recording is bound to: the binding of the set it was uploaded into.</summary>
    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.CavingGroup;

    /// <summary>
    /// The trip report made from this recording, by whichever path made it. At most one: a
    /// recording becomes one report, and a second path offering to make another is refused.
    /// </summary>
    public Guid? ConvertedTripLogId { get; set; }

    /// <summary>
    /// When the phone believes it last wrote the recording, by its own clock. Provenance only and
    /// never compared: a phone's clock is not this server's.
    /// </summary>
    public DateTimeOffset? ClientUpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public List<TripRecordingPoint> Points { get; set; } = [];

    public bool IsDeleted => DeletedAt is not null;

    public string AuditId => Id.ToString();
}

/// <summary>One scan on a recording: a marked place reached at a moment, with what was noted there.</summary>
/// <remarks>
/// A scan names a place and never carries a position of its own. Where it was is the place's
/// position, and is given to a reader exactly when the place is — which is why a point on a place a
/// reader may not be given is left out of the recording that reader receives rather than handed
/// over without its place.
/// </remarks>
public class TripRecordingPoint : ITimestamped, IAuditable, IAuditChild
{
    /// <summary>The phone's identifier for the scan, adopted verbatim.</summary>
    public Guid Id { get; set; }

    public Guid RecordingId { get; set; }

    public TripRecording? Recording { get; set; }

    /// <summary>The marked place scanned; null for a point the phone recorded at no place.</summary>
    public Guid? PlaceFeatureId { get; set; }

    public DateTimeOffset ScannedAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>The phone user who made the scan, by the phone's own identifier for them.</summary>
    public Guid? CreatedByDeviceUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public bool IsDeleted => DeletedAt is not null;

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(TripRecording);

    public string? RootEntityId => RecordingId.ToString();
}
