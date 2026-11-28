// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.TestData;

/// <summary>
/// The part of a run's removal that is its caves' underground: the centrelines the reading drew
/// from its survey models, the models, sources, surveys, depth places and level readings, and the
/// documents and stored bytes that held its files. All of it hangs on the run's own caves, so it
/// goes before them — a run's own survey never counts as somebody's use of a cave it made.
/// </summary>
public static class TestDataUndergroundRemoval
{
    /// <summary>Removes the rows; answers the stored files whose bytes go once the removal has committed.</summary>
    public static async Task<IReadOnlyList<StoredFile>> RemoveAsync(
        SilexGisDbContext db,
        FeatureWriteService writer,
        DocumentWriteService documents,
        ILookup<string, string> listed,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(listed);

        var models = Guids(listed["survey_models"]);
        if (models.Count > 0)
        {
            // The centrelines the reading drew: features of their own, under the cave, which the
            // run never listed because a job made them.
            var centrelines = await db.Centerlines.IgnoreQueryFilters()
                .Where(c => c.SurveyModelId != null && models.Contains(c.SurveyModelId.Value))
                .Select(c => c.Id)
                .ToListAsync(ct);
            foreach (var id in centrelines)
            {
                await writer.PurgeAsync(id, ct);
                await db.SaveChangesAsync(ct);
            }
        }

        var depthPlaces = Guids(listed["cave_depth_places"]);
        await db.CaveDepthPlaces.Where(x => depthPlaces.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var levels = Guids(listed["cave_level_bands"]);
        await db.CaveLevelBands.Where(x => levels.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var sources = Guids(listed["survey_sources"]);
        await db.SurveySources.Where(x => sources.Contains(x.Id)).ExecuteDeleteAsync(ct);
        await db.SurveyModels.Where(x => models.Contains(x.Id)).ExecuteDeleteAsync(ct);
        var surveys = Guids(listed["cave_surveys"]);
        await db.CaveSurveys.Where(x => surveys.Contains(x.Id)).ExecuteDeleteAsync(ct);

        // Documents go through the document writer, which takes their versions and files with them
        // and the rules naming them through the change tracker.
        var gone = new List<StoredFile>();
        foreach (var file in Guids(listed["files"]))
        {
            gone.AddRange(await documents.DeleteDocumentOfFileAsync(file, ct));
        }

        await db.SaveChangesAsync(ct);
        return gone;
    }

    private static List<Guid> Guids(IEnumerable<string> ids) => [.. ids.Select(id => Guid.Parse(id, CultureInfo.InvariantCulture))];
}
