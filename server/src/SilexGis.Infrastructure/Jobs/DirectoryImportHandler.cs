// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Jobs;

/// <summary>Which directory to walk, and into which batch.</summary>
public sealed record DirectoryImportPayload(string Path, Guid BatchId);

/// <summary>
/// Copies a directory on the server's own disk into the archive, mirroring its folders as
/// cabinets.
///
/// <para>
/// The same walk as an expanded archive — same runner, same filing, same report — differing
/// only in where the bytes are read from. What is particular to it is the guard: the path is
/// re-resolved and re-checked against the operator's allow-list here, not merely when the
/// request was accepted, because the disk can change between a job being queued and it
/// running, and the check that counts is the one nearest the read.
/// </para>
/// <para>
/// The source directory is never touched. Files are copied into the store, and an import that
/// goes wrong is undone by deleting what it created rather than by hoping the original is
/// still there.
/// </para>
/// </summary>
public sealed class DirectoryImportHandler(
    SilexGisDbContext db,
    BulkIngestRunner runner,
    UploadBatchService batches,
    ServerDirectorySource directories) : IProcessingJobHandler
{
    public string Kind => ProcessingJobKinds.DirectoryImport;

    public async Task ExecuteAsync(ProcessingJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        var payload = JsonSerializer.Deserialize<DirectoryImportPayload>(job.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Directory import payload is not readable.");

        var batch = await db.UploadBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == payload.BatchId, ct);
        if (batch is null)
        {
            return; // the batch was pruned; there is nothing to report into
        }

        var (resolved, code) = directories.Resolve(payload.Path);
        if (resolved is null)
        {
            await batches.CompleteAsync(batch.Id, code, ct);
            return;
        }

        // Rebuilt for the person who asked rather than carried from the request that queued
        // this: rights may have been taken away in between, and the walk must grant exactly
        // what its requester holds now.
        var ctx = await AccessContextResolver.ResolveAsync(db, batch.StartedByUserId, ct);
        if (ctx is null)
        {
            await batches.CompleteAsync(batch.Id, ServerImportPaths.NotFoundCode, ct);
            return;
        }

        await runner.RunAsync(batch, ctx, directories.ReadAsync(resolved, ct), ct);
        await batches.CompleteAsync(batch.Id, error: null, ct);
    }
}
