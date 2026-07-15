// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Who may access a stored file: its uploader, an admin, or anyone with Read access to
/// at least one object the file is attached to. Attachment targets are polymorphic, so
/// readability is resolved per entity type here — the single home for that mapping.
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
            .Select(a => new { a.EntityType, a.EntityId })
            .ToListAsync(ct);

        foreach (var link in links)
        {
            if (await CanReadEntityAsync(db, user, link.EntityType, link.EntityId, ct))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Read access to a polymorphic attachment target.</summary>
    public static async Task<bool> CanReadEntityAsync(
        SilexGisDbContext db, UserContext user, AttachedEntityType entityType, Guid entityId, CancellationToken ct)
    {
        switch (entityType)
        {
            case AttachedEntityType.Cave:
                var cave = await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == entityId, ct);
                return cave is not null && await new AclPermissionService(db).CanAsync(user, cave, ObjectPermission.Read, ct);

            case AttachedEntityType.CaveEntrance:
                // Entrances inherit their cave's ACL.
                var entranceCave = await db.CaveEntrances.AsNoTracking()
                    .Where(e => e.Id == entityId)
                    .Join(db.Caves.AsNoTracking(), e => e.CaveId, c => c.Id, (e, c) => c)
                    .FirstOrDefaultAsync(ct);
                return entranceCave is not null && await new AclPermissionService(db).CanAsync(user, entranceCave, ObjectPermission.Read, ct);

            case AttachedEntityType.SurfaceFeature:
                var feature = await db.SurfaceFeatures.AsNoTracking().FirstOrDefaultAsync(f => f.Id == entityId, ct);
                return feature is not null && await new AclPermissionService(db).CanAsync(user, feature, ObjectPermission.Read, ct);

            case AttachedEntityType.Geofile:
                var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == entityId, ct);
                return geofile is not null && await new AclPermissionService(db).CanAsync(user, geofile, ObjectPermission.Read, ct);

            case AttachedEntityType.Team:
                return user.IsMemberOf(entityId);

            case AttachedEntityType.TripLog:
                var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entityId, ct);
                return trip is not null && await new AclPermissionService(db).CanAsync(user, trip, ObjectPermission.Read, ct);

            default:
                return false;
        }
    }

    /// <summary>
    /// Who may modify a file's version chain (upload a new version, list/delete old versions):
    /// an admin, the head's uploader, or anyone with Write on at least one entity the head is
    /// attached to. A shared document is one document — a new version moves every attachment,
    /// so Write on any one attached entity suffices. Evaluate against the chain head, which is
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
            .Select(a => new { a.EntityType, a.EntityId })
            .ToListAsync(ct);

        foreach (var link in links)
        {
            if (await CanWriteEntityAsync(db, user, link.EntityType, link.EntityId, ct))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Write access to a polymorphic attachment target (attach/detach files).</summary>
    public static async Task<bool> CanWriteEntityAsync(
        SilexGisDbContext db, UserContext user, AttachedEntityType entityType, Guid entityId, CancellationToken ct)
    {
        switch (entityType)
        {
            case AttachedEntityType.Cave:
                var cave = await db.Caves.AsNoTracking().FirstOrDefaultAsync(c => c.Id == entityId, ct);
                return cave is not null && await new AclPermissionService(db).CanAsync(user, cave, ObjectPermission.Write, ct);

            case AttachedEntityType.CaveEntrance:
                var entranceCave = await db.CaveEntrances.AsNoTracking()
                    .Where(e => e.Id == entityId)
                    .Join(db.Caves.AsNoTracking(), e => e.CaveId, c => c.Id, (e, c) => c)
                    .FirstOrDefaultAsync(ct);
                return entranceCave is not null && await new AclPermissionService(db).CanAsync(user, entranceCave, ObjectPermission.Write, ct);

            case AttachedEntityType.SurfaceFeature:
                var feature = await db.SurfaceFeatures.AsNoTracking().FirstOrDefaultAsync(f => f.Id == entityId, ct);
                return feature is not null && await new AclPermissionService(db).CanAsync(user, feature, ObjectPermission.Write, ct);

            case AttachedEntityType.Geofile:
                var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == entityId, ct);
                return geofile is not null && await new AclPermissionService(db).CanAsync(user, geofile, ObjectPermission.Write, ct);

            case AttachedEntityType.Team:
                return user.IsMemberOf(entityId) || user.IsAdmin;

            case AttachedEntityType.TripLog:
                var trip = await db.TripLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entityId, ct);
                return trip is not null && await new AclPermissionService(db).CanAsync(user, trip, ObjectPermission.Write, ct);

            default:
                return false;
        }
    }
}
