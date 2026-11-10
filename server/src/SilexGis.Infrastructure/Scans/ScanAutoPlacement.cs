// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Infrastructure.Scans;

/// <summary>
/// Places a scan from the coordinate system its file named, once its tiles exist.
///
/// <para>
/// A georeferenced cloud is tiled like any other: the worker subtracts one point of it from every
/// coordinate, and the tiles hold offsets from that point in the file's own grid. The point that
/// was subtracted is therefore a grid coordinate, and carrying it through the projector gives
/// where the tiles' zero stands on the earth; the grid's convergence there says how far the
/// tiles' north is from true north. The tiles are not touched — the scale of a grid metre against
/// a ground metre, which a wall mesh's conversion corrects by rewriting its vertices, is left in
/// them, and is a few decimetres per kilometre at most.
/// </para>
/// </summary>
public static class ScanAutoPlacement
{
    /// <summary>
    /// The placement the file's coordinate system gives for these tiles, or null when it gives
    /// none: no coordinate system, a system this installation cannot build a transform from or
    /// that is not a projected one, or a zero point that lands off the world.
    /// </summary>
    public static ScanPlacement? FromFileCrs(ICoordinateProjector projector, PointCloudFacts facts) =>
        Resolve(projector, facts)?.Placement;

    /// <summary>
    /// The placement and the EPSG code of the system it was made through: the code the header
    /// named, or the one its well-known text was recognised as — null when the text names a
    /// system the register has no number for, which still places the scan.
    /// </summary>
    private static (ScanPlacement Placement, int? Epsg)? Resolve(ICoordinateProjector projector, PointCloudFacts facts)
    {
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Crs is not { } crs || ScanPlacement.TilesZeroPoint(facts) is not { } zero)
        {
            return null;
        }

        // A header may name its system two ways, and current writers use only the second: a
        // GeoTIFF key directory carrying an EPSG code, which LAS 1.0–1.3 files have, or the
        // system's well-known text, which the 1.4 point formats are required to carry and often
        // carry alone. The code is tried first because it is exact; the text when there is no
        // code, or the code is one the register cannot build a transform from.
        int? epsg = null;
        ProjectedPoint? point = null;
        if (crs.HorizontalEpsg is { } code)
        {
            point = projector.ToWgs84(code, zero.X, zero.Y);
            epsg = point is null ? null : code;
        }

        if (point is null && !string.IsNullOrWhiteSpace(crs.Wkt))
        {
            point = projector.ToWgs84(crs.Wkt, zero.X, zero.Y);
            epsg = point is null ? null : projector.EpsgOf(crs.Wkt);
        }

        if (point is not { } landed)
        {
            return null;
        }

        // The zero point's own Z is the height the tiles' zero plane sits at — an altitude in
        // whatever vertical system the file used, left as it is. Clamped into the band a
        // declared height is held to, so a file whose Z is not an altitude at all (a scanner
        // that wrote zero, a grid with a false origin in Z) still places on the plan and only
        // its height is wrong, rather than the whole scan being left unplaced.
        var height = double.IsFinite(zero.Z)
            ? Math.Clamp(zero.Z, ScanPlacement.LowestHeightM, ScanPlacement.HighestHeightM)
            : 0;

        if (!ScanPlacement.IsOnEarth(landed.Longitude, landed.Latitude, height))
        {
            return null;
        }

        return (ScanPlacement.FromFileCrs(landed.Longitude, landed.Latitude, height, landed.ConvergenceDeg), epsg);
    }

    /// <summary>
    /// Writes a placement on the model. One place for the placement's columns, so that a
    /// placement made by hand, one taken from the file, one from the satellite fixes and one
    /// fitted to control points all write the same thing. The control points and their figure
    /// are cleared: they describe the placement they were fitted to, and this is another one —
    /// <see cref="ApplyFit"/> writes them back when it is theirs.
    /// </summary>
    public static void Apply(SurveyModel model, ScanPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(placement);
        model.Anchor = new Point(placement.Longitude, placement.Latitude) { SRID = 4326 };
        model.AnchorHeightM = placement.HeightM;
        model.PlacementHeadingDeg = placement.HeadingDeg;
        model.PlacementPitchDeg = placement.PitchDeg == 0 ? null : placement.PitchDeg;
        model.PlacementRollDeg = placement.RollDeg == 0 ? null : placement.RollDeg;
        model.PlacementSource = placement.Source;
        model.PlacementRmsM = null;
        model.PlacementControlPoints = null;
    }

    /// <summary>Writes a placement fitted to control points, with the pairs and their residuals beside it.</summary>
    public static void ApplyFit(SurveyModel model, ScanControlFit fit, ScanControlRecord record)
    {
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(record);
        Apply(model, fit.Placement);
        model.PlacementRmsM = fit.RmsM;
        model.PlacementControlPoints = record.ToJson();
    }

    /// <summary>The placement the model's columns hold, or null when it is not placed — no anchor or no height.</summary>
    public static ScanPlacement? Read(SurveyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Anchor is { } anchor && model.AnchorHeightM is { } height
            ? new ScanPlacement(
                anchor.X,
                anchor.Y,
                height,
                model.PlacementHeadingDeg ?? 0,
                model.PlacementSource ?? ScanPlacementSource.Manual,
                model.PlacementPitchDeg ?? 0,
                model.PlacementRollDeg ?? 0)
            : null;
    }

    /// <summary>Leaves the model unplaced: its tiles keep their frame and lose their place on the earth.</summary>
    public static void Clear(SurveyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Anchor = null;
        model.AnchorHeightM = null;
        model.PlacementHeadingDeg = null;
        model.PlacementPitchDeg = null;
        model.PlacementRollDeg = null;
        model.PlacementSource = null;
        model.PlacementRmsM = null;
        model.PlacementControlPoints = null;
    }

    /// <summary>
    /// Whether a placement somebody arrived at deliberately stands on the model — by hand or by
    /// control points. Such a placement is kept by every later tiling; the automatic ones, from
    /// the file's coordinate system or from the satellite fixes, are worked out again.
    /// </summary>
    public static bool IsDeliberate(SurveyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.PlacementSource is ScanPlacementSource.Manual or ScanPlacementSource.ControlPoints;
    }

    /// <summary>
    /// Whether, after <see cref="AfterTiling"/>, the scan is still waiting for a placement the
    /// satellite fixes could give: it is unplaced or was placed from the fixes before, its
    /// recording heard GNSS, and its tiles have a zero point to place.
    /// </summary>
    public static bool WantsGnss(SurveyModel model, PointCloudFacts facts)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(facts);
        return model.PlacementSource is null or ScanPlacementSource.Gnss
            && facts.Recording is { HasGnss: true }
            && ScanPlacement.TilesZeroPoint(facts) is not null;
    }

    /// <summary>
    /// After a tiling: a scan placed by hand or by control points is left where it was put,
    /// since the tiles' frame is what the person placed and a new tiling of the same file frames
    /// it the same way; a scan placed from its file, or not placed, is placed from the file
    /// again, because the worker chose this tiling's zero point and it need not be the last
    /// one's. A scan its file cannot place may then be placed from its satellite fixes
    /// (<see cref="WantsGnss"/>), which needs the recording's files and is the caller's to read.
    /// </summary>
    public static void AfterTiling(ICoordinateProjector projector, SurveyModel model, PointCloudFacts facts)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (IsDeliberate(model))
        {
            return;
        }

        if (Resolve(projector, facts) is { } resolved)
        {
            Apply(model, resolved.Placement);
            model.SourceEpsg = resolved.Epsg;
        }
    }
}
