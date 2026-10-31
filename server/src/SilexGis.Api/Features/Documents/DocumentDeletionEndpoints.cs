// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Jobs;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Documents;

/// <summary>
/// Deleting a document, listing the deleted ones and putting one back.
/// </summary>
/// <remarks>
/// A deleted document is marked, not removed: it keeps its versions, files, comments, attachments,
/// links and filings, and every reader treats it as absent until it is restored. Who may do any of
/// it is the question an edit asks — see <see cref="DocumentDeletionService"/>. No route here
/// removes the rows or the bytes; the only thing that does is a sweep an installation switches on
/// with a retention window, and it is off unless it was.
/// </remarks>
internal static class DocumentDeletionEndpoints
{
    public static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> DeleteAsync(
        Guid id,
        DocumentDeletionService deletion,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        return await deletion.DeleteAsync(ctx, id, ct) switch
        {
            DocumentDeleteOutcome.Deleted => TypedResults.NoContent(),
            DocumentDeleteOutcome.Forbidden => ApiProblems.Forbidden("document.write_forbidden"),
            DocumentDeleteOutcome.InUse => ApiProblems.Conflict(
                "document.in_use",
                "A survey archive entry, survey model, georeferenced map or geofile is built on this document; remove it first."),
            _ => ApiProblems.NotFound("document.not_found"),
        };
    }

    public static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> RestoreAsync(
        Guid id,
        DocumentDeletionService deletion,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        return await deletion.RestoreAsync(ctx, id, ct) switch
        {
            DocumentRestoreOutcome.Restored => TypedResults.NoContent(),
            DocumentRestoreOutcome.Forbidden => ApiProblems.Forbidden("document.write_forbidden"),
            DocumentRestoreOutcome.NotDeleted =>
                ApiProblems.Conflict(SoftDeleteRules.NotDeletedCode, "This document is not deleted."),
            DocumentRestoreOutcome.WindowPassed => ApiProblems.Conflict(
                SoftDeleteRules.RestoreWindowPassedCode, "This document was deleted too long ago to be restored."),
            _ => ApiProblems.NotFound("document.not_found"),
        };
    }

    public static async Task<Results<Ok<PagedResult<DeletedDocumentDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        DocumentDeletionService deletion,
        IAccessContextAccessor accessAccessor,
        IUserContextAccessor userAccessor,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        var user = await userAccessor.GetAsync(ct);
        if (ctx is null || user is null)
        {
            return TypedResults.Unauthorized();
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var (shown, total) = await deletion.ListAsync(ctx, photographsOnly: false, p, size, ct);

        // Resolved rather than joined: the label an account may be shown under is a rule with one
        // home, and it is never their address.
        var names = await ProfileDirectory.ResolveLabelsAsync(
            db,
            user,
            shown.Where(r => r.Document.DeletedByUserId != null).Select(r => r.Document.DeletedByUserId!.Value),
            ct);

        return TypedResults.Ok(new PagedResult<DeletedDocumentDto>(
            [
                .. shown.Select(r => new DeletedDocumentDto(
                    r.Document.Id,
                    r.Document.Title,
                    r.Document.DocumentTypeId,
                    r.Document.DeletedAt!.Value,
                    r.RestorableUntil,
                    r.Document.DeletedByUserId,
                    r.Document.DeletedByUserId is { } by ? names.GetValueOrDefault(by) : null)),
            ],
            p,
            size,
            total));
    }

    /// <summary>
    /// What a delete is about to do here, for any signed-in caller: it describes the installation,
    /// not a document, so there is nothing in it to withhold from one of them.
    /// </summary>
    public static async Task<Results<Ok<DocumentConfigDto>, UnauthorizedHttpResult>> ConfigAsync(
        IAccessContextAccessor accessAccessor,
        IOptions<DocumentRetentionOptions> retention,
        CancellationToken ct)
    {
        if (await accessAccessor.GetAsync(ct) is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new DocumentConfigDto(
            retention.Value.Window is null ? null : retention.Value.RetentionDays));
    }
}
