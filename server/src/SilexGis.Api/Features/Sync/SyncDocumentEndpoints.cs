// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// Photos and documents for a phone: a catalogue of what hangs on the set's features, read in
/// pages like the features themselves, and addresses for the files a caver then chooses to fetch.
/// Nothing is pushed — the catalogue lists, and bytes move only for the items a phone names.
/// </summary>
public static class SyncDocumentEndpoints
{
    /// <summary>The most files one request may ask addresses for.</summary>
    public const int LinkItemsMax = 200;

    /// <summary>The word that asks for a file itself rather than a picture of it.</summary>
    public const string OriginalRendition = "original";

    /// <summary>
    /// A link changed when it was itself last written, when the document it hangs was, or when
    /// the feature it hangs on was — whichever is latest. The document's time means a renamed
    /// document or a new revision moves every link to it. The feature's time is what carries a
    /// link to a device that already read past it: a feature can come into what the set hands
    /// this caller through a change to its own row alone (a location guard lifted, a move under
    /// one of the roots), the feature stream then delivers it again, and its links have to come
    /// with it, or the device holds the cave and never its photos.
    /// </summary>
    private static readonly SyncStreamOrder<SyncLinkRow> ByChange = new(
        r => r.Link.UpdatedAt > r.Document.UpdatedAt
            ? (r.Link.UpdatedAt > r.FeatureUpdatedAt ? r.Link.UpdatedAt : r.FeatureUpdatedAt)
            : (r.Document.UpdatedAt > r.FeatureUpdatedAt ? r.Document.UpdatedAt : r.FeatureUpdatedAt),
        r => r.Link.Id);

    private static readonly SyncStreamOrder<SyncLinkGone> ByGone = new(g => g.At, g => g.Id);

    public static void MapSyncDocumentEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapGet("/{id:guid}/documents", DocumentsAsync)
            .WithName("syncDocuments")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("One page of the document links on a sync set's features, and the links that have gone.");
    }

    public static void MapSyncFileLinkEndpoints(this RouteGroupBuilder sync)
    {
        sync.MapPost("/files/links", LinksAsync).WithValidation<SyncFileLinksRequest>()
            .WithName("syncFileLinks")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Addresses to fetch named documents' files from, each re-checked against the caller's access.");
    }

    private static async Task<Results<Ok<SyncDocumentsPageDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        DocumentsAsync(
            Guid id,
            string? cursor,
            int? pageSize,
            SilexGisDbContext db,
            FeatureProtection protection,
            PhotoPositionDisclosure photos,
            IAccessContextAccessor accessAccessor,
            IOptions<SyncOptions> options,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (opening, problem) = await SyncStreamPaging.OpenAsync(
            db, ctx, SyncStream.Documents, id, cursor, pageSize, options.Value, ct);
        if (opening is null)
        {
            return problem!;
        }

        if (opening.Roots.Count == 0)
        {
            return TypedResults.Ok(new SyncDocumentsPageDto(
                opening.Set.Revision, opening.Settings, [], [], opening.Cursor, false));
        }

        // Only what hangs on a feature the feature stream would hand this caller right now: a link
        // on a withheld cave is absent, not stubbed, because a row hanging there says where to look.
        var handed = await SyncScope.HandedAsync(db, protection, ctx, opening.Roots, ct);
        var handedIds = handed.Select(f => f.Id);

        var linked =
            from link in db.Attachments.AsNoTracking()
            join feature in handed on link.FeatureId equals (Guid?)feature.Id
            join file in db.StoredFiles.AsNoTracking() on link.FileId equals file.Id
            join version in db.DocumentVersions.AsNoTracking() on file.DocumentVersionId equals version.Id
            select new { Link = link, File = file, version.DocumentId, FeatureUpdatedAt = feature.UpdatedAt };

        // Being able to read the cave a document hangs on is how most documents are reached, but
        // a rule written against the document itself is asked first and its deny is final — the
        // stream lists only what the caller may read, decided before the page is cut.
        var readable = await SyncDocuments.ReadableAsync(
            db,
            ctx,
            db.Documents.AsNoTracking().Where(d => linked.Select(x => x.DocumentId).Contains(d.Id)),
            ct);
        var live =
            from x in linked
            join document in readable on x.DocumentId equals document.Id
            select new SyncLinkRow
            {
                Link = x.Link, File = x.File, Document = document, FeatureUpdatedAt = x.FeatureUpdatedAt,
            };

        // Two kinds of going. A link detached — by any path, including the cascades no application
        // code sees — is in the removal log the database keeps. A link whose document was deleted
        // is still a row, and goes when the document did. Both are told for links on what the set
        // hands this caller, as an identifier and a moment that locate nothing.
        var removed = db.SyncLinkRemovals.AsNoTracking()
            .Where(r => r.FeatureId != null && handedIds.Contains(r.FeatureId.Value))
            .Select(r => new SyncLinkGone { Id = r.AttachmentId, At = r.RemovedAt });
        var orphaned = await OrphanedAsync(
            db,
            linked
                .Where(x => !db.Documents.Any(d => d.Id == x.DocumentId))
                .Select(x => new LinkedDocument(x.Link.Id, x.DocumentId)),
            ct);

        live = SyncStreamPaging.After(live, ByChange, opening.From);
        removed = SyncStreamPaging.After(removed, ByGone, opening.From);

        var liveRows = await SyncStreamPaging.Probe(live, ByChange, opening.Size).ToListAsync(ct);
        var removedRows = await SyncStreamPaging.Probe(removed, ByGone, opening.Size).ToListAsync(ct);
        var orphanedRows = SyncStreamPaging.Probe(
            SyncStreamPaging.After(orphaned.AsQueryable(), ByGone, opening.From), ByGone, opening.Size).ToList();

        var cut = SyncStreamPaging.Cut(
            opening,
            liveRows.Select(ByChange.Entry),
            removedRows.Select(ByGone.Entry),
            orphanedRows.Select(ByGone.Entry));

        var rows = cut.Rows.OfType<SyncLinkRow>().ToList();

        // What each file may be handed out as, decided by the one reach rule for the page at once.
        var reach = await photos.ReachAsync(ctx, [.. rows.Select(r => r.File.Id)], ct);
        var documentIds = rows.Select(r => r.Document.Id).Distinct().ToList();
        var details = await db.PhotoDetails.AsNoTracking()
            .Where(p => documentIds.Contains(p.DocumentId))
            .ToDictionaryAsync(p => p.DocumentId, ct);

        return TypedResults.Ok(new SyncDocumentsPageDto(
            opening.Set.Revision,
            opening.Settings,
            [.. rows.Select(r => new SyncDocumentLinkDto(
                r.Link.Id,
                r.Document.Id,
                new SyncLinkTargetDto(SyncLinkTargetKind.Feature, r.Link.FeatureId!.Value),
                r.Link.Role,
                r.Link.Caption,
                r.Link.SortOrder,
                r.Link.IsPrimary,
                r.Link.InDeviceDefaultSet,
                r.Link.UpdatedAt,
                SyncDocuments.ToDto(r.Document, r.File, details.GetValueOrDefault(r.Document.Id), reach)))],
            [.. cut.Rows.OfType<SyncLinkGone>().Select(g => new SyncTombstoneDto(g.Id, g.At))],
            cut.NextCursor,
            cut.HasMore));
    }

    /// <summary>
    /// The links on handed features whose document has been deleted, each stamped with the moment
    /// the document went. <paramref name="unlisted"/> is the links whose document no longer reads
    /// as there, found under every filter.
    /// </summary>
    /// <remarks>
    /// Asked in two statements rather than one, and the split is the point. Reading a deleted
    /// document means looking past the filter that hides deleted rows, and that switch cannot be
    /// thrown for one table of a statement: it would lift the same filter off the features the
    /// links are restricted to, and a deleted feature would count as handed. So the links are found
    /// under every filter — a link whose document no longer reads as there — and only their
    /// documents' deletion times are read past it, by identifier. Both are small: a deleted
    /// document's links last until its file is purged, and then the removal log has them.
    /// </remarks>
    private static async Task<List<SyncLinkGone>> OrphanedAsync(
        SilexGisDbContext db, IQueryable<LinkedDocument> unlisted, CancellationToken ct)
    {
        var pairs = await unlisted.ToListAsync(ct);
        if (pairs.Count == 0)
        {
            return [];
        }

        var documentIds = pairs.Select(p => p.DocumentId).Distinct().ToList();
        var deletedAt = await db.Documents.AsNoTracking().IgnoreQueryFilters()
            .Where(d => documentIds.Contains(d.Id) && d.DeletedAt != null)
            .Select(d => new { d.Id, DeletedAt = d.DeletedAt!.Value })
            .ToDictionaryAsync(d => d.Id, d => d.DeletedAt, ct);

        return [.. pairs
            .Where(p => deletedAt.ContainsKey(p.DocumentId))
            .Select(p => new SyncLinkGone { Id = p.LinkId, At = deletedAt[p.DocumentId] })];
    }

    /// <summary>
    /// Addresses for the files a phone chose to fetch. Each item is decided again here — the
    /// catalogue the phone read may be hours old, and access can have been withdrawn since — so an
    /// item is answered only when the caller may read the document now, at the reach the one reach
    /// rule gives now.
    /// </summary>
    /// <remarks>
    /// The address of a file itself lives for a sitting rather than for one fetch: the content
    /// route answers ranges, and a phone on a poor connection resumes a large file over several of
    /// them. A picture is small and is fetched at once, so its address keeps the short lifetime.
    /// </remarks>
    private static async Task<Results<Ok<SyncFileLinksDto>, UnauthorizedHttpResult>> LinksAsync(
        SyncFileLinksRequest request,
        SilexGisDbContext db,
        PhotoPositionDisclosure photos,
        IFileAccessTokenService tokens,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var ids = request.Items.Select(i => i.DocumentId).Distinct().ToList();
        var readable = await SyncDocuments.ReadableAsync(
            db, ctx, db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id)), ct);

        // The file a document serves is the first of its current revision — the row attachments
        // point at, spelled the way the document routes spell it.
        var files = await readable
            .Select(d => new
            {
                DocumentId = d.Id,
                File = db.StoredFiles.AsNoTracking()
                    .Where(f => db.DocumentVersions
                        .Any(v => v.Id == f.DocumentVersionId && v.DocumentId == d.Id && v.IsCurrent))
                    .OrderBy(f => f.CreatedAt)
                    .ThenBy(f => f.Id)
                    .FirstOrDefault(),
            })
            .Where(x => x.File != null)
            .ToDictionaryAsync(x => x.DocumentId, x => x.File!, ct);

        var reach = await photos.ReachAsync(ctx, [.. files.Values.Select(f => f.Id)], ct);
        // The same clock the address's own expiry is stamped against, so the time a phone is told
        // is the time the address actually stops opening.
        var now = DateTimeOffset.UtcNow;

        var links = new List<SyncFileLinkDto>();
        var refused = new List<SyncFileLinkRefusalDto>();
        foreach (var item in request.Items)
        {
            var rendition = item.Rendition.Clone();

            // A document that is not there, was deleted, or that this caller may no longer read
            // are one answer: telling them apart would say which of the three it is.
            if (!files.TryGetValue(item.DocumentId, out var file))
            {
                refused.Add(new SyncFileLinkRefusalDto(item.DocumentId, rendition, SyncCodes.DocumentUnavailable));
                continue;
            }

            if (rendition.ValueKind == JsonValueKind.String)
            {
                var delivery = FileDeliveries.Of(reach.OriginalOf(file));
                if (delivery == FileDelivery.DerivativesOnly)
                {
                    refused.Add(new SyncFileLinkRefusalDto(item.DocumentId, rendition, SyncCodes.OriginalWithheld));
                    continue;
                }

                var token = tokens.CreateToken(file.Id, delivery, FileAccessLifetime.Session);
                links.Add(new SyncFileLinkDto(
                    item.DocumentId,
                    rendition,
                    $"/api/v1/files/{file.Id}/content?token={Uri.EscapeDataString(token)}",
                    now.Add(FileAccessTokenService.SessionLifetime)));
                continue;
            }

            if (file.Kind != FileKind.Image)
            {
                refused.Add(new SyncFileLinkRefusalDto(item.DocumentId, rendition, SyncCodes.RenditionUnavailable));
                continue;
            }

            // A picture opens under the narrowest reach there is: the rendering route accepts
            // every reach alike, so nothing a picture needs is lost and the address can open
            // nothing else.
            var size = rendition.GetInt32();
            var pictureToken = tokens.CreateToken(file.Id, FileDelivery.DerivativesOnly);
            links.Add(new SyncFileLinkDto(
                item.DocumentId,
                rendition,
                $"/api/v1/files/{file.Id}/thumbnail?size={size}&token={Uri.EscapeDataString(pictureToken)}",
                now.Add(FileAccessTokenService.FetchLifetime)));
        }

        return TypedResults.Ok(new SyncFileLinksDto(links, refused));
    }
}

/// <summary>A link as the documents stream reads it, before it is shaped for the device.</summary>
internal sealed class SyncLinkRow
{
    public required Attachment Link { get; init; }

    public required StoredFile File { get; init; }

    public required Document Document { get; init; }

    /// <summary>When the feature the link hangs on last changed.</summary>
    public required DateTimeOffset FeatureUpdatedAt { get; init; }
}

/// <summary>A link and the document it hangs, by identifier.</summary>
internal sealed record LinkedDocument(Guid LinkId, Guid DocumentId);

/// <summary>A link that has gone, as the documents stream reads it.</summary>
internal sealed class SyncLinkGone
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>The refusal codes the address route answers per item.</summary>
internal static class SyncCodes
{
    /// <summary>No such document, deleted, or not readable by this caller now.</summary>
    public const string DocumentUnavailable = "sync.document_unavailable";

    /// <summary>The file itself is not handed to this caller; pictures of it may be.</summary>
    public const string OriginalWithheld = "sync.original_withheld";

    /// <summary>The server draws no picture of this kind of file.</summary>
    public const string RenditionUnavailable = "sync.rendition_unavailable";
}

public sealed class SyncFileLinksRequestValidator : AbstractValidator<SyncFileLinksRequest>
{
    public SyncFileLinksRequestValidator()
    {
        RuleFor(x => x.Items).NotNull()
            .Must(items => items is { Count: >= 1 and <= SyncDocumentEndpoints.LinkItemsMax })
            .WithMessage($"Between 1 and {SyncDocumentEndpoints.LinkItemsMax} items.");
        RuleForEach(x => x.Items).NotNull().ChildRules(item =>
        {
            item.RuleFor(i => i.DocumentId).NotEmpty();
            item.RuleFor(i => i.Rendition).Must(IsKnownRendition)
                .WithMessage("\"original\", or one of 2400, 1200, 480 and 160.");
        });
    }

    private static bool IsKnownRendition(JsonElement rendition) => rendition.ValueKind switch
    {
        JsonValueKind.String => rendition.GetString() == SyncDocumentEndpoints.OriginalRendition,
        JsonValueKind.Number => rendition.TryGetInt32(out var size) && ThumbnailService.AllowedSizes.Contains(size),
        _ => false,
    };
}
