// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>Kind of a configured map layer (02-data-model.md §4). Stored values.</summary>
public enum MapLayerKind : short
{
    Xyz = 0,
    Wmts = 1,
    Wms = 2,
    Vector = 3,
    Cog = 4,
}
