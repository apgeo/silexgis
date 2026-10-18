// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;

namespace SilexGis.Api.Common;

/// <summary>
/// The one refusal for a map window that is not a window: four numbers that do not parse, a box
/// past the WGS84 limits, or one whose west/east or south/north pair is the wrong way round.
///
/// <para>
/// A reversed or off-world box handed to PostGIS is not an error there — it is an envelope that
/// either matches nothing or spans the planet many times over — so a route that narrows by it
/// would answer as if the box were real: an empty file, or every row the caller may read under a
/// request that asked for a small part of the map. Every route that takes a window refuses such a
/// box with the same code, so a client branches on one thing wherever the window was sent.
/// </para>
/// </summary>
public static class WindowProblems
{
    public const string InvalidBboxCode = "map.invalid_bbox";

    private const string Detail =
        "bbox must be 'west,south,east,north' inside -180..180 and -90..90, with west < east and south < north.";

    /// <summary>For a route whose window is required: an absent box is refused like a bad one.</summary>
    public static ProblemHttpResult? Required(string? bbox, out Bbox box)
    {
        if (Bbox.TryParse(bbox, out box) && box.IsWithinWorld)
        {
            return null;
        }

        return ApiProblems.BadRequest(InvalidBboxCode, Detail);
    }

    /// <summary>
    /// For a route whose window is optional: no box (absent or blank) means no window, and
    /// anything else must be a real one — a malformed box is refused, never read as "no window".
    /// </summary>
    public static ProblemHttpResult? Optional(string? bbox, out Bbox? box)
    {
        box = null;
        if (string.IsNullOrWhiteSpace(bbox))
        {
            return null;
        }

        if (Required(bbox, out var parsed) is { } problem)
        {
            return problem;
        }

        box = parsed;
        return null;
    }
}
