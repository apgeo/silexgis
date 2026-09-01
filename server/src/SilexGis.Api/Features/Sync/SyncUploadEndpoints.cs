// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Npgsql;
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

/// <summary>
/// The write half of the mobile contract: one batch of rows, applied in the order they were
/// given, arbitrated row by row against what this server already holds.
/// </summary>
/// <remarks>
/// <para>
/// Three properties hold this endpoint together, and each of them exists because of a way an
/// offline device breaks. A batch carries an identifier the device minted, so an answer lost on
/// the way back costs a resend rather than a duplicate registry. A row carries the identifier the
/// device gave it, adopted verbatim, so neither side ever has to translate the other's names. And
/// a row carries the server revision the device last saw, which is the only thing compared when
/// deciding whether its edit still applies — never the device's own clock, which is unsynchronised,
/// resettable by whoever holds the phone, and routinely wrong by hours.
/// </para>
/// <para>
/// Every upload is recorded as an import batch. That is not bookkeeping: it is what lets a caver
/// look at what a phone put into the registry and take all of it back in one act, through the same
/// screen and the same button that undoes a bad file.
/// </para>
/// </remarks>
public static class SyncUploadEndpoints
{
    /// <summary>The kind a place gets when the device does not name one.</summary>
    private const string DefaultPlaceTypeCode = "cave_place";

    /// <summary>
    /// How many neighbours one created row is told about. A caver deciding whether they have just
    /// re-entered a cave the club already holds needs the nearest few, not a census of the massif.
    /// </summary>
    private const int DuplicatesPerRow = 5;

    /// <summary>The cave kind a device's cave lands as when it does not name one.</summary>
    private const string DefaultCaveTypeCode = "cave";

    /// <summary>The entrance kind a device's entrance lands as when it does not name one.</summary>
    private const string DefaultEntranceTypeCode = "natural";

    public static void MapSyncUploadEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapPost("/{id:guid}/upload", UploadAsync)
            .WithValidation<SyncUploadRequest>()
            .WithName("syncUpload")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Applies one batch of device rows, arbitrated row by row; resends are answered, not re-applied.");
    }

    private static async Task<Results<Ok<SyncUploadResultDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        UploadAsync(
            Guid id,
            SyncUploadRequest request,
            SilexGisDbContext db,
            FeatureWriteService writer,
            FeatureProtection protection,
            VisibleProximitySearch proximity,
            IAccessService access,
            IAccessContextAccessor accessAccessor,
            IOptions<SyncOptions> options,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Asked before the rows are looked at, not after: a device speaking a protocol this build
        // does not implement must be told so while its data is still on the phone.
        if (request.ContractVersion != SyncEndpoints.ContractVersion)
        {
            return ApiProblems.Conflict(
                "sync.contract_unsupported",
                $"This server speaks contract version {SyncEndpoints.ContractVersion}.");
        }

        if (request.Rows.Count > options.Value.ResolvedUploadRowsMax)
        {
            return ApiProblems.BadRequest(
                "sync.batch_too_large",
                $"An upload carries at most {options.Value.ResolvedUploadRowsMax} rows.");
        }

        // Resolved by owner, deliberately and unconditionally, for the same reason the download
        // is: a phone authenticates as one account, and somebody else's set is answered like one
        // that is not there whatever that account is otherwise allowed. Nothing an installation
        // can configure widens a write of a set — only reading one by its own address.
        var set = await db.SyncSets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ctx.UserId, ct);
        if (set is null)
        {
            return ApiProblems.NotFound("sync.set_not_found");
        }

        var replayed = await ReplayAsync(
            db, protection, proximity, ctx, request.BatchId, options.Value.ResolvedDuplicateRadiusMeters, ct);
        if (replayed is not null)
        {
            return TypedResults.Ok(replayed);
        }

        // Asked here and not only when the set was written. Every row this batch creates is bound
        // to the set's club, and binding content to a club hands that club's members whatever
        // their rulesets grant over its content — so the question is asked at the moment the
        // binding is actually made. A membership withdrawn since the set was created would
        // otherwise keep pushing rows into a club the caver has left. After the resend check on
        // purpose: a replay writes nothing, so refusing it would only withhold from a device the
        // answer to a batch this server had already accepted.
        if (set.CavingGroupId is { } boundGroupId
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, boundGroupId))
        {
            return ApiProblems.Forbidden(
                "sync.caving_group_forbidden", "You are not entitled to add rows to that caving group.");
        }

        var taxonomies = await SyncTaxonomies.LoadAsync(db, ct);

        // What this caller may not place, decided in the one place this slice decides it, asked
        // once for every row and container the batch mentions. Rows created earlier in this same
        // batch are not asked about and are not in the answer: they did not exist when the
        // question was put, and a row the caller has just created is a row the caller placed.
        var mentioned = request.Rows.Select(r => r.Id)
            .Concat(request.Rows.Where(r => r.ParentId is not null).Select(r => r.ParentId!.Value))
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
        var withheld = await SyncWithhold.WithheldIdsAsync(protection, ctx, present, ct);

        var batch = new ImportBatch
        {
            Source = ImportSource.DeviceSync,
            SyncBatchId = request.BatchId,
            ConfirmedByUserId = ctx.UserId,
            // Nobody went through these one at a time; a device pushed them. Recorded rather than
            // inferred, because it is the first thing worth knowing about a batch that went wrong.
            Mode = ImportBatchMode.AutoCreated,
            Options = JsonSerializer.Serialize(new { syncSetId = set.Id }),
        };

        var results = new List<SyncUploadRowResultDto>(request.Rows.Count);
        var items = new List<ImportBatchItem>();

        // One transaction for the whole batch. Rows are arbitrated individually and a refused row
        // does not take the others down with it, but the record of what was written and the rows
        // themselves have to land or not land together — a batch whose provenance line is missing
        // is a batch nobody can take back.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // The identifier is claimed before a single row is written. Two copies of the same
            // batch in flight at once both get past the replay check above, and if the rows went
            // in first the loser would fail on a feature's own primary key — a violation this
            // handler cannot tell from any other, and so a 500 rather than the documented "that
            // batch is already being applied". Claiming first puts the collision on the index
            // that means exactly that.
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(ct);

            foreach (var row in request.Rows)
            {
                var (result, item) = await ApplyRowAsync(
                    db, writer, access, ctx, set, taxonomies, withheld, row, ct);
                results.Add(result);
                if (item is not null)
                {
                    items.Add(item);
                }

                switch (result.Status)
                {
                    case SyncRowStatus.Created:
                        batch.CreatedCount++;
                        break;
                    case SyncRowStatus.Updated:
                    case SyncRowStatus.Deleted:
                        batch.AttachedCount++;
                        break;
                    default:
                        batch.SkippedCount++;
                        break;
                }

                // Row by row, inside the batch's own transaction. Rows arrive in the order they
                // depend on each other — a cave before its entrances, an area before the places
                // inside it — and the checks the next row runs are database questions: whether its
                // container exists, whether this caller may read it, what its ancestry is. A row
                // still sitting unsaved in the tracker answers none of them, so a cave and its
                // entrance sent together would have the entrance refused for having no cave.
                //
                // A refused row leaves whatever it had begun to change behind it, and none of that
                // may reach the database: the refusal is the whole of what happened to that row.
                if (result.Status is SyncRowStatus.Created or SyncRowStatus.Updated or SyncRowStatus.Deleted)
                {
                    await db.SaveChangesAsync(ct);
                }
                else
                {
                    db.ChangeTracker.Clear();
                }
            }

            // A row's revision is stamped by the database as it is written, not by the handler,
            // so it is read back rather than predicted: a device told the value from before the
            // write would send it up again and be told its own edit had gone stale.
            var rows = await StampRevisionsAsync(db, results, ct);
            var answer = new SyncUploadResultDto(
                request.BatchId,
                batch.Id,
                Replayed: false,
                Written: rows.Count(r => r.Status is SyncRowStatus.Created or SyncRowStatus.Updated or SyncRowStatus.Deleted),
                Refused: rows.Count(r => r.Status is SyncRowStatus.Conflict or SyncRowStatus.Rejected),
                rows,
                await ConflictEchoAsync(db, protection, ctx, rows, ct),
                await DuplicatesAsync(
                    db, proximity, ctx, rows, options.Value.ResolvedDuplicateRadiusMeters, ct));

            // Recorded before the transaction closes, so the decision and the record of it are
            // one write and a resend cannot find a batch that never learnt what it answered.
            // Decisions only: positions are read out of the rows when a resend arrives rather
            // than kept here, because an account's right to see a position can be taken away and
            // a stored coordinate would be handed back regardless.
            batch.SyncResult = JsonSerializer.Serialize(rows, SyncUploadJson.Options);

            // Re-attached rather than added: the row that claimed the identifier above was
            // written at the start, and a refused row in between clears the tracker, so by now
            // this instance may be tracked by nothing.
            db.ImportBatches.Update(batch);
            items.ForEach(i => i.ImportBatchId = batch.Id);
            db.ImportBatchItems.AddRange(items);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return TypedResults.Ok(answer);
        }
        catch (Exception e) when (IsBatchCollision(e))
        {
            // The same batch arrived twice at once — a phone that retried while the first attempt
            // was still in flight. The loser writes nothing and answers with what the winner
            // recorded, which is the same answer a later resend would get.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var stored = await ReplayAsync(
                db, protection, proximity, ctx, request.BatchId,
                options.Value.ResolvedDuplicateRadiusMeters, ct);
            return stored is not null
                ? TypedResults.Ok(stored)
                : ApiProblems.Conflict("sync.batch_conflict", "That batch is already being applied. Send it again.");
        }
        catch (FeatureWriteException e)
        {
            // A row-level refusal is answered per row; reaching here means the write service
            // refused something the loop could not attribute to one row, so nothing is kept.
            await transaction.RollbackAsync(ct);
            return ApiProblems.BadRequest(e.Code, string.Join("; ", e.Errors));
        }
    }

    /// <summary>
    /// The answer an earlier request with this batch identifier recorded, or null if there was
    /// none. Scoped to the account, because a batch identifier only means anything inside the
    /// account that minted it.
    /// </summary>
    private static async Task<SyncUploadResultDto?> ReplayAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        VisibleProximitySearch proximity,
        AccessContext ctx,
        Guid batchId,
        double duplicateRadiusMeters,
        CancellationToken ct)
    {
        var prior = await db.ImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.ConfirmedByUserId == ctx.UserId && b.SyncBatchId == batchId, ct);
        if (prior?.SyncResult is not { } stored)
        {
            return null;
        }

        var rows = JsonSerializer.Deserialize<List<SyncUploadRowResultDto>>(stored, SyncUploadJson.Options) ?? [];
        return new SyncUploadResultDto(
            batchId,
            prior.Id,
            Replayed: true,
            Written: rows.Count(r => r.Status is SyncRowStatus.Created or SyncRowStatus.Updated or SyncRowStatus.Deleted),
            Refused: rows.Count(r => r.Status is SyncRowStatus.Conflict or SyncRowStatus.Rejected),
            rows,

            // Worked out again rather than read back with the decisions. What the account was
            // allowed to see on the day the batch was applied is not what it is allowed to see
            // on the day it retries, and a resend is a fresh question with a fresh answer.
            await ConflictEchoAsync(db, protection, ctx, rows, ct),
            await DuplicatesAsync(db, proximity, ctx, rows, duplicateRadiusMeters, ct));
    }

    /// <summary>
    /// This server's own version of every row that lost a conflict, shaped exactly as a download
    /// would have delivered it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the second place in this slice where a position travels from the server to a
    /// device, and it is on a route where none of the download's filtering runs — so it asks the
    /// one place that decides who may be given a position, gets the same answer, and does the same
    /// thing with it. A row this caller may not place is absent from the echo rather than blurred
    /// in it: a device told its edit lost, and given nothing to compare against, still knows to
    /// re-read the row, while a device handed a snapped point would write it to cleartext storage
    /// and pass it on as though it were the cave.
    /// </para>
    /// <para>
    /// Readability is asked here too, and not inherited from the arbitration that produced these
    /// decisions. A resend is answered from this method alone and may arrive long afterwards, by
    /// which time a grant the first attempt relied on can have been withdrawn.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<SyncFeatureDto>> ConflictEchoAsync(
        SilexGisDbContext db,
        FeatureProtection protection,
        AccessContext ctx,
        IReadOnlyList<SyncUploadRowResultDto> rows,
        CancellationToken ct)
    {
        var lost = rows.Where(r => r.Status is SyncRowStatus.Conflict).Select(r => r.Id).ToList();
        if (lost.Count == 0)
        {
            return [];
        }

        var visible = db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers);
        var candidates = await visible.Where(f => lost.Contains(f.Id)).ToListAsync(ct);
        var withheld = await SyncWithhold.WithheldIdsAsync(
            protection, ctx, [.. candidates.Select(f => f.Id)], ct);

        return await SyncFeatureShaping.ToDtosAsync(
            db, visible, [.. candidates.Where(f => !withheld.Contains(f.Id))], ct);
    }

    /// <summary>
    /// What was already here, near a row this batch created. A report and never a verdict: the
    /// row is written either way and its status never mentions this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A device is offline when it decides to add a cave, so it cannot ask first — and a caver
    /// who surveyed a shaft forty metres from one a clubmate entered last week has no way of
    /// knowing. Refusing the row is the wrong answer, because the device would have nowhere to
    /// put the work and the caver would be arguing with a phone in a car park; so the row lands
    /// and the answer says what it landed next to.
    /// </para>
    /// <para>
    /// Who may be in the comparison at all is decided by the search itself and is not re-derived
    /// here: "there is already something within fifty metres of this point" is a position, so the
    /// pool is what this caller may read <em>and</em> may place exactly. The cost is real and is
    /// accepted — a row pushed beside a protected entrance this caller may not place is reported
    /// as near nothing — because the alternative is answering a position to somebody entitled to
    /// no position.
    /// </para>
    /// <para>
    /// Rows this batch wrote are never each other's duplicates. A device sending a cave and the
    /// three places inside it sends four points a few metres apart by construction, and reporting
    /// those against each other would bury the one hit that means anything.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<SyncDuplicateDto>> DuplicatesAsync(
        SilexGisDbContext db,
        VisibleProximitySearch proximity,
        AccessContext ctx,
        IReadOnlyList<SyncUploadRowResultDto> rows,
        double radiusMeters,
        CancellationToken ct)
    {
        var written = rows
            .Where(r => r.Status is SyncRowStatus.Created or SyncRowStatus.Updated)
            .Select(r => r.Id)
            .ToHashSet();
        var created = rows.Where(r => r.Status is SyncRowStatus.Created).Select(r => r.Id).ToList();
        if (created.Count == 0)
        {
            return [];
        }

        // Read back rather than taken from the request, so a resend answers from the same place
        // the first attempt did — and so a coordinate the arbitration declined to write is never
        // the one compared against.
        var placed = (await db.Features.AsNoTracking()
                .Where(f => created.Contains(f.Id) && f.Geom != null)
                .Select(f => new { f.Id, f.Geom })
                .ToListAsync(ct))
            .Where(f => f.Geom is Point)
            .ToDictionary(f => f.Id, f => (Point)f.Geom!);
        if (placed.Count == 0)
        {
            return [];
        }

        var nearby = await proximity.NearAsync(placed.Values, radiusMeters, ctx, ct);
        var report = new List<SyncDuplicateDto>();
        foreach (var (id, point) in placed)
        {
            var near = nearby
                .Where(hit => !written.Contains(hit.FeatureId))
                .Select(hit => (Hit: hit, Distance: Geodesy.DistanceMeters(point.Coordinate, hit.Geom.Coordinate)))
                .Where(x => x.Distance <= radiusMeters)
                .OrderBy(x => x.Distance)
                .Take(DuplicatesPerRow)
                .Select(x => new SyncDuplicateCandidateDto(
                    x.Hit.FeatureId,
                    x.Hit.Name,
                    x.Hit.Kind,
                    Math.Round(x.Distance, 1),
                    x.Hit.CaveFeatureId))
                .ToList();
            if (near.Count > 0)
            {
                report.Add(new SyncDuplicateDto(id, near));
            }
        }

        return report;
    }

    /// <summary>
    /// Fills in each written row's revision from the value the database stamped, which is the
    /// value the device sends back as its base revision next time.
    /// </summary>
    private static async Task<IReadOnlyList<SyncUploadRowResultDto>> StampRevisionsAsync(
        SilexGisDbContext db, List<SyncUploadRowResultDto> rows, CancellationToken ct)
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
    /// Whether a failed save is the same batch arriving twice, rather than any other constraint.
    /// Matched on the index by name so a different violation is not reported as a resend.
    /// </summary>
    private static bool IsBatchCollision(Exception e) =>
        e is DbUpdateException { InnerException: PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_import_batches_user_sync_batch",
        } };

    /// <summary>
    /// Arbitrates and applies one row. Returns what to tell the device, and the provenance line
    /// to record — null for a row that changed nothing, so that undoing the batch does not reach
    /// rows it never touched.
    /// </summary>
    private static async Task<(SyncUploadRowResultDto Result, ImportBatchItem? Item)> ApplyRowAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessService access,
        AccessContext ctx,
        SyncSet set,
        SyncTaxonomies taxonomies,
        IReadOnlySet<Guid> withheld,
        SyncUploadRowDto row,
        CancellationToken ct)
    {
        // Read past the filter that hides deleted rows on purpose. A row that was removed here
        // and is still on the phone must be answered as gone; read through the filter it would
        // look absent, and a create would then try to put it back under the same identifier —
        // undoing the deletion and colliding with the row that is still there.
        var existing = await db.Features.IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == row.Id, ct);

        if (existing is null)
        {
            if (row.Deleted)
            {
                // The device is ahead of this server, or the row never got here. Either way there
                // is nothing to remove and nothing went wrong.
                return (Ok(row.Id, SyncRowStatus.Unchanged), null);
            }

            if (row.BaseRevision is not null)
            {
                return (Refuse(row.Id, "sync.row_not_found",
                    "This row is not on the server. Send it as a new row."), null);
            }

            return await CreateAsync(db, writer, access, ctx, set, taxonomies, withheld, row, ct);
        }

        // Readability first, and before anything says whether the row is deleted or writable: a
        // caller who may not read a row learns only that its identifier is taken. That does
        // disclose the existence of the identifier, and is accepted — a version-7 uuid carries
        // enough randomness that guessing a live one is infeasible unless it has already leaked,
        // and the alternative is minting a different identifier for the row, which puts a
        // translation table between the two sides for ever.
        if (!(await access.DecideAsync(ctx, AccessAction.Read, existing, ct)).Allowed)
        {
            return (Refuse(row.Id, "sync.id_conflict", "That identifier is already in use here."), null);
        }

        if (existing.DeletedAt is not null)
        {
            return row.Deleted
                ? (Ok(row.Id, SyncRowStatus.Unchanged), null)
                : (Refuse(row.Id, "sync.row_deleted", "This row was removed on the server. Drop it locally."), null);
        }

        // Removing a row and editing one are different rights, and this channel asks for the one
        // the act needs — the same way the rest of the API does, where a delete is refused to an
        // account that may edit. Asking for write on both would let a grant meant to allow
        // corrections take a cave and everything inside it out of the registry.
        if (row.Deleted)
        {
            if (!(await access.DecideAsync(ctx, AccessAction.Delete, existing, ct)).Allowed)
            {
                return (Refuse(row.Id, "sync.row_delete_forbidden", "You may not remove this row."), null);
            }
        }
        else if (!(await access.DecideAsync(ctx, AccessAction.Write, existing, ct)).Allowed)
        {
            return (Refuse(row.Id, "sync.row_forbidden", "You may not write this row."), null);
        }

        if (row.BaseRevision is null)
        {
            // A create whose row is already here: the device sent it before and did not hear the
            // answer. Writing it again would overwrite whatever has happened to the row since,
            // with no revision to arbitrate against, so nothing is written.
            return (Ok(row.Id, SyncRowStatus.Unchanged, existing.UpdatedAt), null);
        }

        if (!SameRevision(existing.UpdatedAt, row.BaseRevision.Value))
        {
            return (new SyncUploadRowResultDto(
                row.Id,
                SyncRowStatus.Conflict,
                existing.UpdatedAt,
                "sync.conflict",
                "This row changed on the server after you last read it."), null);
        }

        if (row.Deleted)
        {
            // Read before the delete and untracked, because the soft delete stamps the rows in
            // the database and the mirror below asks the tracker first: a tracked copy still
            // carrying its pre-delete state would count this entrance as present.
            var goingEntrance = await db.CaveEntrances.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == existing.Id, ct);
            var caveOfEntrance = goingEntrance?.CaveFeatureId;
            await writer.SoftDeleteAsync(existing.Id, ct);
            db.Entry(existing).State = EntityState.Detached;

            // Removing an entrance moves the cave above it. A cave's entrance count is a stored
            // column and its map point is a copy of its main entrance's, so a delete that skipped
            // this would leave the cave plotting at a position that no longer exists and claiming
            // an entrance that is gone — which the integrity check then reports as two faults.
            // A cave that went in the same act has no mirror worth refreshing.
            if (caveOfEntrance is { } caveId
                && await db.Features.AsNoTracking().IgnoreQueryFilters()
                    .AnyAsync(f => f.Id == caveId && f.DeletedAt == null, ct))
            {
                await writer.SyncCaveMirrorAsync(caveId, ct);
            }

            return (Ok(row.Id, SyncRowStatus.Deleted), Touched(row.Id));
        }

        return await UpdateAsync(db, writer, withheld, existing, row, ct);
    }

    /// <summary>
    /// Whether the revision a device sends back is the one this server stamped. Compared at the
    /// precision the database keeps rather than the precision .NET keeps: the value goes out of
    /// here with tick resolution and comes back off a column that stores microseconds, so an
    /// exact comparison of the two would report a conflict against a row nobody had touched.
    /// </summary>
    private static bool SameRevision(DateTimeOffset stored, DateTimeOffset claimed) =>
        stored.UtcTicks / 10 == claimed.UtcTicks / 10;

    private static SyncUploadRowResultDto Ok(Guid id, SyncRowStatus status, DateTimeOffset? revision = null) =>
        new(id, status, revision, null, null);

    private static SyncUploadRowResultDto Refuse(Guid id, string code, string detail) =>
        new(id, SyncRowStatus.Rejected, null, code, detail);

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

    private static async Task<(SyncUploadRowResultDto, ImportBatchItem?)> CreateAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        IAccessService access,
        AccessContext ctx,
        SyncSet set,
        SyncTaxonomies taxonomies,
        IReadOnlySet<Guid> withheld,
        SyncUploadRowDto row,
        CancellationToken ct)
    {
        Geometry? geom = null;
        if (row.Geometry is not null)
        {
            geom = row.Geometry.ToGeometryOrNull();
            if (geom is null)
            {
                return (Refuse(row.Id, "sync.geometry_invalid", "That geometry could not be read."), null);
            }
        }

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
            OwnerUserId = ctx.UserId,
            CavingGroupId = set.CavingGroupId,
            Visibility = set.UploadVisibility,
            ClientUpdatedAt = row.ClientUpdatedAt,
        };

        try
        {
            switch (row.Kind)
            {
                case FeatureKind.Cave:
                {
                    if (!taxonomies.TryCaveType(row.CaveTypeCode ?? DefaultCaveTypeCode, out var caveTypeId))
                    {
                        return (Refuse(row.Id, "sync.type_unknown", "This installation has no such cave kind."), null);
                    }

                    // A cave may sit inside something — a surface area is the ordinary case, and
                    // is what a device's place codes are allocated from. It is optional, because
                    // a cave with nothing above it is a legal row; but when one is named the edge
                    // is made, because dropping it silently would leave the cave outside the very
                    // selection the caver rooted at that area.
                    var caveContainer = await ContainerOrNullAsync(db, access, ctx, row, ct);
                    if (caveContainer.Problem is { } refusedCave)
                    {
                        return (refusedCave, null);
                    }

                    var caveFacts = await CreateContext.ParentCreateFactsAsync(
                        db, ctx, caveContainer.Feature?.Id, set.CavingGroupId, FeatureKind.Cave, ct);
                    if (!MayCreate(ctx, caveFacts))
                    {
                        return (RefuseCreate(row.Id, set, ctx, caveFacts), null);
                    }

                    // A cave's own point is a cache of its main entrance and is written by the
                    // service that maintains it; a cave that arrives carrying one is stored
                    // without it rather than refused, because the entrance that follows in the
                    // same batch is where that position belongs.
                    feature.Geom = null;
                    var cave = new Cave { CaveTypeId = caveTypeId };
                    feature.Cave = cave;
                    await writer.CreateCaveAsync(
                        feature,
                        cave,
                        caveContainer.Feature is { } caveParent
                            ? [new ParentSpec(caveParent.Id, IsPrimary: true)]
                            : [],
                        ct);
                    break;
                }

                case FeatureKind.CaveEntrance:
                {
                    var parent = await ContainerAsync(db, access, ctx, row, FeatureKind.Cave, ct);
                    if (parent.Problem is { } refusedEntrance)
                    {
                        return (refusedEntrance, null);
                    }

                    if (geom is not Point)
                    {
                        return (Refuse(row.Id, "sync.geometry_invalid", "An entrance carries a point."), null);
                    }

                    // The write-right rule, on the path where it is least obvious, and the
                    // one place in this file where it is easiest to talk oneself out of.
                    //
                    // Creating an entrance writes a position onto the cave above it: the cave's
                    // map point is its main entrance's, refreshed by the writer on every entrance
                    // change, and a cave with no entrances yet adopts the first one whether or not
                    // it was offered as the main one. So this create moves a row that already
                    // existed, and that row's position is one this caller may never have been
                    // shown.
                    //
                    // The rest of this API guards updates and leaves creates open, on the
                    // reasoning that a coordinate arriving with a new row is the caller's own and
                    // echoing it back discloses nothing. That reasoning is about where the value
                    // came from, not about which verb carried it, and it does not survive this
                    // channel: a device replays coordinates this server handed it, so a point
                    // arriving here may be one the caller was given rather than one they took.
                    // Guarding creates is therefore not an inconsistency to tidy away later —
                    // the axis is provenance, and on this route the provenance is different.
                    if (withheld.Contains(parent.Feature!.Id))
                    {
                        return (Refuse(row.Id, "sync.location_forbidden",
                            "You may not place an entrance on this cave."), null);
                    }

                    if (!taxonomies.TryEntranceType(row.EntranceTypeCode ?? DefaultEntranceTypeCode, out var typeId))
                    {
                        return (Refuse(row.Id, "sync.type_unknown", "This installation has no such entrance kind."), null);
                    }

                    var entranceFacts = await CreateContext.ParentCreateFactsAsync(
                        db, ctx, parent.Feature.Id, set.CavingGroupId, FeatureKind.CaveEntrance, ct);
                    if (!MayCreate(ctx, entranceFacts))
                    {
                        return (RefuseCreate(row.Id, set, ctx, entranceFacts), null);
                    }

                    var altitude = AltitudeOf(row, geom);
                    feature.Geom = WithAltitude((Point)geom, altitude);
                    var entrance = new CaveEntrance
                    {
                        CaveFeatureId = parent.Feature.Id,
                        EntranceTypeId = typeId,
                        IsMain = row.IsMain,
                        Altitude = altitude,
                        PositionQuality = row.PositionQuality ?? PositionQuality.Unknown,
                    };
                    await writer.CreateEntranceAsync(feature, entrance, ct);
                    if (entrance.IsMain)
                    {
                        await writer.SetMainEntranceAsync(parent.Feature.Id, feature.Id, ct);
                    }

                    break;
                }

                case FeatureKind.Generic:
                {
                    if (!taxonomies.TryFeatureType(row.FeatureTypeCode ?? DefaultPlaceTypeCode, out var typeId))
                    {
                        return (Refuse(row.Id, "sync.type_unknown", "This installation has no such kind of place."), null);
                    }

                    // Whether a row needs a container is a property of its kind, read from the
                    // taxonomy. The kinds a sync selection is rooted in sit at the top of the
                    // tree — a surface area holds the caves, and holds the segment every place
                    // code beneath it is allocated from — so a channel that demanded a container
                    // of every row would refuse those for ever, and with them everything a device
                    // numbers underneath one.
                    var parent = taxonomies.RequiresParent(typeId)
                        ? await ContainerAsync(db, access, ctx, row, null, ct)
                        : await ContainerOrNullAsync(db, access, ctx, row, ct);
                    if (parent.Problem is { } refusedPlace)
                    {
                        return (refusedPlace, null);
                    }

                    var placeFacts = await CreateContext.ParentCreateFactsAsync(
                        db, ctx, parent.Feature?.Id, set.CavingGroupId, FeatureKind.Generic, ct, typeId);
                    if (!MayCreate(ctx, placeFacts))
                    {
                        return (RefuseCreate(row.Id, set, ctx, placeFacts), null);
                    }

                    feature.FeatureTypeId = typeId;
                    await writer.CreateGenericAsync(
                        feature,
                        parent.Feature is { } placeParent
                            ? [new ParentSpec(placeParent.Id, IsPrimary: true)]
                            : [],
                        ct);
                    break;
                }

                default:
                    return (Refuse(row.Id, "sync.kind_unsupported", "This kind of row is not carried by sync."), null);
            }
        }
        catch (FeatureWriteException e)
        {
            return (Refuse(row.Id, e.Code, string.Join("; ", e.Errors)), null);
        }

        return (Ok(row.Id, SyncRowStatus.Created), Made(row.Id));
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

    private static async Task<(SyncUploadRowResultDto, ImportBatchItem?)> UpdateAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        IReadOnlySet<Guid> withheld,
        Feature feature,
        SyncUploadRowDto row,
        CancellationToken ct)
    {
        // The fields a device owns, written whatever the caller may see: a name and a
        // description are not positions, and refusing an edit to them would lose work for no
        // protection. The guard below is stated per field for exactly this reason — a caller who
        // may not move a cave may still rename it, and a rule that refused the whole row would
        // throw away the half of the edit that was never in question.
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
        // people the guard below exists to keep it from. The labels a device keeps here — its own
        // index for a cave, the codes it generates — locate nothing. Do not relax that to make
        // something fit.
        if (row.Properties is { } properties)
        {
            feature.Properties = properties.GetRawText();
        }

        // The three cave fields that are positions in prose — the closest address, the land
        // registry number and the notes saying how to find the place — are guarded by not being
        // here at all: an uploaded row cannot carry them, so there is no submitted value to write
        // back over the stored one. Anything that later teaches this channel to carry them has to
        // bring them inside the guard below.

        // The write-right rule. A caller without exact view was never shown this row's real
        // position — elsewhere in this API they are shown a grid-snapped one instead — so
        // whatever coordinate they send back came from somewhere other than the stored value, and
        // writing it would replace a true position with a degraded one. Their other edits stand.
        var exact = !withheld.Contains(feature.Id);

        try
        {
            if (feature.Kind == FeatureKind.CaveEntrance)
            {
                var entrance = await db.CaveEntrances.FirstAsync(e => e.Id == feature.Id, ct);

                // Both, because moving an entrance moves the cave: the cave's own map point is a
                // cache of its main entrance's. Exact view is a decision about one row, so the
                // cave's has to be asked for separately from the entrance's.
                if (exact && !withheld.Contains(entrance.CaveFeatureId))
                {
                    var geom = row.Geometry?.ToGeometryOrNull();
                    if (row.Geometry is not null && geom is not Point)
                    {
                        return (Refuse(row.Id, "sync.geometry_invalid", "An entrance carries a point."), null);
                    }

                    entrance.PositionQuality = row.PositionQuality ?? PositionQuality.Unknown;
                    if (geom is Point point)
                    {
                        entrance.Altitude = AltitudeOf(row, point);
                        feature.Geom = WithAltitude(point, entrance.Altitude);
                    }

                    if (row.IsMain && !entrance.IsMain)
                    {
                        await writer.SetMainEntranceAsync(entrance.CaveFeatureId, entrance.Id, ct);
                    }
                    else
                    {
                        await writer.SyncCaveMirrorAsync(entrance.CaveFeatureId, ct);
                    }
                }
            }
            else if (feature.Kind == FeatureKind.Generic && exact && row.Geometry is not null)
            {
                var geom = row.Geometry.ToGeometryOrNull();
                if (geom is null)
                {
                    return (Refuse(row.Id, "sync.geometry_invalid", "That geometry could not be read."), null);
                }

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

            // A cave feature is deliberately absent from the two arms above. Its geometry is not
            // a field anybody writes — it is the main entrance's, kept in step by the writer —
            // so an uploaded cave carrying a point has nowhere to put it and is not refused for
            // trying.
            await writer.ValidatePropertiesAsync(feature, ct);
        }
        catch (FeatureWriteException e)
        {
            return (Refuse(row.Id, e.Code, string.Join("; ", e.Errors)), null);
        }

        return (Ok(row.Id, SyncRowStatus.Updated), Touched(row.Id));
    }

    /// <summary>
    /// The row a new row hangs under, checked for existence and for the caller's right to add to
    /// it. Containment is what protection and visibility are inherited along, so the edge is made
    /// by the writer from a container this caller may actually write — never taken as a
    /// free-standing structure the device declares.
    /// </summary>
    /// <summary>
    /// The same check, for a row whose kind does not need a container: no container named is no
    /// problem, and a container that <em>was</em> named is checked exactly as it would be if the
    /// kind had required one. A named container is never quietly dropped — the edge is what
    /// protection and visibility are inherited along, so losing it changes who can see the row.
    /// </summary>
    private static async Task<(Feature? Feature, SyncUploadRowResultDto? Problem)> ContainerOrNullAsync(
        SilexGisDbContext db,
        IAccessService access,
        AccessContext ctx,
        SyncUploadRowDto row,
        CancellationToken ct) =>
        row.ParentId is null ? (null, null) : await ContainerAsync(db, access, ctx, row, null, ct);

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

        var parent = await db.Features.FirstOrDefaultAsync(f => f.Id == parentId, ct);
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

/// <summary>
/// How an upload's recorded answer is written to and read back from the batch it belongs to. The
/// same options in both directions, stated once, so a resend is answered with what was recorded
/// rather than with a differently-spelled version of it.
/// </summary>
internal static class SyncUploadJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
