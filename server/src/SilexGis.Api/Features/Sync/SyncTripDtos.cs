// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace SilexGis.Api.Features.Sync;

/// <summary>One trip recording a phone asks this server to write.</summary>
/// <param name="Id">The phone's identifier for the recording, adopted verbatim the first time it arrives.</param>
/// <param name="BaseRevision">
/// The server revision the phone last saw for this recording, or null to say it is new. Compared
/// for equality and nothing else, exactly as a feature row's is.
/// </param>
/// <param name="Deleted">Asks for the recording to go. Arbitrated exactly like an edit.</param>
/// <param name="CaveId">The cave the recording was made in; required unless the recording is being removed.</param>
/// <param name="Title">The recording's title as the phone shows it; may be empty.</param>
/// <param name="Log">The phone's running log of the trip.</param>
/// <param name="StartedAt">When the recording started; required unless the recording is being removed.</param>
/// <param name="EndedAt">When the recording was closed; absent while it is still open.</param>
/// <param name="CreatedByDeviceUserId">The phone user who started the recording, by the phone's own identifier.</param>
/// <param name="ClientUpdatedAt">When the phone believes it last wrote the recording, by its own clock. Provenance only.</param>
public sealed record SyncUploadTripRecordingDto(
    Guid Id,
    DateTimeOffset? BaseRevision = null,
    bool Deleted = false,
    Guid? CaveId = null,
    string? Title = null,
    string? Description = null,
    string? Log = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null,
    Guid? CreatedByDeviceUserId = null,
    DateTimeOffset? ClientUpdatedAt = null);

/// <summary>One scan on a recording that a phone asks this server to write.</summary>
/// <param name="Id">The phone's identifier for the scan, adopted verbatim.</param>
/// <param name="RecordingId">
/// The recording the scan belongs to. A scan belongs to one recording for good: the same
/// identifier sent under another recording is refused as taken.
/// </param>
/// <param name="PlaceId">The marked place scanned; absent for a scan made at no place.</param>
/// <param name="ScannedAt">When the place was scanned; required unless the scan is being removed.</param>
/// <param name="CreatedByDeviceUserId">The phone user who made the scan, by the phone's own identifier.</param>
public sealed record SyncUploadTripPointDto(
    Guid Id,
    Guid RecordingId,
    DateTimeOffset? BaseRevision = null,
    bool Deleted = false,
    Guid? PlaceId = null,
    DateTimeOffset? ScannedAt = null,
    string? Notes = null,
    Guid? CreatedByDeviceUserId = null);

/// <summary>One recording as the trips stream hands it, with its scans embedded.</summary>
/// <param name="Revision">
/// The recording's own server revision — the value the phone sends back as its base revision when
/// it edits the recording. Its scans carry revisions of their own.
/// </param>
/// <param name="Points">
/// Every live scan on the recording that this reader may be given, in the order they were made.
/// The list is otherwise complete, so a phone replaces its copy of the recording's scans wholesale.
/// A scan of a place this reader is not handed is left out rather than sent without its place.
/// </param>
public sealed record SyncTripRecordingDto(
    Guid Id,
    Guid CaveId,
    string Title,
    string? Description,
    string? Log,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    Guid? CreatedByDeviceUserId,
    DateTimeOffset Revision,
    IReadOnlyList<SyncTripPointDto> Points);

/// <summary>One scan on a recording, as the trips stream hands it.</summary>
public sealed record SyncTripPointDto(
    Guid Id,
    Guid? PlaceId,
    DateTimeOffset ScannedAt,
    string? Notes,
    Guid? CreatedByDeviceUserId,
    DateTimeOffset Revision);

/// <summary>
/// One page of the trips stream: the recordings made in the caves the set hands this reader, and
/// the recordings that have gone, on one cursor.
/// </summary>
public sealed record SyncTripsPageDto(
    long SetRevision,
    JsonElement Settings,
    IReadOnlyList<SyncTripRecordingDto> Recordings,
    IReadOnlyList<SyncTombstoneDto> Tombstones,
    string? NextCursor,
    bool HasMore);
