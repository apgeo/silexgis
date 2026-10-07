// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Answers, for the reports one signed-in read is about to show, whether each told position lies
/// outside the parts of the cave the watch declared.
/// </summary>
/// <remarks>
/// <para>
/// <b>The declaration is the watch's list of survey parts</b> — the entries a reported depth is
/// looked for in. They are where the party said it was going, so a station reported somewhere
/// else is worth a mark beside it. The comparison itself is Domain's
/// (<see cref="TrackingDepthResolver.OutsideDeclaredParts"/>); what is here is the one small read
/// that turns the names being shown into stations with their survey names, and the two gates in
/// front of it.
/// </para>
/// <para>
/// <b>Two things must both be open to the reader before anything is said.</b> The declaration is
/// station vocabulary of the watch's cave, and a reported place is location data of the cave it
/// was placed in. "Outside the declared parts" beside a withheld place would tell a reader that
/// somebody is at a station and that it is not one of a set they were refused; beside a told place
/// under a withheld declaration it would hand over one fact about a list they may not read. So the
/// caller passes whether the declaration is being told, and asks <see cref="Outside"/> only on the
/// branch where the place is — the answer is false everywhere else, which is also what a watch
/// with no declaration answers.
/// </para>
/// <para>
/// <b>Only a place measured on the watch's present survey can be compared.</b> A station name is
/// a name inside one survey; a report made against a survey the watch has since left names a
/// station whose part of this survey nobody stated. Such a report, and one whose station the
/// survey no longer holds, is not marked.
/// </para>
/// <para>
/// Nothing here sends, raises or stands anything down: it is a word on a row for the people
/// running the watch, and no published read asks it.
/// </para>
/// </remarks>
internal sealed class TrackingDeclaredParts
{
    /// <summary>The answer where nothing can be said: nobody is outside of anything.</summary>
    internal static readonly TrackingDeclaredParts Unsaid = new(null, [], []);

    private readonly Guid? modelId;
    private readonly IReadOnlyCollection<string> declared;
    private readonly Dictionary<string, TrackingDepthResolver.Station> byViewerName;

    private TrackingDeclaredParts(
        Guid? modelId,
        IReadOnlyCollection<string> declared,
        Dictionary<string, TrackingDepthResolver.Station> byViewerName)
    {
        this.modelId = modelId;
        this.declared = declared;
        this.byViewerName = byViewerName;
    }

    /// <summary>
    /// Whether this report's station is known to lie outside the declared parts. Ask it only for a
    /// report whose place is being told to the caller.
    /// </summary>
    internal bool Outside(TripPositionEvent report) =>
        modelId is not null
        && report.SurveyModelId == modelId
        && report.ViewerStationName is { } station
        && TrackingDepthResolver.OutsideDeclaredParts(
            byViewerName.TryGetValue(station, out var known) ? known : null, declared);

    /// <summary>
    /// Reads what is needed to answer for <paramref name="told"/> — the reports whose places this
    /// caller is about to be shown — and nothing where there is no question to answer.
    /// </summary>
    /// <param name="declarationTold">
    /// Whether the caller is being told the watch's declared parts at all (they are withheld with
    /// the rest of the watch's station vocabulary).
    /// </param>
    internal static async Task<TrackingDeclaredParts> ForAsync(
        SilexGisDbContext db,
        Domain.Entities.TripTracking? tracking,
        bool declarationTold,
        IEnumerable<TripPositionEvent> told,
        CancellationToken ct)
    {
        if (tracking?.SurveyModelId is not { } model || tracking.DepthFilter.Length == 0 || !declarationTold)
        {
            return Unsaid;
        }

        var shown = told
            .Where(e => e.SurveyModelId == model && e.ViewerStationName is not null)
            .Select(e => e.ViewerStationName!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (shown.Count == 0) return Unsaid;

        var naming = await db.SurveyModels.AsNoTracking()
            .Where(m => m.Id == model)
            .Select(m => new { m.Format, m.RootSurveyName })
            .FirstOrDefaultAsync(ct);
        // The survey is gone: its stations went with it, and nothing can be compared.
        if (naming is null) return Unsaid;

        // A report keeps the viewer's spelling of its station; the rows may hold another. Ask for
        // every row either reading could mean, then take for each name the first reading the
        // survey holds — the order a typed station name is resolved in when it is recorded.
        var candidates = shown
            .SelectMany(name => SurveyStationNames.StoredCandidates(naming.Format, naming.RootSurveyName, name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var rows = await db.SurveyStations.AsNoTracking()
            .Where(s => s.SurveyModelId == model && candidates.Contains(s.Name))
            .Select(s => new { s.Name, s.SurveyName })
            .ToListAsync(ct);
        var byStoredName = rows
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().SurveyName, StringComparer.Ordinal);

        var byViewerName = new Dictionary<string, TrackingDepthResolver.Station>(StringComparer.Ordinal);
        foreach (var name in shown)
        {
            foreach (var stored in SurveyStationNames.StoredCandidates(naming.Format, naming.RootSurveyName, name))
            {
                if (!byStoredName.TryGetValue(stored, out var surveyName)) continue;
                // Altitude and the entrance flag play no part in the comparison, so they are not read.
                byViewerName[name] = TrackingDepthResolver.Station.Of(
                    naming.Format, naming.RootSurveyName, stored, surveyName, z: 0, isEntrance: false);
                break;
            }
        }

        return new TrackingDeclaredParts(model, tracking.DepthFilter, byViewerName);
    }
}
