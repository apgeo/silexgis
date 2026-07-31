// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// Broad grouping of feature kinds, denormalized onto feature rows (from the feature
/// type, by the write service) so map layers and partial indexes can separate point-ish
/// content from large area polygons. Stored values — do not renumber.
/// </summary>
public enum FeatureCategory : short
{
    /// <summary>Surface features: entrances, peaks, walls, sinkholes, …</summary>
    Surface = 0,

    /// <summary>Underground features: caves, centerlines, stalactites, cave sectors, …</summary>
    Underground = 1,

    /// <summary>Extended areas: karst zones, massifs, cave systems' extents, …</summary>
    Area = 2,

    /// <summary>Built structures.</summary>
    Structure = 3,
}
