// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// A parsed attachment/tagging target: EITHER any feature (<see cref="EntityType"/> null,
/// <see cref="Id"/> is a feature id) OR a non-feature entity addressed by the polymorphic
/// pair — mirroring the XOR shape the attachment/tagging rows store.
/// </summary>
public readonly record struct AttachmentTarget(AttachedEntityType? EntityType, Guid Id)
{
    public bool IsFeature => EntityType is null;
}

/// <summary>
/// Wire vocabulary of polymorphic target types: "feature" (any feature id, whatever its
/// kind) plus the camelCase non-feature entity names ("tripLog", "cavingGroup", "geofile",
/// "georeferencedMap", "mapView", "storedFile"). Parsed case-insensitively so
/// query-string values behave like the camelCase JSON enum convention.
/// </summary>
public static class AttachmentTargets
{
    public const string FeatureName = "feature";

    /// <summary>On success a null <paramref name="entityType"/> means the feature world.</summary>
    public static bool TryParse(string? value, out AttachedEntityType? entityType)
    {
        entityType = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, FeatureName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Enum.TryParse<AttachedEntityType>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            entityType = parsed;
            return true;
        }

        return false;
    }

    /// <summary>Wire name of a stored target row (feature FK XOR polymorphic pair).</summary>
    public static string NameOf(Guid? featureId, AttachedEntityType? entityType) =>
        featureId is not null
            ? FeatureName
            : JsonNamingPolicy.CamelCase.ConvertName(entityType!.Value.ToString());
}

/// <summary>
/// Who may access a stored file: its uploader, a full administrator, or anyone with Read
/// access to at least one object the file is attached to. Attachment targets are either
/// features (real FK) or non-feature entities (polymorphic pair), so readability is
/// resolved per world here — the single home for that mapping. Caving-group targets
/// (club logo, club documents): read = member of that group or Read on the group record
/// per the access walk; write = Write on the group record — membership alone stops
/// implying management, exactly as the roster role does.
/// </summary>
public static class FileAccessRules
{
    public static async Task<bool> CanAccessAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, StoredFile file, CancellationToken ct)
    {
        if (ctx.IsFullAdmin || file.UploadedBy == ctx.UserId)
        {
            return true;
        }

        var links = await db.Attachments.AsNoTracking()
            .Where(a => a.FileId == file.Id)
            .Select(a => new { a.FeatureId, a.EntityType, a.EntityId })
            .ToListAsync(ct);

        foreach (var link in links)
        {
            var readable = link.FeatureId is { } featureId
                ? await CanReadFeatureAsync(db, ctx, featureId, ct)
                : await CanReadEntityAsync(db, access, ctx, link.EntityType!.Value, link.EntityId!.Value, ct);
            if (readable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Who may modify a file's version chain (upload a new version, list/delete old versions):
    /// a full administrator, the head's uploader, or anyone with Write on at least one object
    /// the head is attached to. A shared document is one document — a new version moves every
    /// attachment, so Write on any one attached object suffices. Evaluate against the chain
    /// head, which is the row attachments point at.
    /// </summary>
    public static async Task<bool> CanWriteFileAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, StoredFile head, CancellationToken ct)
    {
        if (ctx.IsFullAdmin || head.UploadedBy == ctx.UserId)
        {
            return true;
        }

        var links = await db.Attachments.AsNoTracking()
            .Where(a => a.FileId == head.Id)
            .Select(a => new { a.FeatureId, a.EntityType, a.EntityId })
            .ToListAsync(ct);

        foreach (var link in links)
        {
            var writable = link.FeatureId is { } featureId
                ? await CanWriteFeatureAsync(db, access, ctx, featureId, ct)
                : await CanWriteEntityAsync(db, access, ctx, link.EntityType!.Value, link.EntityId!.Value, ct);
            if (writable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Read access to a parsed polymorphic target (feature or non-feature).</summary>
    public static Task<bool> CanReadTargetAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, AttachmentTarget target, CancellationToken ct) =>
        target.EntityType is { } entityType
            ? CanReadEntityAsync(db, access, ctx, entityType, target.Id, ct)
            : CanReadFeatureAsync(db, ctx, target.Id, ct);

    /// <summary>Write access to a parsed polymorphic target (feature or non-feature).</summary>
    public static Task<bool> CanWriteTargetAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, AttachmentTarget target, CancellationToken ct) =>
        target.EntityType is { } entityType
            ? CanWriteEntityAsync(db, access, ctx, entityType, target.Id, ct)
            : CanWriteFeatureAsync(db, access, ctx, target.Id, ct);

    /// <summary>
    /// Read access to any feature, whatever its kind — the shared visibility filter.
    /// Soft-deleted features are invisible via the model-level query filter.
    /// </summary>
    public static Task<bool> CanReadFeatureAsync(
        SilexGisDbContext db, AccessContext ctx, Guid featureId, CancellationToken ct) =>
        db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .AnyAsync(f => f.Id == featureId, ct);

    /// <summary>Write access to any feature (attach/detach files, tag/untag).</summary>
    public static async Task<bool> CanWriteFeatureAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, Guid featureId, CancellationToken ct)
    {
        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == featureId, ct);
        return feature is not null
            && (await access.DecideAsync(ctx, AccessAction.Write, feature, ct)).Allowed;
    }

    /// <summary>Read access to a non-feature polymorphic target.</summary>
    public static async Task<bool> CanReadEntityAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx,
        AttachedEntityType entityType, Guid entityId, CancellationToken ct)
    {
        switch (entityType)
        {
            case AttachedEntityType.TripLog:
                return await CanEntityAsync(db, access, ctx, db.TripLogs, entityId, AccessAction.Read, ct);

            case AttachedEntityType.CavingGroup:
                return CanReadCavingGroupTarget(ctx, entityId);

            case AttachedEntityType.Geofile:
                return await CanEntityAsync(db, access, ctx, db.Geofiles, entityId, AccessAction.Read, ct);

            case AttachedEntityType.GeoreferencedMap:
                return await CanEntityAsync(db, access, ctx, db.GeoreferencedMaps, entityId, AccessAction.Read, ct);

            case AttachedEntityType.MapView:
                return await CanEntityAsync(db, access, ctx, db.MapViews, entityId, AccessAction.Read, ct);

            case AttachedEntityType.StoredFile:
                // A file (as a tag target) inherits the access of the objects it is
                // attached to — the same rule the file endpoints use.
                var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == entityId, ct);
                return file is not null && await CanAccessAsync(db, access, ctx, file, ct);

            default:
                return false;
        }
    }

    /// <summary>Write access to a non-feature polymorphic target (attach/detach files, tag/untag).</summary>
    public static async Task<bool> CanWriteEntityAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx,
        AttachedEntityType entityType, Guid entityId, CancellationToken ct)
    {
        switch (entityType)
        {
            case AttachedEntityType.TripLog:
                return await CanEntityAsync(db, access, ctx, db.TripLogs, entityId, AccessAction.Write, ct);

            case AttachedEntityType.CavingGroup:
                return HoldsOnCavingGroupRecord(ctx, entityId, AccessAction.Write);

            case AttachedEntityType.Geofile:
                return await CanEntityAsync(db, access, ctx, db.Geofiles, entityId, AccessAction.Write, ct);

            case AttachedEntityType.GeoreferencedMap:
                return await CanEntityAsync(db, access, ctx, db.GeoreferencedMaps, entityId, AccessAction.Write, ct);

            case AttachedEntityType.MapView:
                return await CanEntityAsync(db, access, ctx, db.MapViews, entityId, AccessAction.Write, ct);

            case AttachedEntityType.StoredFile:
                // Writing a file's tags is governed by the file-write rule, evaluated against the
                // chain head (the row attachments/taggings point at). Resolve the head in case a
                // non-head id was passed.
                var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == entityId, ct);
                if (file is null)
                {
                    return false;
                }

                var head = await db.StoredFiles.AsNoTracking()
                    .Where(f => f.VersionGroupId == file.VersionGroupId)
                    .OrderByDescending(f => f.VersionNumber)
                    .FirstAsync(ct);
                return await CanWriteFileAsync(db, access, ctx, head, ct);

            default:
                return false;
        }
    }

    /// <summary>
    /// Read of a caving-group-attached object (logo, club documents, member-scoped map
    /// layers): membership via the caller's caver, or a grant naming THIS group. One
    /// home for the rule — the map/attachment paths use it too.
    /// </summary>
    /// <remarks>
    /// Deliberately not the plain walk: every account holds domain-wide
    /// <c>CavingGroups · Read</c> so the group directory is browsable, and a club's
    /// documents are not its directory entry. Only a decision anchored on this very
    /// group (an object-scope entry — what the per-group managers seed carries) widens
    /// past membership.
    /// </remarks>
    public static bool CanReadCavingGroupTarget(AccessContext ctx, Guid cavingGroupId) =>
        ctx.CavingGroupIds.Contains(cavingGroupId)
        || HoldsOnCavingGroupRecord(ctx, cavingGroupId, AccessAction.Read);

    /// <summary>
    /// The access walk against a caving-group record itself, counted only when the
    /// decision rests on an entry naming the group (Object level) or on full
    /// administration. Group records carry no owner/visibility trio, so entries alone
    /// decide — the pure evaluator suffices and no storage round-trip is needed.
    /// </summary>
    private static bool HoldsOnCavingGroupRecord(AccessContext ctx, Guid cavingGroupId, AccessAction action)
    {
        var decision = AccessEvaluator.Decide(
            ctx,
            AccessDomain.CavingGroups,
            action,
            new AccessTargetFacts { ObjectId = cavingGroupId });
        return decision.Allowed
            && (decision.Source == AccessDecisionSource.FullAdministrators
                || decision.Level == AccessLevel.Object);
    }

    /// <summary>Load-and-check for walk-governed non-feature entities.</summary>
    private static async Task<bool> CanEntityAsync<T>(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, IQueryable<T> set, Guid id,
        AccessAction action, CancellationToken ct)
        where T : class, IProtectedEntity
    {
        var entity = await set.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is not null
            && (await access.DecideAsync(ctx, action, entity, ct)).Allowed;
    }
}
