// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// What the readings of a survey model need to check where they place it, and where they record
/// what the check found.
/// </summary>
public static class SurveyModelPlacement
{
    /// <summary>
    /// Where the model's cave is, as stored — its exact position, never a blurred one, because the
    /// comparison it feeds is made here on the server and only its conclusion is kept. Null for a
    /// cave with no geometry.
    /// </summary>
    public static async Task<(double Longitude, double Latitude)?> CavePositionAsync(
        SilexGisDbContext db, Guid caveFeatureId, CancellationToken ct)
    {
        var geom = await db.Features.AsNoTracking()
            .Where(f => f.Id == caveFeatureId)
            .Select(f => f.Geom)
            .FirstOrDefaultAsync(ct);
        if (geom is null || geom.IsEmpty)
        {
            return null;
        }

        var point = geom.InteriorPoint;
        return (point.X, point.Y);
    }

    /// <summary>
    /// Records what a reading found about the model's declaration, replacing what an earlier one
    /// found: null says the declaration placed the file at its cave, or that nothing better was
    /// found.
    /// </summary>
    public static void Record(SurveyModel model, SurveyPlacementAdvice? advice)
    {
        model.PlacementOffByKm = advice?.OffByKm;
        model.SuggestedSourceEpsg = advice?.SourceEpsg;
        model.SuggestsLocalMetres = advice is { UseLocalMetres: true };
        model.SuggestedOffByKm = advice?.SuggestedOffByKm;
    }
}
