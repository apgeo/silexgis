// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>What a reviewer decided about one held row, as the device is told it.</summary>
public enum SyncReviewOutcome
{
    /// <summary>Written, as the device sent it or in part; <c>revision</c> is the row as it now stands.</summary>
    Accepted,

    /// <summary>Turned down; nothing was written.</summary>
    Rejected,

    /// <summary>Replaced by a later upload of the same row, which waits in its place.</summary>
    Superseded,

    /// <summary>Accepted by a reviewer but refused when it was written — the account may no longer
    /// make that change, or what it depended on was not written.</summary>
    Failed,
}

/// <summary>
/// One decision on a row the caller's phone sent while the account was under review.
/// </summary>
/// <param name="ReviewId">The id the upload answered the row with.</param>
/// <param name="Entity">The upload section the row came in: <c>rows</c>, <c>people</c>,
/// <c>documents</c>, <c>documentLinks</c>, <c>tripRecordings</c> or <c>tripPoints</c>.</param>
/// <param name="RowId">The row's id as the upload answered it (for a document link, the document's).</param>
/// <param name="Note">What the reviewer wrote to the uploader, if anything.</param>
/// <param name="Revision">The revision the server holds the row at now; absent when it holds no such
/// row this caller may read (a create that was not written, say).</param>
/// <param name="Code">Why a <see cref="SyncReviewOutcome.Failed"/> row could not be written — the code
/// the upload itself would have answered.</param>
/// <param name="Current">For a turned-down or failed edit or removal of a feature, the server's row
/// shaped as the feature stream would deliver it; absent when this caller may not be given it.</param>
public sealed record SyncReviewDto(
    Guid ReviewId,
    string Entity,
    Guid RowId,
    SyncReviewOutcome Outcome,
    DateTimeOffset DecidedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? Revision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncFeatureDto? Current);

/// <summary>One page of the reviews stream, in decision order.</summary>
public sealed record SyncReviewsPageDto(
    long SetRevision,
    JsonElement Settings,
    IReadOnlyList<SyncReviewDto> Reviews,
    string? NextCursor,
    bool HasMore);

/// <summary>
/// The decisions reviewers made about what the caller's phones sent while the account was under
/// review, read in pages like every other stream.
/// </summary>
/// <remarks>
/// <para>
/// The stream is the caller's own: decisions on rows this account uploaded, through any of its
/// sets — an upload's identity belongs to the account, not to the set it was sent through, and a
/// phone told a row is held must learn its fate wherever it reads from. It is read through a set
/// because every stream is, and a cursor carries the set's revision like any other.
/// </para>
/// <para>
/// A decision changes nothing the other streams do not already carry: an accepted row reaches the
/// phone through its own stream as any written row does. What this stream adds is what no other
/// can say — that a row was turned down, replaced, or could not be written — and for a turned-down
/// edit of a feature, the server's row, so the phone can put back what it still shows. That row
/// passes the one withhold rule exactly as the upload's conflict echo does, and is absent rather
/// than blurred when this caller may not be given it.
/// </para>
/// </remarks>
public static class SyncReviewEndpoints
{
    private static readonly SyncStreamOrder<SyncReviewRow> ByDecision = new(r => r.DecidedAt, r => r.ReviewId);

    public static void MapSyncReviewEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapGet("/{id:guid}/reviews", ReviewsAsync)
            .WithName("syncReviews")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("One page of the decisions reviewers made about rows the caller's phones sent while the account was under review.");
    }

    private static async Task<Results<Ok<SyncReviewsPageDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        ReviewsAsync(
            Guid id,
            string? cursor,
            int? pageSize,
            SilexGisDbContext db,
            FeatureProtection protection,
            IAccessContextAccessor accessAccessor,
            IOptions<SyncOptions> options,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var (opening, problem) = await SyncStreamPaging.OpenAsync(
            db, ctx, SyncStream.Reviews, id, cursor, pageSize, options.Value, ct);
        if (opening is null)
        {
            return problem!;
        }

        var decided =
            from item in db.ImportBatchItems.AsNoTracking()
            join batch in db.ImportBatches.AsNoTracking() on item.ImportBatchId equals batch.Id
            where batch.ConfirmedByUserId == ctx.UserId
                && batch.Source == ImportSource.DeviceSync
                && item.ReviewId != null
                && item.DecidedAt != null
                && item.ProposalSection != null
                && item.ProposalRowId != null
                && item.ReviewDecision != null
                && item.ReviewDecision != ImportReviewDecision.Pending
            select new SyncReviewRow
            {
                ReviewId = item.ReviewId!.Value,
                DecidedAt = item.DecidedAt!.Value,
                Section = item.ProposalSection!,
                RowId = item.ProposalRowId!.Value,
                Decision = item.ReviewDecision!.Value,
                Action = item.Action,
                Note = item.DecisionNote,
                FailureCode = item.ReviewFailureCode,
                LinkProposal = item.ProposalSection == SyncUploadSections.DocumentLinks ? item.Proposal : null,
            };
        decided = SyncStreamPaging.After(decided, ByDecision, opening.From);
        var probed = await SyncStreamPaging.Probe(decided, ByDecision, opening.Size).ToListAsync(ct);
        var cut = SyncStreamPaging.Cut(opening, probed.Select(ByDecision.Entry));
        var rows = cut.Rows.OfType<SyncReviewRow>().ToList();

        var revisions = await RevisionsAsync(db, ctx, rows, ct);

        // A turned-down edit or removal of a feature is the one decision that leaves the phone
        // showing something the server does not hold, so it is the one answered with the server's
        // row. A create turned down has no server row to send; the phone drops its own.
        var echoed = rows
            .Where(r => r.Section == SyncUploadSections.Rows
                && r.Action != ImportDecisionAction.Create
                && r.Decision is ImportReviewDecision.Rejected or ImportReviewDecision.Failed)
            .Select(r => r.RowId)
            .Distinct()
            .ToList();
        var current = (await SyncFeatureShaping.CurrentAsync(db, protection, ctx, echoed, ct))
            .ToDictionary(f => f.Id);

        return TypedResults.Ok(new SyncReviewsPageDto(
            opening.Set.Revision,
            opening.Settings,
            [.. rows.Select(r => new SyncReviewDto(
                r.ReviewId,
                r.Section,
                AnswerIdOf(r),
                OutcomeOf(r.Decision),
                r.DecidedAt,
                r.Note,
                revisions.TryGetValue((r.Section, r.RowId), out var revision) ? revision : null,
                r.Decision == ImportReviewDecision.Failed ? r.FailureCode : null,
                r.Section == SyncUploadSections.Rows && echoed.Contains(r.RowId)
                    ? current.GetValueOrDefault(r.RowId)
                    : null))],
            cut.NextCursor,
            cut.HasMore));
    }

    private static SyncReviewOutcome OutcomeOf(ImportReviewDecision decision) => decision switch
    {
        ImportReviewDecision.Accepted => SyncReviewOutcome.Accepted,
        ImportReviewDecision.Rejected => SyncReviewOutcome.Rejected,
        ImportReviewDecision.Superseded => SyncReviewOutcome.Superseded,
        ImportReviewDecision.Failed => SyncReviewOutcome.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "A waiting row has no outcome."),
    };

    /// <summary>
    /// The id the upload answered the row with. A document link has no id of its own the phone
    /// chose, and was answered with its document's; every other row with its own.
    /// </summary>
    private static Guid AnswerIdOf(SyncReviewRow row)
    {
        if (row.LinkProposal is { } proposal
            && JsonSerializer.Deserialize<SyncLinkProposal>(proposal, SyncUploadJson.Options) is { } link)
        {
            return link.Row.DocumentId;
        }

        return row.RowId;
    }

    /// <summary>
    /// The revision each page row's server row stands at now, for the rows this caller may read —
    /// each kind asked through the read rule its own stream uses. A row the server does not hold,
    /// or that this caller may not read, has none.
    /// </summary>
    private static async Task<Dictionary<(string Section, Guid RowId), DateTimeOffset>> RevisionsAsync(
        SilexGisDbContext db, AccessContext ctx, IReadOnlyList<SyncReviewRow> rows, CancellationToken ct)
    {
        var found = new Dictionary<(string, Guid), DateTimeOffset>();
        List<Guid> IdsOf(string section) => [.. rows.Where(r => r.Section == section).Select(r => r.RowId).Distinct()];

        var features = IdsOf(SyncUploadSections.Rows);
        if (features.Count > 0)
        {
            foreach (var f in await db.Features.AsNoTracking()
                         .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                         .Where(f => features.Contains(f.Id))
                         .Select(f => new { f.Id, f.UpdatedAt })
                         .ToListAsync(ct))
            {
                found[(SyncUploadSections.Rows, f.Id)] = f.UpdatedAt;
            }
        }

        var documents = IdsOf(SyncUploadSections.Documents);
        if (documents.Count > 0)
        {
            var readable = await SyncDocuments.ReadableAsync(
                db, ctx, db.Documents.AsNoTracking().Where(d => documents.Contains(d.Id)), ct);
            foreach (var d in await readable.Select(d => new { d.Id, d.UpdatedAt }).ToListAsync(ct))
            {
                found[(SyncUploadSections.Documents, d.Id)] = d.UpdatedAt;
            }
        }

        var recordings = IdsOf(SyncUploadSections.TripRecordings);
        if (recordings.Count > 0)
        {
            foreach (var r in await db.TripRecordings.AsNoTracking()
                         .VisibleTo(ctx, AccessDomain.TripLogs)
                         .Where(r => recordings.Contains(r.Id))
                         .Select(r => new { r.Id, r.UpdatedAt })
                         .ToListAsync(ct))
            {
                found[(SyncUploadSections.TripRecordings, r.Id)] = r.UpdatedAt;
            }
        }

        var points = IdsOf(SyncUploadSections.TripPoints);
        if (points.Count > 0)
        {
            var readableRecordings = db.TripRecordings.AsNoTracking().VisibleTo(ctx, AccessDomain.TripLogs);
            foreach (var p in await db.TripRecordingPoints.AsNoTracking()
                         .Where(p => points.Contains(p.Id) && readableRecordings.Any(r => r.Id == p.RecordingId))
                         .Select(p => new { p.Id, p.UpdatedAt })
                         .ToListAsync(ct))
            {
                found[(SyncUploadSections.TripPoints, p.Id)] = p.UpdatedAt;
            }
        }

        // A raster map is read through its cave, and a pin through its map's cave and its place,
        // as the rasters stream reads them.
        var mapIds = IdsOf(SyncUploadSections.RasterMaps);
        var pinIds = IdsOf(SyncUploadSections.RasterPins);
        if (mapIds.Count > 0 || pinIds.Count > 0)
        {
            var pins = pinIds.Count == 0
                ? []
                : await db.RasterPins.AsNoTracking()
                    .Where(p => pinIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.RasterMapId, p.PlaceFeatureId, p.UpdatedAt })
                    .ToListAsync(ct);
            var wanted = mapIds.Concat(pins.Select(p => p.RasterMapId)).Distinct().ToList();
            var maps = await db.RasterMaps.AsNoTracking()
                .Where(m => wanted.Contains(m.Id))
                .Select(m => new { m.Id, m.CaveFeatureId, m.UpdatedAt })
                .ToListAsync(ct);
            var asked = maps.Select(m => m.CaveFeatureId).Concat(pins.Select(p => p.PlaceFeatureId)).Distinct().ToList();
            var visible = (await db.Features.AsNoTracking()
                    .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                    .Where(f => asked.Contains(f.Id))
                    .Select(f => f.Id)
                    .ToListAsync(ct))
                .ToHashSet();
            var readableMaps = maps.Where(m => visible.Contains(m.CaveFeatureId)).ToDictionary(m => m.Id);
            foreach (var m in readableMaps.Values.Where(m => mapIds.Contains(m.Id)))
            {
                found[(SyncUploadSections.RasterMaps, m.Id)] = m.UpdatedAt;
            }

            foreach (var p in pins.Where(p => readableMaps.ContainsKey(p.RasterMapId) && visible.Contains(p.PlaceFeatureId)))
            {
                found[(SyncUploadSections.RasterPins, p.Id)] = p.UpdatedAt;
            }
        }

        return found;
    }
}

/// <summary>One decided line of the reviews stream before it is cut.</summary>
internal sealed class SyncReviewRow
{
    public Guid ReviewId { get; init; }

    public DateTimeOffset DecidedAt { get; init; }

    public string Section { get; init; } = "";

    public Guid RowId { get; init; }

    public ImportReviewDecision Decision { get; init; }

    public ImportDecisionAction Action { get; init; }

    public string? Note { get; init; }

    public string? FailureCode { get; init; }

    /// <summary>A document link's kept proposal, read only to name the document it answered with.</summary>
    public string? LinkProposal { get; init; }
}
