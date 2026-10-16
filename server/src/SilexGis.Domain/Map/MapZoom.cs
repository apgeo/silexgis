// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Map;

/// <summary>The zoom range the map works in, on the 256-pixel web-mercator tile pyramid.</summary>
public static class MapZoom
{
    /// <summary>
    /// The highest zoom any map route accepts. A request for more is answered at this one, and a
    /// saved view's camera above it reads as this one.
    /// </summary>
    public const int Highest = 24;
}
