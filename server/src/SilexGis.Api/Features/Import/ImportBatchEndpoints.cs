// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
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
            .WithValidation<ImportBatchRevertRequest>()
            .WithSummary("Takes the confirmation back: what it created is deleted; for a phone upload, what it edited and deleted returns too.");

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
            .ThenByDescending(b => b.Id)
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

        // A held line is a proposal, not something the batch made: it is listed by the review
        // routes until a reviewer accepts it, and only then becomes a line of what was imported.
        var items = await db.ImportBatchItems.AsNoTracking()
            .Where(i => i.ImportBatchId == batch.Id
                && (i.ReviewDecision == null || i.ReviewDecision == ImportReviewDecision.Accepted))
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

        // The same for trips, and read the same way — past the filter that hides deleted rows.
        // After an undo every trip of the batch is deleted and still there, and this page is
        // where somebody asks what the import had made: the line says the trip is deleted, and
        // the reader is sent to where it can be put back. Still only what the caller may read.
        // A trip removed for good is the one case with nothing to look up; the line's pointer
        // went null with it, and what the line recorded of the source row answers instead.
        var tripIds = items.Where(i => i.TripLogId is not null).Select(i => i.TripLogId!.Value).ToList();
        var trips = tripIds.Count == 0
            ? []
            : await db.TripLogs.AsNoTracking()
                .IgnoreQueryFilters()
                .VisibleTo(ctx, AccessDomain.TripLogs)
                .Where(t => tripIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Title, t.DeletedAt })
                .ToDictionaryAsync(t => t.Id, ct);

        // Only a trip spreadsheet's lines carry a recorded trip title. Every other source stores
        // the source row's own attributes here verbatim, so a file whose rows happen to carry a
        // "title" attribute would otherwise be read back as the title of a trip that does not
        // exist — and would hand the reader a name the line above deliberately withheld.
        var recordsTripTitles = batch.Source == ImportSource.TripCsv;

        var names = await GeofileNamesAsync(db, [batch], ct);
        return TypedResults.Ok(new ImportBatchDetailDto(
            StagedImportEndpoints.ToDto(
                batch, batch.GeofileId is { } gid ? names.GetValueOrDefault(gid) : null, CanRevert(ctx, batch)),
            [
                .. items.Select(i =>
                {
                    var feature = i.FeatureId is { } fid ? visible.GetValueOrDefault(fid) : null;
                    var trip = i.TripLogId is { } tid ? trips.GetValueOrDefault(tid) : null;
                    var attached = i.AttachedToFeatureId is { } aid ? visible.GetValueOrDefault(aid) : null;
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
                        i.Action,
                        i.TripLogId,
                        trip?.Title ?? (recordsTripTitles ? RecordedTitle(i.SourceProperties) : null),
                        trip?.DeletedAt is not null,
                        i.UndoneAt,
                        attached?.Name,
                        attached?.DeletedAt is not null);
                })
            ]));
    }

    /// <summary>
    /// The title the line recorded of the row it was written from, if it recorded one. Read out
    /// of the stored source rather than out of the object, because after an undo the object is
    /// the one thing that is not there any more.
    ///
    /// <para>
    /// Only ever asked of a trip spreadsheet's lines. What a line stores here is whatever its
    /// source was: for a trip row it is the parsed row, whose title is the trip's; for a vector
    /// or photo import it is the source object's own attributes, where a "title" is the file
    /// author's word for something else entirely.
    /// </para>
    /// </summary>
    private static string? RecordedTitle(string json)
    {
        var properties = ParseProperties(json);
        if (properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty("title", out var title)
            || title.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = title.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Takes a confirmation back. A file import's objects are deleted as one unit, as they always
    /// were. A phone upload is undone truly: the rows it created are deleted, the rows it deleted
    /// come back, and every field it changed returns to its value before the upload — unless
    /// somebody has changed that field since, which leaves it and says so (<c>force</c> puts it back
    /// anyway). A phone upload can also be taken back a few lines at a time (<c>itemIds</c>); it
    /// counts as reverted once every line has been.
    /// </summary>
    private static async Task<Results<Ok<ImportBatchRevertResultDto>, UnauthorizedHttpResult, ProblemHttpResult>> RevertAsync(
        Guid id,
        ImportBatchRevertRequest? request,
        SilexGisDbContext db,
        ImportCommitService commitService,
        DeviceSyncUndoService syncUndo,
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

        // A batch held for review wrote nothing of what it proposes until a reviewer accepted it,
        // so there is nothing to take back while any of it waits or when all of it was turned
        // down; what it holds is decided through review, not undone. Once decided with something
        // accepted, what was accepted is undone like any upload.
        if (!IsUndoable(batch))
        {
            return ApiProblems.Conflict(
                HeldCode, "This import is held for review; it is decided by a reviewer, not undone.");
        }

        if (!CanRevert(ctx, batch))
        {
            return batch.IsReverted
                ? ApiProblems.BadRequest("import_batch.already_reverted", "This import has already been undone.")
                : ApiProblems.Forbidden("import_batch.revert_forbidden");
        }

        IReadOnlyList<ImportBatchUndoItemDto> lines = [];
        if (batch.Source == ImportSource.DeviceSync)
        {
            try
            {
                var undone = await syncUndo.UndoAsync(batch, ctx, request?.ItemIds, request?.Force ?? false, ct);
                lines = [.. undone.Select(r => new ImportBatchUndoItemDto(
                    r.ItemId,
                    r.FeatureId,
                    r.Change,
                    r.Outcome,
                    r.Reason,
                    [.. r.Applied],
                    [.. r.Skipped.Select(s => new ImportBatchUndoSkippedDto(s.Field, s.Reason))],
                    r.DevicePersonId,
                    r.DocumentId,
                    r.TripRecordingId,
                    r.TripRecordingPointId,
                    r.TripLogId,
                    r.TripOutcome,
                    r.TripReason,
                    r.RasterMapId,
                    r.RasterPinId))];
            }
            catch (SyncUndoException e)
            {
                return ApiProblems.BadRequest(e.Code, e.Message);
            }
        }
        else if (request?.ItemIds is { Length: > 0 })
        {
            return ApiProblems.BadRequest(
                "import_batch.partial_undo_unsupported",
                "Only an upload from a phone can be taken back a few lines at a time; a file import is undone whole.");
        }
        else
        {
            await commitService.RevertAsync(batch, ctx.UserId, ct);
        }

        var names = await GeofileNamesAsync(db, [batch], ct);
        return TypedResults.Ok(new ImportBatchRevertResultDto(
            StagedImportEndpoints.ToDto(
                batch, batch.GeofileId is { } gid ? names.GetValueOrDefault(gid) : null, CanRevert(ctx, batch)),
            lines));
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

        // The row an updating line changed is named only if the caller may see it, as in a batch's
        // own detail.
        var attached = item.AttachedToFeatureId is { } aid
            ? await db.Features.AsNoTracking()
                .IgnoreQueryFilters()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => f.Id == aid)
                .Select(f => new { f.Name, f.DeletedAt })
                .FirstOrDefaultAsync(ct)
            : null;

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
                item.Action,
                // This route is reached through a feature, so the line it answers with is a
                // feature's line and carries no trip.
                null,
                null,
                false,
                item.UndoneAt,
                attached?.Name,
                attached?.DeletedAt is not null),
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
        !batch.IsReverted && IsUndoable(batch) && (ctx.IsFullAdmin || batch.ConfirmedByUserId == ctx.UserId);

    private static bool IsUndoable(ImportBatch batch) =>
        batch.ReviewState is ImportReviewState.NotHeld or ImportReviewState.Accepted or ImportReviewState.PartlyAccepted;

    /// <summary>An undo was asked of a batch held for review.</summary>
    public const string HeldCode = "import_batch.held";

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

/// <summary>What to take back of a confirmation. Every field is optional; an empty body takes back all of it.</summary>
/// <param name="ItemIds">
/// Phone uploads only: the lines to take back, leaving the rest in force. All of them when omitted.
/// </param>
/// <param name="Force">
/// Phone uploads only: put a field back even when somebody has changed it since the upload, and
/// bring a row back even when somebody deleted it again since. Never past the protection rules: a
/// position still goes back only for a caller who may see it exactly.
/// </param>
public sealed record ImportBatchRevertRequest(long[]? ItemIds, bool? Force);

public sealed class ImportBatchRevertRequestValidator : AbstractValidator<ImportBatchRevertRequest>
{
    public ImportBatchRevertRequestValidator()
    {
        RuleFor(x => x.ItemIds)
            .Must(ids => ids is null || ids.Length <= 10_000)
            .WithMessage("At most 10000 lines at a time.");
        RuleForEach(x => x.ItemIds).GreaterThan(0);
    }
}

/// <summary>A field of an edited row an undo left as it is.</summary>
/// <param name="Field">The field, as the history names it.</param>
/// <param name="Reason">superseded | forbidden | redacted | no_pre_image | not_restorable.</param>
public sealed record ImportBatchUndoSkippedDto(string Field, string Reason);

/// <summary>What an undo did with one line of a phone upload.</summary>
/// <param name="ItemId">The line.</param>
/// <param name="FeatureId">The row; null when it has since been removed for good.</param>
/// <param name="Change">What the upload did to the row: created | updated | deleted; null when nothing says any more.</param>
/// <param name="Outcome">undone | partly | not_undone.</param>
/// <param name="Reason">
/// For not_undone: already_undone | purged | container_deleted | superseded | fields_left |
/// no_history | forbidden, or the write's own code when putting the values back was refused.
/// </param>
/// <param name="Applied">The fields of an edited row put back.</param>
/// <param name="Skipped">The fields of an edited row left as they are.</param>
/// <param name="DevicePersonId">For a line about one of the phone's users rather than a row: that phone user.</param>
/// <param name="DocumentId">
/// For a line about a photo or document the phone sent, or about where it hung one: that document.
/// Such a line's <c>Applied</c> names <c>file</c> when the bytes stepped back to the revision before
/// the upload's, the fields put back, or the links taken down or hung again.
/// </param>
/// <param name="TripRecordingId">For a line about a phone recording: that recording.</param>
/// <param name="TripRecordingPointId">For a line about one scan of a phone recording: that scan.</param>
/// <param name="RasterMapId">For a line about a phone's raster map: that map.</param>
/// <param name="RasterPinId">For a line about one pin on a raster map: that pin.</param>
/// <param name="TripLogId">
/// For a recording line whose recording had been made into a trip report: that report — null when the
/// caller may not read it, while the outcome is still given.
/// </param>
/// <param name="TripOutcome">What became of the report: undone (deleted with its recording, or with the change that closed it) | not_undone.</param>
/// <param name="TripReason">Why the report was left: trip_changed | converted_elsewhere.</param>
public sealed record ImportBatchUndoItemDto(
    long ItemId,
    Guid? FeatureId,
    string? Change,
    string Outcome,
    string? Reason,
    string[] Applied,
    ImportBatchUndoSkippedDto[] Skipped,
    Guid? DevicePersonId = null,
    Guid? DocumentId = null,
    Guid? TripRecordingId = null,
    Guid? TripRecordingPointId = null,
    Guid? TripLogId = null,
    string? TripOutcome = null,
    string? TripReason = null,
    Guid? RasterMapId = null,
    Guid? RasterPinId = null);

/// <summary>
/// The answer to taking a confirmation back: the batch as it now stands, and for a phone upload
/// what happened to each line asked for (empty for any other kind of confirmation, which is
/// undone whole).
/// </summary>
public sealed record ImportBatchRevertResultDto(ImportBatchDto Batch, IReadOnlyList<ImportBatchUndoItemDto> Items);
