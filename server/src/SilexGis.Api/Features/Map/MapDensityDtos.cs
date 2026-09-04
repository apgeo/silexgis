// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace SilexGis.Api.Features.Map;

/// <summary>One cell of a density grid.</summary>
/// <param name="Count">
/// Entrances counted in the cell. Every entrance the caller may read is counted, including those
/// whose exact position is closed to them — a count that left those out would fall by one for each
/// hidden cave, which is a disclosure of a different shape.
/// </param>
/// <param name="AreaKm2">
/// The cell's ground area, measured on the spheroid. Cells in the same grid do not all have the
/// same area: a cell of a given height in degrees is narrower on the ground the further it is from
/// the equator.
/// </param>
/// <param name="DensityPerKm2">
/// The count over the area actually being asked about — the part of the cell inside the study area
/// when one was named, and the whole cell when none was.
/// </param>
/// <param name="StudyAreaFraction">
/// How much of the cell lies inside the study-area outline, from 0 to 1. Null when no study area
/// was named.
/// </param>
/// <param name="KernelDensityPerKm2">
/// The smoothed intensity at this cell's centre — the kernel surface read at one point. It differs
/// from <paramref name="DensityPerKm2"/> deliberately: that one is what this cell holds, this one
/// is what the neighbourhood holds, so a cell that happens to be empty between two full ones is
/// nought on the first and clearly not nought on the second.
/// </param>
public sealed record DensityCellDto(
    double West,
    double South,
    double East,
    double North,
    int Count,
    double AreaKm2,
    double DensityPerKm2,
    double? StudyAreaFraction,
    double KernelDensityPerKm2);

/// <summary>
/// How thickly cave entrances sit over a window, counted into a grid.
/// </summary>
/// <param name="CellMetres">The cell size actually used, which is the one that was asked for.</param>
/// <param name="MinimumCellMetres">
/// The finest cell this installation will publish. Served so a client can offer the range rather
/// than discover its edge by being refused.
/// </param>
/// <param name="ProtectionGridMetres">
/// The grid protected coordinates were rounded onto before being counted. It is what
/// <paramref name="MinimumCellMetres"/> is derived from, and it is published because a density
/// figure means something different when the positions behind it are known to that precision.
/// </param>
/// <param name="StudyAreaKm2">
/// The ground area of the study outline inside this window, or null when none was named. This is
/// the denominator the overall density is taken over — not the outline's whole area, since the
/// window may cut it.
/// </param>
/// <param name="FeatureCount">
/// How many entrances the grid counted in total: the n behind every number here, stated because a
/// density over a set the caller cannot enumerate is otherwise unreadable.
/// </param>
/// <param name="BandwidthMetres">
/// The kernel's standard deviation, in metres. Stated because a kernel surface without its
/// bandwidth is unreadable — the same data at two bandwidths is two different maps, and neither is
/// wrong.
/// </param>
/// <param name="MinimumBandwidthMetres">
/// The narrowest kernel this installation will publish, which is the cell size. Smoothing cannot
/// recover detail the binning has already removed, so a finer kernel would only draw each cell as
/// a separate blob and invite a reader to take the peaks for cave positions.
/// </param>
public sealed record DensityGridDto(
    double CellMetres,
    double MinimumCellMetres,
    double ProtectionGridMetres,
    double BandwidthMetres,
    double MinimumBandwidthMetres,
    Guid? StudyAreaId,
    double? StudyAreaKm2,
    int FeatureCount,
    int CellCount,
    IReadOnlyList<DensityCellDto> Cells);

/// <param name="Bbox">The window, as <c>west,south,east,north</c> in degrees.</param>
/// <param name="CellMetres">
/// Grid cell size in metres. Defaults to the finest this installation permits, which is the
/// location-protection grid.
/// </param>
/// <param name="AreaId">
/// An outline to normalise by, so cells half outside the karst are divided by the half that is
/// karst. The caller must be able to read it and to place it exactly.
/// </param>
/// <param name="BandwidthMetres">
/// The kernel's standard deviation for the smoothed surface. Defaults to twice the cell, and may
/// not be finer than the cell.
/// </param>
/// <remarks>
/// Every property names its query string explicitly. Bound as a parameter record the framework
/// would publish the C# names, so the contract would advertise <c>Bbox</c> and <c>CellMetres</c>
/// while every other endpoint in this API advertises <c>bbox</c> and <c>zoom</c>. Model binding is
/// case-insensitive, so a hand-written request works either way and nothing fails — but the
/// generated client is written from the contract, so it would send the capitalised spelling for
/// these three routes alone, and the inconsistency would be invisible until somebody compared two
/// URLs.
/// </remarks>
public sealed record MapDensityRequest(
    [property: FromQuery(Name = "bbox")] string? Bbox,
    [property: FromQuery(Name = "cellMetres")] double? CellMetres,
    [property: FromQuery(Name = "areaId")] Guid? AreaId,
    [property: FromQuery(Name = "bandwidthMetres")] double? BandwidthMetres);

/// <summary>
/// The bounds on a density request, in one place so the validator and the handler cannot drift
/// apart.
/// </summary>
public static class MapDensityLimits
{
    /// <summary>
    /// The most cells one request builds. A window is enumerated whole, empty cells included, so
    /// this is what stops a wide window at a fine cell from becoming a result nobody can draw. It
    /// is checked against the cell count computed from the window, not against the rows returned,
    /// so the refusal happens before the query runs.
    /// </summary>
    public const int MaxCells = 5_000;

    /// <summary>
    /// The coarsest cell that means anything. Beyond a few hundred kilometres a cell is not a
    /// density estimate, it is the whole window with extra steps.
    /// </summary>
    public const double MaxCellMetres = 500_000d;

    /// <summary>
    /// The widest kernel worth drawing. Beyond it the surface is one broad hill over the whole
    /// window whatever the data does.
    /// </summary>
    public const double MaxBandwidthMetres = 500_000d;
}

public sealed class MapDensityRequestValidator : AbstractValidator<MapDensityRequest>
{
    public MapDensityRequestValidator()
    {
        RuleFor(x => x.Bbox).NotEmpty();

        // The lower bound is deliberately not here. It depends on this installation's
        // location-protection grid and it has to refuse with a code a client can branch on, and a
        // validation failure carries one code for every rule in the request. So the floor is
        // enforced in the handler; what is left for the validator is the arithmetic that is wrong
        // at any setting.
        RuleFor(x => x.CellMetres)
            .GreaterThan(0)
            .LessThanOrEqualTo(MapDensityLimits.MaxCellMetres)
            .When(x => x.CellMetres is not null);

        // The floor under the bandwidth is the cell size, which is not known here for the same
        // reason the floor under the cell is not: it depends on the request and on the
        // installation. What is left for the validator is the arithmetic that is wrong at any
        // setting.
        RuleFor(x => x.BandwidthMetres)
            .GreaterThan(0)
            .LessThanOrEqualTo(MapDensityLimits.MaxBandwidthMetres)
            .When(x => x.BandwidthMetres is not null);
    }
}
