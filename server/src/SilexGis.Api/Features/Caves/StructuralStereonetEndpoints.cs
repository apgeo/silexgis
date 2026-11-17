// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Features;
using SilexGis.Domain.Geo;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Caves;

/// <summary>One reading's pole as plotted on the net.</summary>
/// <param name="FeatureId">The reading.</param>
/// <param name="MeasurementType">The kind of plane, by code, when the reading names one.</param>
/// <param name="DipDirectionDegrees">The plane's dip direction as stored.</param>
/// <param name="DipDegrees">The plane's dip as stored.</param>
/// <param name="TrendDegrees">The pole's trend — opposite the dip direction.</param>
/// <param name="PlungeDegrees">The pole's plunge — the complement of the dip.</param>
/// <param name="X">Where the pole lands on the net of radius one, east positive.</param>
/// <param name="Y">Where the pole lands on the net of radius one, north positive.</param>
public sealed record StereonetPoleDto(
    Guid FeatureId,
    string? MeasurementType,
    double DipDirectionDegrees,
    double DipDegrees,
    double TrendDegrees,
    double PlungeDegrees,
    double X,
    double Y);

/// <summary>
/// The poles of the structural readings gathered for a cave or an area, on an equal-area,
/// lower-hemisphere net, with Kamb's density over them.
/// </summary>
/// <param name="ReadingCount">Readings plotted — only those the caller may both read and place
/// exactly, and deliberately not a count of the readings that exist there.</param>
/// <param name="MeasurementType">The kind of plane asked for, or null for every kind.</param>
/// <param name="RadiusMetres">The reach around a cave the readings were gathered from; null for
/// an area, whose outline is the reach.</param>
/// <param name="Poles">The poles, in feature-id order.</param>
/// <param name="Density">Kamb's density over the net, or null when there is nothing to count.</param>
public sealed record StereonetDto(
    int ReadingCount,
    string? MeasurementType,
    double? RadiusMetres,
    IReadOnlyList<StereonetPoleDto> Poles,
    StereonetDensity? Density);

/// <summary>What a caller asks for a cave's stereonet.</summary>
/// <param name="Id">The cave.</param>
/// <param name="RadiusMetres">How far around the cave to gather readings. Optional; a kilometre by
/// default, the reach the passage comparison uses.</param>
/// <param name="MeasurementType">Which kind of plane to plot, by code. Optional; every kind by default.</param>
public sealed record CaveStereonetRequest(Guid Id, double? RadiusMetres, string? MeasurementType);

/// <summary>What a caller asks for an area's stereonet.</summary>
/// <param name="Id">The area.</param>
/// <param name="MeasurementType">Which kind of plane to plot, by code. Optional; every kind by default.</param>
public sealed record AreaStereonetRequest(Guid Id, string? MeasurementType);

/// <summary>The rules on the cave stereonet request.</summary>
public sealed class CaveStereonetRequestValidator : AbstractValidator<CaveStereonetRequest>
{
    public CaveStereonetRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("A cave id is required.");
        RuleFor(x => x.RadiusMetres)
            .InclusiveBetween(
                CaveStructureComparisonLimits.MinRadiusMetres,
                CaveStructureComparisonLimits.MaxRadiusMetres)
            .When(x => x.RadiusMetres is not null)
            .WithMessage(
                $"The reach must be between {CaveStructureComparisonLimits.MinRadiusMetres} and "
                + $"{CaveStructureComparisonLimits.MaxRadiusMetres} metres.");
        RuleFor(x => x.MeasurementType)
            .Must(t => t is null || StructuralMeasurement.Types.Contains(t))
            .WithMessage($"The measurement type must be one of: {string.Join(", ", StructuralMeasurement.Types)}.");
    }
}

/// <summary>The rules on the area stereonet request.</summary>
public sealed class AreaStereonetRequestValidator : AbstractValidator<AreaStereonetRequest>
{
    public AreaStereonetRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("An area id is required.");
        RuleFor(x => x.MeasurementType)
            .Must(t => t is null || StructuralMeasurement.Types.Contains(t))
            .WithMessage($"The measurement type must be one of: {string.Join(", ", StructuralMeasurement.Types)}.");
    }
}

/// <summary>
/// Stereonets of the structural readings around a cave and under an area.
/// </summary>
/// <remarks>
/// <para>
/// The poles carry orientations and no coordinates, but which readings answer says where they are,
/// so each reading is admitted only for a caller who may both read and place it exactly, and a
/// withheld one is in no pole, no count and no density. The cave is refused whole, as "no such
/// cave", to a caller who may read it but not place it — the answer every route measuring around a
/// cave gives — because the set of readings within reach of it would otherwise outline where it is.
/// An area the caller may not read is refused as "no such feature".
/// </para>
/// <para>Signed-in callers only; nothing here is offered on a shared link.</para>
/// </remarks>
public static class StructuralStereonetEndpoints
{
    private const string CaveNotFoundCode = "cave.not_found";
    private const string AreaNotFoundCode = "feature.not_found";

    public static RouteGroupBuilder MapStructuralStereonetEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/caves").WithTags("Caves")
            .MapGet("/{id:guid}/stereonet", CaveStereonetAsync)
            .WithValidation<CaveStereonetRequest>()
            .WithSummary(
                "The poles of the structural readings within reach of a cave on an equal-area, "
                + "lower-hemisphere net, with Kamb's density contours.");

        api.MapGroup("/features").WithTags("Features")
            .MapGet("/{id:guid}/stereonet", AreaStereonetAsync)
            .WithValidation<AreaStereonetRequest>()
            .WithSummary(
                "The poles of the structural readings under an area on an equal-area, "
                + "lower-hemisphere net, with Kamb's density contours.");

        return api;
    }

    private static async Task<Results<Ok<StereonetDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        CaveStereonetAsync(
            [AsParameters] CaveStereonetRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            IAccessService access,
            FeatureProtection protection,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (await SurveyModelAccess.MeasurableCaveAsync(db, access, protection, ctx, request.Id, ct) is null)
        {
            return ApiProblems.NotFound(CaveNotFoundCode);
        }

        var radius = request.RadiusMetres ?? CaveStructureComparisonLimits.DefaultRadiusMetres;
        var typeId = await ReadingTypeIdAsync(db, ct);
        var rows = typeId is null
            ? []
            : await StructuralMeasurementSql.NearCaveAsync(
                db, ctx, request.Id, typeId.Value, request.MeasurementType, radius,
                CaveStructureComparisonLimits.MaxReadings, ct);

        return TypedResults.Ok(Plot(rows, request.MeasurementType, radius));
    }

    private static async Task<Results<Ok<StereonetDto>, UnauthorizedHttpResult, ProblemHttpResult>>
        AreaStereonetAsync(
            [AsParameters] AreaStereonetRequest request,
            SilexGisDbContext db,
            IAccessContextAccessor accessAccessor,
            CancellationToken ct)
    {
        var ctx = await accessAccessor.GetAsync(ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!await db.Features.AsNoTracking().VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .AnyAsync(f => f.Id == request.Id, ct))
        {
            return ApiProblems.NotFound(AreaNotFoundCode);
        }

        var typeId = await ReadingTypeIdAsync(db, ct);
        var rows = typeId is null
            ? []
            : await StructuralMeasurementSql.InAreaAsync(
                db, ctx, request.Id, typeId.Value, request.MeasurementType,
                CaveStructureComparisonLimits.MaxReadings, ct);

        return TypedResults.Ok(Plot(rows, request.MeasurementType, null));
    }

    // Resolved by code, not by a name a person could edit.
    private static Task<long?> ReadingTypeIdAsync(SilexGisDbContext db, CancellationToken ct) =>
        db.FeatureTypes.AsNoTracking()
            .Where(t => t.Code == FeatureTypeSeeds.StructuralMeasurement)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(ct);

    private static StereonetDto Plot(
        IReadOnlyList<StructuralReadingRow> rows, string? measurementType, double? radius)
    {
        var poles = new List<StereonetPoleDto>();
        var lines = new List<LineOrientation>();
        foreach (var row in rows)
        {
            // A stored document an administrator's loosened form let through without a usable
            // orientation is not a reading the net can show.
            if (StructuralMeasurement.ReadOrientation(row.PropertiesJson) is not { } plane)
            {
                continue;
            }

            var pole = StructuralMeasurement.Pole(plane);
            var point = Stereonet.Project(pole);
            lines.Add(pole);
            poles.Add(new StereonetPoleDto(
                row.FeatureId,
                StructuralMeasurement.ReadType(row.PropertiesJson),
                plane.DipDirectionDegrees,
                plane.DipDegrees,
                pole.TrendDegrees,
                pole.PlungeDegrees,
                point.X,
                point.Y));
        }

        return new StereonetDto(poles.Count, measurementType, radius, poles, Stereonet.KambDensity(lines));
    }
}
