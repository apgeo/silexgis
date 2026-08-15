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

    /// <summary>Stored files — the bytes, as distinct from the document identity they hang
    /// off. File access is derived from attachment targets and the uploader; the right to
    /// add one is Create in <see cref="Documents"/>, since an upload creates a document.
    /// Entries written here therefore currently decide nothing on their own, and the file
    /// routes are due to be brought under the document walk.</summary>
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

    /// <summary>
    /// Documents: the stable identity that versions, their files and their pages hang
    /// off. Document rows carry the owner/caving-group/visibility trio, so a document is
    /// governed like any other owned content — by entries written against it, by its
    /// owner, and by its visibility. Create here answers "who may upload a document",
    /// which for a document store is a first-class question rather than an afterthought.
    /// Document kinds are controlled vocabulary and stay under taxonomies: anyone who may
    /// author a document must not thereby be able to rewrite the metadata schema every
    /// other uploader is measured against.
    /// </summary>
    Documents = 19,

    /// <summary>
    /// Terrain: the elevation surface the 3D scene draws, and the builds that produce it.
    /// An installation-level domain like <see cref="Settings"/>, <see cref="Audit"/> and
    /// <see cref="Jobs"/> — a terrain build has no owner, no caving group and no audience,
    /// because it is an installation asset made from public elevation data and carries no
    /// cave position. So its rows hold none of the owner/caving-group/visibility facts,
    /// entries here are only ever valid at the whole-installation scope, and every check
    /// is asked of the domain rather than of a row. Execute starts a build and publishes
    /// one, Read lists builds with their status and size, Delete removes a retained build.
    /// Value 20 is deliberately skipped: it belongs to a domain being added in parallel,
    /// and this catalogue is append-only — a value is never renumbered or reused.
    /// </summary>
    Terrain = 21,
}
