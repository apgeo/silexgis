// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Uploads;

/// <summary>
/// Drops: what arrived together, from where, by whose hand, and what became of each file.
///
/// <para>
/// A batch is a record of something that happened rather than a thing anybody owns, and it is
/// visible only to the person who made it and to a full administrator. That is not a
/// permission model so much as an observation: the batch itself carries no content, and the
/// documents it created are governed by their own rules — a batch that listed titles somebody
/// may not read would be a way of reading them.
/// </para>
/// </summary>
public static class UploadBatchEndpoints
{
    public const string NotFoundCode = "upload_batch.not_found";
    public const string ImportForbiddenCode = "import.forbidden";

    public static RouteGroupBuilder MapUploadBatchEndpoints(this RouteGroupBuilder api)
    {
        var batches = api.MapGroup("/upload-batches").WithTags("Uploads");

        batches.MapGet("/", ListAsync)
            .WithSummary("The caller's own drops, newest first.");
        batches.MapPost("/", OpenAsync).WithValidation<UploadBatchOpenRequest>()
            .WithSummary("Opens a drop so everything uploaded into it can be found together afterwards.");
        batches.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One drop and its counts.");
        batches.MapGet("/{id:guid}/items", ListItemsAsync)
            .WithSummary("The per-file report of one drop.");
        batches.MapPost("/{id:guid}/close", CloseAsync)
            .WithSummary("Closes a drop; nothing more can be counted into it.");

        batches.MapGet("/import-roots", ImportRootsAsync)
            .WithSummary("Directories on the server this installation may import from.");
        batches.MapPost("/import-directory", ImportDirectoryAsync)
            .WithValidation<DirectoryImportRequest>()
            .WithSummary("Starts a background import from a directory the server itself can reach.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<UploadBatchDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var query = db.UploadBatches.AsNoTracking()
            .Where(b => b.StartedByUserId == ctx.UserId || ctx.IsFullAdmin)
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id);

        return TypedResults.Ok(await query.ToPagedAsync(p, size, ToDto, ct));
    }

    private static async Task<Results<Created<UploadBatchDto>, UnauthorizedHttpResult, ProblemHttpResult>> OpenAsync(
        UploadBatchOpenRequest request,
        SilexGisDbContext db,
        UploadBatchService batches,
        UploadIngestService ingest,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Opening a drop creates nothing yet, but naming it after a shelf is a statement about
        // where its files are going — so the shelf is checked here rather than only when the
        // first file arrives, which is when somebody has already chosen four hundred of them.
        if (!await ingest.MayFileIntoAsync(ctx, request.CabinetId, ct))
        {
            return ApiProblems.Forbidden(UploadDestinationBinding.FilingForbiddenCode);
        }

        if (request.CabinetId is { } cabinetId
            && !await db.Cabinets.AsNoTracking().AnyAsync(c => c.Id == cabinetId, ct))
        {
            return ApiProblems.BadRequest(UploadDestinationBinding.CabinetNotFoundCode);
        }

        var tagId = await ResolveTagAsync(db, request.TagName, ct);
        var batch = await batches.OpenAsync(
            ctx.UserId, UploadSource.Interactive, request.Label, request.CabinetId, tagId,
            sourceDescription: null, ct);

        return TypedResults.Created($"/api/v1/upload-batches/{batch.Id}", ToDto(batch));
    }

    private static async Task<Results<Ok<UploadBatchDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var batch = await OwnAsync(db, ctx, id, ct);
        return batch is null ? ApiProblems.NotFound(NotFoundCode) : TypedResults.Ok(ToDto(batch));
    }

    /// <summary>
    /// The per-file report. Document titles are resolved only for the documents this caller may
    /// read, which for their own drop is nearly all of them — but not necessarily every one,
    /// since a file can be filed onto a shelf whose rules the uploader does not themselves hold
    /// read on.
    /// </summary>
    private static async Task<Results<Ok<PagedResult<UploadBatchItemDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListItemsAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        UploadItemOutcome? outcome = null,
        int? page = null,
        int? pageSize = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await OwnAsync(db, ctx, id, ct) is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var rows = db.UploadBatchItems.AsNoTracking()
            .Where(i => i.UploadBatchId == id)
            .Where(i => outcome == null || i.Outcome == outcome)
            .OrderBy(i => i.Id);

        var paged = await rows.ToPagedAsync(
            p,
            size,
            i => new UploadBatchItemDto(
                i.Id, i.SourcePath, i.SizeBytes, i.Outcome, i.DocumentId, null, i.CabinetId,
                i.Reason, i.DuplicateOfDocumentId, i.CreatedAt),
            ct);

        // Titles are filled in afterwards, through the documents' own read rule. A line naming
        // a document is a fact about the import; the document's title is content, and this
        // report is not a way to read one that is otherwise withheld.
        var titles = await ReadableTitlesAsync(
            db, access, ctx, [.. paged.Items.Where(i => i.DocumentId is not null).Select(i => i.DocumentId!.Value)], ct);

        return TypedResults.Ok(paged with
        {
            Items =
            [
                .. paged.Items.Select(i => i.DocumentId is { } documentId && titles.TryGetValue(documentId, out var title)
                    ? i with { DocumentTitle = title }
                    : i),
            ],
        });
    }

    private static async Task<Results<Ok<UploadBatchDto>, UnauthorizedHttpResult, ProblemHttpResult>> CloseAsync(
        Guid id,
        SilexGisDbContext db,
        UploadBatchService batches,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var batch = await OwnAsync(db, ctx, id, ct);
        if (batch is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Idempotent: closing a closed drop is the same fact, and a client whose "done" request
        // was retried should not be told it did something wrong.
        if (batch.Status == UploadBatchStatus.Open)
        {
            await batches.CompleteAsync(id, error: null, ct);
        }

        var closed = await db.UploadBatches.AsNoTracking().FirstAsync(b => b.Id == id, ct);
        return TypedResults.Ok(ToDto(closed));
    }

    /// <summary>
    /// The directories this installation may import from. Answered for anybody who may run an
    /// import, because it is the field that page is built from.
    /// </summary>
    private static async Task<Results<Ok<DirectoryImportConfigDto>, UnauthorizedHttpResult, ProblemHttpResult>> ImportRootsAsync(
        ServerDirectorySource directories,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        return MayImportFromDisk(ctx)
            ? TypedResults.Ok(new DirectoryImportConfigDto(directories.Roots()))
            : ApiProblems.Forbidden(ImportForbiddenCode);
    }

    /// <summary>
    /// Starts a walk over a directory the server itself can reach.
    /// </summary>
    /// <remarks>
    /// Two guards, and they are not alternatives. The caller must be a full administrator,
    /// because this is the only request in the application that names a location the server
    /// reads directly. And the path must resolve inside a root the operator listed when they
    /// deployed the installation, because an administrator account is not the same thing as
    /// the operator who owns the machine — a compromised one must not become a way to read the
    /// database's data directory.
    /// </remarks>
    private static async Task<Results<Created<UploadBatchDto>, UnauthorizedHttpResult, ProblemHttpResult>> ImportDirectoryAsync(
        DirectoryImportRequest request,
        SilexGisDbContext db,
        UploadBatchService batches,
        UploadIngestService ingest,
        ServerDirectorySource directories,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!MayImportFromDisk(ctx))
        {
            return ApiProblems.Forbidden(ImportForbiddenCode);
        }

        var (resolved, code) = directories.Resolve(request.Path);
        if (resolved is null)
        {
            return code == ServerImportPaths.NoRootsCode
                ? ApiProblems.Conflict(code, "This installation has no import directories configured.")
                : ApiProblems.BadRequest(code!, "That directory cannot be imported from.");
        }

        if (!await ingest.MayFileIntoAsync(ctx, request.CabinetId, ct))
        {
            return ApiProblems.Forbidden(UploadDestinationBinding.FilingForbiddenCode);
        }

        if (request.CabinetId is { } cabinetId
            && !await db.Cabinets.AsNoTracking().AnyAsync(c => c.Id == cabinetId, ct))
        {
            return ApiProblems.BadRequest(UploadDestinationBinding.CabinetNotFoundCode);
        }

        var tagId = await ResolveTagAsync(db, request.TagName, ct);
        var batch = await batches.OpenAsync(
            ctx.UserId,
            UploadSource.ServerDirectory,
            request.Label,
            request.CabinetId,
            tagId,

            // The resolved path rather than what was typed: the report should say which
            // directory was actually read, not which one somebody meant.
            resolved,
            ct);

        db.ProcessingJobs.Add(new ProcessingJob
        {
            Kind = ProcessingJobKinds.DirectoryImport,
            Payload = JsonSerializer.Serialize(
                new DirectoryImportPayload(resolved, batch.Id), JsonSerializerOptions.Web),
            RequestedBy = ctx.UserId,
        });
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/upload-batches/{batch.Id}", ToDto(batch));
    }

    /// <summary>
    /// Whether this caller may make the server read its own disk. Full administrators only —
    /// deliberately not a domain right, because there is no object to scope one to and the
    /// question is about the machine rather than about any content on it.
    /// </summary>
    private static bool MayImportFromDisk(AccessContext ctx) => ctx.IsFullAdmin;

    private static Task<UploadBatch?> OwnAsync(
        SilexGisDbContext db, AccessContext ctx, Guid id, CancellationToken ct) =>
        db.UploadBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && (b.StartedByUserId == ctx.UserId || ctx.IsFullAdmin), ct);

    /// <summary>
    /// The tag a drop applies, created when it does not exist yet. Named rather than
    /// identified because somebody types it — and a name that already exists is the same tag,
    /// since tags are installation-wide by design.
    /// </summary>
    private static async Task<long?> ResolveTagAsync(SilexGisDbContext db, string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var slug = Tag.Slugify(name);
        if (slug.Length == 0)
        {
            return null;
        }

        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (existing is not null)
        {
            return existing.Id;
        }

        var tag = new Tag { Name = name.Trim(), Slug = slug };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return tag.Id;
    }

    /// <summary>Titles of the documents among these that the caller may actually read.</summary>
    private static async Task<Dictionary<Guid, string>> ReadableTitlesAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyCollection<Guid> documentIds,
        CancellationToken ct)
    {
        var titles = new Dictionary<Guid, string>();
        if (documentIds.Count == 0)
        {
            return titles;
        }

        var documents = await db.Documents.AsNoTracking()
            .Where(d => documentIds.Contains(d.Id))
            .ToListAsync(ct);

        foreach (var document in documents)
        {
            var content = await DocumentQueries.CurrentFileAsync(db, document.Id, ct);
            if (await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content?.File, ct))
            {
                titles[document.Id] = document.Title;
            }
        }

        return titles;
    }

    private static UploadBatchDto ToDto(UploadBatch b) => new(
        b.Id,
        b.Source,
        b.Status,
        b.Label,
        b.CabinetId,
        b.TagId,
        b.SourceDescription,
        b.TotalCount,
        b.StoredCount,
        b.SkippedCount,
        b.FailedCount,
        b.Error,
        b.CreatedAt,
        b.CompletedAt);
}
