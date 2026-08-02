// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Documents;

public sealed class DocumentUpdateRequestValidator : AbstractValidator<DocumentUpdateRequest>
{
    public DocumentUpdateRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);

        // Shape only. Whether the metadata conforms to the kind's schema needs the schema,
        // which is a database read, so that check lives in the write path rather than here.
        RuleFor(x => x.Metadata)
            .Must(BeAJsonObjectOrAbsent)
            .WithMessage("Metadata must be a JSON object.");
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
/// Access is resolved through the file the document currently serves, which is exactly how
/// every other path that reaches these bytes resolves it — a document is readable when its
/// content is, and writable when its content is. That keeps one answer to "may this caller
/// see this" while the file rules remain the only implementation of it.
/// </remarks>
public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        var documents = api.MapGroup("/documents").WithTags("Documents");

        documents.MapGet("/{id:guid}", GetAsync)
            .WithSummary("A document's title, kind and typed metadata.");
        documents.MapPut("/{id:guid}", UpdateAsync)
            .WithValidation<DocumentUpdateRequest>()
            .WithSummary("Updates a document's title, kind and typed metadata; requires write access.");

        return api;
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
        if (subject is null || !await FileAccessRules.CanAccessAsync(db, access, ctx, subject.Content.File, ct))
        {
            // Existence of an inaccessible document is not disclosed.
            return ApiProblems.NotFound("document.not_found");
        }

        return TypedResults.Ok(ToDto(subject));
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
        if (subject is null || !await FileAccessRules.CanWriteFileAsync(db, access, ctx, subject.Content.File, ct))
        {
            return ApiProblems.NotFound("document.not_found"); // existence not disclosed to non-writers
        }

        try
        {
            await documents.UpdateAsync(
                id,
                new DocumentUpdate(request.Title, request.DocumentTypeId, RawMetadata(request.Metadata)),
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
        return updated is null ? ApiProblems.NotFound("document.not_found") : TypedResults.Ok(ToDto(updated));
    }

    /// <summary>A document with the revision and file it currently serves, and its kind's code.</summary>
    private sealed record DocumentSubject(Document Document, DocumentFile Content, string? TypeCode);

    private static async Task<DocumentSubject?> LoadAsync(SilexGisDbContext db, Guid id, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return null;
        }

        var content = await DocumentQueries.CurrentFileAsync(db, id, ct);
        if (content is null)
        {
            // A document whose current revision carries no file has nothing to serve and
            // nothing to resolve access against; it is not shown rather than shown unguarded.
            return null;
        }

        var typeCode = document.DocumentTypeId is { } typeId
            ? await db.DocumentTypes.AsNoTracking().Where(t => t.Id == typeId).Select(t => t.Code).FirstOrDefaultAsync(ct)
            : null;
        return new DocumentSubject(document, content, typeCode);
    }

    private static DocumentDto ToDto(DocumentSubject subject)
    {
        var (document, content, typeCode) = subject;
        var file = content.File;
        return new DocumentDto(
            document.Id,
            document.Title,
            document.DocumentTypeId,
            typeCode,
            JsonSerializer.Deserialize<JsonElement>(document.Metadata),
            document.MetadataSchemaVersion,
            file.Id,
            content.Version.VersionNumber,
            file.MimeType,
            file.SizeBytes,
            file.Kind,
            file.PageCount,
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
