// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Permissions;

/// <summary>Which column of the access tables carries a reference to another row.</summary>
public enum AccessReferenceColumn
{
    /// <summary><c>access_entries.scope_id</c> of an object-scoped entry in the arm's domain.</summary>
    ObjectScope,

    /// <summary><c>access_entries.scope_id</c> of an entry whose scope kind is the arm's.</summary>
    ScopeId,

    /// <summary><c>access_entries.subject_id</c> of a direct entry of the arm's subject kind.</summary>
    Subject,

    /// <summary><c>permission_group_members.member_id</c> of the arm's member kind.</summary>
    PermissionMember,

    /// <summary><c>access_entries.scope_feature_id</c>, a real foreign key that restricts.</summary>
    ScopeFeature,

    /// <summary><c>access_entries.granted_via_expedition_id</c>, a real foreign key that restricts.</summary>
    GrantedViaExpedition,
}

/// <summary>
/// One way an access row can name another row: the column, the discriminator that says the
/// column means this, and the tables the named row can live in.
/// </summary>
/// <remarks>
/// <see cref="Targets"/> holds more than one type only where one domain governs more than one
/// table: an album is governed as a document, so an object-scoped rule in the document domain
/// names a document or an album, and is dangling only when it names neither.
/// </remarks>
public sealed record AccessAnchorArm(string Name, AccessReferenceColumn Column, IReadOnlyList<Type> Targets)
{
    public AccessDomain Domain { get; init; }

    public AccessScopeKind ScopeKind { get; init; }

    public AccessSubjectKind SubjectKind { get; init; }

    /// <summary>
    /// Whether the database refuses a dangling value by itself. Those columns cannot hold an
    /// orphan, so the sweep has nothing to look for there — but a hard delete must still take
    /// the rows first, or the restricting key refuses the delete.
    /// </summary>
    public bool HasForeignKey => Column is AccessReferenceColumn.ScopeFeature or AccessReferenceColumn.GrantedViaExpedition;
}

/// <summary>
/// A database cascade that removes rows an access row can name, without anything in the change
/// tracker saying so: deleting the principal deletes the dependents in the same statement.
/// </summary>
public sealed record AccessAnchorCascade(
    Type Principal,
    Type Dependent,
    string ForeignKey,
    Func<SilexGisDbContext, Guid[], IQueryable<Guid>> Dependents);

/// <summary>
/// Everything an access row can point at, in one list: which rows can be named, by which column,
/// and which database cascades remove such rows behind the change tracker's back.
/// </summary>
/// <remarks>
/// <para>
/// Three things are built on this list and cannot disagree because of it: the removal of the rows
/// that name a row being hard-deleted (<see cref="AccessAnchorCleanup"/>), the search for rows
/// whose target is already gone (<see cref="AccessOrphanSql"/>), and the integrity check that
/// reports them. A domain, a scope kind or a subject kind added later must be added here, and an
/// architecture test fails until it is — the alternative is a rule that outlives its object and
/// reads as a live grant on every surface listing rules by subject.
/// </para>
/// <para>
/// A soft delete is not a delete for any of this: the row stays in its table, so the rules naming
/// it stay too, and putting it back gives back exactly the access it had.
/// </para>
/// </remarks>
public static class AccessAnchors
{
    public static IReadOnlyList<AccessAnchorArm> Arms { get; } =
    [
        ObjectArm(AccessDomain.TripLogs, typeof(TripLog)),
        ObjectArm(AccessDomain.Geofiles, typeof(Geofile)),
        ObjectArm(AccessDomain.GeoreferencedMaps, typeof(GeoreferencedMap)),
        ObjectArm(AccessDomain.MapViews, typeof(MapView)),
        ObjectArm(AccessDomain.Files, typeof(StoredFile)),
        ObjectArm(AccessDomain.Documents, typeof(Document), typeof(Album)),
        ObjectArm(AccessDomain.Expeditions, typeof(Expedition)),
        ObjectArm(AccessDomain.Checklists, typeof(Checklist)),
        ObjectArm(AccessDomain.Events, typeof(Event)),
        ObjectArm(AccessDomain.CavingGroups, typeof(CavingGroup)),
        ObjectArm(AccessDomain.PermissionGroups, typeof(PermissionGroup)),
        ObjectArm(AccessDomain.FeatureSets, typeof(FeatureSet)),

        ScopeArm(AccessScopeKind.CavingGroup, typeof(CavingGroup)),
        ScopeArm(AccessScopeKind.FeatureSet, typeof(FeatureSet)),
        ScopeArm(AccessScopeKind.Cabinet, typeof(Cabinet)),

        new("subject:User", AccessReferenceColumn.Subject, [typeof(SilexGisUser)]) { SubjectKind = AccessSubjectKind.User },
        new("subject:CavingGroup", AccessReferenceColumn.Subject, [typeof(CavingGroup)]) { SubjectKind = AccessSubjectKind.CavingGroup },
        new("member:User", AccessReferenceColumn.PermissionMember, [typeof(SilexGisUser)]) { SubjectKind = AccessSubjectKind.User },
        new("member:CavingGroup", AccessReferenceColumn.PermissionMember, [typeof(CavingGroup)]) { SubjectKind = AccessSubjectKind.CavingGroup },

        new("feature", AccessReferenceColumn.ScopeFeature, [typeof(Feature)]),
        new("grantedViaExpedition", AccessReferenceColumn.GrantedViaExpedition, [typeof(Expedition)]),
    ];

    /// <summary>
    /// The database cascades that end at a row in <see cref="Arms"/>. Deleting an account takes
    /// the map views it owns; deleting a document takes its versions, a version its files, and a
    /// file the renditions converted from it — each in the database, where no interceptor sees it.
    /// </summary>
    public static IReadOnlyList<AccessAnchorCascade> Cascades { get; } =
    [
        new(typeof(SilexGisUser), typeof(MapView), nameof(MapView.OwnerUserId),
            (db, ids) => db.MapViews.Where(v => ids.Contains(v.OwnerUserId)).Select(v => v.Id)),
        new(typeof(Document), typeof(DocumentVersion), nameof(DocumentVersion.DocumentId),
            (db, ids) => db.DocumentVersions.Where(v => ids.Contains(v.DocumentId)).Select(v => v.Id)),
        new(typeof(DocumentVersion), typeof(StoredFile), nameof(StoredFile.DocumentVersionId),
            (db, ids) => db.StoredFiles.Where(f => ids.Contains(f.DocumentVersionId)).Select(f => f.Id)),
        new(typeof(StoredFile), typeof(StoredFile), nameof(StoredFile.ConvertedFromFileId),
            (db, ids) => db.StoredFiles
                .Where(f => f.ConvertedFromFileId != null && ids.Contains(f.ConvertedFromFileId.Value))
                .Select(f => f.Id)),
    ];

    private static readonly HashSet<Type> Sources =
    [
        .. Arms.SelectMany(a => a.Targets),
        .. Cascades.Select(c => c.Principal),
    ];

    /// <summary>
    /// Whether hard-deleting a row of this type can leave an access row naming nothing — directly,
    /// or through a database cascade it sets off.
    /// </summary>
    public static bool IsCleanupSource(Type type) => Sources.Contains(type);

    private static AccessAnchorArm ObjectArm(AccessDomain domain, params Type[] targets) =>
        new($"object:{domain}", AccessReferenceColumn.ObjectScope, targets) { Domain = domain, ScopeKind = AccessScopeKind.Object };

    private static AccessAnchorArm ScopeArm(AccessScopeKind scope, Type target) =>
        new($"scope:{scope}", AccessReferenceColumn.ScopeId, [target]) { ScopeKind = scope };
}
