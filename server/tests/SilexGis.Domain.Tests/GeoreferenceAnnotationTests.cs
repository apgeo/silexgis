// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Nodes;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public sealed class GeoreferenceAnnotationTests
{
    [Fact]
    public void A_placement_written_out_reads_back_the_same()
    {
        IReadOnlyList<MapControlPoint> points =
            [new(0, 0, 24.0, 45.5, "NW"), new(1, 0, 24.1, 45.5), new(1, 1, 24.1, 45.4), new(0.25, 0.75, 24.02, 45.42)];
        IReadOnlyList<IReadOnlyList<double>> mask = [[0.1, 0.1], [0.9, 0.1], [0.9, 0.9], [0.1, 0.9]];

        foreach (var transformation in MapTransformations.All)
        {
            var written = GeoreferenceAnnotation.Write(transformation, points, mask, 2000, 1000, "urn:test");
            var (back, problem) = GeoreferenceAnnotation.Read(JsonNode.Parse(written.ToJsonString()), 0, 0);
            problem.ShouldBeNull(transformation);
            back!.Transformation.ShouldBe(transformation);
            back.Crs.ShouldBe("EPSG:4326");
            back.Gcps.Count.ShouldBe(4);
            back.Gcps[3].U.ShouldBe(0.25, 1e-6);
            back.Gcps[3].V.ShouldBe(0.75, 1e-6);
            back.Gcps[3].X.ShouldBe(24.02);
            back.Mask!.Count.ShouldBe(4);
            back.Mask[2][0].ShouldBe(0.9, 1e-6);
        }
    }

    [Fact]
    public void An_annotation_made_elsewhere_is_read_from_its_own_image_size()
    {
        // The format's own example shape: pixels on a 2000 × 1000 canvas, a rectangle outline, a
        // page of annotations around it, and no transformation named.
        var page = JsonNode.Parse("""
            {"type":"AnnotationPage","items":[{
              "type":"Annotation","motivation":"georeferencing",
              "target":{"type":"SpecificResource",
                "source":{"id":"https://example.org/canvas/1","type":"Canvas","width":2000,"height":1000},
                "selector":{"type":"SvgSelector","value":"<svg width=\"2000\" height=\"1000\"><rect x=\"100\" y=\"100\" width=\"1800\" height=\"800\"/></svg>"}},
              "body":{"type":"FeatureCollection","features":[
                {"type":"Feature","properties":{"resourceCoords":[150,850]},"geometry":{"type":"Point","coordinates":[5.10,52.00]}},
                {"type":"Feature","properties":{"resourceCoords":[1850,850]},"geometry":{"type":"Point","coordinates":[5.20,52.00]}},
                {"type":"Feature","properties":{"resourceCoords":[1000,150]},"geometry":{"type":"Point","coordinates":[5.15,52.05]}}]}}]}
            """);
        var (placement, problem) = GeoreferenceAnnotation.Read(page, 400, 300);
        problem.ShouldBeNull();
        placement!.Transformation.ShouldBe(MapTransformations.Affine);
        placement.Gcps[0].U.ShouldBe(0.075, 1e-9);
        placement.Gcps[0].V.ShouldBe(0.85, 1e-9);
        placement.Mask![0].ShouldBe([0.05, 0.1]);
        placement.Mask[2].ShouldBe([0.95, 0.9]);
        MapPlacementRules.Validate(placement, 200).ShouldBeEmpty();
    }

    [Fact]
    public void An_annotation_that_cannot_be_read_says_why()
    {
        GeoreferenceAnnotation.Read(JsonNode.Parse("[]"), 10, 10).Problem.ShouldNotBeNull();
        GeoreferenceAnnotation.Read(JsonNode.Parse("""{"body":{}}"""), 10, 10).Problem.ShouldNotBeNull();
        GeoreferenceAnnotation.Read(
            JsonNode.Parse("""{"body":{"transformation":{"type":"bendy"},"features":[]}}"""), 10, 10)
            .Problem.ShouldNotBeNull();
        GeoreferenceAnnotation.Read(
            JsonNode.Parse("""{"body":{"features":[{"properties":{"resourceCoords":[1]},"geometry":{"coordinates":[1,2]}}]}}"""), 10, 10)
            .Problem.ShouldNotBeNull();
    }
}
