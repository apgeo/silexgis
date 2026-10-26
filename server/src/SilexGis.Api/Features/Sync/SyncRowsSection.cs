// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>What the arbitration of one feature row decided to write.</summary>
internal enum SyncRowAct
{
    Create = 0,
    Update = 1,
    Delete = 2,
}

/// <summary>
/// One feature row the first step has checked and found writable, as plain values that can be
/// kept and applied later: the row as the device sent it, what to do with it, and what the checks
/// settled about it. No entity rides along — the second step reads its target afresh by id.
/// </summary>
/// <param name="Act">Create, edit or remove.</param>
/// <param name="Row">The row as the device sent it.</param>
/// <param name="BaseRevision">
/// For an edit or a removal, the server's revision the row was arbitrated against; the second
/// step writes only while the row is still at it.
/// </param>
/// <param name="TypeId">
/// For a create, the kind the row lands as — a cave kind, an entrance kind or a kind of place,
/// by <see cref="SyncUploadRowDto.Kind"/>.
/// </param>
/// <param name="ContainerId">
/// The row it hangs under: for a create the container that was checked, for an entrance (created
/// or edited) its cave.
/// </param>
/// <param name="WritesPosition">
/// Whether the row's position fields are written — geometry, altitude, position quality and the
/// main-entrance flag. False for an edit by a caller the withhold rule keeps from the true
/// position: their other fields are written and those are dropped. Always true for a create.
/// </param>
internal sealed record SyncRowProposal(
    SyncRowAct Act,
    SyncUploadRowDto Row,
    DateTimeOffset? BaseRevision = null,
    long? TypeId = null,
    Guid? ContainerId = null,
    bool WritesPosition = true);

/// <summary>
/// The feature rows of an upload — caves, entrances, places and the other mapped kinds — arbitrated
/// row by row against what this server already holds.
/// </summary>
/// <remarks>
/// <para>
/// The first step decides everything that can be decided before writing: whether the row is
/// here, whether the caller may read, edit or remove it, whether the device held the current
/// revision, whether its kind, geometry and properties fit, whether the caller may add it where it
/// is going, and which of its fields the withhold rule drops. The second step writes what was
/// decided, and refuses only what the writer itself refuses while making the write.
/// </para>
/// <para>
/// The checks of one row are database questions asked against the state the earlier rows of the
/// same upload have left — an entrance sent beside its new cave finds the cave because the cave
/// was written before the entrance was resolved. An upload resolved now and applied later
/// therefore has to resolve each row after applying the ones before it, or re-resolve at apply
/// time any row whose container came in the same upload.
/// </para>
/// </remarks>
internal sealed class SyncRowsSection(
    FeatureWriteService featureWriter,
    FeatureProtection featureProtection,
    IAccessService accessService)
    : SyncUploadSection<SyncUploadRowDto, SyncRowProposal>
{
    /// <summary>The kind a place gets when the device does not name one.</summary>
    private const string DefaultPlaceTypeCode = "cave_place";

    /// <summary>The cave kind a device's cave lands as when it does not name one.</summary>
    private const string DefaultCaveTypeCode = "cave";

    /// <summary>The entrance kind a device's entrance lands as when it does not name one.</summary>
    private const string DefaultEntranceTypeCode = "natural";

    private SyncTaxonomies? taxonomies;
    private IReadOnlySet<Guid> withheld = new HashSet<Guid>();

    public override string Name => SyncUploadSections.Rows;

    protected override IEnumerable<Guid> Refers(SyncRowProposal proposal) =>
        new[] { proposal.ContainerId, proposal.Row.ParentId }.OfType<Guid>().Distinct();

    protected override DateTimeOffset? StandingRevision(SyncRowProposal proposal) => proposal.BaseRevision;

    protected override SyncHeldFacts Facts(SyncRowProposal proposal) => new(
        proposal.Act == SyncRowAct.Create ? null : new SyncHeldTarget(SyncHeldTargetKind.Feature, proposal.Row.Id),
        proposal.Act == SyncRowAct.Create ? null : proposal.BaseRevision,
        [.. Refers(proposal).Select(id => new SyncHeldTarget(SyncHeldTargetKind.Feature, id))],
        CarriesPosition: true);

    protected override SyncUploadRowDto ItemOf(SyncRowProposal proposal) => proposal.Row;

    protected override SyncUploadRowDto? Rebased(SyncUploadRowDto item, DateTimeOffset revision) =>
        item.BaseRevision is null ? null : item with { BaseRevision = revision };

    /// <summary>
    /// An edit cut down to the fields a reviewer accepted. An upload is a partial write — a field
    /// left empty is a field left alone — so a field not accepted is emptied, and the position
    /// (point, altitude, position quality and the main-entrance flag together, which move as one)
    /// is dropped the same way the withhold rule drops it.
    /// </summary>
    protected override SyncRowProposal Narrowed(SyncRowProposal proposal, IReadOnlyList<string> fields) =>
        proposal.Act != SyncRowAct.Update
            ? proposal
            : proposal with
            {
                Row = proposal.Row with
                {
                    Name = fields.Contains(SyncHeldFields.Name) ? proposal.Row.Name : null,
                    Description = fields.Contains(SyncHeldFields.Description) ? proposal.Row.Description : null,
                    Properties = fields.Contains(SyncHeldFields.Properties) ? proposal.Row.Properties : null,
                },
                WritesPosition = proposal.WritesPosition && fields.Contains(SyncHeldFields.Position),
            };

    protected override IReadOnlyList<SyncUploadRowDto>? ItemsOf(SyncUploadRequest request) => request.Rows;

    protected override async Task PrepareAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadRowDto> items, CancellationToken ct)
    {
        var db = run.Db;
        taxonomies = await SyncTaxonomies.LoadAsync(db, ct);

        // What this caller may not place, decided in the one place this slice decides it, asked
        // once for every row and container the batch mentions. Rows created earlier in this same
        // batch are not asked about and are not in the answer: they did not exist when the
        // question was put, and a row the caller has just created is a row the caller placed.
        var mentioned = items.Select(r => r.Id)
            .Concat(items.Where(r => r.ParentId is not null).Select(r => r.ParentId!.Value))
            .Distinct()
            .ToList();

        // The cave above a mentioned entrance is asked about too, and it is found from this
        // server's own rows rather than from the request. Moving an entrance moves the cave whose
        // map point it is, so the cave's answer is part of the decision — and a request field is
        // the wrong place to learn which cave that is: `parentId` is optional on an update, so a
        // client that simply left it out would be asking a question the set had no answer for and
        // getting "not withheld" by default. A guard whose inputs the caller chooses is a guard
        // the caller can switch off.
        var containingCaves = await db.CaveEntrances.AsNoTracking().IgnoreQueryFilters()
            .Where(e => mentioned.Contains(e.Id))
            .Select(e => e.CaveFeatureId)
            .ToListAsync(ct);
        var candidates = mentioned.Concat(containingCaves).Distinct().ToList();
        var present = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => candidates.Contains(f.Id))
            .Select(f => f.Id)
            .ToListAsync(ct);
        withheld = await SyncWithhold.WithheldIdsAsync(featureProtection, run.Access, present, ct);
    }

    /// <summary>
    /// Checks one row: what to tell the device when nothing is to be written, or what to write.
    /// Reads only, and untracked, so that nothing it looked at can reach the database.
    /// </summary>
    public override async Task<SyncResolution<SyncRowProposal>> ResolveAsync(
        SyncUploadRun run, SyncUploadRowDto row, CancellationToken ct)
    {
        var ctx = run.Access;

        // Read past the filter that hides deleted rows on purpose. A row that was removed here
        // and is still on the phone must be answered as gone; read through the filter it would
        // look absent, and a create would then try to put it back under the same identifier —
        // undoing the deletion and colliding with the row that is still there.
        var existing = await run.Db.Features.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == row.Id, ct);

        if (existing is null)
        {
            if (row.Deleted)
            {
                // The device is ahead of this server, or the row never got here. Either way there
                // is nothing to remove and nothing went wrong.
                return Answer(Ok(row.Id, SyncRowStatus.Unchanged));
            }

            if (row.BaseRevision is not null)
            {
                return Answer(Refuse(row.Id, "sync.row_not_found",
                    "This row is not on the server. Send it as a new row."));
            }

            return await ResolveCreateAsync(run, row, ct);
        }

        // Readability first, and before anything says whether the row is deleted or writable: a
        // caller who may not read a row learns only that its identifier is taken. That does
        // disclose the existence of the identifier, and is accepted — a version-7 uuid carries
        // enough randomness that guessing a live one is infeasible unless it has already leaked,
        // and the alternative is minting a different identifier for the row, which puts a
        // translation table between the two sides for ever.
        if (!(await accessService.DecideAsync(ctx, AccessAction.Read, existing, ct)).Allowed)
        {
            return Answer(Refuse(row.Id, "sync.id_conflict", "That identifier is already in use here."));
        }

        if (existing.DeletedAt is not null)
        {
            return row.Deleted
                ? Answer(Ok(row.Id, SyncRowStatus.Unchanged))
                : Answer(Refuse(row.Id, "sync.row_deleted", "This row was removed on the server. Drop it locally."));
        }

        // Removing a row and editing one are different rights, and this channel asks for the one
        // the act needs — the same way the rest of the API does, where a delete is refused to an
        // account that may edit. Asking for write on both would let a grant meant to allow
        // corrections take a cave and everything inside it out of the registry.
        if (row.Deleted)
        {
            if (!(await accessService.DecideAsync(ctx, AccessAction.Delete, existing, ct)).Allowed)
            {
                return Answer(Refuse(row.Id, "sync.row_delete_forbidden", "You may not remove this row."));
            }
        }
        else if (!(await accessService.DecideAsync(ctx, AccessAction.Write, existing, ct)).Allowed)
        {
            return Answer(Refuse(row.Id, "sync.row_forbidden", "You may not write this row."));
        }

        if (row.BaseRevision is null)
        {
            // A create whose row is already here: the device sent it before and did not hear the
            // answer. Writing it again would overwrite whatever has happened to the row since,
            // with no revision to arbitrate against, so nothing is written.
            return Answer(Ok(row.Id, SyncRowStatus.Unchanged, existing.UpdatedAt));
        }

        if (!SameRevision(existing.UpdatedAt, row.BaseRevision.Value))
        {
            return Answer(Conflict(row.Id, existing.UpdatedAt));
        }

        return row.Deleted
            ? SyncResolution<SyncRowProposal>.Write(
                new SyncRowProposal(SyncRowAct.Delete, row, BaseRevision: existing.UpdatedAt))
            : await ResolveUpdateAsync(run, existing, row, ct);
    }

    /// <summary>
    /// Writes one checked row. The target is read again by id rather than carried over from the
    /// first step, and the write goes ahead only while the row is still at the revision it was
    /// arbitrated against — which it always is when both steps run back to back, and which is
    /// what keeps a proposal applied later from overwriting a change made in between.
    /// </summary>
    public override async Task<SyncApplied> ApplyAsync(
        SyncUploadRun run, SyncRowProposal proposal, CancellationToken ct)
    {
        var db = run.Db;
        var row = proposal.Row;
        if (proposal.Act == SyncRowAct.Create)
        {
            return await ApplyCreateAsync(run, proposal, ct);
        }

        var existing = proposal.Act == SyncRowAct.Delete
            ? await db.Features.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == row.Id, ct)
            : await db.Features.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == row.Id, ct);
        if (existing is null
            || existing.DeletedAt is not null
            || !SameRevision(existing.UpdatedAt, proposal.BaseRevision!.Value))
        {
            return new SyncApplied(Conflict(row.Id, existing?.UpdatedAt), null);
        }

        if (proposal.Act == SyncRowAct.Delete)
        {
            // Read before the delete, because the soft delete stamps the rows in the database
            // and the mirror below asks the tracker first: a tracked copy still carrying its
            // pre-delete state would count this entrance as present.
            var goingEntrance = await db.CaveEntrances.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == existing.Id, ct);
            var caveOfEntrance = goingEntrance?.CaveFeatureId;
            await featureWriter.SoftDeleteAsync(existing.Id, ct);

            // Removing an entrance moves the cave above it. A cave's entrance count is a stored
            // column and its map point is a copy of its main entrance's, so a delete that skipped
            // this would leave the cave plotting at a position that no longer exists and claiming
            // an entrance that is gone — which the integrity check then reports as two faults.
            // A cave that went in the same act has no mirror worth refreshing.
            if (caveOfEntrance is { } caveId
                && await db.Features.AsNoTracking().IgnoreQueryFilters()
                    .AnyAsync(f => f.Id == caveId && f.DeletedAt == null, ct))
            {
                await featureWriter.SyncCaveMirrorAsync(caveId, ct);
            }

            return new SyncApplied(Ok(row.Id, SyncRowStatus.Deleted), Touched(row.Id));
        }

        return await ApplyUpdateAsync(db, existing, proposal, ct);
    }

    /// <summary>
    /// A row's revision is stamped by the database as it is written, not by the handler, so it is
    /// read back rather than predicted: a device told the value from before the write would send
    /// it up again and be told its own edit had gone stale.
    /// </summary>
    protected override async Task<IReadOnlyList<SyncUploadRowResultDto>> FinishAsync(
        SyncUploadRun run, IReadOnlyList<SyncUploadRowResultDto> results, CancellationToken ct) =>
        await StampRevisionsAsync(run.Db, results, ct);

    private static SyncResolution<SyncRowProposal> Answer(SyncUploadRowResultDto result) =>
        SyncResolution<SyncRowProposal>.Answer(result);

    private static SyncResolution<SyncRowProposal> Write(SyncRowProposal proposal) =>
        SyncResolution<SyncRowProposal>.Write(proposal);

    /// <summary>
    /// Fills in each written row's revision from the value the database stamped, which is the
    /// value the device sends back as its base revision next time.
    /// </summary>
    private static async Task<IReadOnlyList<SyncUploadRowResultDto>> StampRevisionsAsync(
        SilexGisDbContext db, IReadOnlyList<SyncUploadRowResultDto> rows, CancellationToken ct)
    {
        var written = rows
            .Where(r => r.Status is SyncRowStatus.Created or SyncRowStatus.Updated or SyncRowStatus.Deleted)
            .Select(r => r.Id)
            .ToList();
        if (written.Count == 0)
        {
            return rows;
        }

        var stamps = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .Where(f => written.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.UpdatedAt, ct);

        return [.. rows.Select(r => stamps.TryGetValue(r.Id, out var stamp) ? r with { Revision = stamp } : r)];
    }


    /// <summary>
    /// Whether the revision a device sends back is the one this server stamped. Compared at the
    /// precision the database keeps rather than the precision .NET keeps: the value goes out of
    /// here with tick resolution and comes back off a column that stores microseconds, so an
    /// exact comparison of the two would report a conflict against a row nobody had touched.
    /// </summary>
    private static bool SameRevision(DateTimeOffset stored, DateTimeOffset claimed) =>
        SyncRevisions.Same(stored, claimed);

    private static SyncUploadRowResultDto Ok(Guid id, SyncRowStatus status, DateTimeOffset? revision = null) =>
        new(id, status, revision, null, null);

    private static SyncUploadRowResultDto Refuse(Guid id, string code, string detail) =>
        new(id, SyncRowStatus.Rejected, null, code, detail);

    private static SyncUploadRowResultDto Conflict(Guid id, DateTimeOffset? revision) =>
        new(id, SyncRowStatus.Conflict, revision, "sync.conflict",
            "This row changed on the server after you last read it.");

    /// <summary>A provenance line for a row this batch changed but did not create.</summary>
    private static ImportBatchItem Touched(Guid featureId) => new()
    {
        AttachedToFeatureId = featureId,
        Action = ImportDecisionAction.Attach,
    };

    /// <summary>A provenance line for a row this batch created — the line undo works from.</summary>
    private static ImportBatchItem Made(Guid featureId) => new()
    {
        FeatureId = featureId,
        Action = ImportDecisionAction.Create,
    };

    /// <summary>
    /// The checks of a new row, in the order that decides which refusal a row with several faults
    /// is given: its geometry, then per kind its kind, its container, the withhold gate and the
    /// caller's right to add it there.
    /// </summary>
    private async Task<SyncResolution<SyncRowProposal>> ResolveCreateAsync(
        SyncUploadRun run, SyncUploadRowDto row, CancellationToken ct)
    {
        var db = run.Db;
        var ctx = run.Access;
        var set = run.Set;
        var kinds = taxonomies!;

        Geometry? geom = null;
        if (row.Geometry is not null)
        {
            geom = row.Geometry.ToGeometryOrNull();
            if (geom is null)
            {
                return Answer(Refuse(row.Id, "sync.geometry_invalid", "That geometry could not be read."));
            }
        }

        switch (row.Kind)
        {
            case FeatureKind.Cave:
            {
                if (!kinds.TryCaveType(row.CaveTypeCode ?? DefaultCaveTypeCode, out var caveTypeId))
                {
                    return Answer(Refuse(row.Id, "sync.type_unknown", "This installation has no such cave kind."));
                }

                // A cave may sit inside something — a surface area is the ordinary case, and is
                // what a device's place codes are allocated from. It is optional, because a cave
                // with nothing above it is a legal row; but when one is named the edge is made,
                // because dropping it silently would leave the cave outside the very selection
                // the caver rooted at that area.
                var container = await ContainerOrNullAsync(db, accessService, ctx, row, ct);
                if (container.Problem is { } refusedCave)
                {
                    return Answer(refusedCave);
                }

                var facts = await CreateContext.ParentCreateFactsAsync(
                    db, ctx, container.Feature?.Id, set.CavingGroupId, FeatureKind.Cave, ct);
                if (!MayCreate(ctx, facts))
                {
                    return Answer(RefuseCreate(row.Id, set, ctx, facts));
                }

                return Write(new SyncRowProposal(
                    SyncRowAct.Create, row, TypeId: caveTypeId, ContainerId: container.Feature?.Id));
            }

            case FeatureKind.CaveEntrance:
            {
                var parent = await ContainerAsync(db, accessService, ctx, row, FeatureKind.Cave, ct);
                if (parent.Problem is { } refusedEntrance)
                {
                    return Answer(refusedEntrance);
                }

                if (geom is not Point)
                {
                    return Answer(Refuse(row.Id, "sync.geometry_invalid", "An entrance carries a point."));
                }

                // The write-right rule, on the path where it is least obvious, and the one place
                // in this file where it is easiest to talk oneself out of.
                //
                // Creating an entrance writes a position onto the cave above it: the cave's map
                // point is its main entrance's, refreshed by the writer on every entrance change,
                // and a cave with no entrances yet adopts the first one whether or not it was
                // offered as the main one. So this create moves a row that already existed, and
                // that row's position is one this caller may never have been shown.
                //
                // The rest of this API guards updates and leaves creates open, on the reasoning
                // that a coordinate arriving with a new row is the caller's own and echoing it
                // back discloses nothing. That reasoning is about where the value came from, not
                // about which verb carried it, and it does not survive this channel: a device
                // replays coordinates this server handed it, so a point arriving here may be one
                // the caller was given rather than one they took. Guarding creates is therefore
                // not an inconsistency to tidy away later — the axis is provenance, and on this
                // route the provenance is different.
                if (withheld.Contains(parent.Feature!.Id))
                {
                    return Answer(Refuse(row.Id, "sync.location_forbidden",
                        "You may not place an entrance on this cave."));
                }

                if (!kinds.TryEntranceType(row.EntranceTypeCode ?? DefaultEntranceTypeCode, out var typeId))
                {
                    return Answer(Refuse(row.Id, "sync.type_unknown", "This installation has no such entrance kind."));
                }

                var facts = await CreateContext.ParentCreateFactsAsync(
                    db, ctx, parent.Feature.Id, set.CavingGroupId, FeatureKind.CaveEntrance, ct);
                if (!MayCreate(ctx, facts))
                {
                    return Answer(RefuseCreate(row.Id, set, ctx, facts));
                }

                return Write(new SyncRowProposal(
                    SyncRowAct.Create, row, TypeId: typeId, ContainerId: parent.Feature.Id));
            }

            case FeatureKind.Generic:
            {
                if (!kinds.TryFeatureType(row.FeatureTypeCode ?? DefaultPlaceTypeCode, out var typeId))
                {
                    return Answer(Refuse(row.Id, "sync.type_unknown", "This installation has no such kind of place."));
                }

                // Whether a row needs a container is a property of its kind, read from the
                // taxonomy. The kinds a sync selection is rooted in sit at the top of the tree —
                // a surface area holds the caves, and holds the segment every place code beneath
                // it is allocated from — so a channel that demanded a container of every row
                // would refuse those for ever, and with them everything a device numbers
                // underneath one.
                var parent = kinds.RequiresParent(typeId)
                    ? await ContainerAsync(db, accessService, ctx, row, null, ct)
                    : await ContainerOrNullAsync(db, accessService, ctx, row, ct);
                if (parent.Problem is { } refusedPlace)
                {
                    return Answer(refusedPlace);
                }

                var facts = await CreateContext.ParentCreateFactsAsync(
                    db, ctx, parent.Feature?.Id, set.CavingGroupId, FeatureKind.Generic, ct, typeId);
                if (!MayCreate(ctx, facts))
                {
                    return Answer(RefuseCreate(row.Id, set, ctx, facts));
                }

                return Write(new SyncRowProposal(
                    SyncRowAct.Create, row, TypeId: typeId, ContainerId: parent.Feature?.Id));
            }

            default:
                return Answer(Refuse(row.Id, "sync.kind_unsupported", "This kind of row is not carried by sync."));
        }
    }

    /// <summary>
    /// Writes a new row the first step accepted. What can still refuse it here is the writer's own
    /// checks — the property document against its kind's schema, the containment edge — which are
    /// made by the code that makes the write.
    /// </summary>
    private async Task<SyncApplied> ApplyCreateAsync(SyncUploadRun run, SyncRowProposal proposal, CancellationToken ct)
    {
        var row = proposal.Row;
        var geom = row.Geometry?.ToGeometryOrNull();
        var feature = new Feature
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            Properties = row.Properties?.GetRawText() ?? "{}",
            // Mirrored into Z on the way in, the same as an entrance's, so an altitude a device
            // recorded on a place inside a cave is kept rather than accepted and discarded. A row
            // that sent none keeps a plain two-dimensional point: "no altitude" and "at sea
            // level" are different answers and must stay so.
            Geom = geom is Point createdPoint ? WithAltitude(createdPoint, AltitudeOf(row, geom)) : geom,
            OwnerUserId = run.Access.UserId,
            CavingGroupId = run.Set.CavingGroupId,
            Visibility = run.Set.UploadVisibility,
            ClientUpdatedAt = row.ClientUpdatedAt,
        };
        IReadOnlyList<ParentSpec> parents = proposal.ContainerId is { } containerId
            ? [new ParentSpec(containerId, IsPrimary: true)]
            : [];

        try
        {
            switch (row.Kind)
            {
                case FeatureKind.Cave:
                {
                    // A cave's own point is a cache of its main entrance and is written by the
                    // service that maintains it; a cave that arrives carrying one is stored
                    // without it rather than refused, because the entrance that follows in the
                    // same batch is where that position belongs.
                    feature.Geom = null;
                    var cave = new Cave { CaveTypeId = proposal.TypeId!.Value };
                    feature.Cave = cave;
                    await featureWriter.CreateCaveAsync(feature, cave, parents, ct);
                    break;
                }

                case FeatureKind.CaveEntrance:
                {
                    var caveId = proposal.ContainerId!.Value;
                    var altitude = AltitudeOf(row, geom!);
                    feature.Geom = WithAltitude((Point)geom!, altitude);
                    var entrance = new CaveEntrance
                    {
                        CaveFeatureId = caveId,
                        EntranceTypeId = proposal.TypeId!.Value,
                        IsMain = row.IsMain,
                        Altitude = altitude,
                        PositionQuality = row.PositionQuality ?? PositionQuality.Unknown,
                    };
                    await featureWriter.CreateEntranceAsync(feature, entrance, ct);
                    if (entrance.IsMain)
                    {
                        await featureWriter.SetMainEntranceAsync(caveId, feature.Id, ct);
                    }

                    break;
                }

                default:
                    feature.FeatureTypeId = proposal.TypeId!.Value;
                    await featureWriter.CreateGenericAsync(feature, parents, ct);
                    break;
            }
        }
        catch (FeatureWriteException e)
        {
            return new SyncApplied(Refuse(row.Id, e.Code, string.Join("; ", e.Errors)), null);
        }

        return new SyncApplied(Ok(row.Id, SyncRowStatus.Created), Made(row.Id));
    }

    /// <summary>
    /// Whether this caller may add a row of this shape at all. Creation has no row to judge yet,
    /// so it is decided against where the row is going — the container it will hang under and the
    /// club binding it will carry — which is how a grant scoped to one cave's subtree, or to a
    /// club's own content, reaches exactly as far as it was meant to and no further. Null facts
    /// mean the container could not be read, and that has already been answered as a missing one.
    /// </summary>
    private static bool MayCreate(AccessContext ctx, AccessTargetFacts? facts) =>
        facts is not null && CreateRules.MayCreate(ctx, AccessDomain.Features, facts);

    /// <summary>
    /// A create this caller may not make. When the selection names no caving group the refusal
    /// says so, because that is the field a caver can actually act on and it is not part of the
    /// request that failed — it was chosen when the selection was made, and a client told only
    /// "you may not create rows here" sends its author to inspect the row's kind and its
    /// container, which are the two things they did choose and neither of which is the cause.
    /// The distinct code states a fact about the selection rather than a diagnosis: a caller may
    /// hold no create right at all, and then binding a group would not help them either.
    /// </summary>
    private static SyncUploadRowResultDto RefuseCreate(
        Guid id, SyncSet set, AccessContext ctx, AccessTargetFacts? facts) =>
        set.CavingGroupId is null && facts is not null && BindingWouldHaveAllowed(ctx, facts)
            ? Refuse(
                id,
                "sync.set_unbound",
                "This selection names no caving group, and creating through it needs one.")
            : Refuse(id, CreateRules.ForbiddenCode, "You may not create rows here.");

    /// <summary>
    /// Whether the missing binding is actually the reason. Asked by re-deciding the same create
    /// against each club the caller belongs to: if any of them would have carried it, the
    /// selection's empty binding is the thing standing in the way, and the caver can fix it by
    /// choosing one. If none would, the caller holds no create right here at all and saying
    /// "pick a group" would send them to do something that changes nothing — so the ordinary
    /// refusal stands. A wrong diagnosis is worse than a general one.
    /// </summary>
    private static bool BindingWouldHaveAllowed(AccessContext ctx, AccessTargetFacts facts) =>
        ctx.CavingGroupIds.Any(groupId =>
            CreateRules.MayCreate(ctx, AccessDomain.Features, facts with { CavingGroupId = groupId }));

    /// <summary>
    /// The checks of an edit, after the arbitration: which of its fields the withhold rule lets
    /// through, whether the geometry it would write is readable, and whether its property
    /// document fits its kind — in that order, which is the order that decides the refusal a row
    /// with several faults is given.
    /// </summary>
    private async Task<SyncResolution<SyncRowProposal>> ResolveUpdateAsync(
        SyncUploadRun run, Feature existing, SyncUploadRowDto row, CancellationToken ct)
    {
        // The write-right rule. A caller without exact view was never shown this row's real
        // position — elsewhere in this API they are shown a grid-snapped one instead — so
        // whatever coordinate they send back came from somewhere other than the stored value, and
        // writing it would replace a true position with a degraded one. Their other edits stand.
        var exact = !withheld.Contains(existing.Id);
        var writesPosition = false;
        Guid? caveOfEntrance = null;

        if (existing.Kind == FeatureKind.CaveEntrance)
        {
            caveOfEntrance = await run.Db.CaveEntrances.AsNoTracking()
                .Where(e => e.Id == existing.Id)
                .Select(e => e.CaveFeatureId)
                .FirstAsync(ct);

            // Both, because moving an entrance moves the cave: the cave's own map point is a
            // cache of its main entrance's. Exact view is a decision about one row, so the cave's
            // has to be asked for separately from the entrance's.
            writesPosition = exact && !withheld.Contains(caveOfEntrance.Value);
            if (writesPosition && row.Geometry is not null && row.Geometry.ToGeometryOrNull() is not Point)
            {
                return Answer(Refuse(row.Id, "sync.geometry_invalid", "An entrance carries a point."));
            }
        }
        else if (existing.Kind == FeatureKind.Generic && exact && row.Geometry is not null)
        {
            writesPosition = true;
            if (row.Geometry.ToGeometryOrNull() is null)
            {
                return Answer(Refuse(row.Id, "sync.geometry_invalid", "That geometry could not be read."));
            }
        }

        // A cave feature is deliberately absent from the two arms above. Its geometry is not a
        // field anybody writes — it is the main entrance's, kept in step by the writer — so an
        // uploaded cave carrying a point has nowhere to put it and is not refused for trying.

        // The property document the edit leaves — the one sent, or the stored one when none was —
        // is checked on a copy that is never tracked, against the kind the row already has: the
        // edit cannot change a row's kind, so this is the same check the writer repeats when the
        // edit is applied.
        try
        {
            await featureWriter.ValidatePropertiesAsync(
                new Feature
                {
                    Id = existing.Id,
                    FeatureTypeId = existing.FeatureTypeId,
                    Properties = row.Properties?.GetRawText() ?? existing.Properties,
                },
                ct);
        }
        catch (FeatureWriteException e)
        {
            return Answer(Refuse(row.Id, e.Code, string.Join("; ", e.Errors)));
        }

        return Write(new SyncRowProposal(
            SyncRowAct.Update,
            row,
            BaseRevision: existing.UpdatedAt,
            ContainerId: caveOfEntrance,
            WritesPosition: writesPosition));
    }

    /// <summary>Writes an edit the first step accepted, onto the row read afresh and tracked.</summary>
    private async Task<SyncApplied> ApplyUpdateAsync(
        SilexGisDbContext db, Feature feature, SyncRowProposal proposal, CancellationToken ct)
    {
        var row = proposal.Row;

        // The fields a device owns, written whatever the caller may see: a name and a
        // description are not positions, and refusing an edit to them would lose work for no
        // protection. The guard is stated per field for exactly this reason — a caller who may
        // not move a cave may still rename it, and a rule that refused the whole row would throw
        // away the half of the edit that was never in question.
        //
        // Applied only when sent. An upload is a partial write: a device may correct one field
        // and leave the rest of the row alone, and the fields it did not mention must survive.
        // Writing these two unconditionally emptied whichever one the device had not named, which
        // is how a correction to a description used to leave a nameless cave in the registry.
        //
        // The consequence, which is the other half of this decision: an absent JSON member and an
        // explicit null arrive here as the same value, so this generation of the protocol cannot
        // express "the caver cleared this description". Clearing needs a shape that distinguishes
        // the two — a sentinel, or a members-present list — and that is a contract change, not a
        // behaviour change, because a phone in the field pins the generation it was built against.
        if (row.Name is not null)
        {
            feature.Name = row.Name;
        }

        if (row.Description is not null)
        {
            feature.Description = row.Description;
        }

        feature.ClientUpdatedAt = row.ClientUpdatedAt;

        // The property document goes in unguarded, and that is safe only because no coordinate is
        // ever allowed into it. It is handed to every reader of a row with no protection filter
        // anywhere on its path, so a position stored in it would be published to precisely the
        // people the withhold rule exists to keep it from. The labels a device keeps here — its
        // own index for a cave, the codes it generates — locate nothing. Do not relax that to
        // make something fit.
        if (row.Properties is { } properties)
        {
            feature.Properties = properties.GetRawText();
        }

        // The three cave fields that are positions in prose — the closest address, the land
        // registry number and the notes saying how to find the place — are guarded by not being
        // here at all: an uploaded row cannot carry them, so there is no submitted value to write
        // back over the stored one. Anything that later teaches this channel to carry them has to
        // bring them inside the withhold rule.

        try
        {
            if (feature.Kind == FeatureKind.CaveEntrance && proposal.WritesPosition)
            {
                var entrance = await db.CaveEntrances.FirstAsync(e => e.Id == feature.Id, ct);
                entrance.PositionQuality = row.PositionQuality ?? PositionQuality.Unknown;
                if (row.Geometry?.ToGeometryOrNull() is Point point)
                {
                    entrance.Altitude = AltitudeOf(row, point);
                    feature.Geom = WithAltitude(point, entrance.Altitude);
                }

                if (row.IsMain && !entrance.IsMain)
                {
                    await featureWriter.SetMainEntranceAsync(entrance.CaveFeatureId, entrance.Id, ct);
                }
                else
                {
                    await featureWriter.SyncCaveMirrorAsync(entrance.CaveFeatureId, ct);
                }
            }
            else if (feature.Kind == FeatureKind.Generic && proposal.WritesPosition
                && row.Geometry?.ToGeometryOrNull() is { } geom)
            {
                feature.Geom = geom is Point movedPoint
                    ? WithAltitude(movedPoint, AltitudeOf(row, geom))
                    : geom;

                // Said explicitly, because comparing two points is a two-dimensional question:
                // an edit that moves nothing but the altitude leaves a point that compares equal
                // to the stored one, and the change would be dropped before it reached the
                // database. The altitude is the whole of what a place inside a cave has to say
                // about depth, so losing it silently is worse than writing an unchanged row.
                db.Entry(feature).Property(f => f.Geom).IsModified = true;
            }

            await featureWriter.ValidatePropertiesAsync(feature, ct);
        }
        catch (FeatureWriteException e)
        {
            return new SyncApplied(Refuse(row.Id, e.Code, string.Join("; ", e.Errors)), null);
        }

        return new SyncApplied(Ok(row.Id, SyncRowStatus.Updated), Touched(row.Id));
    }

    /// <summary>
    /// The same check as <see cref="ContainerAsync"/>, for a row whose kind does not need a
    /// container: no container named is no problem, and a container that <em>was</em> named is
    /// checked exactly as it would be if the kind had required one. A named container is never
    /// quietly dropped — the edge is what protection and visibility are inherited along, so losing
    /// it changes who can see the row.
    /// </summary>
    private static async Task<(Feature? Feature, SyncUploadRowResultDto? Problem)> ContainerOrNullAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        SyncUploadRowDto row,
        CancellationToken ct) =>
        row.ParentId is null ? (null, null) : await ContainerAsync(db, access, ctx, row, null, ct);

    /// <summary>
    /// The row a new row hangs under, checked for existence and for the caller's right to add to
    /// it. Containment is what protection and visibility are inherited along, so the edge is made
    /// by the writer from a container this caller may actually write — never taken as a
    /// free-standing structure the device declares.
    /// </summary>
    private static async Task<(Feature? Feature, SyncUploadRowResultDto? Problem)> ContainerAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        SyncUploadRowDto row,
        FeatureKind? required,
        CancellationToken ct)
    {
        if (row.ParentId is not { } parentId)
        {
            return (null, Refuse(row.Id, "sync.parent_required", "This row only exists inside a containing row."));
        }

        var parent = await db.Features.AsNoTracking().FirstOrDefaultAsync(f => f.Id == parentId, ct);
        if (parent is null
            || (required is { } kind && parent.Kind != kind)
            || !(await access.DecideAsync(ctx, AccessAction.Read, parent, ct)).Allowed)
        {
            return (null, Refuse(row.Id, "sync.parent_not_found", "The containing row is not on this server."));
        }

        if (!(await access.DecideAsync(ctx, AccessAction.Write, parent, ct)).Allowed)
        {
            return (null, Refuse(row.Id, "sync.parent_forbidden", "You may not add to the containing row."));
        }

        return (parent, null);
    }

    /// <summary>Altitude as the device gave it, or lifted from the position's third ordinate.</summary>
    private static decimal? AltitudeOf(SyncUploadRowDto row, Geometry geom) =>
        row.Altitude ?? (geom.Coordinate is { Z: var z } && !double.IsNaN(z) ? (decimal)z : null);

    /// <summary>
    /// The stored point, with the altitude mirrored into Z so the map payload and the attribute
    /// agree without a second lookup.
    /// </summary>
    private static Point WithAltitude(Point point, decimal? altitude)
    {
        var c = point.Coordinate;
        Coordinate coordinate = altitude is null
            ? new Coordinate(c.X, c.Y)
            : new CoordinateZ(c.X, c.Y, (double)altitude.Value);
        return new Point(coordinate) { SRID = 4326 };
    }
}
