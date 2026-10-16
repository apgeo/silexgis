// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a georeferenced map can be filtered by, beyond the fields every protected row has.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cave a map belongs to is not here.</b> A cave map's footprint is the cave's location, so
/// the map list leaves out every map whose cave the reader may not place exactly. Offered as a
/// condition, "maps of cave X" would either repeat that rule a second time or, forgetting it, let
/// somebody count the maps of a protected cave they are not entitled to locate.
/// </para>
/// <para>
/// Nor is the footprint, which is a position, nor the processing state, which says something about
/// the server's queue rather than about the map.
/// </para>
/// </remarks>
public static class GeoreferencedMapFilterFields
{
    public const string World = "georeferencedMap";

    /// <summary>Geological, topographic, tourist, cave map or other — the map's own declared kind.</summary>
    public const string MapKind = "mapKind";

    public static IReadOnlyList<FieldDescriptor> Own { get; } =
    [
        new FieldDescriptor(MapKind, "filters.fields.mapKind", FieldKind.Id, Options: "mapKinds"),
    ];
}
