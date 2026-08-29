// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.AnnotatedTexts;

/// <summary>
/// Link-annotated text: prose written in this application, over which resource links mark
/// passages.
///
/// <para>
/// There is no new world here. A body is a <see cref="Document"/> like any other — the routes
/// below create one, read its blocks and stack a new revision on it — and every rule about who
/// may read it, which club it belongs to, where it is filed and what happened to it is the
/// document slice's, unchanged. What this slice adds is the format: blocks of plain text whose
/// character stream is defined rather than extracted, so a passage selected in a browser and a
/// passage stored in an anchor are measured against the same string, to the character.
/// </para>
///
/// <para>
/// It links to nothing itself. Attaching a body to the scan it is the reading of, or to the cave
/// it describes, is a resource link, and resource links are written through their own routes by
/// the rules that own them — a second way to create one here would be a second place for those
/// rules to be enforced slightly differently.
/// </para>
/// </summary>
public static class AnnotatedTextEndpoints
{
    public static RouteGroupBuilder MapAnnotatedTextEndpoints(this RouteGroupBuilder api)
    {
        var texts = api.MapGroup("/annotated-texts").WithTags("AnnotatedTexts");

        texts.MapPost("/", CreateAsync)
            .WithValidation<AnnotatedTextCreateRequest>()
            .WithSummary("Writes a new link-annotated text document.");
        texts.MapGet("/{documentId:guid}", GetAsync)
            .WithSummary("A link-annotated text document's blocks, with the file its anchors are measured against.");
        texts.MapPut("/{documentId:guid}", ReplaceAsync)
            .WithValidation<AnnotatedTextReplaceRequest>()
            .WithSummary(
                "Replaces the body with a new revision and re-measures the links over it; "
                + "passages that survive stay exact, passages that are gone read as degraded.");

        return api;
    }

    private static async Task<Results<Created<AnnotatedTextDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        AnnotatedTextCreateRequest request,
        SilexGisDbContext db,
        AnnotatedTextService texts,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!CreateRules.MayCreate(ctx, AccessDomain.Documents, request.CavingGroupId))
        {
            return ApiProblems.Forbidden("access.create_forbidden");
        }

        if (request.CavingGroupId is { } groupId)
        {
            if (!await db.CavingGroups.AsNoTracking().AnyAsync(g => g.Id == groupId, ct))
            {
                return ApiProblems.BadRequest("document.caving_group_not_found", "The caving group does not exist.");
            }

            // Binding content to a club hands that club's members whatever their rulesets
            // grant over its content, so it is guarded beyond create.
            if (!CavingGroupBindingRules.MayBind(ctx, AccessDomain.Documents, groupId))
            {
                return ApiProblems.Forbidden(CavingGroupBindingRules.ForbiddenCode);
            }
        }

        var body = new AnnotatedTextBody(request.Blocks);
        DocumentFile stored;
        try
        {
            stored = await texts.CreateAsync(
                body, request.Title, ctx.UserId, request.Visibility, request.CavingGroupId, ct);
        }
        catch (DocumentWriteException e)
        {
            return ApiProblems.BadRequest(e.Code, e.Message);
        }

        await db.SaveChangesAsync(ct);

        var document = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == stored.Version.DocumentId, ct);
        var dto = ToDto(document, stored, body, mayWrite: true);
        return TypedResults.Created($"/api/v1/annotated-texts/{dto.DocumentId}", dto);
    }

    private static async Task<Results<Ok<AnnotatedTextDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid documentId,
        SilexGisDbContext db,
        AnnotatedTextService texts,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        var content = document is null ? null : await DocumentQueries.CurrentFileAsync(db, documentId, ct);
        if (document is null
            || document.IsDeleted
            || !await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content?.File, ct)
            || content is null)
        {
            // Existence of an unreadable document is not disclosed.
            return ApiProblems.NotFound("document.not_found");
        }

        var body = await texts.ReadBodyAsync(content.File, ct);
        if (body is null)
        {
            // Two different facts, one answer, and deliberately so: a document that is not one
            // of these, and one whose bytes will not parse, are both "this route has nothing
            // to give you". The viewer falls back to the general document viewer either way.
            return ApiProblems.BadRequest(
                AnnotatedTextService.NotAnnotatedTextCode,
                "The document is not a readable link-annotated text document.");
        }

        var mayWrite = await DocumentAccessRules.CanWriteAsync(db, access, ctx, document, content.File, ct);
        return TypedResults.Ok(ToDto(document, content, body, mayWrite));
    }

    private static async Task<Results<Ok<AnnotatedTextWriteDto>, UnauthorizedHttpResult, ProblemHttpResult>> ReplaceAsync(
        Guid documentId,
        AnnotatedTextReplaceRequest request,
        SilexGisDbContext db,
        AnnotatedTextService texts,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        var content = document is null ? null : await DocumentQueries.CurrentFileAsync(db, documentId, ct);
        if (document is null || document.IsDeleted)
        {
            return ApiProblems.NotFound("document.not_found");
        }

        if (!await DocumentAccessRules.CanWriteAsync(db, access, ctx, document, content?.File, ct))
        {
            // A caller who may read but not write learns only that they may not write it; one
            // who may not read learns nothing at all.
            return await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content?.File, ct)
                ? ApiProblems.Forbidden("document.write_forbidden")
                : ApiProblems.NotFound("document.not_found");
        }

        var body = new AnnotatedTextBody(request.Blocks);
        AnnotatedTextWrite written;
        try
        {
            written = await texts.ReplaceBodyAsync(documentId, body, ctx.UserId, ct);
        }
        catch (DocumentWriteException e)
        {
            return e.Code switch
            {
                "document.not_found" => ApiProblems.NotFound(e.Code),
                AnnotatedTextService.NoContentCode => ApiProblems.NotFound(e.Code),
                _ => ApiProblems.BadRequest(e.Code, e.Message),
            };
        }

        var updated = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId, ct);
        return TypedResults.Ok(new AnnotatedTextWriteDto(
            ToDto(updated, written.Content, body, mayWrite: true),
            new ReanchorReportDto(
                written.Reanchoring.Unmoved, written.Reanchoring.Moved, written.Reanchoring.Lost)));
    }

    private static AnnotatedTextDto ToDto(
        Document document, DocumentFile content, AnnotatedTextBody body, bool mayWrite) =>
        new(
            document.Id,
            content.File.Id,
            document.Title,
            document.Visibility,
            document.CavingGroupId,
            content.Version.VersionNumber,
            AnnotatedText.CanonicalText(body.Blocks).Length,
            mayWrite,
            body.Blocks);
}
