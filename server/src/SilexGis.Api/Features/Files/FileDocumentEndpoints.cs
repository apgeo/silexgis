// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using ImageMagick;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Features.Files;

public static partial class FileEndpoints
{
    private static async Task<Results<Ok<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        PhotoPositionDisclosure photos,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The same walk the document surface uses, because this response carries a delivery
        // token: a rule written against a document has to bind wherever its bytes are handed
        // out, or it binds nowhere.
        var subject = await DocumentQueries.FileSubjectAsync(db, id, ct);
        if (subject is null || !await DocumentAccessRules.CanReadFileAsync(db, access, ctx, subject, ct))
        {
            // Existence of an inaccessible file is not disclosed.
            return ApiProblems.NotFound("file.not_found");
        }

        // Reading the picture and placing what it shows are two different rights, so the
        // second is asked for separately before the bytes that answer it are offered.
        var reach = await photos.ReachAsync(ctx, [subject.File.Id], ct);

        // The copy something made of this file so its pages could be drawn, where one exists.
        // It is looked up here rather than in listings: this is the response a reader's screen
        // is built from, and it is the only one that has to know how to show the document.
        var rendition = await db.StoredFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.ConvertedFromFileId == id, ct);
        return TypedResults.Ok(subject.File.ToDto(subject.Version, tokens, reach, rendition));
    }

    private static async Task<Results<Ok<FileDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        FileUpdateRequest request,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        IAccessService access,
        PhotoPositionDisclosure photos,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // The write rule is the document's own, evaluated against the file that document
        // currently serves — the row attachments point at — whichever of its files was named.
        var subject = await DocumentQueries.FileSubjectAsync(db, id, ct);
        if (subject is null || !await DocumentAccessRules.CanWriteFileAsync(db, access, ctx, subject, ct))
        {
            return ApiProblems.NotFound("file.not_found"); // existence not disclosed to non-writers
        }

        // The date describes the revision, not the bytes: editing it through any of a
        // revision's files sets it once, for that revision.
        var version = await db.DocumentVersions.FirstAsync(v => v.Id == subject.File.DocumentVersionId, ct);
        version.DocumentDate = request.DocumentDate;
        await db.SaveChangesAsync(ct);

        // Asked here for the same reason it is asked on the way out of a plain read: being
        // allowed to correct a document's date says nothing about being allowed to know where
        // its subject is, and the two answers must not differ by which verb was used.
        var reach = await photos.ReachAsync(ctx, [subject.File.Id], ct);
        return TypedResults.Ok(subject.File.ToDto(version, tokens, reach));
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> ContentAsync(
        Guid id,
        string? token,
        SilexGisDbContext db,
        IFileStore fileStore,
        IFileAccessTokenService tokens,
        FileAccessRecorder accessHistory,
        LocationFreeCopyService locationFreeCopies,
        ILogger<LocationFreeCopyService> logger,
        CancellationToken ct)
    {
        // Nothing here can re-decide anything: a URL that was handed out is a decision
        // already taken, and the token carries no right this route could re-weigh. All it
        // does is honour how far that decision went — a token minted for renderings only
        // opens no original, and it says so the same way an unknown file does, because
        // whether these bytes exist is part of what was being kept back.
        var grant = tokens.Validate(token, id);
        if (grant is null || grant.Delivery == FileDelivery.DerivativesOnly)
        {
            return ApiProblems.NotFound("file.not_found"); // expired/tampered tokens don't disclose existence
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        // Which bytes, decided by the reach alone. A location-free reach never falls through to
        // the stored file: that file is exactly what the reach was minted to keep back, so a copy
        // that cannot be made answers as a missing file rather than as the original.
        string path;
        if (grant.Delivery == FileDelivery.LocationFree)
        {
            if (!LocationFreeCopyService.CanRemoveLocation(file.Kind, file.MimeType))
            {
                return ApiProblems.NotFound("file.not_found");
            }

            try
            {
                path = await locationFreeCopies.GetOrCreateAsync(file, ct);
            }
            catch (MagickException e)
            {
                logger.LogWarning(e, "Could not write a location-free copy of file {FileId}", file.Id);
                return ApiProblems.NotFound("file.not_found");
            }
        }
        else
        {
            path = fileStore.GetAbsolutePath(file.StoragePath);
        }

        // Handing over the whole file is the event worth recording: this is the one route that
        // gives out what was uploaded — or the whole of it less its location — rather than
        // something drawn from it. Recorded
        // after the refusals, so a rejected request leaves no trace of a read that did not
        // happen, and against the person the token was minted for — the only identity a
        // route browsers reach ambiently can have.
        await accessHistory.RecordAsync(id, grant.UserId, ct);

        // What the download is called is the mint's decision too. A name is still sent where the
        // upload's own is withheld, because it is the name that makes the answer an attachment:
        // these are bytes somebody uploaded, and without it a browser sent to this address would
        // render them under the application's own origin instead of saving them.
        return TypedResults.PhysicalFile(
            path,
            contentType: file.MimeType,
            fileDownloadName: grant.Naming == FileNaming.Withheld ? NamelessDownloadName : file.OriginalName,
            enableRangeProcessing: true);
    }

    /// <summary>
    /// What a download is called when the address it was fetched from withholds the upload's name.
    /// </summary>
    /// <remarks>
    /// One fixed word for every such file, with no extension: an extension would be cut from the
    /// upload's own name, and the part of a file name after its last dot is whatever its uploader
    /// typed there. Nothing that fetches these addresses reads the name — a viewer is told the
    /// format beside the address it is handed.
    /// </remarks>
    private const string NamelessDownloadName = "download";

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> ThumbnailAsync(
        Guid id,
        string? token,
        int? size,
        SilexGisDbContext db,
        ThumbnailService thumbnails,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        // Either reach opens a rendering: this application produces those bytes and strips
        // every metadata profile out of them, so they show what the picture shows and carry
        // nothing the picture did not.
        if (tokens.Validate(token, id) is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || file.Kind != FileKind.Image)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        var effectiveSize = size ?? 480;
        if (!ThumbnailService.AllowedSizes.Contains(effectiveSize))
        {
            return ApiProblems.BadRequest(
                "file.thumbnail_size_unsupported",
                $"Supported sizes: {string.Join(", ", ThumbnailService.AllowedSizes)}.");
        }

        string path;
        try
        {
            // Drawn with the turn somebody recorded. The upload is untouched, so this is the
            // only place a rotation becomes visible — and it is every place a picture is shown,
            // because everything anybody is shown is a rendering.
            path = await thumbnails.GetOrCreateAsync(
                file.Id, file.StoragePath, effectiveSize, ct, file.OrientationQuarterTurns);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Unreadable/corrupt image — report as unprocessable rather than crash.
            return ApiProblems.BadRequest("file.thumbnail_failed", "The image could not be processed.");
        }

        return TypedResults.PhysicalFile(path, contentType: "image/webp");
    }

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> PageRenderAsync(
        Guid id,
        int page,
        string? token,
        int? size,
        SilexGisDbContext db,
        PageRenderService pages,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        // Either reach opens a rendering, exactly as it does for a thumbnail, and for the same
        // reason: these bytes are drawn here, they show what the page shows, and they carry
        // nothing the page did not. That is what makes reading a document in this application
        // safe to offer to someone who may not have the file — a viewer that reached for the
        // stored bytes instead would hand over precisely what was being withheld.
        if (tokens.Validate(token, id) is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || !PageRenderService.CanRender(file.MimeType))
        {
            return ApiProblems.NotFound("file.not_found");
        }

        // Page numbers are the reader's, so they start at one; the upper bound is only checked
        // where the file said how many pages it has, since a renderer asked for a page past the
        // end reports its own failure and there is nothing better to say about it.
        if (page < 1 || (file.PageCount is int count && page > count))
        {
            return ApiProblems.NotFound("file.page_not_found");
        }

        var effectiveSize = size ?? 1200;
        if (!PageRenderService.AllowedSizes.Contains(effectiveSize))
        {
            return ApiProblems.BadRequest(
                "file.render_size_unsupported",
                $"Supported sizes: {string.Join(", ", PageRenderService.AllowedSizes)}.");
        }

        string? path;
        try
        {
            path = await pages.GetOrCreateAsync(file.Id, file.StoragePath, page, effectiveSize, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A page that will not draw is a fact about this file, not a fault in the request.
            return ApiProblems.BadRequest("file.render_failed", "The page could not be drawn.");
        }

        return path is null
            ? ApiProblems.NotFound("file.page_not_found")
            : TypedResults.PhysicalFile(path, contentType: "image/webp");
    }

    /// <summary>
    /// The words of one page, exactly as the reader that produced the search index recorded
    /// them.
    /// </summary>
    /// <remarks>
    /// This is the stream a durable text selection is measured against: a quote captured in a
    /// browser means nothing until it is found in the text the server itself holds, and a
    /// selection that stored a browser's own offsets would break the first time anything
    /// re-read the file. So the offsets are computed against this, and a selection whose words
    /// are not in it is refused rather than stored pointing somewhere else.
    /// <para>
    /// It is guarded exactly as the page picture beside it, and for the same reason: the
    /// picture already shows every one of these words to whoever may open it, so handing them
    /// over as characters discloses nothing the drawing did not. A page nothing has read yet
    /// answers "not found" rather than empty text — the difference between "there are no words
    /// here" and "nobody has looked" is the whole of what a caller needs to know.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PageTextDto>, ProblemHttpResult>> PageTextAsync(
        Guid id,
        int page,
        string? token,
        SilexGisDbContext db,
        IFileAccessTokenService tokens,
        CancellationToken ct)
    {
        if (tokens.Validate(token, id) is null)
        {
            return ApiProblems.NotFound("file.not_found");
        }

        if (page < 1)
        {
            return ApiProblems.NotFound("file.page_not_found");
        }

        var row = await db.DocumentPages.AsNoTracking()
            .Where(p => p.FileId == id && p.PageNumber == page)
            .Select(p => new { p.Text, p.Extractor })
            .FirstOrDefaultAsync(ct);

        if (row is null || row.Extractor is null)
        {
            return ApiProblems.NotFound("file.page_text_not_read");
        }

        return TypedResults.Ok(new PageTextDto(page, row.Text ?? string.Empty));
    }
}
