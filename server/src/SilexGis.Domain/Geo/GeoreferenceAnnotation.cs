// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Geo;

/// <summary>
/// Reads and writes a placement as a IIIF Georeference Annotation — the open format the Allmaps
/// tools and other IIIF georeferencers exchange — so a sheet placed elsewhere can be brought in, and
/// one placed here taken out.
/// </summary>
/// <remarks>
/// <para>
/// The format names control points in the pixels of the image it targets and on the ground in
/// longitude and latitude, and the outline worth showing as an SVG polygon or rectangle in the same
/// pixels. A placement here names points as fractions of the picture, so pixels are divided by the
/// size the annotation states for its image — an annotation made on a picture of another resolution
/// lands on the same spots.
/// </para>
/// <para>
/// The format itself names two transformations: polynomial (order 1 to 3) and thin-plate spline.
/// The perspective fit and the two-point similarity are written with the names the Allmaps tools
/// use for them (<c>projective</c>, <c>helmert</c>) and read back from those names.
/// </para>
/// </remarks>
public static partial class GeoreferenceAnnotation
{
    public const string Malformed = "georeferenced_map.annotation_invalid";

    /// <summary>
    /// The annotation for a placement whose points are already in longitude and latitude.
    /// </summary>
    /// <param name="wgs84Points">The placement's control points, ground coordinates in EPSG:4326.</param>
    public static JsonObject Write(
        string transformation,
        IReadOnlyList<MapControlPoint> wgs84Points,
        IReadOnlyList<IReadOnlyList<double>>? mask,
        int width,
        int height,
        string canvasId)
    {
        ArgumentNullException.ThrowIfNull(wgs84Points);
        var features = new JsonArray();
        foreach (var p in wgs84Points)
        {
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["properties"] = new JsonObject
                {
                    ["resourceCoords"] = new JsonArray(Round(p.U * width), Round(p.V * height)),
                },
                ["geometry"] = new JsonObject
                {
                    ["type"] = "Point",
                    ["coordinates"] = new JsonArray(p.X, p.Y),
                },
            });
        }

        var outline = mask ?? [[0, 0], [1, 0], [1, 1], [0, 1]];
        var points = string.Join(" ", outline.Select(m => string.Create(
            CultureInfo.InvariantCulture, $"{Round(m[0] * width)},{Round(m[1] * height)}")));

        return new JsonObject
        {
            ["@context"] = new JsonArray(
                "http://iiif.io/api/extension/georef/1/context.json",
                "http://iiif.io/api/presentation/3/context.json"),
            ["type"] = "Annotation",
            ["motivation"] = "georeferencing",
            ["target"] = new JsonObject
            {
                ["type"] = "SpecificResource",
                ["source"] = new JsonObject
                {
                    ["id"] = canvasId,
                    ["type"] = "Canvas",
                    ["width"] = width,
                    ["height"] = height,
                },
                ["selector"] = new JsonObject
                {
                    ["type"] = "SvgSelector",
                    ["value"] = $"<svg width=\"{width}\" height=\"{height}\"><polygon points=\"{points}\" /></svg>",
                },
            },
            ["body"] = new JsonObject
            {
                ["type"] = "FeatureCollection",
                ["transformation"] = TransformationOf(transformation),
                ["features"] = features,
            },
        };
    }

    /// <summary>
    /// The placement an annotation describes, in EPSG:4326, or the reason it cannot be read.
    /// </summary>
    /// <param name="width">The picture's width, used when the annotation states no size of its own.</param>
    /// <param name="height">The picture's height, likewise.</param>
    public static (MapPlacement? Placement, string? Problem) Read(JsonNode? annotation, int width, int height)
    {
        if (annotation is not JsonObject root)
        {
            return (null, "The annotation is not a JSON object.");
        }

        // A page of annotations holding one is accepted too, which is what the tools export.
        if (root["type"]?.GetValue<string>() == "AnnotationPage" && root["items"] is JsonArray { Count: > 0 } items)
        {
            root = items[0] as JsonObject ?? root;
        }

        var source = root["target"]?["source"];
        var w = Number(source?["width"]) ?? width;
        var h = Number(source?["height"]) ?? height;
        if (!(w > 0 && h > 0))
        {
            return (null, "The annotation names no image size, and the sheet has not been drawn.");
        }

        if (root["body"]?["features"] is not JsonArray features)
        {
            return (null, "The annotation's body holds no control points.");
        }

        var points = new List<MapControlPoint>();
        foreach (var feature in features)
        {
            var pixel = feature?["properties"]?["resourceCoords"] as JsonArray;
            var ground = feature?["geometry"]?["coordinates"] as JsonArray;
            if (pixel is not { Count: 2 } || ground is not { Count: >= 2 }
                || Number(pixel[0]) is not { } px || Number(pixel[1]) is not { } py
                || Number(ground[0]) is not { } lon || Number(ground[1]) is not { } lat)
            {
                return (null, "A control point lacks its image or ground coordinates.");
            }

            var label = feature?["properties"]?["label"] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
            points.Add(new MapControlPoint(px / w, py / h, lon, lat, label));
        }

        var transformation = TransformationFrom(root["body"]?["transformation"], points.Count);
        if (transformation is null)
        {
            return (null, "The annotation names a transformation this server does not have.");
        }

        IReadOnlyList<IReadOnlyList<double>>? mask = null;
        if (root["target"]?["selector"]?["value"] is JsonValue svg && svg.TryGetValue<string>(out var svgText))
        {
            mask = MaskFrom(svgText, w, h);
        }

        return (new MapPlacement(MapPlacement.CurrentVersion, transformation, "EPSG:4326", points, mask), null);
    }

    private static JsonObject TransformationOf(string transformation) => transformation switch
    {
        MapTransformations.Affine => Polynomial(1),
        MapTransformations.Polynomial2 => Polynomial(2),
        MapTransformations.Polynomial3 => Polynomial(3),
        MapTransformations.ThinPlateSpline => new JsonObject { ["type"] = "thinPlateSpline" },
        MapTransformations.Homography => new JsonObject { ["type"] = "projective" },
        MapTransformations.Similarity => new JsonObject { ["type"] = "helmert" },
        _ => Polynomial(1),
    };

    private static JsonObject Polynomial(int order) =>
        new() { ["type"] = "polynomial", ["options"] = new JsonObject { ["order"] = order } };

    private static string? TransformationFrom(JsonNode? node, int points)
    {
        // Absent means the format's default, a first-order polynomial — or, below three points,
        // the only fit two points allow.
        if (node is null)
        {
            return points == 2 ? MapTransformations.Similarity : MapTransformations.Affine;
        }

        var type = node["type"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : null;
        return type switch
        {
            "polynomial" or "polynomial1" => (Number(node["options"]?["order"]) ?? 1) switch
            {
                1 => MapTransformations.Affine,
                2 => MapTransformations.Polynomial2,
                3 => MapTransformations.Polynomial3,
                _ => null,
            },
            "polynomial2" => MapTransformations.Polynomial2,
            "polynomial3" => MapTransformations.Polynomial3,
            "thinPlateSpline" => MapTransformations.ThinPlateSpline,
            "projective" => MapTransformations.Homography,
            "helmert" => MapTransformations.Similarity,
            _ => null,
        };
    }

    private static IReadOnlyList<IReadOnlyList<double>>? MaskFrom(string svg, double w, double h)
    {
        var polygon = PolygonPoints().Match(svg);
        if (polygon.Success)
        {
            var numbers = Numbers().Matches(polygon.Groups[1].Value)
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
            if (numbers.Count >= 6 && numbers.Count % 2 == 0)
            {
                return [.. Enumerable.Range(0, numbers.Count / 2)
                    .Select(i => (IReadOnlyList<double>)[numbers[2 * i] / w, numbers[(2 * i) + 1] / h])];
            }

            return null;
        }

        var element = RectElement().Match(svg);
        var rect = element.Success
            ? RectAttributes().Matches(element.Value)
                .GroupBy(m => m.Groups[1].Value)
                .ToDictionary(g => g.Key, g => double.Parse(g.First().Groups[2].Value, CultureInfo.InvariantCulture))
            : [];
        if (rect.TryGetValue("width", out var rw) && rect.TryGetValue("height", out var rh))
        {
            var x = rect.GetValueOrDefault("x");
            var y = rect.GetValueOrDefault("y");
            return [[x / w, y / h], [(x + rw) / w, y / h], [(x + rw) / w, (y + rh) / h], [x / w, (y + rh) / h]];
        }

        return null;
    }

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : null;

    private static double Round(double value) => Math.Round(value, 2);

    [GeneratedRegex("<polygon[^>]*\\bpoints\\s*=\\s*\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PolygonPoints();

    [GeneratedRegex("-?\\d+(?:\\.\\d+)?(?:[eE][-+]?\\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Numbers();

    [GeneratedRegex("<rect[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex RectElement();

    [GeneratedRegex("\\b(x|y|width|height)\\s*=\\s*\"(-?\\d+(?:\\.\\d+)?)\"", RegexOptions.CultureInvariant)]
    private static partial Regex RectAttributes();
}
