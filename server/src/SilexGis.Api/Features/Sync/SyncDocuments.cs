// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The questions the sync routes ask about documents, answered once for both the catalogue a
/// phone reads and the addresses it then asks for — so a document listed is one the address
/// route will also hand over, and one refused there is one the catalogue no longer lists.
/// </summary>
internal static class SyncDocuments
{
    /// <summary>The picture sizes the server draws, largest first, as a phone is offered them.</summary>
    public static readonly IReadOnlyList<int> RenditionSizes =
        [.. ThumbnailService.AllowedSizes.OrderDescending()];

    /// <summary>
    /// Of a set of candidate documents, the ones this caller may read — by the document's own
    /// rules, with reach through an attached object resolved for the candidates the rules leave
    /// open. A query, so the stream pages over readable documents only: a page cut before
    /// unreadable rows were dropped would carry their number in its size.
    /// </summary>
    public static async Task<IQueryable<Document>> ReadableAsync(
        SilexGisDbContext db, AccessContext ctx, IQueryable<Document> candidates, CancellationToken ct)
    {
        // Spelled as the document listings spell it: whatever ownership, the read audience and
        // the entries already admit is not walked again, and only the rest is asked whether it is
        // reached through something its file hangs on. That reach rides the same arm as
        // ownership, below every entry, so a deny written against a document stays a deny.
        var admitted = candidates
            .VisibleTo(ctx, AccessDomain.Documents, null, (db.CabinetDocuments, db.Cabinets))
            .Select(d => d.Id);
        var reached = await DocumentAccessRules.ReachedByAttachmentAsync(
            db, ctx, candidates.Where(d => !admitted.Contains(d.Id)).Select(d => d.Id), ct);
        return candidates.VisibleTo(ctx, AccessDomain.Documents, reached, (db.CabinetDocuments, db.Cabinets));
    }

    /// <summary>The kind word a phone shows a file as.</summary>
    public static SyncFileKind KindOf(FileKind kind) => kind switch
    {
        FileKind.Image => SyncFileKind.Photo,
        FileKind.Video => SyncFileKind.Video,
        FileKind.Audio => SyncFileKind.Audio,
        FileKind.Document => SyncFileKind.Document,
        _ => SyncFileKind.Other,
    };

    /// <summary>
    /// The reach a catalogue entry announces, from the one reach rule's answer. A file whose
    /// original is withheld is still offered as pictures when the server draws any of it.
    /// </summary>
    public static SyncOriginalReach ReachOf(OriginalReach original, FileKind kind) => original switch
    {
        OriginalReach.Stored => SyncOriginalReach.Original,
        OriginalReach.LocationFree => SyncOriginalReach.LocationFree,
        _ => kind == FileKind.Image ? SyncOriginalReach.Renditions : SyncOriginalReach.None,
    };

    /// <summary>
    /// The catalogue entry for a document's file as this caller sees it. Carries no position on
    /// any reach: where a picture was taken is not part of what a phone is told about it here.
    /// </summary>
    /// <param name="linkedPhotographer">
    /// For a photo that names no photographer, the caver its phone user is linked to
    /// (<see cref="PhotographerResolution.LinkedCaversAsync"/>); credited only when the photo names nobody.
    /// </param>
    public static SyncDocumentDto ToDto(
        Document document, StoredFile file, PhotoDetails? details, PhotoReach reach, Guid? linkedPhotographer)
    {
        var original = reach.OriginalOf(file);
        var announced = ReachOf(original, file.Kind);

        // Size and hash describe the bytes the original would hand over, so they are given only
        // when that is the stored file. The copy without location data is a different file, made
        // the first time somebody asks for it; quoting the stored file's numbers for it would tell
        // a phone to expect bytes it will never receive.
        var stored = original == OriginalReach.Stored;
        return new SyncDocumentDto(
            document.Id,
            document.Title,
            details?.Caption,
            KindOf(file.Kind),
            file.MimeType,
            stored ? file.SizeBytes : null,
            stored ? file.Sha256 : null,
            file.DocumentVersionId,
            announced,
            file.Kind == FileKind.Image ? RenditionSizes : [],
            PhotographerResolution.CaverOf(details, linkedPhotographer),
            document.CreatedByDeviceUserId,
            file.ContentCreatedAt,
            document.CreatedAt,
            document.UpdatedAt);
    }
}
