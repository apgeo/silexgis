// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// What one karst area adds up to: how many caves are in it, how thickly they sit, how much passage
/// they hold, and a composite reading of how karstified the ground is.
///
/// <para>
/// The counting lives below this slice, with the rest of the shared query bodies. What lives here
/// is the part that is a decision rather than a query: that membership is the declared one, that the
/// answer says so, and that the drift between the hierarchy and the map is reported as a hint rather
/// than folded into any total.
/// </para>
/// </summary>
public static class AreaKarstStatisticsEndpoints
{
    /// <summary>The feature type whose outlines count as mapped depressions.</summary>
    private const string DepressionTypeCode = FeatureTypeSeeds.Sinkhole;

    public static RouteGroupBuilder MapAreaKarstStatisticsEndpoints(this RouteGroupBuilder api)
    {
        var features = api.MapGroup("/features").WithTags("Features");

        features.MapGet("/{id:guid}/karst-statistics", AreaStatisticsAsync)
            .WithSummary(
                "Counts, densities, totals, extremes, a rock-type breakdown and a classed "
                + "karstification index for one area, over the caves declared to be in it.");

        // The same figures as a spreadsheet. It is a separate route rather than a format parameter
        // on the one above because the two return different media types and a generated client that
        // has to branch on a query string to know what it is holding is worse for every caller of
        // the JSON one.
        features.MapGet("/{id:guid}/karst-statistics.csv", AreaStatisticsCsvAsync)
            .WithSummary("The same area statistics as a two-column CSV, for a spreadsheet.");

        return api;
    }

    private static async Task<Results<Ok<AreaKarstStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        AreaStatisticsAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct) =>
        await GatherAsync(id, db, accessAccessor, protection, ct);

    private static async Task<Results<ContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>>
        AreaStatisticsCsvAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var result = await GatherAsync(id, db, accessAccessor, protection, ct);

        return result.Result switch
        {
            Ok<AreaKarstStatisticsDto> ok when ok.Value is { } dto =>
                TypedResults.Text(ToCsv(dto), "text/csv"),
            UnauthorizedHttpResult unauthorized => unauthorized,
            ProblemHttpResult problem => problem,
            _ => ApiProblems.NotFound("feature.not_found"),
        };
    }

    /// <summary>
    /// The figures as name-and-value rows rather than as a table of one row.
    ///
    /// <para>
    /// A wide single row is what a naive export of a record produces, and it is the shape nobody can
    /// read: forty columns whose headers scroll off the screen, with three of them repeating five
    /// times for the extremes and one per rock type. Long form survives a component being absent,
    /// survives a rock type being added, and opens in a spreadsheet as something a person can look
    /// at. The membership basis is the first row for the same reason it is the first field of the
    /// JSON: every count below it is over the declared hierarchy and not over what is spatially
    /// inside the outline, and a figure copied out of here without that is a different claim.
    /// </para>
    /// </summary>
    private static string ToCsv(AreaKarstStatisticsDto dto)
    {
        var csv = new System.Text.StringBuilder();
        csv.Append("measure,value\n");

        void Row(string measure, object? value) =>
            csv.Append(Escape(measure)).Append(',').Append(Escape(Format(value))).Append('\n');

        Row("area_id", dto.AreaId);
        Row("membership_basis", dto.Basis);
        Row("area_km2", dto.AreaKm2);
        Row("cave_count", dto.CaveCount);
        Row("entrance_count", dto.EntranceCount);
        Row("caves_per_km2", dto.CavesPerKm2);
        Row("entrances_per_km2", dto.EntrancesPerKm2);
        Row("surveyed_length_m", dto.SurveyedLengthM);
        Row("surveyed_cave_count", dto.SurveyedCaveCount);
        Row("surveyed_metres_per_km2", dto.SurveyedMetresPerKm2);
        Row("depression_count", dto.DepressionCount);
        Row("depression_area_km2", dto.DepressionAreaKm2);
        Row("depression_area_ratio", dto.DepressionAreaRatio);
        Row("karstification_score", dto.Karstification.Score);
        Row("karstification_class", dto.Karstification.Class);

        foreach (var component in dto.Karstification.Components)
        {
            Row($"karstification_component.{component.Name}", component.Value);
            Row($"karstification_component.{component.Name}.reference", component.Reference);
        }

        foreach (var (cave, i) in dto.DeepestCaves.Select((c, i) => (c, i + 1)))
        {
            Row($"deepest_cave.{i}.name", cave.Name);
            Row($"deepest_cave.{i}.value", cave.Value);
        }

        foreach (var (cave, i) in dto.LongestCaves.Select((c, i) => (c, i + 1)))
        {
            Row($"longest_cave.{i}.name", cave.Name);
            Row($"longest_cave.{i}.value", cave.Value);
        }

        foreach (var rock in dto.RockTypes)
        {
            Row($"rock_type.{rock.Code ?? rock.Name ?? "unknown"}", rock.CaveCount);
        }

        Row("unparented_inside_count", dto.UnparentedInsideCount);
        Row("placeable_cave_count", dto.PlaceableCaveCount);

        return csv.ToString();
    }

    /// <summary>
    /// Numbers in the invariant culture, because a spreadsheet on a machine set to a comma decimal
    /// separator would otherwise read a written "3,5" as two columns and silently shift every field
    /// after it.
    /// </summary>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        double d => d.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;

    /// <summary>
    /// The figures themselves, gathered once and rendered by either route, so the JSON and the
    /// spreadsheet cannot come to disagree about what an area holds — or, worse, about who may ask.
    /// </summary>
    private static async Task<Results<Ok<AreaKarstStatisticsDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        GatherAsync(
            Guid id,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        // An area this caller may not read answers exactly as one that does not exist, so asking
        // about it discloses nothing about whether it is there.
        var exists = await db.Features.AsNoTracking()
            .VisibleTo(ctx, db.Features, db.FeatureSetMembers)
            .AnyAsync(f => f.Id == id, ct);

        if (!exists)
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        // Everything below is measured against the outline: its ground area is the denominator of
        // every per-square-kilometre figure, the depression ratio is areas inside it, and the
        // drift hint tests caves for being inside it. An outline places itself, so all of that is
        // a description of where it is, and a caller who may not be shown that must not be able to
        // derive it — least of all by moving a cave of their own and watching the hint change. So
        // the answer requires exact placement, and to everybody else the area answers as one that
        // is not there: the same answer as for an outline that does not exist, so the refusal
        // itself discloses nothing.
        if (!(await protection.ExactViewIdsAsync(ctx, [id], ct)).Contains(id))
        {
            return ApiProblems.NotFound("feature.not_found");
        }

        var depressionTypeId = await db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == DepressionTypeCode)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

        var totals = await KarstAreaStatisticsSql.TotalsAsync(
            db, ctx, id, depressionTypeId ?? 0L, ct);

        var deepest = await KarstAreaStatisticsSql.ExtremesAsync(
            db, ctx, id, KarstAreaStatisticsSql.DepthColumn, AreaKarstStatisticsLimits.ExtremeCount, ct);
        var longest = await KarstAreaStatisticsSql.ExtremesAsync(
            db, ctx, id, KarstAreaStatisticsSql.SurveyedLengthColumn,
            AreaKarstStatisticsLimits.ExtremeCount, ct);
        var rockTypes = await KarstAreaStatisticsSql.RockTypesAsync(db, ctx, id, ct);
        var unparentedInside = await KarstAreaStatisticsSql.UnparentedInsideCountAsync(db, ctx, id, ct);
        var placeable = await KarstAreaStatisticsSql.PlaceableCaveCountAsync(db, ctx, id, ct);

        var areaKm2 = totals.AreaM2 is { } m2 && m2 > 0d ? m2 / 1_000_000d : (double?)null;
        // No depression outlines means the reading could not be taken, not that the reading is
        // nought. Most registries hold caves long before anybody draws a doline, and scoring an
        // unmapped area zero on this component would rank it below an area genuinely without
        // depressions. The two are indistinguishable from here, so the honest answer is silence.
        var depressionRatio = totals.AreaM2 is { } ground && ground > 0d && totals.DepressionCount > 0
            ? (totals.DepressionAreaM2 ?? 0d) / ground
            : (double?)null;

        var cavesPerKm2 = areaKm2 is { } km2 ? totals.CaveCount / km2 : (double?)null;

        var components = KarstificationIndex.Components(cavesPerKm2, depressionRatio);
        var score = KarstificationIndex.Score(components);

        return TypedResults.Ok(new AreaKarstStatisticsDto(
            id,
            AreaKarstStatisticsLimits.DeclaredBasis,
            areaKm2,
            totals.CaveCount,
            totals.EntranceCount,
            cavesPerKm2,
            areaKm2 is { } e ? totals.EntranceCount / e : null,
            totals.SurveyedLengthM,
            totals.SurveyedCaveCount,
            areaKm2 is { } s ? (totals.SurveyedLengthM ?? 0d) / s : null,
            totals.DepressionCount,
            totals.DepressionAreaM2 is { } d ? d / 1_000_000d : null,
            depressionRatio,
            [.. deepest.Select(ToExtreme)],
            [.. longest.Select(ToExtreme)],
            [.. rockTypes.Select(r => new AreaRockTypeCountDto(r.RockTypeId, r.Code, r.Name, r.CaveCount))],
            new KarstificationIndexDto(
                score,
                KarstificationIndex.Classify(score),
                [.. components.Select(c =>
                    new KarstificationComponentDto(c.Name, c.Value, c.Normalised, c.Reference))]),
            unparentedInside,
            placeable));
    }

    private static AreaCaveExtremeDto ToExtreme(KarstAreaExtremeRow row) =>
        new(row.FeatureId, row.Name, row.Value);
}
