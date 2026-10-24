// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Sync;

/// <summary>What a document link hangs on. Only features carry links on this stream today.</summary>
public enum SyncLinkTargetKind
{
    Feature = 0,
}

/// <summary>The kind of file a document serves, in the few words a phone shows it as.</summary>
public enum SyncFileKind
{
    Photo = 0,
    Video = 1,
    Audio = 2,
    Document = 3,
    Other = 4,
}

/// <summary>What a reader asking for a document's file itself, rather than a picture of it, is given.</summary>
public enum SyncOriginalReach
{
    /// <summary>The stored bytes exactly as uploaded.</summary>
    Original = 0,

    /// <summary>The whole picture at full size with the location data it carried taken out.</summary>
    LocationFree = 1,

    /// <summary>No file itself; only the pictures the server draws of it.</summary>
    Renditions = 2,

    /// <summary>Neither the file nor any picture of it.</summary>
    None = 3,
}

/// <summary>The object a link hangs its document on.</summary>
public sealed record SyncLinkTargetDto(SyncLinkTargetKind Kind, Guid Id);

/// <summary>
/// The document a link hangs, as a catalogue entry: enough to show it in a list and to decide
/// whether to fetch it, and never where the picture was taken.
/// </summary>
/// <param name="SizeBytes">
/// The size of what fetching the original hands over. Present only when that is the stored file —
/// a location-free copy is written on first request and its size is not known before then.
/// </param>
/// <param name="Sha256">The hash of the same bytes, on the same terms as <paramref name="SizeBytes"/>.</param>
/// <param name="VersionId">The document revision this entry describes; a new one means new bytes.</param>
/// <param name="Renditions">The picture sizes the server draws of it, largest first; empty for none.</param>
/// <param name="CapturedAt">When the content was made, as read from the file, where it could be.</param>
public sealed record SyncDocumentDto(
    Guid Id,
    string Title,
    string? Description,
    SyncFileKind FileKind,
    string MimeType,
    long? SizeBytes,
    string? Sha256,
    Guid VersionId,
    SyncOriginalReach Reach,
    IReadOnlyList<int> Renditions,
    Guid? PhotographerCaverId,
    DateTimeOffset? CapturedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One row of the documents stream: a link hanging a document on something the set carries.</summary>
/// <param name="Id">The link's own identifier — what a tombstone names when the link goes.</param>
/// <param name="InDefaultSet">Whether this file is among the ones a phone fetches as "the usual files".</param>
/// <param name="Revision">The link's own last change, to the microsecond.</param>
public sealed record SyncDocumentLinkDto(
    Guid Id,
    Guid DocumentId,
    SyncLinkTargetDto Target,
    AttachmentRole Role,
    string? Caption,
    int SortOrder,
    bool IsPrimary,
    bool InDefaultSet,
    DateTimeOffset Revision,
    SyncDocumentDto Document);

/// <summary>One page of the documents stream.</summary>
/// <param name="Links">The links that changed, oldest change first.</param>
/// <param name="Tombstones">
/// Links that have gone — detached, or hanging a document that was deleted — interleaved with the
/// links on the same key.
/// </param>
public sealed record SyncDocumentsPageDto(
    long SetRevision,
    JsonElement Settings,
    IReadOnlyList<SyncDocumentLinkDto> Links,
    IReadOnlyList<SyncTombstoneDto> Tombstones,
    string? NextCursor,
    bool HasMore);

/// <summary>One file a phone asks to fetch.</summary>
/// <param name="Rendition">
/// <c>"original"</c> for the file itself, or one of the picture sizes as a number — 2400, 1200,
/// 480 or 160.
/// </param>
public sealed record SyncFileLinkItem(Guid DocumentId, JsonElement Rendition);

/// <summary>The files a phone asks for addresses to, at most two hundred at a time.</summary>
public sealed record SyncFileLinksRequest(IReadOnlyList<SyncFileLinkItem> Items);

/// <summary>An address a file can be fetched from, until <paramref name="ExpiresAt"/>.</summary>
public sealed record SyncFileLinkDto(Guid DocumentId, JsonElement Rendition, string Url, DateTimeOffset ExpiresAt);

/// <summary>An item no address was given for, and why.</summary>
public sealed record SyncFileLinkRefusalDto(Guid DocumentId, JsonElement Rendition, string Code);

/// <summary>The addresses given, and the items refused, each in the order asked.</summary>
public sealed record SyncFileLinksDto(IReadOnlyList<SyncFileLinkDto> Links, IReadOnlyList<SyncFileLinkRefusalDto> Refused);
