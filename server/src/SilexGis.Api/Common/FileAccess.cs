// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// A parsed attachment/tagging target: EITHER any feature (<see cref="EntityType"/> null,
/// <see cref="Id"/> is a feature id) OR a non-feature entity addressed by the polymorphic
/// pair — mirroring the XOR shape the attachment/tagging/ACL rows store.
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
/// Who may access a stored file: its uploader, an admin, or anyone with Read access to
/// at least one object the file is attached to. Attachment targets are either features
/// (real FK) or non-feature entities (polymorphic pair), so readability is resolved per
/// world here — the single home for that mapping.
/// </summary>
public static class FileAccessRules
{
    public static async Task<bool> CanAccessAsync(
        SilexGisDbContext db, UserContext user, StoredFile file, CancellationToken ct)
    {
        if (user.IsAdmin || file.UploadedBy == user.UserId)
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
                ? await CanReadFeatureAsync(db, user, featureId, ct)
                : await CanReadEntityAsync(db, user, link.EntityType!.Value, link.EntityId!.Value, ct);
            if (readable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Who may modify a file's version chain (upload a new version, list/delete old versions):
    /// an admin, the head's uploader, or anyone with Write on at least one object the head is
    /// attached to. A shared document is one document — a new version moves every attachment,
    /// so Write on any one attached object suffices. Evaluate against the chain head, which is
    /// the row attachments point at.
    /// </summary>
    public static async Task<bool> CanWriteFileAsync(
        SilexGisDbContext db, UserContext user, StoredFile head, CancellationToken ct)
    {
        if (user.IsAdmin || head.UploadedBy == user.UserId)
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
                ? await CanWriteFeatureAsync(db, user, featureId, ct)
                : await CanWriteEntityAsync(db, user, link.EntityType!.Value, link.EntityId!.Value, ct);
            if (writable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Read access to a parsed polymorphic target (feature or non-feature).</summary>
    public static Task<bool> CanReadTargetAsync(
        SilexGisDbContext db, UserContext user, AttachmentTarget target, CancellationToken ct) =>
        target.EntityType is { } entityType
            ? CanReadEntityAsync(db, user, entityType, target.Id, ct)
            : CanReadFeatureAsync(db, user, target.Id, ct);

    /// <summary>Write access to a parsed polymorphic target (feature or non-feature).</summary>
    public static Task<bool> CanWriteTargetAsync(
        SilexGisDbContext db, UserContext user, AttachmentTarget target, CancellationToken ct) =>
        target.EntityType is { } entityType
            ? CanWriteEntityAsync(db, user, entityType, target.Id, ct)
            : CanWriteFeatureAsync(db, user, target.Id, ct);

    /// <summary>
    /// Read access to any feature, whatever its kind — the visibility filter including ACL
    /// grants. Soft-deleted features are invisible via the model-level query filter.
    /// </summary>
    public static Task<bool> CanReadFeatureAsync(
        SilexGisDbContext db, UserContext user, Guid featureId, CancellationToken ct) =>
        db.Features.AsNoTracking()
            .VisibleTo(user, db.ObjectAcls)
            .AnyAsync(f => f.Id == featureId, ct);

    /// <summary>Write access to any feature (attach/detach files, tag/untag).</summary>
    public static async Task<bool> CanWriteFeatureAsync(
        SilexGisDbContext db, UserContext user, Guid featureId, CancellationToken ct)
    {
        var feature = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == featureId, ct);
        return feature is not null
            && await new AclPermissionService(db).CanAsync(user, feature, ObjectPermission.Write, ct);
    }

    /// <summary>Read access to a non-feature polymorphic target.</summary>
    public static async Task<bool> CanReadEntityAsync(
        SilexGisDbContext db, UserContext user, AttachedEntityType entityType, Guid entityId, CancellationToken ct)
    {
        switch (entityType)
        {
            case AttachedEntityType.TripLog:
                return await CanEntityAsync(db, user, db.TripLogs, entityId, ObjectPermission.Read, ct);

            case AttachedEntityType.CavingGroup:
                return user.IsMemberOf(entityId);

            case AttachedEntityType.Geofile:
                return await CanEntityAsync(db, user, db.Geofiles, entityId, ObjectPermission.Read, ct);

            case AttachedEntityType.GeoreferencedMap:
                return await CanEntityAsync(db, user, db.GeoreferencedMaps, entityId, ObjectPermission.Read, ct);

            case AttachedEntityType.MapView:
                return await CanEntityAsync(db, user, db.MapViews, entityId, ObjectPermission.Read, ct);

            case AttachedEntityType.StoredFile:
                // A file (as a tag target) inherits the access of the objects it is
                // attached to — the same rule the file endpoints use.
                var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == entityId, ct);
                return file is not null && await CanAccessAsync(db, user, file, ct);

            default:
                return false;
        }
    }

    /// <summary>Write access to a non-feature polymorphic target (attach/detach files, tag/untag).</summary>
    public static async Task<bool> CanWriteEntityAsync(
        SilexGisDbContext db, UserContext user, AttachedEntityType entityType, Guid entityId, CancellationToken ct)
    {
        switch (entityType)
        {
            case AttachedEntityType.TripLog:
                return await CanEntityAsync(db, user, db.TripLogs, entityId, ObjectPermission.Write, ct);

            case AttachedEntityType.CavingGroup:
                return user.IsMemberOf(entityId) || user.IsAdmin;

            case AttachedEntityType.Geofile:
                return await CanEntityAsync(db, user, db.Geofiles, entityId, ObjectPermission.Write, ct);

            case AttachedEntityType.GeoreferencedMap:
                return await CanEntityAsync(db, user, db.GeoreferencedMaps, entityId, ObjectPermission.Write, ct);

            case AttachedEntityType.MapView:
                return await CanEntityAsync(db, user, db.MapViews, entityId, ObjectPermission.Write, ct);

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
                return await CanWriteFileAsync(db, user, head, ct);

            default:
                return false;
        }
    }

    /// <summary>Load-and-check for ACL-governed non-feature entities.</summary>
    private static async Task<bool> CanEntityAsync<T>(
        SilexGisDbContext db, UserContext user, IQueryable<T> set, Guid id,
        ObjectPermission permission, CancellationToken ct)
        where T : class, IProtectedEntity
    {
        var entity = await set.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is not null
            && await new AclPermissionService(db).CanAsync(user, entity, permission, ct);
    }
}
