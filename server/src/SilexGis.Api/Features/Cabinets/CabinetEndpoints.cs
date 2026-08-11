// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
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
    public const string DocumentTypeUnknownCode = "document.type_unknown";

    public static RouteGroupBuilder MapCabinetEndpoints(this RouteGroupBuilder api)
    {
        var cabinets = api.MapGroup("/cabinets").WithTags("Cabinets");

        cabinets.MapGet("/", ListAsync)
            .WithSummary("The whole filing tree, each cabinet with its ancestry and how many documents filed directly on it the caller may read.");
        cabinets.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One cabinet, with how many documents filed directly on it the caller may read.");
        cabinets.MapGet("/{id:guid}/documents", ListDocumentsAsync)
            .WithSummary("Documents filed in a cabinet that the caller may read.");
        cabinets.MapPost("/", CreateAsync).WithValidation<CabinetWriteRequest>()
            .WithSummary("Creates a cabinet.");
        cabinets.MapPut("/{id:guid}", UpdateAsync).WithValidation<CabinetWriteRequest>()
            .WithSummary("Renames, re-describes or moves a cabinet and everything below it.");
        cabinets.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Deletes an empty cabinet no rule points at.");
        cabinets.MapPost("/filing", BulkFileAsync).WithValidation<BulkFilingRequest>()
            .WithSummary("Files and unfiles many documents at once; reports per-document refusals rather than failing whole.");
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

        var reached = await ReachOverAsync(db, ctx, FiledOn(db, db.Cabinets.AsNoTracking().Select(c => c.Id)), ct);
        return TypedResults.Ok(
            await ProjectAsync(db.Cabinets.AsNoTracking().OrderBy(c => c.Name), db, ctx, reached, ct));
    }

    private static async Task<Results<Ok<CabinetDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id, SilexGisDbContext db, IAccessContextAccessor accessAccessor, CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var cabinet = await ProjectOneAsync(db, ctx, id, ct);
        return cabinet is null ? ApiProblems.NotFound(NotFoundCode) : TypedResults.Ok(cabinet);
    }

    /// <summary>
    /// What is on this shelf for this caller. The listing is filtered by the whole document
    /// read rule — the caller's entries, ownership, the read audience, cabinet rules, and
    /// reach through an object the document's file hangs on — so it answers the same
    /// question as fetching any one of these documents by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reach is resolved before the query rather than by dropping rows after it, because
    /// resolving it afterwards would leave the total beside the rows counting documents the
    /// caller was not shown — which is how a number ends up announcing what a list withheld.
    /// </para>
    /// <para>
    /// It is asked only about the documents nothing else already admits and whose file hangs
    /// on something, since it can only widen and has nothing to say about the rest. That is a
    /// narrowing and not a bound: it scales with how much of the shelf is withheld from this
    /// caller, not with the page being shown, so a shelf holding a great many attached
    /// documents none of which this caller's entries admit is walked in full to answer.
    /// </para>
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
        var filed = db.Documents.AsNoTracking()
            .Where(d => db.CabinetDocuments.Any(m => m.DocumentId == d.Id && shelves.Contains(m.CabinetId)));
        var reached = await ReachOverAsync(db, ctx, filed, ct);

        var documents = filed
            .VisibleTo(ctx, AccessDomain.Documents, reached, (db.CabinetDocuments, db.Cabinets))
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

        // Resolved once for the shelf rather than per row: the expectation is a property of
        // where the documents are filed, not of any one of them.
        var expected = await ExpectedKeysAsync(db, id, ct);

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
                row.Document.UpdatedAt,
                CabinetDefaultRules.MissingMetadataKeys(expected, AnsweredKeys(row.Document.Metadata))),
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

        if (await UnknownDocumentTypeAsync(db, request.DefaultDocumentTypeId, ct))
        {
            return ApiProblems.BadRequest(DocumentTypeUnknownCode, "The default document type does not exist.");
        }

        Cabinet cabinet;
        try
        {
            cabinet = await cabinets.CreateAsync(name, request.Description, request.ParentId, ct);
            ApplyDefaults(cabinet, request);
            await db.SaveChangesAsync(ct);
        }
        catch (CabinetWriteException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        return TypedResults.Created(
            $"/api/v1/cabinets/{cabinet.Id}",
            (await ProjectOneAsync(db, ctx, cabinet.Id, ct))!);
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

        if (await UnknownDocumentTypeAsync(db, request.DefaultDocumentTypeId, ct))
        {
            return ApiProblems.BadRequest(DocumentTypeUnknownCode, "The default document type does not exist.");
        }

        cabinet.Name = name;
        cabinet.Description = request.Description;
        ApplyDefaults(cabinet, request);

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

        return TypedResults.Ok((await ProjectOneAsync(db, ctx, id, ct))!);
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

        // Resource-link members naming the shelf have no FK; they go with it.
        await db.ResLinkMembers
            .Where(m => m.EntityType == AttachedEntityType.Cabinet && m.EntityId == cabinet.Id)
            .ExecuteDeleteAsync(ct);
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
    /// where only a domain-wide right answers, because there is no shelf to name. The rule
    /// itself lives in Domain, so surfaces that reach a shelf sideways answer identically.
    /// </summary>
    private static bool MayAdminister(AccessContext ctx, Cabinet? cabinet) =>
        CabinetAccessRules.MayAdminister(ctx, cabinet?.AncestorIds);

    /// <summary>
    /// Writes a shelf's own defaults. Absent means cleared, not unchanged: this is a full-DTO
    /// write like every other cabinet edit, so a client that omits a field is saying the shelf
    /// no longer has an opinion about it.
    /// </summary>
    private static void ApplyDefaults(Cabinet cabinet, CabinetWriteRequest request)
    {
        cabinet.DefaultDocumentTypeId = request.DefaultDocumentTypeId;
        cabinet.DefaultVisibility = request.DefaultVisibility;
        cabinet.DefaultTagIds = [.. (request.DefaultTagIds ?? []).Distinct()];

        // Trimmed and de-duplicated: an expected key is compared against what a document's
        // metadata actually holds, and " author" would never match anything.
        cabinet.RequiredMetadataKeys =
        [
            .. (request.RequiredMetadataKeys ?? [])
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Whether a named default document type does not exist. Checked rather than left to the
    /// foreign key, so a mistyped id is a stable code instead of a constraint violation.
    /// </summary>
    private static async Task<bool> UnknownDocumentTypeAsync(
        SilexGisDbContext db, long? documentTypeId, CancellationToken ct) =>
        documentTypeId is { } id && !await db.DocumentTypes.AsNoTracking().AnyAsync(t => t.Id == id, ct);

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

    /// <summary>
    /// The cabinets, each with how many documents this caller may read on it — the same
    /// question, under the same rule, as the listing that opens when the shelf is clicked.
    /// A count and the list it sits beside must come from one rule: a number larger than
    /// what the list shows states exactly how much was withheld, and one smaller than it
    /// reads as a defect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It counts documents filed <em>directly</em> on the cabinet, which is what the listing
    /// beside it shows by default. Asking for the subtree is a different question, and the
    /// listing has a switch for it; the number on the tree keeps answering the one the tree
    /// is drawn from, so a shelf's label and a shelf's contents describe the same shelf.
    /// </para>
    /// <para>
    /// The counting is one grouped statement over the filings rather than a count per
    /// cabinet, because the per-cabinet form makes the whole access walk a correlated
    /// subplan and runs it once per row of a tree that is fetched on every documents page.
    /// </para>
    /// <para>
    /// The refusal to delete a cabinet that still holds something is deliberately NOT this
    /// number — it asks the membership table outright. Administration is about what is
    /// filed, not about what the administrator happens to be allowed to read, and a
    /// refusal explained by a count that read zero would look like a bug.
    /// </para>
    /// </remarks>
    private static async Task<List<CabinetDto>> ProjectAsync(
        IQueryable<Cabinet> source,
        SilexGisDbContext db,
        AccessContext ctx,
        IReadOnlyCollection<Guid> reached,
        CancellationToken ct)
    {
        var shelves = await source.ToListAsync(ct);
        if (shelves.Count == 0)
        {
            return [];
        }

        // Inheritance needs the ancestors of every shelf being projected, which for the whole
        // tree is the tree itself. Fetched once for the whole projection rather than per row:
        // resolving a shelf's defaults is pure arithmetic over rows, and the only expensive
        // part would be asking the database for them one shelf at a time.
        var ancestorIds = shelves.SelectMany(c => c.AncestorIds).Distinct().ToList();
        var chain = await db.Cabinets.AsNoTracking()
            .Where(c => ancestorIds.Contains(c.Id))
            .ToListAsync(ct);
        foreach (var shelf in shelves)
        {
            if (chain.TrueForAll(c => c.Id != shelf.Id))
            {
                chain.Add(shelf);
            }
        }

        var ids = shelves.Select(c => c.Id).ToList();
        var readable = db.Documents.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Documents, reached, (db.CabinetDocuments, db.Cabinets))
            .Select(d => d.Id);
        var counts = await db.CabinetDocuments.AsNoTracking()
            .Where(m => ids.Contains(m.CabinetId) && readable.Contains(m.DocumentId))
            .GroupBy(m => m.CabinetId)
            .Select(g => new { CabinetId = g.Key, Filed = g.Count() })
            .ToDictionaryAsync(x => x.CabinetId, x => x.Filed, ct);

        return
        [
            .. shelves.Select(c => new CabinetDto(
                c.Id,
                c.ParentId,
                c.Name,
                c.Description,
                c.AncestorIds,
                counts.GetValueOrDefault(c.Id),
                DefaultsOf(c, chain))),
        ];
    }

    /// <summary>
    /// Which of the given documents this caller reaches only because their file hangs on
    /// something they may read. Asked before the query that uses it, so the answer is a
    /// term of that query rather than a filter over its results — and asked only about the
    /// documents no entry, ownership or read audience has already admitted, since reach can
    /// only ever widen and so has nothing to say about the rest.
    /// <para>
    /// On the whole tree the candidate set is every filed document this caller's entries do
    /// not admit, which on a large archive is most of it. The walk it feeds settles in a
    /// fixed number of queries however many candidates there are, but it holds them in
    /// memory while it does, and this is a tree the documents pages fetch on every visit.
    /// Bounding it is not possible without making the answer depend on how many other
    /// documents happened to be withheld, which would be a worse thing to be.
    /// </para>
    /// </summary>
    private static Task<IReadOnlyCollection<Guid>> ReachOverAsync(
        SilexGisDbContext db, AccessContext ctx, IQueryable<Document> candidates, CancellationToken ct)
    {
        var admitted = candidates
            .VisibleTo(ctx, AccessDomain.Documents, null, (db.CabinetDocuments, db.Cabinets))
            .Select(d => d.Id);
        return DocumentAccessRules.ReachedByAttachmentAsync(
            db, ctx, candidates.Where(d => !admitted.Contains(d.Id)).Select(d => d.Id), ct);
    }

    /// <summary>One cabinet with its count, under the same rule the whole tree uses.</summary>
    private static async Task<CabinetDto?> ProjectOneAsync(
        SilexGisDbContext db, AccessContext ctx, Guid id, CancellationToken ct)
    {
        var shelf = db.Cabinets.AsNoTracking().Where(c => c.Id == id);
        var reached = await ReachOverAsync(db, ctx, FiledOn(db, shelf.Select(c => c.Id)), ct);
        return (await ProjectAsync(shelf, db, ctx, reached, ct)).FirstOrDefault();
    }

    /// <summary>
    /// A shelf's own settings beside what actually applies once inheritance is resolved. Both,
    /// because they answer different questions: the editor has to show what this shelf itself
    /// says, and the upload dialog has to show what an upload will actually get.
    /// </summary>
    private static CabinetDefaultsDto DefaultsOf(Cabinet cabinet, IReadOnlyCollection<Cabinet> chain)
    {
        var effective = CabinetDefaultRules.Resolve(cabinet.Id, chain);
        return new CabinetDefaultsDto(
            cabinet.DefaultDocumentTypeId,
            cabinet.DefaultVisibility,
            cabinet.DefaultTagIds,
            cabinet.RequiredMetadataKeys,
            effective.DocumentTypeId,
            effective.Visibility,
            effective.TagIds,
            effective.RequiredMetadataKeys);
    }

    /// <summary>
    /// Files and unfiles many documents in one request, reporting per-document refusals rather
    /// than failing whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every document is judged exactly as it would be one at a time: read on the document,
    /// write on the document, and the right to write documents at each shelf named. Doing them
    /// together is a convenience for the caller, never a relaxation — a bulk path that
    /// permitted what the single path refuses would be the way round the single path.
    /// </para>
    /// <para>
    /// Partial results, deliberately. A selection of two hundred documents will, on a real
    /// archive, contain one somebody else owns; refusing the whole request for it would make
    /// the feature unusable on exactly the archives it exists for, and would leave the caller
    /// picking through the selection by hand to find the offender. A document the caller may
    /// not read is in neither list — its existence is not disclosed by a refusal.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<BulkFilingResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> BulkFileAsync(
        BulkFilingRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var fileInto = request.FileIntoCabinetIds ?? [];
        var unfileFrom = request.UnfileFromCabinetIds ?? [];
        var named = fileInto.Concat(unfileFrom).Distinct().ToList();

        var shelves = await db.Cabinets.AsNoTracking()
            .Where(c => named.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);
        if (shelves.Count != named.Count)
        {
            return ApiProblems.BadRequest(NotFoundCode, "One of the cabinets does not exist.");
        }

        // The shelves are checked once for the whole request rather than per document: whether
        // this caller may write documents at a given shelf does not depend on which document.
        // A shelf they may not administer fails the request outright — unlike a document they
        // may not write, it is the caller's own choice of destination and not a property of
        // the selection.
        if (named.Exists(id => !MayAdminister(ctx, shelves[id])))
        {
            return ApiProblems.Forbidden();
        }

        var filed = new List<Guid>();
        var refused = new Dictionary<Guid, string>();

        foreach (var documentId in request.DocumentIds.Distinct())
        {
            var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
            var content = document is null ? null : await DocumentQueries.CurrentFileAsync(db, documentId, ct);

            if (document is null
                || !await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content?.File, ct))
            {
                // Never an existence oracle: a document the caller may not read is absent from
                // the answer entirely, exactly as one that is not there.
                continue;
            }

            if (!await DocumentAccessRules.CanWriteAsync(db, access, ctx, document, content?.File, ct))
            {
                refused[documentId] = DocumentWriteForbiddenCode;
                continue;
            }

            await ApplyFilingAsync(db, documentId, fileInto, unfileFrom, ct);
            filed.Add(documentId);
        }

        // One save for the whole request: a refile is one act to the person who asked for it,
        // and a half-applied move is the state that leaves an archive looking rearranged
        // without being.
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new BulkFilingResultDto(filed, refused));
    }

    /// <summary>
    /// Stages one document's filing changes. Filing what is already filed and unfiling what is
    /// not are both the same fact stated twice, so neither is an error.
    /// </summary>
    private static async Task ApplyFilingAsync(
        SilexGisDbContext db,
        Guid documentId,
        IReadOnlyList<Guid> fileInto,
        IReadOnlyList<Guid> unfileFrom,
        CancellationToken ct)
    {
        var existing = await db.CabinetDocuments
            .Where(m => m.DocumentId == documentId)
            .ToListAsync(ct);

        foreach (var cabinetId in fileInto)
        {
            if (existing.TrueForAll(m => m.CabinetId != cabinetId))
            {
                db.CabinetDocuments.Add(new CabinetDocument { CabinetId = cabinetId, DocumentId = documentId });
            }
        }

        foreach (var membership in existing.Where(m => unfileFrom.Contains(m.CabinetId)))
        {
            db.CabinetDocuments.Remove(membership);
        }
    }

    /// <summary>The metadata keys a shelf expects, its ancestors' included.</summary>
    private static async Task<IReadOnlyList<string>> ExpectedKeysAsync(
        SilexGisDbContext db, Guid cabinetId, CancellationToken ct)
    {
        var shelf = await db.Cabinets.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cabinetId, ct);
        if (shelf is null)
        {
            return [];
        }

        var ancestorIds = shelf.AncestorIds;
        var chain = await db.Cabinets.AsNoTracking().Where(c => ancestorIds.Contains(c.Id)).ToListAsync(ct);
        if (chain.TrueForAll(c => c.Id != shelf.Id))
        {
            chain.Add(shelf);
        }

        return CabinetDefaultRules.Resolve(cabinetId, chain).RequiredMetadataKeys;
    }

    /// <summary>
    /// The metadata keys a document actually answers. A key present but empty counts as
    /// unanswered: a required field left blank is exactly the case the checklist exists for,
    /// and treating it as answered would make the mark useless.
    /// </summary>
    private static IReadOnlyCollection<string> AnsweredKeys(string metadata)
    {
        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return
            [
                .. document.RootElement.EnumerateObject()
                    .Where(p => p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                        && !(p.Value.ValueKind == JsonValueKind.String
                            && string.IsNullOrWhiteSpace(p.Value.GetString())))
                    .Select(p => p.Name),
            ];
        }
        catch (JsonException)
        {
            // Metadata that will not parse answers nothing, which is the honest reading and
            // keeps a malformed row from failing a listing of two hundred good ones.
            return [];
        }
    }

    /// <summary>The documents filed directly on any of the given cabinets.</summary>
    private static IQueryable<Document> FiledOn(SilexGisDbContext db, IQueryable<Guid> cabinetIds) =>
        db.Documents.AsNoTracking()
            .Where(d => db.CabinetDocuments.Any(m => m.DocumentId == d.Id && cabinetIds.Contains(m.CabinetId)));
}
