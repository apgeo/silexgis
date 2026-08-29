// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Documents;

/// <summary>
/// A remark on a document as it goes over the wire.
/// </summary>
/// <remarks>
/// <see cref="Body"/> is plain text and travels as plain text: nothing on either side of
/// this record parses it as markup, so what one member typed cannot become markup in
/// another member's browser.
/// <para>
/// <see cref="MayEdit"/> and <see cref="MayDelete"/> are the server's own answers, not
/// hints for the client to re-derive. A caller comparing author ids for itself would be
/// guessing at a rule that has an administrator branch in it; every route re-asks these
/// questions before acting on them anyway.
/// </para>
/// </remarks>
public sealed record DocumentCommentDto(
    Guid Id,
    Guid DocumentId,
    Guid? ParentId,
    string Body,
    Guid? AuthorId,
    string? AuthorName,
    DocumentAnchorKind AnchorKind,
    JsonElement? Anchor,
    Guid? AnchorFileId,
    bool MayEdit,
    bool MayDelete,
    DateTimeOffset? EditedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A new remark. The anchor and the thread it belongs to are fixed at creation — moving a
/// remark to a different passage, or under a different parent, would change what it was
/// replying to after the fact.
/// </summary>
public sealed record DocumentCommentCreateRequest(
    Guid? ParentId,
    string Body,
    DocumentAnchorKind AnchorKind,
    JsonElement? Anchor,
    Guid? AnchorFileId);

public sealed class DocumentCommentCreateRequestValidator : AbstractValidator<DocumentCommentCreateRequest>
{
    public DocumentCommentCreateRequestValidator()
    {
        // Shape only. What makes a body and an anchor acceptable is stated once in the
        // domain rules and re-asked in the handler, so a caller cannot get two different
        // answers depending on which door they came through.
        RuleFor(x => x.Body).NotEmpty().MaximumLength(DocumentCommentRules.MaxBodyLength);
        RuleFor(x => x.AnchorKind).IsInEnum();
        RuleFor(x => x.Anchor).Must(BeAJsonObjectOrAbsent).WithMessage("An anchor must be a JSON object.");
    }

    private static bool BeAJsonObjectOrAbsent(JsonElement? anchor) =>
        anchor is null
        || anchor.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined;
}

/// <summary>What an author may change afterwards: the words, and nothing else.</summary>
public sealed record DocumentCommentUpdateRequest(string Body);

public sealed class DocumentCommentUpdateRequestValidator : AbstractValidator<DocumentCommentUpdateRequest>
{
    public DocumentCommentUpdateRequestValidator() =>
        RuleFor(x => x.Body).NotEmpty().MaximumLength(DocumentCommentRules.MaxBodyLength);
}

/// <summary>
/// The conversation about a document.
/// </summary>
/// <remarks>
/// <para>
/// Every route here is addressed through the document the remark sits on, and every route
/// authorises by asking the document access rules about that document — one question, the
/// same one the document's own surface asks. A comment route that decided for itself would
/// become a way of learning that a document exists without being allowed to see it, and a
/// route addressed by comment id alone would be a second door onto the same rows with its
/// own idea of who may open it.
/// </para>
/// <para>
/// Posting is open to any signed-in caller who can reach the document: reaching it is the
/// whole test, and no right over the document — write, share, ownership — is asked for on
/// top. Editing and deleting stay narrow, and both are asked of the comment rules rather
/// than decided here.
/// </para>
/// <para>
/// Nothing here distinguishes "no such document" from "not yours", and nothing counts rows
/// the caller could not list: the count and the page come out of the same authorised query,
/// so a number can never disagree with what is beside it.
/// </para>
/// </remarks>
public static class DocumentCommentEndpoints
{
    public const string DocumentNotFoundCode = "document.not_found";
    public const string NotFoundCode = "comment.not_found";
    public const string ParentNotFoundCode = "comment.parent_not_found";
    public const string ReplyDepthCode = "comment.reply_depth_exceeded";
    public const string BodyInvalidCode = "comment.body_invalid";
    public const string AnchorInvalidCode = "comment.anchor_invalid";
    public const string AnchorFileNotFoundCode = "comment.anchor_file_not_found";
    public const string EditForbiddenCode = "comment.edit_forbidden";
    public const string DeleteForbiddenCode = "comment.delete_forbidden";

    public static RouteGroupBuilder MapDocumentCommentEndpoints(this RouteGroupBuilder api)
    {
        var comments = api.MapGroup("/documents/{documentId:guid}/comments").WithTags("Documents");

        comments.MapGet("/", ListAsync)
            .WithSummary("The remarks written on a document, oldest first; requires read of the document.");
        comments.MapPost("/", CreateAsync).WithValidation<DocumentCommentCreateRequest>()
            .WithSummary(
                "Writes a remark on a document; any signed-in caller who may read the document may post, "
                + "and no right over the document itself is required.");
        comments.MapPut("/{id:guid}", UpdateAsync).WithValidation<DocumentCommentUpdateRequest>()
            .WithSummary("Rewrites a remark; its author only.");
        comments.MapDelete("/{id:guid}", DeleteAsync)
            .WithSummary("Removes a remark and its replies; its author or an administrator.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<DocumentCommentDto>>, UnauthorizedHttpResult, ProblemHttpResult>>
        ListAsync(
            Guid documentId,
            SilexGisDbContext db,
            IAccessService access,
            IAccessContextAccessor accessAccessor,
            IUserContextAccessor userAccessor,
            CancellationToken ct,
            int? page = null,
            int? pageSize = null)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var document = await ReadableDocumentAsync(db, access, ctx, documentId, ct);
        if (document is null)
        {
            return ApiProblems.NotFound(DocumentNotFoundCode);
        }

        // Oldest first, and by id after that: the thread reads in the order it was written,
        // and two remarks saved in the same instant still page deterministically.
        var query = db.DocumentComments.AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id);

        var (p, size) = Paging.Normalize(page, pageSize);

        // The count comes out of this same query, so it can only ever count what the page
        // would have shown.
        var rows = await query.ToPagedAsync(p, size, c => c, ct);

        // Names are resolved after the page materialises rather than joined in: what a user
        // may be shown under is a rule with one home, never a column read off a join.
        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, rows.Items.Where(r => r.AuthorId is not null).Select(r => r.AuthorId!.Value), ct);

        return TypedResults.Ok(new PagedResult<DocumentCommentDto>(
            [.. rows.Items.Select(c => ToDto(c, labels, ctx))],
            rows.Page,
            rows.PageSize,
            rows.TotalItems));
    }

    private static async Task<Results<Ok<DocumentCommentDto>, UnauthorizedHttpResult, ProblemHttpResult>> CreateAsync(
        Guid documentId,
        DocumentCommentCreateRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null || !DocumentCommentRules.MayPost(user.UserId))
        {
            // Posting asks for nothing beyond a signed-in account, so the only caller this
            // refuses is one there is no account to attribute the remark to.
            return TypedResults.Unauthorized();
        }

        var document = await ReadableDocumentAsync(db, access, ctx, documentId, ct);
        if (document is null)
        {
            // Whether the document can be reached at all is the prior question, and it is
            // the only one posting asks: a discussion nobody but the editors could join
            // would not be a discussion. Refusing for being unreachable therefore reads the
            // same as refusing for not existing.
            return ApiProblems.NotFound(DocumentNotFoundCode);
        }

        var body = DocumentCommentRules.Normalize(request.Body);
        var bodyProblems = DocumentCommentRules.ValidateBody(request.Body);
        if (body is null || bodyProblems.Count > 0)
        {
            return ApiProblems.BadRequest(BodyInvalidCode, string.Join("; ", bodyProblems));
        }

        var anchor = RawAnchor(request.Anchor);
        var anchorProblems = DocumentAnchorRules.Validate(request.AnchorKind, anchor, request.AnchorFileId);
        if (anchorProblems.Count > 0)
        {
            return ApiProblems.BadRequest(AnchorInvalidCode, string.Join("; ", anchorProblems));
        }

        // A pin names a file of *this* document. Accepting any file id would let a remark
        // hold a reference into a document its author was never allowed to open, and would
        // make the reference itself a way of asking whether that file exists.
        if (request.AnchorFileId is { } anchorFileId
            && !await db.StoredFiles.AsNoTracking().AnyAsync(
                f => f.Id == anchorFileId
                     && db.DocumentVersions.Any(v => v.Id == f.DocumentVersionId && v.DocumentId == documentId),
                ct))
        {
            return ApiProblems.BadRequest(AnchorFileNotFoundCode, "The anchored file is not part of this document.");
        }

        // Named out here rather than inside the branch below because it outlives the
        // validation: whoever wrote the remark being answered is one of the two people the
        // new one concerns.
        DocumentComment? parent = null;
        if (request.ParentId is { } parentId)
        {
            // Scoped to this document, so a parent belonging to another one answers as
            // absent — which is both the honest answer to "may this be your parent" and the
            // answer that says nothing about what exists elsewhere.
            parent = await db.DocumentComments.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == parentId && c.DocumentId == documentId, ct);
            if (parent is null)
            {
                return ApiProblems.BadRequest(ParentNotFoundCode, "The comment being replied to does not exist here.");
            }

            if (!DocumentCommentRules.MayReplyTo(parent, documentId))
            {
                return ApiProblems.BadRequest(ReplyDepthCode, "Replies do not nest: reply to the remark that starts the thread.");
            }
        }

        var comment = new DocumentComment
        {
            DocumentId = documentId,
            ParentId = request.ParentId,
            Body = body,
            AuthorId = user.UserId,
            AnchorKind = request.AnchorKind,
            Anchor = anchor,
            AnchorFileId = request.AnchorFileId,
        };

        db.DocumentComments.Add(comment);

        // Queued into the same transaction as the remark itself, so nobody is told about
        // something that did not commit.
        await DocumentCommentNotifier.PostedAsync(db, access, user, document, parent, ct);
        await db.SaveChangesAsync(ct);

        var labels = await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);
        return TypedResults.Ok(ToDto(comment, labels, ctx));
    }

    private static async Task<Results<Ok<DocumentCommentDto>, UnauthorizedHttpResult, ProblemHttpResult>> UpdateAsync(
        Guid documentId,
        Guid id,
        DocumentCommentUpdateRequest request,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var document = await ReadableDocumentAsync(db, access, ctx, documentId, ct);
        if (document is null)
        {
            return ApiProblems.NotFound(DocumentNotFoundCode);
        }

        var comment = await db.DocumentComments.FirstOrDefaultAsync(c => c.Id == id && c.DocumentId == documentId, ct);
        if (comment is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        // Only the author rewrites. An administrator may take a remark down, but putting
        // different words in somebody else's mouth is not a power this system grants, so
        // there is no moderator branch here on purpose.
        if (!DocumentCommentRules.MayEdit(comment, user.UserId))
        {
            return ApiProblems.Forbidden(EditForbiddenCode);
        }

        var body = DocumentCommentRules.Normalize(request.Body);
        var bodyProblems = DocumentCommentRules.ValidateBody(request.Body);
        if (body is null || bodyProblems.Count > 0)
        {
            return ApiProblems.BadRequest(BodyInvalidCode, string.Join("; ", bodyProblems));
        }

        if (body != comment.Body)
        {
            // Stamped only when the words actually changed, so "edited" means edited rather
            // than "saved again". The audit trail keeps what it used to say.
            comment.Body = body;
            comment.EditedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        var labels = await ProfileDirectory.ResolveLabelsAsync(
            db, user, comment.AuthorId is { } author ? [author] : [], ct);
        return TypedResults.Ok(ToDto(comment, labels, ctx));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid documentId,
        Guid id,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var document = await ReadableDocumentAsync(db, access, ctx, documentId, ct);
        if (document is null)
        {
            return ApiProblems.NotFound(DocumentNotFoundCode);
        }

        var comment = await db.DocumentComments.FirstOrDefaultAsync(c => c.Id == id && c.DocumentId == documentId, ct);
        if (comment is null)
        {
            return ApiProblems.NotFound(NotFoundCode);
        }

        if (!DocumentCommentRules.MayDelete(comment, user.UserId, ctx.IsFullAdmin))
        {
            return ApiProblems.Forbidden(DeleteForbiddenCode);
        }

        // Physical, as every child row here is except features: what a deleted remark leaves
        // behind is its audit entry, which holds the text it said. Replies go with it,
        // because an answer without its question is noise — but they are removed through the
        // change tracker rather than left to the database's cascade, which writes no audit
        // row. Somebody else's words disappearing is exactly the event the timeline has to
        // keep. Threads are one level deep, so this query is the whole thread.
        var thread = await db.DocumentComments
            .Where(c => c.Id == comment.Id || c.ParentId == comment.Id)
            .ToListAsync(ct);
        db.DocumentComments.RemoveRange(thread);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// The document, or null when this caller may not read it — the single gate every route
    /// above passes through, and the reason none of them needs a rule of its own.
    /// </summary>
    private static async Task<Document?> ReadableDocumentAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return null;
        }

        var content = await DocumentQueries.CurrentFileAsync(db, documentId, ct);
        return await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content?.File, ct) ? document : null;
    }

    private static DocumentCommentDto ToDto(
        DocumentComment comment, IReadOnlyDictionary<Guid, string> labels, AccessContext ctx)
    {
        return new DocumentCommentDto(
            comment.Id,
            comment.DocumentId,
            comment.ParentId,
            comment.Body,
            comment.AuthorId,
            comment.AuthorId is { } author ? labels.GetValueOrDefault(author) : null,
            comment.AnchorKind,
            comment.Anchor is null ? null : JsonSerializer.Deserialize<JsonElement>(comment.Anchor),
            comment.AnchorFileId,
            DocumentCommentRules.MayEdit(comment, ctx.UserId),
            DocumentCommentRules.MayDelete(comment, ctx.UserId, ctx.IsFullAdmin),
            comment.EditedAt,
            comment.CreatedAt,
            comment.UpdatedAt);
    }

    /// <summary>An absent anchor stays absent — that is what "the whole document" looks like.</summary>
    private static string? RawAnchor(JsonElement? anchor) =>
        anchor is { ValueKind: JsonValueKind.Object } a ? a.GetRawText() : null;
}
