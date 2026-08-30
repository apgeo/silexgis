// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;

namespace SilexGis.Api.Features.WorkAreas;

/// <summary>
/// One stretch of country a club works.
/// </summary>
/// <param name="Id">The feature's own id: a work area is a feature, and this addresses it as one.</param>
/// <param name="Name">What the club calls it.</param>
/// <param name="Description">What the club has written about it, in the feature's own description.</param>
/// <param name="ParentId">
/// The work area this one sits inside, or null at the top level. Stated only when the parent is
/// itself a work area this caller may read, so a link is never offered to a name that will not
/// resolve.
/// </param>
/// <param name="ChildCount">
/// How many work areas sit directly inside this one, counted the same way — so a reader can be
/// told there is another level to open before the shapes for it are asked for.
/// </param>
/// <param name="Geometry">
/// The shape as drawn, or null for an area nobody has outlined yet. A work area may legitimately
/// exist before its boundary does; the board still lists it, and the overview leaves it off the
/// map rather than inventing one.
/// </param>
public sealed record WorkAreaDto(
    Guid Id,
    string? Name,
    string? Description,
    Guid? ParentId,
    int ChildCount,
    GeoJsonGeometry? Geometry);

/// <summary>
/// Every work area in one answer, and whether that answer is all of them.
/// </summary>
/// <param name="Truncated">
/// True when the cap bit. Carried rather than left to be inferred from the count, because a reader
/// comparing the number of areas against the cap is comparing against a number this endpoint is
/// free to change.
/// </param>
public sealed record WorkAreaCollectionDto(IReadOnlyList<WorkAreaDto> Items, bool Truncated);
