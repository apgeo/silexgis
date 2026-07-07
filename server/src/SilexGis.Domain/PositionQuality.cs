// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>How an entrance position was determined. Stored values.</summary>
public enum PositionQuality : short
{
    Unknown = 0,
    Gps = 1,
    Map = 2,
    Estimated = 3,
}
