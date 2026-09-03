// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>
/// What a reviewer confirmed, and the batch their objects will be found in.
/// </summary>
/// <remarks>
/// The selection is carried rather than re-derived. It is what the person actually ticked, and
/// re-scanning the file when the job runs would quietly confirm a different set if the rules or
/// the file's reading changed in between — creating objects nobody chose.
/// </remarks>
public sealed record ImportCommitPayload(
    Guid GeofileId,
    Guid BatchId,
    Guid UserId,
    Guid? TermRuleSetId,
    string Options,
    string Decisions,
    IReadOnlyList<long> Selection,
    ImportBatchMode Mode);

/// <summary>
/// Creates the objects a reviewer confirmed out of an uploaded vector file.
///
/// <para>
/// This runs on the queue because the work does not fit in a request. One confirmed row is a
/// geometry read plus a write that maintains the containment and protection state derived from
/// it; a few thousand rows is tens of thousands of round trips, held inside a single
/// transaction so the batch is all-or-nothing and revertible as one thing. A reverse proxy
/// gives up long before that finishes, and when it does the caller's cancellation reaches the
/// transaction and rolls the whole batch back — the reported failure being a gateway timeout
/// and the visible result being nothing at all, which is the least useful pair of facts to hand
/// somebody who has just reviewed three thousand rows.
/// </para>
/// <para>
/// Everything that can refuse the request is decided before this is queued — the file being
/// read, the right to create, the group to bind to, the rule set. What is left here is work,
/// not judgement, so a queued job fails only for reasons nobody could have been told earlier.
/// </para>
/// </summary>
public sealed class ImportCommitHandler(
    SilexGisDbContext db,
    ImportCommitService commitService,
    TermRuleSetStore ruleSets) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.ImportCommit;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        // Read with the same serializer that wrote it. The import vocabulary spells its enums a
        // particular way, and the web default disagrees — silently at compile time, and at run
        // time only once a job is already queued and cannot be re-asked.
        var payload = ImportJson.Deserialize<ImportCommitPayload>(job.Payload)
            ?? throw new InvalidOperationException("Import commit payload is not readable.");

        // Already done: a retry of a job whose transaction committed must not create the batch
        // twice. The batch id is fixed by whoever queued this, which is what makes that
        // checkable at all.
        if (await db.ImportBatches.AsNoTracking().AnyAsync(b => b.Id == payload.BatchId, ct))
        {
            return;
        }

        var geofile = await db.Geofiles.AsNoTracking().FirstOrDefaultAsync(g => g.Id == payload.GeofileId, ct);
        if (geofile is null)
        {
            // The upload was deleted between confirming and running. Nothing to create and
            // nothing to repair; the batch simply never appears.
            throw new InvalidOperationException(
                $"The file this confirmation was made against is no longer here ({payload.GeofileId}).");
        }

        // Rebuilt for the person who confirmed rather than carried from the request that queued
        // this: rights may have been taken away in between, and what is created must be exactly
        // what its confirmer may create now.
        var ctx = await AccessContextResolver.ResolveAsync(db, payload.UserId, ct)
            ?? throw new InvalidOperationException(
                $"The account that confirmed this import can no longer be resolved ({payload.UserId}).");

        var ruleSet = payload.TermRuleSetId is null
            ? null
            : await ruleSets.ResolveAsync(ctx, payload.TermRuleSetId, ct);

        var options = ImportJson.Deserialize<ImportOptions>(payload.Options)
            ?? throw new InvalidOperationException("The import options are not readable.");
        var decisions = ImportJson.Deserialize<Dictionary<long, ImportDecision>>(payload.Decisions)
            ?? [];

        await commitService.CommitAsync(
            geofile, ruleSet, options, payload.Selection, decisions, payload.Mode, ctx, payload.BatchId, ct);

        // The review is spent: its decisions describe rows that are now objects, and leaving it
        // would offer to create them a second time.
        await db.GeofileImportSessions
            .Where(s => s.GeofileId == payload.GeofileId && s.UserId == payload.UserId)
            .ExecuteDeleteAsync(ct);
    }
}
