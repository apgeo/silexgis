// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>Geometry kind a feature type accepts. Stored values — do not renumber.</summary>
public enum GeometryKind : short
{
    Point = 0,
    Line = 1,
    Polygon = 2,
    Any = 3,
}
