// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Documents;

public sealed class DocumentUpdateRequestValidator : AbstractValidator<DocumentUpdateRequest>
{
    /// <summary>
    /// Longest language tag accepted on the wire. Wider than the stored code because a caller
    /// may legitimately send a full tag — "ro-Latn-RO" — which is narrowed to its primary
    /// subtag on the way in; it is a bound on the request, not a statement about languages.
    /// </summary>
    private const int LanguageTagMaxLength = 32;

    public DocumentUpdateRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Visibility).IsInEnum();

        // Club visibility says "the club this belongs to may read it", so it needs a club
        // to name; without one it would be a band that admits nobody, silently.
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.Visibility == Visibility.CavingGroup)
            .WithMessage("Caving-group visibility needs a caving group.");

        // Shape only. Whether the metadata conforms to the kind's schema needs the schema,
        // which is a database read, so that check lives in the write path rather than here.
        RuleFor(x => x.Metadata)
            .Must(BeAJsonObjectOrAbsent)
            .WithMessage("Metadata must be a JSON object.");

        // Length only. A code that is not a language subtag is not an error here — it means
        // "nobody has said", which is a legitimate thing to ask for and is how the field is
        // cleared. What must not pass is a value too long for the column it is stored in.
        RuleFor(x => x.Language).MaximumLength(LanguageTagMaxLength);
    }

    private static bool BeAJsonObjectOrAbsent(JsonElement? metadata) =>
        metadata is null
        || metadata.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined;
}

/// <summary>
/// The document surface: the identity behind a stored file, and the typed metadata that
/// belongs to the document rather than to any one revision of it.
/// </summary>
/// <remarks>
/// A document is content in its own right: rules written against it decide first, then its
/// owner and its visibility band, and only when none of those has an opinion does the file
/// it currently serves answer — which is how a document reached by being attached to a cave
/// or a trip keeps working. Existence is never disclosed to a caller who cannot read: a
/// refusal reads the same as a document that is not there.
/// </remarks>
public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        var documents = api.MapGroup("/documents").WithTags("Documents");

        documents.MapGet("/unfiled", ListUnfiledAsync)
            .WithSummary("Documents the caller may read that sit in no cabinet — the inbox filing is deferred into.");
        documents.MapGet("/{id:guid}", GetAsync)
            .WithSummary("A document's title, kind and typed metadata.");
        documents.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<DocumentUpdateRequest>()
            .WithSummary("Updates a document's title, kind and typed metadata; requires write access.");

        return api;
    }

    /// <summary>
    /// The inbox: documents filed nowhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Unfiled" is a query rather than a place — a document with no membership row — which is
    /// what keeps filing a pure addition and leaving the inbox automatic. A real cabinet would
    /// have made filing a move, given the inbox rules of its own, and left a ghost row for
    /// every document anybody ever uploaded.
    /// </para>
    /// <para>
    /// It is filtered by exactly the document read rule every other listing uses, so this is
    /// not a way to see anything. What makes it the uploader's own in practice is that a
    /// document with no cabinet has no cabinet rule reaching it and starts private: the rule
    /// admits its owner and whoever an entry names, and nobody else — which is what
    /// "an unfiled file inherits from nothing, so it gets the uploader's own access" means once
    /// it is written down as a query.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<PagedResult<UnfiledDocumentDto>>, UnauthorizedHttpResult>> ListUnfiledAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct,
        Guid? uploadBatchId = null,
        int? page = null,
        int? pageSize = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var unfiled = db.Documents.AsNoTracking()
            .Where(d => !db.CabinetDocuments.Any(m => m.DocumentId == d.Id))
            .Where(d => uploadBatchId == null || d.UploadBatchId == uploadBatchId);

        // Reach through an attachment is asked about the same candidates the listing will show,
        // exactly as the cabinet listing asks it, so a document reached only by being hung on a
        // cave the caller may read appears here too — it is unfiled, and being attached is not
        // being filed.
        var admitted = unfiled
            .VisibleTo(ctx, AccessDomain.Documents, null, (db.CabinetDocuments, db.Cabinets))
            .Select(d => d.Id);
        var reached = await DocumentAccessRules.ReachedByAttachmentAsync(
            db, ctx, unfiled.Where(d => !admitted.Contains(d.Id)).Select(d => d.Id), ct);

        var (p, size) = Paging.Normalize(page, pageSize);
        var visible = unfiled
            .VisibleTo(ctx, AccessDomain.Documents, reached, (db.CabinetDocuments, db.Cabinets))
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
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

        return TypedResults.Ok(await visible.ToPagedAsync(
            p,
            size,
            row => new UnfiledDocumentDto(
                row.Document.Id,
                row.Document.Title,
                row.Document.DocumentTypeId,
                row.Document.Visibility,
                row.Document.CavingGroupId,
                row.Document.UploadBatchId,
                row.CurrentFile == null ? null : row.CurrentFile.Id,
                row.CurrentFile == null ? null : row.CurrentFile.Kind,
                row.CurrentFile == null ? null : row.CurrentFile.MimeType,
                row.CurrentFile == null ? null : row.CurrentFile.SizeBytes,
                row.Document.CreatedAt),
            ct));
    }

    private static async Task<Results<Ok<DocumentDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
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

        var subject = await LoadAsync(db, id, ct);
        if (subject is null
            || !await DocumentAccessRules.CanReadAsync(db, access, ctx, subject.Document, subject.Content?.File, ct)
            || subject.Content is null)
        {
            // Existence of an unreadable document is not disclosed.
            return ApiProblems.NotFound("document.not_found");
        }

        return TypedResults.Ok(ToDto(subject, subject.Content));
    }

    private static async Task<Results<Ok<DocumentDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid id,
        DocumentUpdateRequest request,
        SilexGisDbContext db,
        DocumentWriteService documents,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var subject = await LoadAsync(db, id, ct);
        if (subject is null
            || !await DocumentAccessRules.CanWriteAsync(db, access, ctx, subject.Document, subject.Content?.File, ct))
        {
            // A caller who may read but not write learns only that they may not write it;
            // one who may not read learns nothing at all.
            return await CanReadAsync(db, access, ctx, subject, ct)
                ? ApiProblems.Forbidden("document.write_forbidden")
                : ApiProblems.NotFound("document.not_found");
        }

        if (request.CavingGroupId is { } requestedGroupId
            && !await db.CavingGroups.AsNoTracking().AnyAsync(g => g.Id == requestedGroupId, ct))
        {
            return ApiProblems.BadRequest("document.caving_group_not_found", "The caving group does not exist.");
        }

        // Binding content to a club hands that club's members whatever their rulesets grant
        // over its content, so it is guarded beyond write: only a member, or somebody
        // holding a rule that names that club's documents, may do it. Asked only when the
        // binding actually changes — re-saving a document into the club it is already in is
        // not a fresh act of binding, and refusing it would lock a title correction behind
        // membership.
        if (request.CavingGroupId is { } cavingGroupId
            && cavingGroupId != subject.Document.CavingGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Documents, cavingGroupId))
        {
            return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
        }

        try
        {
            await documents.UpdateAsync(
                id,
                new DocumentUpdate(
                    request.Title,
                    request.DocumentTypeId,
                    RawMetadata(request.Metadata),
                    request.Visibility,
                    request.CavingGroupId,
                    request.Language),
                ct);
        }
        catch (DocumentWriteException e)
        {
            return e.Code switch
            {
                "document.not_found" => ApiProblems.NotFound(e.Code),
                _ => ApiProblems.BadRequest(e.Code, e.Message),
            };
        }

        var updated = await LoadAsync(db, id, ct);
        return updated?.Content is null
            ? ApiProblems.NotFound("document.not_found")
            : TypedResults.Ok(ToDto(updated, updated.Content));
    }

    /// <summary>
    /// A document with the revision and file it currently serves — null when it serves
    /// none — and its kind's code.
    /// </summary>
    private sealed record DocumentSubject(
        Document Document, DocumentFile? Content, string? TypeCode, List<Guid> CabinetIds);

    private static async Task<DocumentSubject?> LoadAsync(SilexGisDbContext db, Guid id, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return null;
        }

        // Loaded whether or not there is content behind it: the document row is what the
        // access decision is made against, and a document that serves no file is refused
        // for having nothing to describe, not for being unreadable.
        var content = await DocumentQueries.CurrentFileAsync(db, id, ct);
        var typeCode = document.DocumentTypeId is { } typeId
            ? await db.DocumentTypes.AsNoTracking().Where(t => t.Id == typeId).Select(t => t.Code).FirstOrDefaultAsync(ct)
            : null;

        // The cabinets it is filed in, not their ancestors: this says where the document
        // was put, which is what a filing control edits. Which cabinets *reach* it — the
        // access question — is the ancestry walk the access rule does for itself.
        var cabinetIds = await db.CabinetDocuments.AsNoTracking()
            .Where(m => m.DocumentId == id)
            .Select(m => m.CabinetId)
            .ToListAsync(ct);
        return new DocumentSubject(document, content, typeCode, cabinetIds);
    }

    private static Task<bool> CanReadAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, DocumentSubject? subject, CancellationToken ct) =>
        subject is null
            ? Task.FromResult(false)
            : DocumentAccessRules.CanReadAsync(db, access, ctx, subject.Document, subject.Content?.File, ct);

    private static DocumentDto ToDto(DocumentSubject subject, DocumentFile content)
    {
        var (document, _, typeCode, cabinetIds) = subject;
        var file = content.File;
        return new DocumentDto(
            document.Id,
            document.Title,
            document.DocumentTypeId,
            typeCode,
            JsonSerializer.Deserialize<JsonElement>(document.Metadata),
            document.MetadataSchemaVersion,
            document.Visibility,
            document.CavingGroupId,
            cabinetIds,
            file.Id,
            content.Version.VersionNumber,
            file.MimeType,
            file.SizeBytes,
            file.Kind,
            file.PageCount,
            file.TextExtraction,
            document.Language,
            file.Author,
            file.Producer,
            file.ContentCreatedAt,
            file.ContentModifiedAt,
            file.DurationSeconds,
            file.Codec,
            document.CreatedAt,
            document.UpdatedAt);
    }

    /// <summary>Absent metadata stays absent — that is what "leave it as stored" looks like.</summary>
    private static string? RawMetadata(JsonElement? metadata) =>
        metadata is { ValueKind: JsonValueKind.Object } m ? m.GetRawText() : null;
}
