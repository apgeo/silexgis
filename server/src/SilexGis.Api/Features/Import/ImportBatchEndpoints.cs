// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Import;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Import;

/// <summary>
/// What each confirmation created, and the one action a confirmation still has: undoing it.
///
/// <para>
/// A batch is what makes a bad import findable months later. It is never edited after it lands
/// — the objects it created are edited as objects — and reverting it soft-deletes everything it
/// created as one unit, for the case where the mapping was wrong and nobody noticed until the
/// map looked odd.
/// </para>
/// <para>
/// A batch is readable by whoever may read the objects in it, which is why the list is filtered
/// through the features rather than through the batch row: the batch itself carries no
/// visibility, and its item list names features and distances between them.
/// </para>
/// </summary>
public static class ImportBatchEndpoints
{
    public static RouteGroupBuilder MapImportBatchEndpoints(this RouteGroupBuilder api)
    {
        var batches = api.MapGroup("/import-batches").WithTags("Import");

        batches.MapGet("/", ListAsync)
            .WithSummary("Confirmations the caller made, or all of them for an administrator.");
        batches.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One confirmation with the objects it created, filtered to what the caller may see.");
        batches.MapPost("/{id:guid}/revert", RevertAsync)
            .WithSummary("Soft-deletes everything the confirmation created, as one unit.");

        api.MapGet("/features/{featureId:guid}/import-provenance", GetProvenanceAsync)
            .WithTags("Import")
            .WithSummary("Which file, which rule, who confirmed it and when — for one object.");

        return api;
    }

    private static async Task<Results<Ok<PagedResult<ImportBatchDto>>, UnauthorizedHttpResult>> ListAsync(
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        Guid? geofileId,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Your own confirmations, unless you administer the installation. A batch is a record
        // of somebody's work rather than a piece of content with a visibility of its own, so
        // "whose was it" is the honest filter — and the objects it made are governed normally.
        var query = db.ImportBatches.AsNoTracking();
        if (!ctx.IsFullAdmin)
        {
            query = query.Where(b => b.ConfirmedByUserId == ctx.UserId);
        }

        if (geofileId is not null)
        {
            query = query.Where(b => b.GeofileId == geofileId);
        }

        var (p, size) = Paging.Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((p - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var names = await GeofileNamesAsync(db, rows, ct);
        var items = rows
            .Select(b => StagedImportEndpoints.ToDto(
                b, b.GeofileId is { } id ? names.GetValueOrDefault(id) : null, CanRevert(ctx, b)))
            .ToList();
        return TypedResults.Ok(new PagedResult<ImportBatchDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<ImportBatchDetailDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetAsync(
        Guid id,
        SilexGisDbContext db,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null || !CanRead(ctx, batch))
        {
            return ApiProblems.NotFound("import_batch.not_found");
        }

        var items = await db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == batch.Id)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

        var featureIds = items
            .SelectMany(i => new[] { i.FeatureId, i.AttachedToFeatureId })
            .Where(f => f is not null)
            .Select(f => f!.Value)
            .Distinct()
            .ToList();

        // Names come from what the caller may see. An object the batch created and then had
        // its visibility narrowed away from this reader stays in the list as a line with no
        // name — the count is the caller's own history and is not hidden, the content is.
        var visible = await db.Features.AsNoTracking()
            .IgnoreQueryFilters()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .Where(f => featureIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.Kind, f.DeletedAt })
            .ToDictionaryAsync(f => f.Id, ct);

        var names = await GeofileNamesAsync(db, [batch], ct);
        return TypedResults.Ok(new ImportBatchDetailDto(
            StagedImportEndpoints.ToDto(
                batch, batch.GeofileId is { } gid ? names.GetValueOrDefault(gid) : null, CanRevert(ctx, batch)),
            [
                .. items.Select(i =>
                {
                    var feature = i.FeatureId is { } fid ? visible.GetValueOrDefault(fid) : null;
                    return new ImportBatchItemDto(
                        i.Id,
                        i.FeatureId,
                        feature?.Name,
                        feature?.Kind,
                        feature?.DeletedAt is not null,
                        i.AttachedToFeatureId,
                        i.SourceFeatureId,
                        i.RuleId,
                        i.RuleName,
                        i.Action);
                })
            ]));
    }

    private static async Task<Results<Ok<ImportBatchDto>, UnauthorizedHttpResult, ProblemHttpResult>> RevertAsync(
        Guid id,
        SilexGisDbContext db,
        ImportCommitService commitService,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null || !CanRead(ctx, batch))
        {
            return ApiProblems.NotFound("import_batch.not_found");
        }

        if (!CanRevert(ctx, batch))
        {
            return batch.IsReverted
                ? ApiProblems.BadRequest("import_batch.already_reverted", "This import has already been undone.")
                : ApiProblems.Forbidden("import_batch.revert_forbidden");
        }

        await commitService.RevertAsync(batch, ctx.UserId, ct);

        var names = await GeofileNamesAsync(db, [batch], ct);
        return TypedResults.Ok(StagedImportEndpoints.ToDto(
            batch, batch.GeofileId is { } gid ? names.GetValueOrDefault(gid) : null, canRevert: false));
    }

    private static async Task<Results<Ok<ImportProvenanceDto>, UnauthorizedHttpResult, ProblemHttpResult>> GetProvenanceAsync(
        Guid featureId,
        SilexGisDbContext db,
        IAccessService access,
        IAccessContextAccessor accessAccessor,
        CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // Asked against the object, not the batch: whoever may read the cave may know where it
        // came from, whether or not they were the one who imported it.
        var feature = await db.Features.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == featureId, ct);
        if (feature is null || !(await access.DecideAsync(ctx, AccessAction.Read, feature, ct)).Allowed)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        var item = await db.ImportBatchItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.FeatureId == featureId, ct);
        if (item is null)
        {
            return ApiProblems.NotFound("import_batch.not_imported");
        }

        var batch = await db.ImportBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == item.ImportBatchId, ct);
        if (batch is null)
        {
            return ApiProblems.NotFound("import_batch.not_found");
        }

        var names = await GeofileNamesAsync(db, [batch], ct);
        return TypedResults.Ok(new ImportProvenanceDto(
            StagedImportEndpoints.ToDto(
                batch, batch.GeofileId is { } gid ? names.GetValueOrDefault(gid) : null, CanRevert(ctx, batch)),
            new ImportBatchItemDto(
                item.Id,
                item.FeatureId,
                feature.Name,
                feature.Kind,
                feature.DeletedAt is not null,
                item.AttachedToFeatureId,
                item.SourceFeatureId,
                item.RuleId,
                item.RuleName,
                item.Action),
            ParseProperties(item.SourceProperties)));
    }

    /// <summary>Your own confirmations, plus every one for an administrator.</summary>
    private static bool CanRead(AccessContext ctx, ImportBatch batch) =>
        ctx.IsFullAdmin || batch.ConfirmedByUserId == ctx.UserId;

    /// <summary>
    /// Undoing is the confirmer's own act, or an administrator's. It is not derived from write
    /// rights on the objects: a batch reverts as a unit, and a caller who may edit some of what
    /// it created must not be able to delete the rest of it in one press.
    /// </summary>
    private static bool CanRevert(AccessContext ctx, ImportBatch batch) =>
        !batch.IsReverted && (ctx.IsFullAdmin || batch.ConfirmedByUserId == ctx.UserId);

    private static async Task<Dictionary<Guid, string>> GeofileNamesAsync(
        SilexGisDbContext db, IReadOnlyList<ImportBatch> batches, CancellationToken ct)
    {
        var ids = batches.Where(b => b.GeofileId is not null).Select(b => b.GeofileId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Geofiles.AsNoTracking()
            .Where(g => ids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);
    }

    private static JsonElement ParseProperties(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}

/// <summary>One confirmation with the lines it produced.</summary>
public sealed record ImportBatchDetailDto(ImportBatchDto Batch, IReadOnlyList<ImportBatchItemDto> Items);
