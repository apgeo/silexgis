// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.ResLinks;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.PlaceStations;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The line-plot survey models of the caves a set carries — Therion <c>.lox</c> and Survex <c>.3d</c>
/// — read in pages like the features themselves, with the places phones mapped to their stations.
/// </summary>
/// <remarks>
/// <para>
/// A line plot is the cave's survey in georeferenced coordinates: holding one is holding the cave's
/// position. So a model is carried only when its cave is one the feature stream would hand this
/// reader right now — inside the set, readable, and not withheld, which for a protected cave means
/// with its exact position open. Both questions are put to the one answer every stream reads
/// against, never decided here.
/// </para>
/// <para>
/// Unlike the other streams, a model this reader stops being handed is told as a tombstone, not left
/// to fall silent: a phone keeping the file of a cave whose exact position it may no longer see is
/// keeping that position. What can be told is what wrote a row after the device's position: the
/// model removed, its cave protected or moved under a protection (which restamps the cave), its cave
/// deleted. A change to the reader's own rights writes no row, so it reaches a device on its next
/// read from the beginning — which lists every model the reader may hold, so a device drops the
/// models such a read did not list.
/// </para>
/// <para>
/// The places mapped to stations are provisional, like the pins on phones' maps: the whole pin,
/// marker and map sync is to be analysed again.
/// </para>
/// </remarks>
public static class SyncSurveyModelEndpoints
{
    /// <summary>
    /// A model changed when it was itself last written — renamed, made current or no longer current
    /// — or when its cave was, whichever is later. The cave's time carries a model to a phone that
    /// already read past it when the cave comes into what the set hands this reader through a change
    /// to its own row alone.
    /// </summary>
    private static readonly SyncStreamOrder<SyncSurveyModelRow> ByChange = new(
        r => r.Model.UpdatedAt > r.CaveUpdatedAt ? r.Model.UpdatedAt : r.CaveUpdatedAt,
        r => r.Model.Id);

    private static readonly SyncStreamOrder<SyncSurveyModelGone> ByGone = new(g => g.At, g => g.Id);

    private static readonly SyncStreamOrder<SyncPlaceStationEntry> ByStation = new(s => s.ChangedAt, s => s.Row.Id);

    private static readonly SyncStreamOrder<SyncPlaceStationGone> ByStationGone = new(g => g.At, g => g.Id);

    /// <summary>Whether a model of this format is handed to phones: the two line-plot formats, never a mesh.</summary>
    internal static bool Listed(SurveyModelFormat format) =>
        format is SurveyModelFormat.Lox or SurveyModelFormat.Survex3d;

    public static void MapSyncSurveyModelEndpoints(this RouteGroupBuilder sets)
    {
        sets.MapGet("/{id:guid}/survey-models", SurveyModelsAsync)
            .WithName("syncSurveyModels")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("One page of the line-plot survey models of a sync set's caves, the places mapped to their stations, and what has gone.");
    }

    private static async Task<Results<Ok<SyncSurveyModelsPageDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        SurveyModelsAsync(
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
            db, ctx, SyncStream.SurveyModels, id, cursor, pageSize, options.Value, ct);
        if (opening is null)
        {
            return problem!;
        }

        if (opening.Roots.Count == 0)
        {
            return TypedResults.Ok(new SyncSurveyModelsPageDto(
                opening.Set.Revision, opening.Settings, [], [], [], [], opening.Cursor, false));
        }

        var readable = SyncScope.Readable(db, ctx, opening.Roots);
        var handed = await SyncScope.WithoutWithheldAsync(readable, protection, ctx, ct);

        var live = SyncStreamPaging.After(Carried(db, handed), ByChange, opening.From);
        var liveRows = await SyncStreamPaging.Probe(live, ByChange, opening).AsNoTracking().ToListAsync(ct);

        var goneRows = new List<SyncSurveyModelGone>();
        foreach (var source in Gone(db, ctx, opening.Roots, readable, handed))
        {
            goneRows.AddRange(await SyncStreamPaging.Probe(
                SyncStreamPaging.After(source, ByGone, opening.From), ByGone, opening).ToListAsync(ct));
        }

        var (onModels, onDocuments) = await StationsAsync(db, ctx, handed, ct);
        var stationRows = new List<SyncPlaceStationEntry>();
        var stationGoneRows = new List<SyncPlaceStationGone>();
        foreach (var source in new[] { onModels, onDocuments })
        {
            stationRows.AddRange(await SyncStreamPaging.Probe(
                SyncStreamPaging.After(source.Where(s => s.Row.DeletedAt == null), ByStation, opening.From),
                ByStation,
                opening).ToListAsync(ct));
            var removed = source.Where(s => s.Row.DeletedAt != null)
                .Select(s => new SyncPlaceStationGone { Id = s.Row.Id, At = s.Row.DeletedAt!.Value });
            stationGoneRows.AddRange(await SyncStreamPaging.Probe(
                SyncStreamPaging.After(removed, ByStationGone, opening.From), ByStationGone, opening).ToListAsync(ct));
        }

        var cut = SyncStreamPaging.Cut(
            opening,
            liveRows.Select(ByChange.Entry),
            goneRows.Select(ByGone.Entry),
            stationRows.Select(ByStation.Entry),
            stationGoneRows.Select(ByStationGone.Entry));

        return TypedResults.Ok(new SyncSurveyModelsPageDto(
            opening.Set.Revision,
            opening.Settings,
            [.. cut.Rows.OfType<SyncSurveyModelRow>().Select(Shape)],
            [.. cut.Rows.OfType<SyncSurveyModelGone>().Select(g => new SyncDeletedRowDto(g.Id, true, g.At))],
            [.. cut.Rows.OfType<SyncPlaceStationEntry>().Select(s => Shape(s.Row)).OfType<SyncPlaceStationDto>()],
            [.. cut.Rows.OfType<SyncPlaceStationGone>().Select(g => new SyncDeletedRowDto(g.Id, true, g.At))],
            cut.NextCursor,
            cut.HasMore));
    }

    /// <summary>
    /// The models the stream carries for a reader: the line plots of the caves in
    /// <paramref name="handed"/>, each with the file it is. Every question about which models, and
    /// so which of their stations, a reader is given starts here.
    /// </summary>
    internal static IQueryable<SyncSurveyModelRow> Carried(SilexGisDbContext db, IQueryable<Feature> handed) =>
        from model in db.SurveyModels
        where model.Format == SurveyModelFormat.Lox || model.Format == SurveyModelFormat.Survex3d
        join cave in handed on model.CaveFeatureId equals cave.Id
        join file in db.StoredFiles on model.FileId equals file.Id
        join version in db.DocumentVersions on file.DocumentVersionId equals version.Id
        select new SyncSurveyModelRow
        {
            Model = model,
            DocumentId = version.DocumentId,
            VersionId = version.Id,
            SizeBytes = file.SizeBytes,
            Sha256 = file.Sha256,
            CaveUpdatedAt = cave.UpdatedAt,
        };

    /// <summary>A model as the stream hands it.</summary>
    internal static SyncSurveyModelDto Shape(SyncSurveyModelRow row) => new(
        row.Model.Id,
        row.Model.CaveFeatureId,
        row.DocumentId,
        row.VersionId,
        row.Model.Name,
        row.Model.Format == SurveyModelFormat.Lox ? SyncSurveyModelFormat.Lox : SyncSurveyModelFormat.Survex3d,
        row.Model.IsCurrent,
        row.SizeBytes,
        row.Sha256,
        row.Model.UpdatedAt);

    /// <summary>
    /// The three ways a model leaves a reader, each as identifiers and moments that locate nothing:
    /// removed (the database's removal log, for caves the reader may read in the set); no longer
    /// handed because its cave is withheld now, stamped when the model or the cave last changed;
    /// and gone with its cave, stamped when the cave was deleted.
    /// </summary>
    private static IQueryable<SyncSurveyModelGone>[] Gone(
        SilexGisDbContext db,
        AccessContext ctx,
        IReadOnlyCollection<Guid> roots,
        IQueryable<Feature> readable,
        IQueryable<Feature> handed)
    {
        var readableIds = readable.Select(f => f.Id);
        var handedIds = handed.Select(f => f.Id);
        var removed = db.SyncSurveyModelRemovals
            .Where(r => readableIds.Contains(r.CaveFeatureId))
            .Select(r => new SyncSurveyModelGone { Id = r.SurveyModelId, At = r.RemovedAt });
        var withheld =
            from model in db.SurveyModels
            where model.Format == SurveyModelFormat.Lox || model.Format == SurveyModelFormat.Survex3d
            join cave in readable on model.CaveFeatureId equals cave.Id
            where !handedIds.Contains(cave.Id)
            select new SyncSurveyModelGone
            {
                Id = model.Id,
                At = model.UpdatedAt > cave.UpdatedAt ? model.UpdatedAt : cave.UpdatedAt,
            };
        var withCave =
            from cave in SyncScope.Gone(db, ctx, roots)
            join model in db.SurveyModels on cave.Id equals model.CaveFeatureId
            where model.Format == SurveyModelFormat.Lox || model.Format == SurveyModelFormat.Survex3d
            select new SyncSurveyModelGone { Id = model.Id, At = cave.DeletedAt!.Value };
        return [removed, withheld, withCave];
    }

    /// <summary>
    /// The places mapped to stations that the stream carries for a reader, live and removed — one
    /// query for mappings into models it carries, one for mappings into survey files the documents
    /// stream carries — each only for a place in <paramref name="handed"/>. A mapping changed when
    /// it, its place, or what hands it its survey last did.
    /// </summary>
    internal static async Task<(IQueryable<SyncPlaceStationEntry> OnModels, IQueryable<SyncPlaceStationEntry> OnDocuments)>
        StationsAsync(SilexGisDbContext db, AccessContext ctx, IQueryable<Feature> handed, CancellationToken ct)
    {
        var mappings = PlaceStationQueries.Rows(db);
        var onModels =
            from r in mappings
            where r.SurveyModelId != null
            join place in handed on r.PlaceId equals place.Id
            join model in Carried(db, handed) on r.SurveyModelId equals (Guid?)model.Model.Id
            let mappingAt = r.UpdatedAt > place.UpdatedAt ? r.UpdatedAt : place.UpdatedAt
            let modelAt = model.Model.UpdatedAt > model.CaveUpdatedAt ? model.Model.UpdatedAt : model.CaveUpdatedAt
            select new SyncPlaceStationEntry { Row = r, ChangedAt = mappingAt > modelAt ? mappingAt : modelAt };

        var (carriedLinks, _, _) = await SyncDocumentEndpoints.CarriedAsync(db, ctx, handed, ct);
        var onDocuments =
            from r in mappings
            where r.DocumentId != null
            join place in handed on r.PlaceId equals place.Id
            let documentAt = carriedLinks
                .Where(x => x.Document.Id == r.DocumentId)
                .Select(x => (DateTimeOffset?)(x.Document.UpdatedAt > x.TargetUpdatedAt ? x.Document.UpdatedAt : x.TargetUpdatedAt))
                .Max()
            where documentAt != null
            let mappingAt = r.UpdatedAt > place.UpdatedAt ? r.UpdatedAt : place.UpdatedAt
            select new SyncPlaceStationEntry
            {
                Row = r,
                ChangedAt = mappingAt > documentAt!.Value ? mappingAt : documentAt.Value,
            };
        return (onModels, onDocuments);
    }

    /// <summary>A mapping as the stream hands it; null for a payload that is not a station.</summary>
    internal static SyncPlaceStationDto? Shape(PlaceStationRow row) =>
        PlaceStationAnchor.Read(row.Anchor) is { } station
            ? new SyncPlaceStationDto(
                row.Id, row.PlaceId, row.SurveyModelId, row.DocumentId, station.Station, station.Splay, row.UpdatedAt)
            : null;
}

/// <summary>One model of the survey-models stream before it is cut, with its file and its cave's last change.</summary>
internal sealed class SyncSurveyModelRow
{
    public required SurveyModel Model { get; init; }

    public required Guid DocumentId { get; init; }

    public required Guid VersionId { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public required DateTimeOffset CaveUpdatedAt { get; init; }
}

/// <summary>A model a reader is to drop, as the survey-models stream tells it.</summary>
internal sealed class SyncSurveyModelGone
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>One mapping of a place to a station, with the moment it last changed for this reader.</summary>
internal sealed class SyncPlaceStationEntry
{
    public required PlaceStationRow Row { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }
}

/// <summary>A mapping removed, as the survey-models stream tells it.</summary>
internal sealed class SyncPlaceStationGone
{
    public Guid Id { get; init; }

    public DateTimeOffset At { get; init; }
}
