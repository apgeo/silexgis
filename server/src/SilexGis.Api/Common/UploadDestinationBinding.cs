// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Where an upload is aimed, as the wire states it. Every part is optional and they combine:
/// a file may be filed on a shelf, hung on a cave, both, or neither.
/// </summary>
/// <param name="CabinetId">The shelf to file into, or none.</param>
/// <param name="RelativePath">
/// The path the browser reported for a file inside a dropped folder
/// (<c>1987/bulletins/march.pdf</c>). The folders become cabinets under
/// <paramref name="CabinetId"/>; the file name itself is ignored here, because the uploaded
/// part carries its own.
/// </param>
/// <param name="AttachEntityType">Wire name of an attachment target ("feature", "cave", …).</param>
/// <param name="AttachEntityId">Which one.</param>
/// <param name="AttachRole">What the attachment is for; the file's kind decides when absent.</param>
/// <param name="CavingGroupId">The club the created document belongs to.</param>
public readonly record struct UploadDestinationRequest(
    Guid? CabinetId,
    string? RelativePath,
    string? AttachEntityType,
    Guid? AttachEntityId,
    AttachmentRole? AttachRole,
    Guid? CavingGroupId);

/// <summary>
/// Turns a stated destination into one the ingest path can use, refusing what it may not do.
///
/// <para>
/// Separate from the endpoints because four routes state a destination — a plain upload, the
/// opening of a resumable one, its completion, and an archive expansion — and they must
/// resolve and guard it identically. A route that checked the shelf but not the object it was
/// attaching to would be a way of publishing a document to everyone who can read a cave,
/// without holding write on either.
/// </para>
/// </summary>
public static class UploadDestinationBinding
{
    public const string CabinetNotFoundCode = "cabinet.not_found";
    public const string FilingForbiddenCode = "document.filing_forbidden";
    public const string PathRefusedCode = "upload.path_refused";
    public const string TargetUnknownCode = "attachment.entity_type_unknown";
    public const string TargetNotFoundCode = "attachment.entity_not_found";

    /// <summary>
    /// The resolved destination, or the problem that stops it. Every refusal here happens
    /// before a byte is transferred.
    /// </summary>
    public static async Task<(UploadDestination? Destination, ProblemHttpResult? Problem)> ResolveAsync(
        UploadDestinationRequest request,
        AccessContext ctx,
        SilexGisDbContext db,
        UploadIngestService ingest,
        IAccessService access,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(ingest);

        if (request.CabinetId is { } cabinetId
            && !await db.Cabinets.AsNoTracking().AnyAsync(c => c.Id == cabinetId, ct))
        {
            return (null, ApiProblems.BadRequest(CabinetNotFoundCode, "The cabinet does not exist."));
        }

        // Asked about the shelf the drop names, before anything is stored. The ingest path
        // asks again about the shelf each file actually lands on — which for a folder drop is
        // one it creates — so this is the early, explainable refusal rather than the only one.
        if (!await ingest.MayFileIntoAsync(ctx, request.CabinetId, ct))
        {
            return (null, ApiProblems.Forbidden(FilingForbiddenCode));
        }

        // The browser's own relative path, validated by the same rule an archive entry gets.
        // A path that names somewhere it should not is refused rather than sanitised, so a
        // folder drop cannot quietly file somebody's archive somewhere they did not ask for.
        var segments = FilingPaths.FolderSegmentsOf(request.RelativePath);
        if (segments is null)
        {
            return (null, ApiProblems.BadRequest(PathRefusedCode, "The folder path cannot be filed."));
        }

        AttachedEntityType? attachEntityType = null;
        Guid? attachEntityId = null;
        Guid? attachFeatureId = null;

        if (!string.IsNullOrWhiteSpace(request.AttachEntityType))
        {
            if (request.AttachEntityId is not { } targetId)
            {
                return (null, ApiProblems.BadRequest(TargetNotFoundCode, "The attachment target is incomplete."));
            }

            if (!AttachmentTargets.TryParse(request.AttachEntityType, out var parsed))
            {
                return (null, ApiProblems.BadRequest(
                    TargetUnknownCode, $"Unknown entity type '{request.AttachEntityType}'."));
            }

            // The same guard the attachment endpoint applies, asked here because this request
            // creates the attachment itself. Attaching hands the document to everyone who can
            // read the target, so it takes write on that target — uploading is not a way round
            // that.
            var target = new AttachmentTarget(parsed, targetId);
            if (!await FileAccessRules.CanWriteTargetAsync(db, access, ctx, target, ct))
            {
                return (null, await FileAccessRules.CanReadTargetAsync(db, access, ctx, target, ct)
                    ? ApiProblems.Forbidden()
                    : ApiProblems.BadRequest(TargetNotFoundCode, "The attachment target does not exist."));
            }

            attachEntityType = parsed;
            attachFeatureId = parsed is null ? targetId : null;
            attachEntityId = parsed is null ? null : targetId;
        }

        return (
            new UploadDestination(
                request.CabinetId,
                segments,
                attachEntityType,
                attachEntityId,
                attachFeatureId,
                request.AttachRole,
                request.CavingGroupId),
            null);
    }
}
