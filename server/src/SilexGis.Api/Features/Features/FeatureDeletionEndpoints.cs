// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Features;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Features;

/// <summary>One deletion of a cave, an entrance or a surface feature, as the list of what can be put back shows it.</summary>
/// <param name="Id">The feature the deletion was asked of — the top of what it took.</param>
/// <param name="EntranceCount">How many cave entrances went with it and return with it.</param>
/// <param name="OtherCount">How many caves and surface features contained in it went with it.</param>
/// <param name="Parents">
/// Where it sat, outermost first, as far as this reader may be told: two entrances of the same
/// name are told apart by their caves.
/// </param>
public sealed record DeletedFeatureDto(
    Guid Id,
    FeatureKind Kind,
    string? FeatureTypeCode,
    string? Name,
    DateTimeOffset DeletedAt,
    int EntranceCount,
    int OtherCount,
    IReadOnlyList<FeatureBreadcrumbDto> Parents);

/// <summary>The deleted feature that has to be restored before the one asked about can be.</summary>
public sealed record DeletedContainerDto(Guid Id, FeatureKind Kind, string? Name);

/// <summary>
/// The deleted caves, entrances and surface features: listing the deletions that can be undone,
/// and undoing one.
/// </summary>
/// <remarks>
/// <para>
/// <b>One row per deletion, not per deleted row.</b> Deleting a cave takes its entrances and
/// everything else contained in it under one stamp, and that is what comes back. So the list
/// shows the top of each deletion with a count of what went with it, and a row that went with
/// something else is never offered by itself: restoring it alone would leave it standing inside
/// a container nobody can see. For the same reason something deleted before its container was is
/// not offered until the container is back.
/// </para>
/// <para>
/// <b>The right that deletes is the right that restores</b>, asked of the row as it stood before
/// it was deleted — its owner, its audience and every rule written about it or about what
/// contains it are all still there. Reading is asked as well, because both routes answer with a
/// name. An entrance has two doors to its delete, its own and its cave's page (which asks for
/// the right to edit the cave); either is a door to its restore.
/// </para>
/// <para>
/// <b>Nothing here says where anything is.</b> The list carries no position at all, so there is
/// nothing in it to protect; the restore answers the feature exactly as its own address does,
/// through the same mapping and the same decision about whether this caller may place it. A
/// caller who could not read a feature before it was deleted is not shown it, counted or named,
/// and is answered as though it had never existed.
/// </para>
/// </remarks>
internal static class FeatureDeletionEndpoints
{
    public static async Task<Results<Ok<PagedResult<DeletedFeatureDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Past the filter that hides deleted features, which hides exactly what this list is
        // about — and their containers read the same way. A row inherits its audience from the
        // rows containing it, and a cave deleted with its karst area has only deleted rows above
        // it: asked against live rows alone, "who may read this" would find nothing there to
        // answer. This is the row's audience as it stood the moment before it was deleted.
        var past = db.Features.AsNoTracking().IgnoreQueryFilters();
        var readable = await past
            .Where(f => f.DeletedAt != null)
            .VisibleTo(ctx, past, db.FeatureSetMembers)
            .Select(f => new DeletedRow
            {
                Id = f.Id,
                Kind = f.Kind,
                FeatureTypeId = f.FeatureTypeId,
                Name = f.Name,
                OwnerUserId = f.OwnerUserId,
                CavingGroupId = f.CavingGroupId,
                Visibility = f.Visibility,
                AncestorIds = f.AncestorIds,
                DeletedAt = f.DeletedAt!.Value,
            })
            .ToListAsync(ct);

        // Which rows are deleted at all, readable or not: whether something can be restored
        // turns on every container it has, including the ones this caller cannot see.
        var deletedIds = ctx.IsFullAdmin
            ? readable.Select(r => r.Id).ToHashSet()
            : (await past.Where(f => f.DeletedAt != null).Select(f => f.Id).ToListAsync(ct)).ToHashSet();

        var restorable = readable
            .Where(r => FeatureDeletionRules.IsRestorableKind(r.Kind)
                && FeatureDeletionRules.DeletedContainers(r.Id, r.AncestorIds, deletedIds.Contains).Count == 0)
            .ToList();
        var allowed = await MayRestoreAsync(db, access, ctx, [.. restorable.Select(r => r.ToFeature())], ct);

        var (p, size) = Paging.Normalize(page, pageSize);
        var mine = restorable
            .Where(r => allowed.Contains(r.Id))
            .OrderByDescending(r => r.DeletedAt)
            .ThenByDescending(r => r.Id)
            .ToList();
        var shown = mine.Skip((p - 1) * size).Take(size).ToList();

        var typeIds = shown.Where(r => r.FeatureTypeId != null).Select(r => r.FeatureTypeId!.Value).Distinct().ToArray();
        var typeCodes = typeIds.Length == 0
            ? []
            : await db.FeatureTypes.AsNoTracking()
                .Where(t => typeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Code, ct);

        // Everything above a row offered here is live, so the ordinary chain — which reads live
        // rows and stops at the first one this caller may not read — is the right one.
        var chains = await FeaturePrimaryChains.OfAsync(
            db, ctx, [.. shown.Select(r => new FeaturePrimaryChains.Subject(r.Id, r.AncestorIds))], ct);

        // Counted over the rows this caller could read, so that a number never says more than
        // the list of the same container did before the deletion.
        var taken = readable
            .Select(r => (r.Id, new FeatureDeletionRules.TakenRow(r.Kind, r.DeletedAt, r.AncestorIds)))
            .ToList();

        return TypedResults.Ok(new PagedResult<DeletedFeatureDto>(
            [
                .. shown.Select(row =>
                {
                    var with = FeatureDeletionRules.TakenWith(row.Id, row.DeletedAt, taken);
                    return new DeletedFeatureDto(
                        row.Id,
                        row.Kind,
                        row.FeatureTypeId is { } typeId ? typeCodes.GetValueOrDefault(typeId) : null,
                        row.Name,
                        row.DeletedAt,
                        with.Entrances,
                        with.Others,
                        [.. (chains.GetValueOrDefault(row.Id) ?? []).Select(s => new FeatureBreadcrumbDto(s.Id, s.Name))]);
                }),
            ],
            p,
            size,
            mine.Count));
    }

    /// <summary>
    /// Puts a deleted feature back with everything deleted along with it, and answers it as its
    /// own address would.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ladder.</b> A feature this caller could not read answers as one that does not
    /// exist, deleted or not; one they could read but not delete is a refusal; one that is not
    /// deleted is a conflict, and so is one inside something that is itself deleted — that answer
    /// names what to restore first, when the caller may be told. A survey line is not restored on
    /// its own, and one the caller may not place does not exist for them here either.
    /// </para>
    /// <para>
    /// <b>No precondition header.</b> Nobody holds a version of a deleted feature — there was no
    /// read to get one from — and two people restoring it at once are two people agreeing.
    /// </para>
    /// <para>
    /// <b>Nothing unique is at stake.</b> Neither a name nor a register number is unique across
    /// features, so a cave restored after its name was given to another simply stands beside its
    /// namesake, as two caves of one name always could.
    /// </para>
    /// </remarks>
    public static async Task<Results<Ok<FeatureEnvelopeDto>, ProblemHttpResult>> RestoreAsync(
        Guid id,
        HttpContext http,
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        FeatureProtection protection,
        IOptions<AccessOptions> accessOptions,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);

        // Past the filter: a deleted feature is what this route is for. Untracked, because the
        // restore writes straight to the database and a tracked copy still carrying the deleted
        // state would be what the cave's entrance count was then taken from.
        var past = db.Features.AsNoTracking().IgnoreQueryFilters();
        var feature = await past.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feature is null
            || ctx is null
            || !await past.Where(f => f.Id == id).VisibleTo(ctx, past, db.FeatureSetMembers).AnyAsync(ct))
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        if (!FeatureDeletionRules.IsRestorableKind(feature.Kind))
        {
            // A survey line the caller may not place is withheld whole everywhere else, and a
            // refusal that told it apart from a missing one would say that it is there.
            return (await protection.ExactViewIdsAsync(ctx, [feature.Id], ct)).Contains(feature.Id)
                ? ApiProblems.Conflict(
                    FeatureDeletionRules.KindNotRestorableCode,
                    "A survey line is restored with its cave, not on its own.")
                : ApiProblems.NotFound("feature.not_found");
        }

        if (!(await MayRestoreAsync(db, access, ctx, [feature], ct)).Contains(feature.Id))
        {
            return ApiProblems.Forbidden();
        }

        if (feature.DeletedAt is null)
        {
            return ApiProblems.Conflict(FeatureDeletionRules.NotDeletedCode, "This feature is not deleted.");
        }

        if (await ContainerInTheWayAsync(db, ctx, feature, ct) is { } inTheWay)
        {
            return inTheWay;
        }

        // One transaction over the write and the look that follows it. The restore lands at
        // once and its audit rows and the cave's entrance count ride the save; and a container
        // deleted between the check above and the write would otherwise leave this feature
        // standing inside it, so the question is asked again once the write is in.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                await writer.RestoreAsync(feature.Id, ct);
            }
            catch (FeatureWriteException ex) when (ex.Code == FeatureDeletionRules.ContainerDeletedCode)
            {
                return ContainerDeleted(null);
            }

            await db.SaveChangesAsync(ct);
            if (await writer.HasDeletedContainerAsync(feature.Id, ct))
            {
                await transaction.RollbackAsync(ct);
                return ContainerDeleted(null);
            }

            await transaction.CommitAsync(ct);
        }

        // Read again through the ordinary filter, which is the proof it is back: what the caller
        // is answered with is what the feature's own address now answers.
        var restored = await db.Features.AsNoTracking()
            .Include(f => f.Cave).Include(f => f.Entrance).Include(f => f.Centerline)
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (restored is null)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        var exact = (await protection.ExactViewIdsAsync(ctx, [restored.Id], ct)).Contains(restored.Id);
        // The version the restore produced, so the page it opens on edits what it was handed.
        await Concurrency.EmitETagAsync(http, db, VersionedTable.Features, restored.Id, ct, FeaturePath(restored.Id));
        return TypedResults.Ok(await FeatureEndpoints.EnvelopeAsync(
            db, ctx, restored, exact, accessOptions.Value.LocationGridMeters, ct));
    }

    /// <summary>The address a feature's version is filed under: its own, not this route's.</summary>
    private static string FeaturePath(Guid id) => $"/api/v1/features/{id}";

    /// <summary>
    /// The refusal for a feature inside something deleted, or null when nothing above it is.
    /// </summary>
    /// <remarks>
    /// It names what to restore first — the outermost deleted container, since anything between
    /// is refused in its turn — but only one this caller could read: a container they could not
    /// see is not named to them now for being in the way.
    /// </remarks>
    private static async Task<ProblemHttpResult?> ContainerInTheWayAsync(
        SilexGisDbContext db, AccessContext ctx, Feature feature, CancellationToken ct)
    {
        var above = feature.AncestorIds.Where(a => a != feature.Id).ToArray();
        if (above.Length == 0)
        {
            return null;
        }

        var past = db.Features.AsNoTracking().IgnoreQueryFilters();
        var deletedAbove = await past
            .Where(f => above.Contains(f.Id) && f.DeletedAt != null)
            .Select(f => new { f.Id, f.AncestorIds })
            .ToListAsync(ct);
        if (deletedAbove.Count == 0)
        {
            return null;
        }

        var ancestry = deletedAbove.ToDictionary(f => f.Id, f => f.AncestorIds);
        var outermost = FeatureDeletionRules.OutermostDeleted([.. ancestry.Keys], container => ancestry[container]);
        var first = await past
            .Where(f => outermost.Contains(f.Id))
            .VisibleTo(ctx, past, db.FeatureSetMembers)
            .OrderBy(f => f.Id)
            .Select(f => new DeletedContainerDto(f.Id, f.Kind, f.Name))
            .FirstOrDefaultAsync(ct);
        return ContainerDeleted(first);
    }

    private static ProblemHttpResult ContainerDeleted(DeletedContainerDto? restoreFirst) =>
        ApiProblems.Conflict(
            FeatureDeletionRules.ContainerDeletedCode,
            "Something containing this feature is deleted, and has to be restored first.",
            "restoreFirst",
            restoreFirst);

    /// <summary>
    /// Of the given features, the ones this caller may put back: the ones they could delete.
    /// Reading is the caller's to have established already — it is asked in the database, of the
    /// feature's audience as it stood before the deletion.
    /// </summary>
    /// <remarks>
    /// Decided by the same evaluator every guard uses, over each row's own facts. An entrance is
    /// also deleted from its cave's page by whoever may edit the cave, so that right on the cave
    /// restores it too. The cave is read past the filter because an entrance deleted before its
    /// cave was is asked about here as well — and is then refused for the cave being in the way,
    /// which is the answer that says what to do, rather than for a right its caller does hold.
    /// </remarks>
    private static async Task<HashSet<Guid>> MayRestoreAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        IReadOnlyCollection<Feature> features,
        CancellationToken ct)
    {
        if (ctx.IsFullAdmin || features.Count == 0)
        {
            return [.. features.Select(f => f.Id)];
        }

        var facts = await access.FactsOfManyAsync([.. features], ct);
        var allowed = features
            .Where(f => AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.Delete, facts[f.Id]).Allowed)
            .Select(f => f.Id)
            .ToHashSet();

        var entranceIds = features
            .Where(f => f.Kind == FeatureKind.CaveEntrance && !allowed.Contains(f.Id))
            .Select(f => f.Id)
            .ToArray();
        if (entranceIds.Length == 0)
        {
            return allowed;
        }

        var caveOf = await db.CaveEntrances.AsNoTracking().IgnoreQueryFilters()
            .Where(e => entranceIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.CaveFeatureId, ct);
        var caveIds = caveOf.Values.Distinct().ToArray();
        var caves = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => caveIds.Contains(f.Id))
            .Select(f => new DeletedRow
            {
                Id = f.Id,
                Kind = f.Kind,
                FeatureTypeId = f.FeatureTypeId,
                Name = null,
                OwnerUserId = f.OwnerUserId,
                CavingGroupId = f.CavingGroupId,
                Visibility = f.Visibility,
                AncestorIds = f.AncestorIds,
                DeletedAt = default,
            })
            .ToListAsync(ct);
        var caveFacts = await access.FactsOfManyAsync([.. caves.Select(c => c.ToFeature())], ct);
        foreach (var (entranceId, caveId) in caveOf)
        {
            if (caveFacts.TryGetValue(caveId, out var cave)
                && AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.Read, cave).Allowed
                && AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.Write, cave).Allowed)
            {
                allowed.Add(entranceId);
            }
        }

        return allowed;
    }

    /// <summary>
    /// What is read of one deleted feature: the columns its access is decided on and what the
    /// list shows of it. Read rather than materialised whole, because the row behind it carries a
    /// geometry and a description this list has no use for — and must not hold, since nothing
    /// here is allowed to say where a feature is — and every deleted feature the caller could
    /// read is fetched to decide which deletions are theirs to undo.
    /// </summary>
    private sealed class DeletedRow
    {
        public required Guid Id { get; init; }

        public required FeatureKind Kind { get; init; }

        public required long? FeatureTypeId { get; init; }

        public required string? Name { get; init; }

        public required Guid OwnerUserId { get; init; }

        public required Guid? CavingGroupId { get; init; }

        public required Visibility Visibility { get; init; }

        public required Guid[] AncestorIds { get; init; }

        public required DateTimeOffset DeletedAt { get; init; }

        /// <summary>The row in the shape the access facts are built from, position left out.</summary>
        public Feature ToFeature() => new()
        {
            Id = Id,
            Kind = Kind,
            FeatureTypeId = FeatureTypeId,
            OwnerUserId = OwnerUserId,
            CavingGroupId = CavingGroupId,
            Visibility = Visibility,
            AncestorIds = AncestorIds,
        };
    }
}
