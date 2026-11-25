// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Access;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Flybys;

/// <summary>
/// A photograph taken at a station of a cave's survey, as far as a render bundle needs it: which
/// picture, and where its station is.
/// </summary>
/// <param name="FileId">The image file the photograph's document serves now.</param>
/// <param name="QuarterTurns">The turn somebody recorded for the picture.</param>
internal sealed record StationPhotograph(
    Guid DocumentId,
    Guid FileId,
    string StoragePath,
    int QuarterTurns,
    double Longitude,
    double Latitude,
    double AltitudeM);

/// <summary>
/// The photographs taken at the stations of a cave, read for one caller — the ones the 3D view
/// marks at those stations for the same caller, by the same statements.
///
/// <para>
/// <b>A photograph in a cave has no place of its own; somebody said where it was taken.</b> That
/// statement is a link naming a station of one of the cave's line plots and the photograph. It is
/// read here under every rule the links of a survey model are listed by: only links that stand,
/// none that a place pinned or mapped on a phone hides whole from this caller, and of a link's
/// members only the documents this caller may read whose current file is an image. A photograph
/// that fails any of it is not there — not counted, not named, and its bytes never opened.
/// </para>
///
/// <para>
/// <b>Whoever calls this has already decided the cave</b>: that the caller may read it and may
/// place it exactly. That is the rule a survey model's links and its stations are answered by,
/// and it is not asked again here.
/// </para>
/// </summary>
internal static class FlybyBundlePhotoSource
{
    /// <summary>How many of one model's links are read: the bound the 3D view reads them by.</summary>
    public const int MaxLinksAModel = 200;

    /// <summary>
    /// The first photograph at each station of the cave's line plots, and how many photographs at
    /// those stations there are in all.
    /// </summary>
    public static async Task<(IReadOnlyList<StationPhotograph> First, int All)> ForCaveAsync(
        SilexGisDbContext db,
        IAccessService access,
        PlacePointDisclosure placePoints,
        PlaceStationDisclosure placeStations,
        AccessContext ctx,
        Guid caveId,
        CancellationToken ct)
    {
        // A line plot that has been read has stations; a mesh has none.
        var models = await db.SurveyModels.AsNoTracking()
            .Where(m => m.CaveFeatureId == caveId && m.Format != SurveyModelFormat.Stl && m.Status == SurveyModelStatus.Ready)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .Select(m => new { m.Id, m.Format, m.RootSurveyName })
            .ToListAsync(ct);

        var first = new List<StationPhotograph>();
        var all = 0;
        foreach (var model in models)
        {
            var incident = db.ResLinkMembers.AsNoTracking()
                .Where(m => m.EntityType == AttachedEntityType.SurveyModel && m.EntityId == model.Id);
            var links = ResLinkStanding.Of(db).AsNoTracking().Where(l => incident.Any(m => m.ResLinkId == l.Id));
            var hidden = await placePoints.HiddenLinkIdsAsync(ctx, links, ct);
            hidden.UnionWith(await placeStations.HiddenLinkIdsAsync(ctx, links, ct));
            if (hidden.Count > 0)
            {
                links = links.Where(l => !hidden.Contains(l.Id));
            }

            // Newest first, as the links of a model are listed: where two links put a photograph
            // at one station, the first of the newer link is the one shown.
            var linkIds = await links
                .OrderByDescending(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .Take(MaxLinksAModel)
                .Select(l => l.Id)
                .ToListAsync(ct);
            if (linkIds.Count == 0)
            {
                continue;
            }

            var members = await db.ResLinkMembers.AsNoTracking()
                .Where(TripLinkFate.Not(TripLinkFate.OfNoLiveTrip(db)))
                .Where(m => linkIds.Contains(m.ResLinkId))
                .OrderBy(m => m.SortOrder)
                .ThenBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);

            // Which of the documents named are pictures this caller may read.
            var documentIds = members
                .Where(m => m.EntityType == AttachedEntityType.Document && m.EntityId is not null)
                .Select(m => m.EntityId!.Value)
                .Distinct()
                .ToList();
            if (documentIds.Count == 0)
            {
                continue;
            }

            var documents = await db.Documents.AsNoTracking().Where(d => documentIds.Contains(d.Id)).ToListAsync(ct);
            var readable = await DocumentAccessRules.ReadableAsync(db, access, ctx, documents, ct);
            var files = await (from version in db.DocumentVersions.AsNoTracking()
                               where readable.Contains(version.DocumentId) && version.IsCurrent
                               join file in db.StoredFiles.AsNoTracking() on version.Id equals file.DocumentVersionId
                               orderby file.CreatedAt, file.Id
                               select new { version.DocumentId, file.Id, file.Kind, file.StoragePath, file.OrientationQuarterTurns })
                .ToListAsync(ct);

            // The file a document serves is the first of its current version; a picture is a
            // document whose served file is an image.
            var pictureOf = files
                .GroupBy(f => f.DocumentId)
                .Select(g => g.First())
                .Where(f => f.Kind == FileKind.Image)
                .ToDictionary(f => f.DocumentId);

            // Station by station, in the order the links and their members come: a link puts each
            // of its pictures at each of its stations.
            var atStation = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
            foreach (var linkId in linkIds)
            {
                var own = members.Where(m => m.ResLinkId == linkId).ToList();
                var stations = own
                    .Where(m => m.EntityType == AttachedEntityType.SurveyModel
                                && m.EntityId == model.Id
                                && m.AnchorKind == AnchorKind.ModelStation)
                    .Select(m => StationOf(m.Anchor))
                    .OfType<string>()
                    .ToList();
                var pictures = own
                    .Where(m => m.EntityType == AttachedEntityType.Document
                                && m.EntityId is { } id
                                && pictureOf.ContainsKey(id))
                    .Select(m => m.EntityId!.Value)
                    .ToList();
                foreach (var station in stations)
                {
                    if (!atStation.TryGetValue(station, out var held))
                    {
                        atStation[station] = held = [];
                    }

                    held.AddRange(pictures.Where(picture => !held.Contains(picture)));
                }
            }

            var named = atStation.Where(pair => pair.Value.Count > 0).ToList();
            if (named.Count == 0)
            {
                continue;
            }

            // Each name read both ways the spelling rule allows, as the stations' own route reads
            // one: the name the drawing shows and the name the rows hold find the same station.
            var candidates = named.ToDictionary(
                pair => pair.Key,
                pair => SurveyStationNames.StoredCandidates(model.Format, model.RootSurveyName, pair.Key),
                StringComparer.Ordinal);
            var stored = candidates.Values.SelectMany(names => names).Distinct(StringComparer.Ordinal).ToList();
            var rows = await db.SurveyStations.AsNoTracking()
                .Where(s => s.SurveyModelId == model.Id && stored.Contains(s.Name))
                .Select(s => new { s.Name, s.Position })
                .ToListAsync(ct);
            var placeOf = rows.GroupBy(r => r.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Position, StringComparer.Ordinal);

            foreach (var (station, pictures) in named)
            {
                // A station the survey no longer has is no place: a photograph is never put
                // somewhere it was not said to be.
                var name = candidates[station].FirstOrDefault(placeOf.ContainsKey);
                if (name is null || placeOf[name] is not { } position || double.IsNaN(position.Coordinate.Z))
                {
                    continue;
                }

                all += pictures.Count;
                var picture = pictureOf[pictures[0]];
                first.Add(new StationPhotograph(
                    picture.DocumentId,
                    picture.Id,
                    picture.StoragePath,
                    picture.OrientationQuarterTurns,
                    position.X,
                    position.Y,
                    position.Coordinate.Z));
            }
        }

        return (first, all);
    }

    /// <summary>The station a member's anchor names, or null for one that names none.</summary>
    private static string? StationOf(string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor))
        {
            return null;
        }

        try
        {
            using var parsed = JsonDocument.Parse(anchor);
            return parsed.RootElement.ValueKind == JsonValueKind.Object
                   && parsed.RootElement.TryGetProperty("station", out var station)
                   && station.ValueKind == JsonValueKind.String
                   && station.GetString() is { Length: > 0 } name
                ? name
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
