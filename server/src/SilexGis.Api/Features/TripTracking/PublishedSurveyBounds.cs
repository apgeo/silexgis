// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Whether a survey about to be handed to the internet may reach as far as somebody else's
/// protected cave.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap.</b> Publishing a trip hands over its survey file, and whether that is allowed is
/// asked of one cave: the one the survey belongs to. A survey file is not bound to one cave,
/// though. An export of a whole system, or of two caves surveyed towards each other, is filed under
/// one of them and draws both, and the position protection of the other is not consulted by
/// anything.
/// </para>
/// <para>
/// <b>What can be known, and it is not much.</b> Nothing stored says which cave a station belongs
/// to, and the file is not read again here. What the server does hold is where the file's named
/// stations are, so the question asked is geometric: does the position of a protected cave, or of
/// one of its entrances, fall inside the rectangle those stations span? That is a reason to look,
/// never a finding. A cave can sit inside the rectangle of a survey that never enters it — an
/// L-shaped survey encloses ground it does not cross, and plan position says nothing of depth —
/// and a neighbour can be drawn in a file whose rectangle stops short of the point that neighbour
/// is recorded at. So the answer warns and refuses nothing, and whoever words it for a person
/// must say that it is a check by position that cannot see inside the file.
/// </para>
/// <para>
/// <b>The answer is one bit, and only about caves the asker may already place.</b> It names no
/// cave and carries no position and no count. That alone would not be enough: somebody who can
/// file a survey of their own choosing under a cave of their own and publish it could ask the
/// question of any rectangle they liked, and halve it until the answer was a position — the one
/// thing the protection exists to keep. So a protected cave counts towards the answer only when
/// the asker may see exactly where it is. A publisher who may not is told nothing, which is what
/// they are told about that cave everywhere else; the installation's own list is read by people
/// who may place every cave, and is where the complete answer shows.
/// </para>
/// <para>
/// <b>May place means may read it and may see exactly where it is — both.</b> The second does not
/// imply the first. Exact position is decided from the protected features above a cave, so whoever
/// owns a protected area holds it for everything beneath that area, including somebody else's
/// private cave there that they cannot open and are not told exists. Counting such a cave would let
/// the area's owner find it by halving rectangles. So a candidate is first a cave the asker may
/// read, by the same filter every list of caves is read through.
/// </para>
/// <para>
/// <b>A survey with no stored stations has no known bounds and answers no.</b> A wall mesh, or a
/// line plot that has not been read yet, has no rows to span a rectangle with. Saying "yes" for
/// those would be a warning on every such publication, which is a warning nobody reads.
/// </para>
/// </remarks>
internal static class PublishedSurveyBounds
{
    /// <summary>A survey being handed out, and the cave whose survey it is taken to be.</summary>
    /// <param name="CaveFeatureId">
    /// The cave the watch is anchored to. It and everything under it are not neighbours: whether
    /// that cave may be published is the question publishing already asks, and refuses by.
    /// </param>
    internal readonly record struct Survey(Guid SurveyModelId, Guid CaveFeatureId);

    /// <summary>The single-survey form of <see cref="ReachingProtectedCavesAsync"/>.</summary>
    internal static async Task<bool> ReachesProtectedCaveAsync(
        SilexGisDbContext db, FeatureProtection protection, AccessContext ctx,
        Guid? surveyModelId, Guid? caveFeatureId, CancellationToken ct)
    {
        if (surveyModelId is not { } model || caveFeatureId is not { } cave) return false;

        var survey = new Survey(model, cave);
        return (await ReachingProtectedCavesAsync(db, protection, ctx, [survey], ct)).Contains(survey);
    }

    /// <summary>
    /// Of the given surveys, those whose stations span a rectangle holding the position of a
    /// protected cave, or of a protected cave's entrance, that is not their own and that
    /// <paramref name="ctx"/> may both read and place exactly.
    /// </summary>
    internal static async Task<HashSet<Survey>> ReachingProtectedCavesAsync(
        SilexGisDbContext db, FeatureProtection protection, AccessContext ctx,
        IReadOnlyCollection<Survey> surveys, CancellationToken ct)
    {
        var reaching = new HashSet<Survey>();
        if (surveys.Count == 0) return reaching;

        var modelIds = surveys.Select(s => s.SurveyModelId).Distinct().ToList();

        // The rectangle of each survey, worked out by the database: four numbers a survey, and no
        // station leaves it.
        var spans = await db.SurveyStations.AsNoTracking()
            .Where(s => modelIds.Contains(s.SurveyModelId))
            .GroupBy(s => s.SurveyModelId)
            .Select(g => new
            {
                SurveyModelId = g.Key,
                West = g.Min(s => s.Position.X),
                East = g.Max(s => s.Position.X),
                South = g.Min(s => s.Position.Y),
                North = g.Max(s => s.Position.Y),
            })
            .ToListAsync(ct);
        if (spans.Count == 0) return reaching;

        // Which caves and entrances stand inside each rectangle — asked of the database, which
        // answers with ids. Read under the ordinary filters, so a deleted cave is no neighbour,
        // and narrowed to what this caller may read: a cave they cannot open must not move the
        // answer, however much of its position they would otherwise be entitled to. A full
        // administrator reads every cave, so the installation's list stays complete.
        // One read a survey: the surveys of one page of links are few, since trips share caves
        // and caves share surveys, and each read is served by the index on the position.
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var inside = new Dictionary<Guid, List<(Guid Id, Guid[] AncestorIds)>>();
        foreach (var span in spans)
        {
            // A survey of one station, or of stations in a line, spans a point or a segment rather
            // than an area; the factory answers with that shape and the question still holds.
            var bounds = factory.ToGeometry(new Envelope(span.West, span.East, span.South, span.North));
            var found = await db.Features.AsNoTracking()
                .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => (f.Kind == FeatureKind.Cave || f.Kind == FeatureKind.CaveEntrance)
                    && f.Geom != null
                    && f.Geom.Intersects(bounds))
                .Select(f => new { f.Id, f.AncestorIds })
                .ToListAsync(ct);
            inside[span.SurveyModelId] = [.. found.Select(f => (f.Id, f.AncestorIds))];
        }

        // Somebody else's: not the survey's own cave and nothing under it. A feature's ancestors
        // include itself, so the one test covers the cave and its entrances alike.
        var neighbours = new Dictionary<Survey, List<Guid>>();
        foreach (var survey in surveys.Distinct())
        {
            if (!inside.TryGetValue(survey.SurveyModelId, out var found)) continue;
            var others = found
                .Where(f => !f.AncestorIds.Contains(survey.CaveFeatureId))
                .Select(f => f.Id)
                .ToList();
            if (others.Count > 0) neighbours[survey] = others;
        }

        var candidates = neighbours.Values.SelectMany(ids => ids).Distinct().ToList();
        if (candidates.Count == 0) return reaching;

        // Protected, by the same two facts that decide whether a cave may be published at all —
        // asked there rather than restated here.
        var unguarded = await TrackingWithholding.UnguardedFeatureIdsAsync(db, protection, candidates, ct);
        var guarded = candidates.Where(id => !unguarded.Contains(id)).ToList();
        if (guarded.Count == 0) return reaching;

        // And of those — every one readable by this caller, by the read above — only the ones the
        // caller may place exactly: the answer must not tell anybody where a cave is that they
        // could not already find.
        var placeable = await protection.ExactViewIdsAsync(ctx, guarded, ct);
        var counted = guarded.Where(placeable.Contains).ToHashSet();

        foreach (var (survey, others) in neighbours)
        {
            if (others.Any(counted.Contains)) reaching.Add(survey);
        }

        return reaching;
    }
}
