// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Documents;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Permissions;
using SilexGis.Domain.Profiles;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.ResLinks;

/// <summary>
/// Wire vocabulary of resource-link target types: "feature" plus the camelCase names of
/// the entity types the member rules admit. Parsed case-insensitively. Deliberately its
/// own allow-set — attachments and taggings speak a narrower one over the same enum, and
/// neither surface may widen the other by accident.
/// </summary>
public static class ResLinkTargets
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

        if (Enum.TryParse<AttachedEntityType>(value, ignoreCase: true, out var parsed)
            && ResLinkRules.IsLinkableType(parsed))
        {
            entityType = parsed;
            return true;
        }

        return false;
    }

    /// <summary>Wire name of a stored member row (feature FK XOR polymorphic pair).</summary>
    public static string NameOf(Guid? featureId, AttachedEntityType? entityType) =>
        featureId is not null
            ? FeatureName
            : JsonNamingPolicy.CamelCase.ConvertName(entityType!.Value.ToString());
}

/// <summary>
/// One target world of the resource-link surface: how its rows display, how they are
/// searched for the picker, and — because both answers are visibility-filtered by the
/// world's own rules — the authoring read floor. A target absent from
/// <see cref="ResolveAsync"/>'s answer is missing and unreadable alike: the link surface
/// never distinguishes the two, so it can never be used to probe for existence.
/// </summary>
public interface IResLinkTargetResolver
{
    /// <summary>The world served; null is the feature world.</summary>
    AttachedEntityType? TargetType { get; }

    /// <summary>Display data for the targets the caller may read, keyed by id; ids that
    /// are missing or unreadable simply have no entry.</summary>
    Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Picker feed: readable rows matching the query, at most
    /// <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct);

    /// <summary>
    /// Which of these targets the caller may write, asked in whatever way that world
    /// decides writing — the question "who curates a link" defers to, because a link is
    /// an assertion about its main member and the people who may change that member are
    /// the people who may correct what is said about it. An id that is not there is
    /// simply absent from the answer: a member row can outlive a polymorphic target, and
    /// a decision about nothing is never an admission.
    /// </summary>
    /// <remarks>
    /// Set-shaped rather than one id at a time because the answer is now needed for a
    /// whole page of links at once — every link on a listing states whether this caller
    /// may curate it — and a world that could only answer one row would make that listing
    /// cost round trips in proportion to its length. Worlds whose write rule reduces to
    /// arithmetic over the caller's entries answer in a single fetch; the two that walk
    /// something per row say so where they do it.
    /// </remarks>
    Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

/// <summary>The registered resolvers, one per admissible target type. Fully populated by
/// construction — a linkable type without a resolver is a wiring error, surfaced loudly
/// here rather than as a member nobody can display.</summary>
public sealed class ResLinkTargetDirectory
{
    private readonly IResLinkTargetResolver feature;
    private readonly Dictionary<AttachedEntityType, IResLinkTargetResolver> byType;

    public ResLinkTargetDirectory(IEnumerable<IResLinkTargetResolver> resolvers)
    {
        var all = resolvers.ToList();
        feature = all.Single(r => r.TargetType is null);
        byType = all.Where(r => r.TargetType is not null).ToDictionary(r => r.TargetType!.Value);
    }

    public IResLinkTargetResolver Of(AttachedEntityType? type) =>
        type is { } entityType
            ? byType.TryGetValue(entityType, out var resolver)
                ? resolver
                : throw new InvalidOperationException($"No resource-link resolver is registered for {entityType}.")
            : feature;

    /// <summary>The authoring floor: whether the caller may read one target.</summary>
    public async Task<bool> CanReadAsync(
        AccessContext ctx, AttachedEntityType? type, Guid id, CancellationToken ct) =>
        (await Of(type).ResolveAsync(ctx, [id], ct)).ContainsKey(id);

    /// <summary>The curation floor: whether the caller may write one target.</summary>
    public async Task<bool> CanWriteAsync(
        AccessContext ctx, AttachedEntityType? type, Guid id, CancellationToken ct) =>
        (await Of(type).WritableIdsAsync(ctx, [id], ct)).Contains(id);

    /// <summary>The curation floor over a whole set of one world's targets.</summary>
    public Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, AttachedEntityType? type, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Of(type).WritableIdsAsync(ctx, ids, ct);
}

/// <summary>Features of any kind, through the shared visibility filter. The title is the
/// feature's name — never a coordinate; exact-location handling stays with the location
/// protection machinery.</summary>
public sealed class FeatureTargetResolver(SilexGisDbContext db, IAccessService access) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => null;

    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // Deliberately fetched unfiltered and decided afterwards, exactly as the feature
        // write path does: the write decision is the one that answers here, and running
        // the visibility filter first would refuse a feature the caller may edit but
        // reaches by a rule the filter does not express.
        var features = await db.Features.AsNoTracking().Where(f => ids.Contains(f.Id)).ToListAsync(ct);
        return await ProtectedWrites.WritableAsync(access, ctx, features, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var rows = await Project(Readable(ctx).Where(f => ids.Contains(f.Id))).ToListAsync(ct);

        // The whole page's containment in one batched answer, not one walk per chip: a
        // role field is a row of these, and a per-chip query would make the field cost
        // grow with what was recorded on the trip.
        var paths = await PathsAsync(ctx, rows, ct);
        return rows.ToDictionary(r => r.Id, r => Display(r, paths.GetValueOrDefault(r.Id)));
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await Project(Readable(ctx)
                .Where(f => f.Name != null
                    && EF.Functions.ILike(EF.Functions.Unaccent(f.Name!), EF.Functions.Unaccent(pattern)))
                .OrderBy(f => f.Name)
                .ThenBy(f => f.Id)
                .Take(limit))
            .ToListAsync(ct);
        return [.. rows.Select(r => new ResLinkTargetHitDto(r.Id, Title(r), r.TypeName))];
    }

    private IQueryable<Feature> Readable(AccessContext ctx) =>
        db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);

    // Filters and ordering stay on the entity queryable above; the constructor projection
    // comes last because query operators cannot reach through it.
    private IQueryable<FeatureRow> Project(IQueryable<Feature> features) =>
        features.Select(f => new FeatureRow(
            f.Id,
            f.Name,
            f.Kind,
            db.FeatureTypes.Where(t => t.Id == f.FeatureTypeId).Select(t => t.Name).FirstOrDefault(),
            f.AncestorIds));

    /// <summary>
    /// Each row's containment path, outermost first, through the one definition of a
    /// feature's parent chain — so a chip's path and the breadcrumb over the feature's own
    /// page are the same sentence, and are truncated at an unreadable step in the same way.
    /// The outermost step is dropped when the installation has a single root: it would then
    /// begin every path in the application and distinguish nothing.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<string>>> PathsAsync(
        AccessContext ctx, IReadOnlyList<FeatureRow> rows, CancellationToken ct)
    {
        var chains = await FeaturePrimaryChains.OfAsync(
            db, ctx, [.. rows.Select(r => new FeaturePrimaryChains.Subject(r.Id, r.AncestorIds))], ct);
        var singleRoot = chains.Values.Any(c => c.Count > 0)
            ? await FeaturePrimaryChains.SingleRootIdAsync(db, ct)
            : null;
        return chains.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)[..
                pair.Value
                    .Where(step => step.Id != singleRoot)
                    .Select(step => step.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)]);
    }

    private static ResLinkTargetDisplayDto Display(FeatureRow r, IReadOnlyList<string>? path) => new(
        Title(r),
        r.TypeName ?? r.Kind.ToString(),
        r.Kind == FeatureKind.Cave ? $"/caves/{r.Id}" : $"/features/{r.Id}",
        null,
        path is { Count: > 0 } ? path : null);

    private static string Title(FeatureRow r) => r.Name ?? r.Kind.ToString();

    private sealed record FeatureRow(
        Guid Id, string? Name, FeatureKind Kind, string? TypeName, Guid[] AncestorIds);
}

/// <summary>
/// Documents, decided per row by the document's own walk — rules written against the
/// document first, reach through an attached object where the rules left it open. Search
/// uses the query-side filter instead, which cannot resolve attachment reach: a document
/// reachable only through something it is attached to is fetchable and displayable but
/// not findable in the picker, the same stated trade-off the cabinet listing makes.
/// </summary>
public sealed class DocumentTargetResolver(SilexGisDbContext db, IAccessService access) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.Document;

    /// <remarks>
    /// The one world that still decides row by row. Writing a document is decided against
    /// the file it currently serves as well as the document row, and that walk reaches
    /// through the file's own rules — it has no set-shaped form today, so the two facts it
    /// needs are fetched for the whole set (documents in one query, their served files in
    /// one more) and only the decision repeats. Documents appear here as the main member of
    /// a link, which is rare beside features and trips; if a listing ever carries many, the
    /// walk is what to make set-shaped, not this method.
    /// </remarks>
    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var documents = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        if (documents.Count == 0)
        {
            return [];
        }

        var currentFiles = await CurrentFilesAsync(documents.Select(d => d.Id).ToList(), ct);
        var writable = new HashSet<Guid>();
        foreach (var document in documents)
        {
            if (await DocumentAccessRules.CanWriteAsync(
                db, access, ctx, document, currentFiles.GetValueOrDefault(document.Id), ct))
            {
                writable.Add(document.Id);
            }
        }

        return writable;
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, ResLinkTargetDisplayDto>();
        if (ids.Count == 0)
        {
            return result;
        }

        var documents = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        var typeNames = await TypeNamesAsync(documents.Select(d => d.DocumentTypeId), ct);

        var currentFiles = await CurrentFilesAsync(documents.Select(d => d.Id).ToList(), ct);

        foreach (var document in documents)
        {
            var content = currentFiles.GetValueOrDefault(document.Id);
            if (await DocumentAccessRules.CanReadAsync(db, access, ctx, document, content, ct))
            {
                result[document.Id] = new ResLinkTargetDisplayDto(
                    document.Title,
                    document.DocumentTypeId is { } typeId ? typeNames.GetValueOrDefault(typeId) : null,
                    null,
                    null);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await db.Documents.AsNoTracking()
            .VisibleTo(ctx, AccessDomain.Documents, null, (db.CabinetDocuments, db.Cabinets))
            .Where(d => EF.Functions.ILike(EF.Functions.Unaccent(d.Title), EF.Functions.Unaccent(pattern)))
            .OrderBy(d => d.Title)
            .ThenBy(d => d.Id)
            .Take(limit)
            .ToListAsync(ct);
        var typeNames = await TypeNamesAsync(rows.Select(d => d.DocumentTypeId), ct);
        return [.. rows.Select(d => new ResLinkTargetHitDto(
            d.Id,
            d.Title,
            d.DocumentTypeId is { } typeId ? typeNames.GetValueOrDefault(typeId) : null))];
    }

    /// <summary>The served file of every named document in one query — the rules
    /// themselves stay the per-row document walk, but their one storage-backed fact must
    /// not cost a round trip per member of a listing.</summary>
    private async Task<Dictionary<Guid, StoredFile>> CurrentFilesAsync(
        IReadOnlyList<Guid> documentIds, CancellationToken ct) =>
        (await (
                from version in db.DocumentVersions.AsNoTracking()
                join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                where documentIds.Contains(version.DocumentId) && version.IsCurrent
                orderby file.CreatedAt, file.Id
                select new { version.DocumentId, File = file })
            .ToListAsync(ct))
        .GroupBy(x => x.DocumentId)
        .ToDictionary(g => g.Key, g => g.First().File);

    private async Task<Dictionary<long, string>> TypeNamesAsync(
        IEnumerable<long?> typeIds, CancellationToken ct)
    {
        var ids = typeIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<long, string>()
            : await db.DocumentTypes.AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
    }
}

public sealed class TripLogTargetResolver(SilexGisDbContext db, IAccessService access) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.TripLog;

    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var trips = await db.TripLogs.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        return await ProtectedWrites.WritableAsync(access, ctx, trips, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var rows = await Readable(ctx).Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        return rows.ToDictionary(
            t => t.Id,
            t => new ResLinkTargetDisplayDto(
                t.Title, Subtitle(t), $"/trip-logs/{t.Id}", null));
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await Readable(ctx)
            .Where(t => EF.Functions.ILike(EF.Functions.Unaccent(t.Title), EF.Functions.Unaccent(pattern)))
            .OrderByDescending(t => t.TripDate)
            .ThenBy(t => t.Id)
            .Take(limit)
            .ToListAsync(ct);
        return [.. rows.Select(t => new ResLinkTargetHitDto(t.Id, t.Title, Subtitle(t)))];
    }

    private IQueryable<TripLog> Readable(AccessContext ctx) =>
        db.TripLogs.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs);

    private static string Subtitle(TripLog t) => t.TripDate.ToString("yyyy-MM-dd");
}

/// <summary>Cavers projected through the roster's disclosure tiers: any signed-in caller
/// may see a person's display label (the linked account's chosen label wins over the
/// roster name), and the subtitle is the person's email exactly as the tiers serve it —
/// a linked person's own per-field profile settings decide, an account-less person's is
/// roster-keeper-only. Every field travels through the one projection in
/// <see cref="CaverProtection"/>, so link display can never show more of a person than
/// the roster itself would.</summary>
public sealed class CaverTargetResolver(
    SilexGisDbContext db, IUserContextAccessor userAccessor) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.Caver;

    /// <summary>
    /// The roster's write right on this one person, the same object-level question the
    /// roster asks before letting somebody remove an entry. The roster's *edit* rule has
    /// a second arm — a person may correct their own contact details — which is
    /// deliberately not honoured here: that arm is about your own personal data, not a
    /// claim that you curate what other people record about you.
    /// </summary>
    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // The decision is pure arithmetic over the caller's entries, so the existence
        // check is the only round trip however many people are named.
        var present = await db.Cavers.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
        return
        [
            .. present.Where(id => AccessEvaluator.Decide(
                ctx,
                AccessDomain.Cavers,
                AccessAction.Write,
                new AccessTargetFacts { ObjectId = id }).Allowed),
        ];
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        // Anonymous callers never turn ids into people. Every reslink caller is signed
        // in today, but the tier rule does not lean on that staying true.
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var cavers = await db.Cavers.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var canKeepRoster = CaverDirectory.CanKeepRoster(ctx);
        var profiles = await ProfileDirectory.ResolveAsync(
            db, user, cavers.Where(c => c.UserId is not null).Select(c => c.UserId!.Value), ct);
        return cavers.ToDictionary(
            c => c.Id,
            c =>
            {
                // For an account holder the profile projection has already applied their
                // own settings; the tier rule itself has one home in CaverProtection.
                var projected = CaverProtection.Project(
                    c, canKeepRoster, c.UserId is { } userId ? profiles.GetValueOrDefault(userId) : null);
                return new ResLinkTargetDisplayDto(projected.FullName, projected.Email, null, null);
            });
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var user = await userAccessor.GetAsync(ct);
        if (user is null)
        {
            return [];
        }

        // Showing a person's label on something already readable is one thing; the
        // picker feed is a roster-wide enumeration by name — the same query the roster
        // list serves — so it answers under the roster's own domain read gate. Every
        // account holds that read on a default install, but the entry is removable, and
        // an installation that removed it must not find its roster searchable here.
        if (!AccessEvaluator.Decide(ctx, AccessDomain.Cavers, AccessAction.Read, null).Allowed)
        {
            return [];
        }

        var pattern = $"%{query}%";
        var ids = await db.Cavers.AsNoTracking()
            .Where(c => EF.Functions.ILike(EF.Functions.Unaccent(c.FullName), EF.Functions.Unaccent(pattern)))
            .OrderBy(c => c.FullName)
            .ThenBy(c => c.Id)
            .Take(limit)
            .Select(c => c.Id)
            .ToListAsync(ct);
        // Matched on the roster name, labelled through the directory — an account's
        // chosen label wins, so one person never appears under two names.
        var labels = await CaverDirectory.ResolveLabelsAsync(db, user, ids, ct);
        return [.. ids
            .Where(labels.ContainsKey)
            .Select(id => new ResLinkTargetHitDto(id, labels[id], null))];
    }
}

/// <summary>Caving groups at directory level: the walk against the group record decides,
/// which every account passes through the domain-wide read unless a deny names the
/// group. Directory facts only — never the club's content.</summary>
public sealed class CavingGroupTargetResolver(SilexGisDbContext db) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.CavingGroup;

    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var present = await db.CavingGroups.AsNoTracking()
            .Where(g => ids.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct);
        return
        [
            .. present.Where(id => AccessEvaluator.Decide(
                ctx,
                AccessDomain.CavingGroups,
                AccessAction.Write,
                new AccessTargetFacts { ObjectId = id }).Allowed),
        ];
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var rows = await db.CavingGroups.AsNoTracking()
            .Where(g => ids.Contains(g.Id))
            .ToListAsync(ct);
        return rows
            .Where(g => MayRead(ctx, g.Id))
            .ToDictionary(g => g.Id, g => new ResLinkTargetDisplayDto(g.Name, null, null, null));
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await db.CavingGroups.AsNoTracking()
            .Where(g => EF.Functions.ILike(EF.Functions.Unaccent(g.Name), EF.Functions.Unaccent(pattern)))
            .OrderBy(g => g.Name)
            .ThenBy(g => g.Id)
            .Take(limit)
            .ToListAsync(ct);
        // The walk is pure arithmetic over the caller's entries, so filtering the page
        // after the fetch costs nothing; a page thinned by a per-group deny under-fills
        // rather than over-shows.
        return [.. rows
            .Where(g => MayRead(ctx, g.Id))
            .Select(g => new ResLinkTargetHitDto(g.Id, g.Name, null))];
    }

    private static bool MayRead(AccessContext ctx, Guid groupId) =>
        AccessEvaluator.Decide(
            ctx,
            AccessDomain.CavingGroups,
            AccessAction.Read,
            new AccessTargetFacts { ObjectId = groupId }).Allowed;
}

public sealed class MapViewTargetResolver(SilexGisDbContext db, IAccessService access) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.MapView;

    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var views = await db.MapViews.AsNoTracking().Where(v => ids.Contains(v.Id)).ToListAsync(ct);
        return await ProtectedWrites.WritableAsync(access, ctx, views, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var rows = await Readable(ctx).Where(v => ids.Contains(v.Id)).ToListAsync(ct);
        return rows.ToDictionary(
            v => v.Id, v => new ResLinkTargetDisplayDto(v.Name, null, null, null));
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await Readable(ctx)
            .Where(v => EF.Functions.ILike(EF.Functions.Unaccent(v.Name), EF.Functions.Unaccent(pattern)))
            .OrderBy(v => v.Name)
            .ThenBy(v => v.Id)
            .Take(limit)
            .ToListAsync(ct);
        return [.. rows.Select(v => new ResLinkTargetHitDto(v.Id, v.Name, null))];
    }

    private IQueryable<MapView> Readable(AccessContext ctx) =>
        db.MapViews.AsNoTracking().VisibleTo(ctx, AccessDomain.MapViews);
}

/// <summary>Cabinets: global filing structure, readable by any signed-in caller — what
/// is guarded is the documents filed in one, and those answer through their own rules.
/// The subtitle is the shelf's ancestry, so two "1987" cabinets stay distinguishable.</summary>
public sealed class CabinetTargetResolver(SilexGisDbContext db) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.Cabinet;

    /// <summary>
    /// A shelf is not a guarded thing in its own right, so "writing a cabinet" is the
    /// question the shelf's own surface reduces to — asked through that rule's one home
    /// rather than restated here, so tightening it later cannot leave this admitting people
    /// the shelf itself refuses.
    /// </summary>
    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // The shelf rule reads the ancestry the row already carries, so one fetch decides
        // the whole set.
        var rows = await db.Cabinets.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.AncestorIds })
            .ToListAsync(ct);
        return [.. rows.Where(c => CabinetAccessRules.MayAdminister(ctx, c.AncestorIds)).Select(c => c.Id)];
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var rows = await db.Cabinets.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var breadcrumbs = await BreadcrumbsAsync(rows, ct);
        return rows.ToDictionary(
            c => c.Id,
            c => new ResLinkTargetDisplayDto(c.Name, breadcrumbs.GetValueOrDefault(c.Id), null, null));
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await db.Cabinets.AsNoTracking()
            .Where(c => EF.Functions.ILike(EF.Functions.Unaccent(c.Name), EF.Functions.Unaccent(pattern)))
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Take(limit)
            .ToListAsync(ct);
        var breadcrumbs = await BreadcrumbsAsync(rows, ct);
        return [.. rows.Select(c => new ResLinkTargetHitDto(c.Id, c.Name, breadcrumbs.GetValueOrDefault(c.Id)))];
    }

    /// <summary>Ancestor names root-first, excluding the cabinet itself; null for roots.</summary>
    private async Task<Dictionary<Guid, string?>> BreadcrumbsAsync(
        IReadOnlyList<Cabinet> rows, CancellationToken ct)
    {
        var parentIds = rows
            .SelectMany(c => c.AncestorIds.Where(id => id != c.Id))
            .Distinct()
            .ToList();
        var names = parentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Cabinets.AsNoTracking()
                .Where(c => parentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.ToDictionary(
            c => c.Id,
            c =>
            {
                var path = c.AncestorIds
                    .Where(id => id != c.Id)
                    .Select(id => names.GetValueOrDefault(id))
                    .Where(name => name is not null)
                    .ToList();
                return path.Count == 0 ? null : string.Join(" / ", path);
            });
    }
}

/// <summary>
/// Survey models inherit their cave's access — and because the files are absolute
/// georeferenced coordinates, the cave being readable is not enough: for a
/// location-protected cave the records are withheld entirely from callers without exact
/// view, the same stance every survey-model read path takes.
/// </summary>
public sealed class SurveyModelTargetResolver(
    SilexGisDbContext db, FeatureProtection protection, IAccessService access) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.SurveyModel;

    /// <summary>
    /// A survey model carries no rights of its own: the cave it belongs to answers for it,
    /// and the question is asked through the one definition every survey-model surface
    /// uses. All three parts of it apply — the cave readable, its exact location open to
    /// this caller, and Write on it — because a curator of the link may delete it, and
    /// admitting somebody the model's own endpoints refuse would let the link surface be
    /// the way around location protection.
    /// </summary>
    /// <remarks>
    /// Two fetches for the whole set — the models, then their caves — and then the shared
    /// definition once per <em>distinct cave</em> rather than once per model, since several
    /// models of one cave answer the same. That definition composes a read decision, an
    /// exact-location check and a write decision, and is deliberately asked whole rather
    /// than taken apart here: a set-shaped variant of it would be a second copy of the rule,
    /// and the surface it protects is the one where a second copy costs the most.
    /// </remarks>
    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var models = await db.SurveyModels.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.CaveFeatureId })
            .ToListAsync(ct);
        if (models.Count == 0)
        {
            return [];
        }

        var caveIds = models.Select(m => m.CaveFeatureId).Distinct().ToList();
        var caves = await db.Features.AsNoTracking()
            .Where(f => caveIds.Contains(f.Id) && f.Kind == FeatureKind.Cave)
            .ToListAsync(ct);

        var writableCaves = new HashSet<Guid>();
        foreach (var cave in caves)
        {
            if (await SurveyModelAccess.MayWriteAsync(access, protection, ctx, cave, ct))
            {
                writableCaves.Add(cave.Id);
            }
        }

        return [.. models.Where(m => writableCaves.Contains(m.CaveFeatureId)).Select(m => m.Id)];
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var models = await db.SurveyModels.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.CaveFeatureId })
            .ToListAsync(ct);
        var caveIds = models.Select(m => m.CaveFeatureId).Distinct().ToList();
        var caves = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => caveIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var exact = await protection.ExactViewIdsAsync(ctx, caveIds, ct);

        return models
            .Where(m => caves.ContainsKey(m.CaveFeatureId) && exact.Contains(m.CaveFeatureId))
            .ToDictionary(
                m => m.Id,
                m => new ResLinkTargetDisplayDto(
                    m.Name, caves[m.CaveFeatureId], $"/caves/{m.CaveFeatureId}", null));
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        // Cave visibility composes into the query; the exact-view cut runs after it, so
        // a page thinned by protected caves under-fills rather than over-shows.
        var rows = await (
                from m in db.SurveyModels.AsNoTracking()
                join f in db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                    on m.CaveFeatureId equals f.Id
                where EF.Functions.ILike(EF.Functions.Unaccent(m.Name), EF.Functions.Unaccent(pattern))
                orderby m.Name, m.Id
                select new { m.Id, m.Name, CaveId = f.Id, CaveName = f.Name })
            .Take(limit)
            .ToListAsync(ct);
        var exact = await protection.ExactViewIdsAsync(
            ctx, rows.Select(r => r.CaveId).Distinct().ToList(), ct);
        return [.. rows
            .Where(r => exact.Contains(r.CaveId))
            .Select(r => new ResLinkTargetHitDto(r.Id, r.Name, r.CaveName))];
    }
}

public sealed class GeofileTargetResolver(SilexGisDbContext db, IAccessService access) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.Geofile;

    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var geofiles = await db.Geofiles.AsNoTracking().Where(g => ids.Contains(g.Id)).ToListAsync(ct);
        return await ProtectedWrites.WritableAsync(access, ctx, geofiles, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var rows = await Readable(ctx).Where(g => ids.Contains(g.Id)).ToListAsync(ct);
        return rows.ToDictionary(
            g => g.Id,
            g => new ResLinkTargetDisplayDto(g.Name, g.Format.ToString(), null, null));
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await Readable(ctx)
            .Where(g => EF.Functions.ILike(EF.Functions.Unaccent(g.Name), EF.Functions.Unaccent(pattern)))
            .OrderBy(g => g.Name)
            .ThenBy(g => g.Id)
            .Take(limit)
            .ToListAsync(ct);
        return [.. rows.Select(g => new ResLinkTargetHitDto(g.Id, g.Name, g.Format.ToString()))];
    }

    private IQueryable<Geofile> Readable(AccessContext ctx) =>
        db.Geofiles.AsNoTracking().VisibleTo(ctx, AccessDomain.Geofiles);
}

/// <summary>Camps, through their own visibility filter and their own write rule — a camp is
/// governed in its own right, so neither answer here is derived from the trips it gathers.
/// The subtitle is the span of days, written the way the row stores it: a camp that never ran
/// on past its first day has no end and reads as that one day.</summary>
public sealed class ExpeditionTargetResolver(SilexGisDbContext db, IAccessService access) : IResLinkTargetResolver
{
    public AttachedEntityType? TargetType => AttachedEntityType.Expedition;

    public async Task<HashSet<Guid>> WritableIdsAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var camps = await db.Expeditions.AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        return await ProtectedWrites.WritableAsync(access, ctx, camps, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, ResLinkTargetDisplayDto>> ResolveAsync(
        AccessContext ctx, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ResLinkTargetDisplayDto>();
        }

        var rows = await Readable(ctx).Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        return rows.ToDictionary(
            e => e.Id,
            // The camp's own page. Only ever named while that page exists in the client: a route
            // this application does not carry puts the reader on the router's error screen, which
            // is worse than leaving the chip un-navigable, so the two are changed together.
            e => new ResLinkTargetDisplayDto(e.Name, Subtitle(e), $"/expeditions/{e.Id}", null));
    }

    public async Task<IReadOnlyList<ResLinkTargetHitDto>> SearchAsync(
        AccessContext ctx, string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await Readable(ctx)
            .Where(e => EF.Functions.ILike(EF.Functions.Unaccent(e.Name), EF.Functions.Unaccent(pattern)))
            .OrderByDescending(e => e.StartDate)
            .ThenBy(e => e.Id)
            .Take(limit)
            .ToListAsync(ct);
        return [.. rows.Select(e => new ResLinkTargetHitDto(e.Id, e.Name, Subtitle(e)))];
    }

    private IQueryable<Expedition> Readable(AccessContext ctx) =>
        db.Expeditions.AsNoTracking().VisibleTo(ctx, AccessDomain.Expeditions);

    private static string Subtitle(Expedition e) =>
        e.EndDate is { } end
            ? $"{e.StartDate:yyyy-MM-dd} – {end:yyyy-MM-dd}"
            : e.StartDate.ToString("yyyy-MM-dd");
}
