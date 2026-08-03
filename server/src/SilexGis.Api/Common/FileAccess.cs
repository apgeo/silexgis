// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
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
        if (ctx.IsFullAdmin || await DocumentQueries.UploaderOfFileAsync(db, file.Id, ct) == ctx.UserId)
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
    /// The same question as <see cref="CanAccessAsync"/>, asked about many files at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asking one at a time costs a round trip per file and another per object that file
    /// hangs on, which a listing turns into a query storm — and the answers do not depend on
    /// each other, so the work is the same work done separately. This resolves each world
    /// once for the whole batch instead — which of the named features are readable, which of
    /// the trips, and so on — so the number of queries is set by how many kinds of thing the
    /// files hang on, not by how many files there are.
    /// </para>
    /// <para>
    /// It must answer identically to the one-at-a-time form, and it does so by asking the
    /// same questions rather than by reimplementing them — the feature filter is the one the
    /// single check uses, the walk-governed worlds go through the filter their point check is
    /// pinned to agree with, and the caving-group rule is called outright, since it needs no
    /// storage. A test runs both forms over the same mixed fixture and compares.
    /// </para>
    /// <para>
    /// One difference, and it is in this form's favour: two files naming each other send the
    /// single check recursing until the stack runs out, while this one walks each file once
    /// and settles. Nothing can author that pair anyway — a file is not an accepted
    /// attachment target — so the forms agree on every row the system can actually hold, and
    /// differ only where the other one has no answer to give at all.
    /// </para>
    /// </remarks>
    public static async Task<HashSet<Guid>> ReadableFileIdsAsync(
        SilexGisDbContext db, AccessContext ctx, IReadOnlyCollection<Guid> fileIds, CancellationToken ct)
    {
        var ids = fileIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        if (ctx.IsFullAdmin)
        {
            return [.. ids];
        }

        // A file can name another file as its target, and that one can name a third, so what
        // is really being asked about is a closure rather than a list. It is collected first,
        // in whole rounds: every file in it needs the same handful of world lookups, and doing
        // those once for all of them is the whole point of this form. A file enters the set
        // once and is never revisited, which is also what lets two files naming each other
        // settle here instead of recursing until the stack runs out.
        var closure = new HashSet<Guid>(ids);
        var links = new List<AttachmentLink>();
        var frontier = ids;
        while (frontier.Count > 0)
        {
            var round = await db.Attachments.AsNoTracking()
                .Where(a => frontier.Contains(a.FileId))
                .Select(a => new AttachmentLink(a.FileId, a.FeatureId, a.EntityType, a.EntityId))
                .ToListAsync(ct);
            links.AddRange(round);

            var next = new List<Guid>();
            foreach (var link in round)
            {
                if (link.EntityType == AttachedEntityType.StoredFile && closure.Add(link.EntityId!.Value))
                {
                    next.Add(link.EntityId!.Value);
                }
            }

            frontier = next;
        }

        // Uploading covers the whole closure, not just what was asked about: a file the caller
        // uploaded is a readable step on the way to one that names it.
        var readable = ctx.UserId is { } userId
            ? await DocumentQueries.FileIdsUploadedByAsync(db, userId, [.. closure], ct)
            : [];
        if (links.Count == 0)
        {
            readable.IntersectWith(ids);
            return readable;
        }

        var readableFeatureIds = await ReadableIdsAsync(
            db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers).Select(f => f.Id),
            [.. links.Where(l => l.FeatureId != null).Select(l => l.FeatureId!.Value)],
            ct);
        var readableTripIds = await ReadableIdsAsync(
            db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs).Select(t => t.Id),
            EntityIdsOf(links, AttachedEntityType.TripLog),
            ct);
        var readableGeofileIds = await ReadableIdsAsync(
            db.Geofiles.AsNoTracking().VisibleTo(ctx, AccessDomain.Geofiles).Select(g => g.Id),
            EntityIdsOf(links, AttachedEntityType.Geofile),
            ct);
        var readableMapIds = await ReadableIdsAsync(
            db.GeoreferencedMaps.AsNoTracking().VisibleTo(ctx, AccessDomain.GeoreferencedMaps).Select(m => m.Id),
            EntityIdsOf(links, AttachedEntityType.GeoreferencedMap),
            ct);
        var readableViewIds = await ReadableIdsAsync(
            db.MapViews.AsNoTracking().VisibleTo(ctx, AccessDomain.MapViews).Select(v => v.Id),
            EntityIdsOf(links, AttachedEntityType.MapView),
            ct);

        // Repeated until nothing new turns up, because one file becoming readable can be the
        // reason another one is, and the two can appear in either order. The set only grows
        // and every file can enter it once, so this ends — and it ends on the same answer the
        // one-at-a-time form reaches by recursing, without any of the recursion.
        var groups = links.GroupBy(l => l.FileId).ToList();
        bool grew;
        do
        {
            grew = false;
            foreach (var group in groups)
            {
                if (readable.Contains(group.Key))
                {
                    continue;
                }

                var reachable = group.Any(l => l.FeatureId is { } featureId
                    ? readableFeatureIds.Contains(featureId)
                    : l.EntityType switch
                    {
                        AttachedEntityType.TripLog => readableTripIds.Contains(l.EntityId!.Value),
                        AttachedEntityType.CavingGroup => CanReadCavingGroupTarget(ctx, l.EntityId!.Value),
                        AttachedEntityType.Geofile => readableGeofileIds.Contains(l.EntityId!.Value),
                        AttachedEntityType.GeoreferencedMap => readableMapIds.Contains(l.EntityId!.Value),
                        AttachedEntityType.MapView => readableViewIds.Contains(l.EntityId!.Value),
                        AttachedEntityType.StoredFile => readable.Contains(l.EntityId!.Value),
                        _ => false,
                    });
                if (reachable)
                {
                    readable.Add(group.Key);
                    grew = true;
                }
            }
        }
        while (grew);

        readable.IntersectWith(ids);
        return readable;

        static List<Guid> EntityIdsOf(IEnumerable<AttachmentLink> rows, AttachedEntityType wanted) =>
            [.. rows.Where(r => r.EntityType == wanted).Select(r => r.EntityId!.Value).Distinct()];
    }

    /// <summary>One attachment row, reduced to what deciding readability needs.</summary>
    private readonly record struct AttachmentLink(
        Guid FileId, Guid? FeatureId, AttachedEntityType? EntityType, Guid? EntityId);

    /// <summary>Intersects candidate ids with an already-filtered id query (empty → empty, no round trip).</summary>
    private static async Task<HashSet<Guid>> ReadableIdsAsync(
        IQueryable<Guid> visibleIds, IReadOnlyList<Guid> candidates, CancellationToken ct) =>
        candidates.Count == 0
            ? []
            : [.. await visibleIds.Where(id => candidates.Contains(id)).ToListAsync(ct)];

    /// <summary>
    /// Who may modify a file's document (upload a new version, list/delete superseded ones):
    /// a full administrator, the uploader of the version being evaluated, or anyone with Write
    /// on at least one object that version's file is attached to. A shared document is one
    /// document — a new version moves every attachment, so Write on any one attached object
    /// suffices. Evaluate against the file the document currently serves, which is the row
    /// attachments point at.
    /// </summary>
    public static async Task<bool> CanWriteFileAsync(
        SilexGisDbContext db, IAccessService access, AccessContext ctx, StoredFile head, CancellationToken ct)
    {
        if (ctx.IsFullAdmin || await DocumentQueries.UploaderOfFileAsync(db, head.Id, ct) == ctx.UserId)
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
                // Writing a file's tags is governed by the file-write rule, evaluated against
                // the file the document currently serves (the row attachments/taggings point
                // at). Resolve it in case a superseded version's id was passed.
                var head = await DocumentQueries.CurrentFileOfDocumentAsync(db, entityId, ct);
                return head is not null && await CanWriteFileAsync(db, access, ctx, head, ct);

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
