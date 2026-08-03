// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Cabinets;

/// <summary>
/// The filing tree: where a document lives, and the collection an access rule can be
/// scoped to so that "the committee may read the club archive" is one rule rather than
/// one per document.
/// </summary>
/// <remarks>
/// <para>
/// A cabinet carries no content of its own and no owning club — it is global structure —
/// so the tree itself is readable by any signed-in caller, and everything that decides who
/// sees what is decided about the documents filed in it. Administering the tree, and
/// filing into it, are judged as document writes evaluated <em>at the cabinet</em>: a
/// caller who holds write over documents domain-wide administers the whole tree, one who
/// holds it only through a rule naming an archive administers that archive, and a deny
/// naming a shelf keeps them off it. That is the same question the rules editor answers
/// when it judges who may author a cabinet-scoped rule, asked in the same way.
/// </para>
/// <para>
/// Filing therefore cannot amplify: it takes write on the document <em>and</em> the right
/// to write documents at that cabinet, so nobody moves a document under rules they do not
/// themselves hold. Membership by itself still grants nothing — filing into a cabinet
/// nobody holds a rule on changes no one's access.
/// </para>
/// </remarks>
public static class CabinetEndpoints
{
    public const string NotFoundCode = "cabinet.not_found";
    public const string NameTakenCode = "cabinet.name_taken";
    public const string InUseCode = "cabinet.in_use";
    public const string NotEmptyCode = "cabinet.not_empty";
    public const string DocumentNotFoundCode = "document.not_found";
    public const string DocumentWriteForbiddenCode = "document.write_forbidden";

    public static RouteGroupBuilder MapCabinetEndpoints(this RouteGroupBuilder api)
    {
        var cabinets = api.MapGroup("/cabinets").WithTags("Cabinets");

        cabinets.MapGet("/", ListAsync)
            .WithSummary("The whole filing tree, each cabinet with its ancestry and size.");
        cabinets.MapGet("/{id:guid}", GetAsync).WithSummary("One cabinet.");
        cabinets.MapGet("/{id:guid}/documents", ListDocumentsAsync)
            .WithSummary("Documents filed in a cabinet that the caller may read.");
        cabinets.MapPost("/", CreateAsync).WithValidation<CabinetWriteRequest>()
            .WithSummary("Creates a cabinet.");
        cabinets.MapPut("/{id:guid}", UpdateAsync).WithValidation<CabinetWriteRequest>()
            .WithSummary("Renames, re-describes or moves a cabinet and everything below it.");
        cabinets.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes an empty cabinet no rule points at.");
        cabinets.MapPut("/{id:guid}/documents/{documentId:guid}", FileAsync)
            .WithSummary("Files a document in a cabinet.");
        cabinets.MapDelete("/{id:guid}/documents/{documentId:guid}", UnfileAsync)
            .WithSummary("Removes a document from a cabinet.");

        return api;
    }

    /// <summary>
    /// The whole tree in one response. It is small by construction — the depth cap and the
    /// unique-among-siblings rule keep it a filing scheme rather than a data set — and a
    /// client that has all of it can draw the sider and every breadcrumb without asking
    /// again.
    /// </summary>
    private static async Task<Results<Ok<List<CabinetDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await Project(db.Cabinets.AsNoTracking().OrderBy(c => c.Name), db).ToListAsync(ct));
    }

    private static async Task<Results<Ok<CabinetDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cabinet = await Project(db.Cabinets.AsNoTracking().Where(c => c.Id == id), db).FirstOrDefaultAsync(ct);
        return cabinet is null ? ApiProblems.NotFound(NotFoundCode) : TypedResults.Ok(cabinet);
    }

    /// <summary>
    /// What is on this shelf for this caller. The listing is filtered by the document read
    /// rule, cabinet rules included, so the count on the cabinet and the length of this
    /// list can legitimately differ.
    /// </summary>
    /// <remarks>
    /// Reach through an attached object is deliberately not resolved here: it is a
    /// per-document walk over every world a file is attached in, and paying it for a whole
    /// shelf would make listing a cabinet cost more than reading it. The consequence is
    /// stated rather than hidden — a document a caller can only reach because it hangs off
    /// a cave they may read is not listed among that cabinet's contents, though fetching it
    /// by id still works.
    /// </remarks>
    private static async Task<Results<Ok<PagedResult<CabinetDocumentDto>>, UnauthorizedHttpResult, ProblemHttpResult>> ListDocumentsAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        bool includeSubtree = false,
        int? page = null,
        int? pageSize = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await db.Cabinets.AnyAsync(c => c.Id == id, ct))
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // A subtree listing matches the stored ancestry rather than walking parents: the
        // array names every cabinet above each one, so "at or below X" is one indexed test.
        var shelves = includeSubtree
            ? db.Cabinets.AsNoTracking().Where(c => c.AncestorIds.Contains(id)).Select(c => c.Id)
            : db.Cabinets.AsNoTracking().Where(c => c.Id == id).Select(c => c.Id);

        var (p, size) = Paging.Normalize(page, pageSize);
        var documents = db.Documents.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Documents, null, (db.CabinetDocuments, db.Cabinets))
            .Where(d => db.CabinetDocuments.Any(m => m.DocumentId == d.Id && shelves.Contains(m.CabinetId)))
            .OrderBy(d => d.Title)
            .ThenBy(d => d.Id)
            .Select(d => new
            {
                Document = d,
                CurrentFile = db.StoredFiles.AsNoTracking()
                    .Where(f => db.DocumentVersions
                        .Any(v => v.Id == f.DocumentVersionId && v.DocumentId == d.Id && v.IsCurrent))
                    .OrderBy(f => f.CreatedAt)
                    .ThenBy(f => f.Id)
                    .FirstOrDefault(),
            });

        return TypedResults.Ok(await documents.ToPagedAsync(
            p,
            size,
            row => new CabinetDocumentDto(
                row.Document.Id,
                row.Document.Title,
                row.Document.DocumentTypeId,
                row.Document.Visibility,
                row.Document.CavingGroupId,
                row.CurrentFile?.Id,
                row.CurrentFile?.Kind,
                row.CurrentFile?.MimeType,
                row.CurrentFile?.SizeBytes,
                row.Document.UpdatedAt),
            ct));
    }

    private static async Task<Results<Created<CabinetDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        CabinetWriteRequest request,
        SilexGisDbContext db,
        CabinetWriteService cabinets,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var parent = await ParentAsync(db, request.ParentId, ct);
        if (request.ParentId is not null && parent is null)
        {
            return ApiProblems.BadRequest(CabinetWriteService.ParentNotFoundCode, "The parent cabinet does not exist.");
        }

        if (!MayAdminister(ctx, parent))
        {
            return ApiProblems.Forbidden();
        }

        var name = request.Name.Trim();
        if (await SiblingNameTakenAsync(db, request.ParentId, name, null, ct))
        {
            return ApiProblems.BadRequest(NameTakenCode, "A cabinet with this name already sits here.");
        }

        Cabinet cabinet;
        try
        {
            cabinet = await cabinets.CreateAsync(name, request.Description, request.ParentId, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (CabinetWriteException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        return TypedResults.Created(
            $"/api/v1/cabinets/{cabinet.Id}",
            await Project(db.Cabinets.AsNoTracking().Where(c => c.Id == cabinet.Id), db).FirstAsync(ct));
    }

    /// <summary>
    /// Renames, re-describes and re-places in one request, because a cabinet's placement is
    /// part of what it is. Moving one takes everything below it, so the caller needs the
    /// right to write documents both where it sits now and where it is going — otherwise
    /// moving a shelf would be a way to put documents under rules the mover does not hold.
    /// </summary>
    private static async Task<Results<Ok<CabinetDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        CabinetWriteRequest request,
        SilexGisDbContext db,
        CabinetWriteService cabinets,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cabinet = await db.Cabinets.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cabinet is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!MayAdminister(ctx, cabinet))
        {
            return ApiProblems.Forbidden();
        }

        var moving = request.ParentId != cabinet.ParentId;
        var parent = await ParentAsync(db, request.ParentId, ct);
        if (request.ParentId is not null && parent is null)
        {
            return ApiProblems.BadRequest(CabinetWriteService.ParentNotFoundCode, "The parent cabinet does not exist.");
        }

        if (moving && !MayAdminister(ctx, parent))
        {
            return ApiProblems.Forbidden();
        }

        var name = request.Name.Trim();
        if (await SiblingNameTakenAsync(db, request.ParentId, name, id, ct))
        {
            return ApiProblems.BadRequest(NameTakenCode, "A cabinet with this name already sits here.");
        }

        cabinet.Name = name;
        cabinet.Description = request.Description;

        try
        {
            if (moving)
            {
                await cabinets.MoveAsync(cabinet, request.ParentId, ct);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (CabinetWriteException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        return TypedResults.Ok(await Project(db.Cabinets.AsNoTracking().Where(c => c.Id == id), db).FirstAsync(ct));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cabinet = await db.Cabinets.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cabinet is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!MayAdminister(ctx, cabinet))
        {
            return ApiProblems.Forbidden();
        }

        // Deleting the anchor of a deny would cancel it silently — the one thing the whole
        // model refuses to let happen by side effect. Allows are refused too: dropping a
        // cabinet out from under any rule should be an act somebody chose.
        if (await db.AccessEntries.AnyAsync(e => e.ScopeKind == AccessScopeKind.Cabinet && e.ScopeId == id, ct))
        {
            return ApiProblems.Conflict(InUseCode, "Rules still point at this cabinet; remove them first.");
        }

        // Emptying comes first and is deliberate. Cascading would silently unfile a whole
        // archive — and unfiling moves access wherever a rule names a cabinet above it.
        if (await db.Cabinets.AnyAsync(c => c.ParentId == id, ct)
            || await db.CabinetDocuments.AnyAsync(m => m.CabinetId == id, ct))
        {
            return ApiProblems.Conflict(NotEmptyCode, "The cabinet still holds cabinets or documents.");
        }

        db.Cabinets.Remove(cabinet);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Files a document. Idempotent: filing what is already filed is the same fact, so it
    /// answers the same way rather than failing.
    /// </summary>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> FileAsync(
        Guid id,
        Guid documentId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct) =>
        await FilingAsync(id, documentId, db, access, accessAccessor, file: true, ct);

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> UnfileAsync(
        Guid id,
        Guid documentId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct) =>
        await FilingAsync(id, documentId, db, access, accessAccessor, file: false, ct);

    /// <summary>
    /// Both halves of filing, because they ask exactly the same question: write on the
    /// document, and the right to write documents at this cabinet. Unfiling is guarded as
    /// tightly as filing — removing a document from a cabinet a deny names would widen its
    /// access, so it is the same act in the other direction.
    /// </summary>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> FilingAsync(
        Guid id,
        Guid documentId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        bool file,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cabinet = await db.Cabinets.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cabinet is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        var content = document is null ? null : await DocumentQueries.CurrentFileAsync(db, documentId, ct);
        if (document is null
            || !await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content?.File, ct))
        {
            // Never an existence oracle: a document the caller may not read is answered as
            // one that is not there, so filing cannot be used to probe for documents.
            return ApiProblems.NotFound(DocumentNotFoundCode);
        }

        if (!await DocumentAccessRules.CanWriteAsync(db, access, ctx, document, content?.File, ct))
        {
            return ApiProblems.Forbidden(DocumentWriteForbiddenCode);
        }

        if (!MayAdminister(ctx, cabinet))
        {
            return ApiProblems.Forbidden();
        }

        var filed = await db.CabinetDocuments.FirstOrDefaultAsync(
            m => m.CabinetId == id && m.DocumentId == documentId, ct);
        if (file)
        {
            if (filed is null)
            {
                db.CabinetDocuments.Add(new CabinetDocument { CabinetId = id, DocumentId = documentId });
                await db.SaveChangesAsync(ct);
            }
        }
        else if (filed is not null)
        {
            db.CabinetDocuments.Remove(filed);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Whether the caller may write documents at this cabinet — the question filing and
    /// tree administration both come down to. A null cabinet means the root of the tree,
    /// where only a domain-wide right answers, because there is no shelf to name.
    /// </summary>
    private static bool MayAdminister(AccessContext ctx, Cabinet? cabinet) =>
        AccessEvaluator.Decide(
            ctx,
            AccessDomain.Documents,
            AccessAction.Write,
            cabinet is null ? null : new AccessTargetFacts { CabinetIds = cabinet.AncestorIds }).Allowed;

    private static Task<Cabinet?> ParentAsync(SilexGisDbContext db, Guid? parentId, CancellationToken ct) =>
        parentId is { } id
            ? db.Cabinets.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            : Task.FromResult<Cabinet?>(null);

    /// <summary>
    /// Whether a sibling already carries this name. The database enforces it as well —
    /// two filtered unique indexes, one for roots and one for the rest — but a request
    /// deserves a stable code rather than a constraint violation.
    /// </summary>
    private static Task<bool> SiblingNameTakenAsync(
        SilexGisDbContext db, Guid? parentId, string name, Guid? excludingId, CancellationToken ct) =>
        db.Cabinets.AnyAsync(
            c => c.ParentId == parentId && c.Name == name && (excludingId == null || c.Id != excludingId),
            ct);

    private static IQueryable<CabinetDto> Project(IQueryable<Cabinet> source, SilexGisDbContext db) =>
        source.Select(c => new CabinetDto(
            c.Id,
            c.ParentId,
            c.Name,
            c.Description,
            c.AncestorIds,
            db.CabinetDocuments.Count(m => m.CabinetId == c.Id)));
}
