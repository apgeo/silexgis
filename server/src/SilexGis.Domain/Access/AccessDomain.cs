// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// Resource domains access entries scope to. Stored as smallint and append-only:
/// values are part of the schema contract — never renumber, never reuse.
/// Survey models carry no access facts of their own and are governed through their
/// cave's feature row, so they have no domain here.
/// </summary>
public enum AccessDomain : short
{
    /// <summary>Every feature kind: caves, entrances, centerlines, generic — and, through
    /// their cave, 3D survey models.</summary>
    Features = 0,

    TripLogs = 1,

    Geofiles = 2,

    GeoreferencedMaps = 3,

    MapViews = 4,

    /// <summary>Stored files. Staff-grade rights over the file store; ordinary file access
    /// is still derived from attachment targets and the uploader.</summary>
    Files = 5,

    MapLayers = 6,

    Tags = 7,

    /// <summary>The parallel-hierarchy list — organizational only; no per-hierarchy
    /// content semantics ride on it.</summary>
    Hierarchies = 8,

    Taxonomies = 9,

    Cavers = 10,

    CavingGroups = 11,

    Users = 12,

    PermissionGroups = 13,

    /// <summary>Named feature sets. Part of the security surface: entries can hang on a
    /// set, so membership edits move access and are audited like entry edits.</summary>
    FeatureSets = 14,

    Settings = 15,

    MessageTemplates = 16,

    Audit = 17,

    Jobs = 18,
}
