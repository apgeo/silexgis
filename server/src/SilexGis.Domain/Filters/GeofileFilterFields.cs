// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Filters;

/// <summary>
/// What a geodata file can be filtered by, beyond the fields every protected row has.
/// </summary>
/// <remarks>
/// The footprint is here, as a position: the envelope of the file's imported rows, which the geofile
/// list shows every reader of the file, decided as a map's footprint is. Not the import state or the
/// feature count, which say how the server's reading of the file went rather than anything about
/// the data in it.
/// </remarks>
public static class GeofileFilterFields
{
    public const string World = "geofile";

    /// <summary>The format the file was uploaded in.</summary>
    public const string Format = "format";

    public static IReadOnlyList<FieldDescriptor> Own { get; } =
    [
        new FieldDescriptor(Format, "filters.fields.fileFormat", FieldKind.Id, Options: "geofileFormats"),
        new FieldDescriptor(SpatialConditions.PositionField, "filters.fields.position", FieldKind.Spatial),
    ];
}
