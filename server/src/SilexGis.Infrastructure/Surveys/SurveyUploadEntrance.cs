// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Features;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Places a cave that a survey upload made at the survey's fixed station, as its main entrance.
/// </summary>
/// <remarks>
/// <para>
/// Only a cave the upload itself created, and only while it has never had an entrance: a cave that
/// was already in the registry has a position somebody chose, and a survey sent to it never moves
/// it. A second version of the same survey, or a rerun, finds the entrance the first one wrote
/// and leaves it — including where somebody has since corrected it on the web, and where somebody
/// has deleted it.
/// </para>
/// <para>
/// The entrance is written through the same path every other entrance is, so it takes the cave's
/// owner, club and visibility, and the cave's protection applies to it from the moment it exists.
/// The position goes nowhere else: not into the run's message, its log or the server's log.
/// </para>
/// </remarks>
public static class SurveyUploadEntrance
{
    /// <summary>The entrance kind a surveyed entrance is written as.</summary>
    private const string EntranceTypeCode = "natural";

    /// <summary>
    /// Writes the main entrance of <paramref name="upload"/>'s cave at the first fixed station of
    /// <paramref name="facts"/> with a position, when the upload created the cave and the cave has
    /// no entrance. Answers whether one was written.
    /// </summary>
    public static async Task<bool> WriteFromFixAsync(
        SilexGisDbContext db, FeatureWriteService features, SurveyUpload upload, TopoDroidSurveyFacts facts, CancellationToken ct)
    {
        if (!upload.CreatedCave || facts.FixedPoints.FirstOrDefault(Placeable) is not { } fix)
        {
            return false;
        }

        var cave = await db.Features.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == upload.CaveFeatureId && f.Kind == FeatureKind.Cave && f.DeletedAt == null, ct);
        // Deleted entrances count: one somebody removed on the web — because the position must
        // not be recorded, or was wrong — must not come back at the archive's fix on a rerun.
        if (cave is null || await db.CaveEntrances.IgnoreQueryFilters().AnyAsync(e => e.CaveFeatureId == cave.Id, ct))
        {
            return false;
        }

        var typeId = await db.EntranceTypes.AsNoTracking()
            .Where(t => t.Code == EntranceTypeCode)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (typeId is null)
        {
            return false;
        }

        // The height the caver entered is above the geoid, which is what an entrance's altitude
        // means; the one the tool computed above the ellipsoid is the fallback.
        var height = fix.GeoidHeight ?? fix.EllipsoidHeight;
        var feature = new Feature
        {
            Name = cave.Name,
            Geom = new Point(height is { } z ? new CoordinateZ(fix.Longitude, fix.Latitude, z) : new Coordinate(fix.Longitude, fix.Latitude))
            {
                SRID = 4326,
            },
        };
        var entrance = new CaveEntrance
        {
            CaveFeatureId = cave.Id,
            EntranceTypeId = typeId.Value,
            IsMain = true,
            Altitude = height is { } h ? (decimal)Math.Round(h, 2) : null,
            PositionQuality = PositionQuality.Gps,
            SurveyedAt = upload.SurveyDate,
        };

        try
        {
            await features.CreateEntranceAsync(feature, entrance, ct);
            await features.SetMainEntranceAsync(cave.Id, feature.Id, ct);
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (FeatureWriteException)
        {
            // The survey is still a survey without its cave placed; the entrance can be drawn on
            // the web. The reason is not written anywhere a position could travel with it.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static bool Placeable(TopoDroidFixedPoint fix) =>
        double.IsFinite(fix.Longitude) && double.IsFinite(fix.Latitude)
        && Math.Abs(fix.Longitude) <= 180 && Math.Abs(fix.Latitude) <= 90
        && !(fix.Longitude == 0 && fix.Latitude == 0);
}
