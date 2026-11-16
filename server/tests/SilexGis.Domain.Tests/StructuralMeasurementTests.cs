// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Strike, dip direction and pole conversions of a structural reading. The worked values are the
/// textbook ones: under the right-hand rule a plane striking 030 and dipping 40 dips towards 120,
/// and its pole plunges 50 towards 300 (as plotted in Lisle and Leyshon, "Stereographic Projection
/// Techniques for Geologists and Civil Engineers", and every structural-geology course since).
/// </summary>
public class StructuralMeasurementTests
{
    [Theory]
    [InlineData(30, 120)]
    [InlineData(0, 90)]
    [InlineData(270, 0)]
    [InlineData(350, 80)]
    [InlineData(360, 90)]
    public void A_right_hand_rule_strike_dips_ninety_degrees_clockwise_of_it(double strike, double dipDirection)
    {
        StructuralMeasurement.DipDirectionFromStrike(strike).ShouldBe(dipDirection, 1e-9);
        StructuralMeasurement.StrikeFromDipDirection(dipDirection).ShouldBe(StructuralMeasurement.NormalizeAzimuth(strike), 1e-9);
    }

    [Fact]
    public void The_pole_trends_opposite_the_dip_direction_and_plunges_at_the_complement_of_the_dip()
    {
        var pole = StructuralMeasurement.Pole(new PlaneOrientation(120, 40));
        pole.TrendDegrees.ShouldBe(300, 1e-9);
        pole.PlungeDegrees.ShouldBe(50, 1e-9);

        // A horizontal bed's pole is vertical; a vertical plane's pole lies in the horizontal.
        StructuralMeasurement.Pole(new PlaneOrientation(0, 0)).PlungeDegrees.ShouldBe(90);
        StructuralMeasurement.Pole(new PlaneOrientation(250, 90)).ShouldBe(new LineOrientation(70, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => StructuralMeasurement.Pole(new PlaneOrientation(10, 91)));
    }

    [Fact]
    public void An_azimuth_of_a_full_turn_is_north()
    {
        StructuralMeasurement.NormalizeAzimuth(360).ShouldBe(0);
        StructuralMeasurement.NormalizeAzimuth(-30).ShouldBe(330);
        StructuralMeasurement.NormalizeAzimuth(725).ShouldBe(5, 1e-9);
    }

    [Fact]
    public void A_strike_entry_is_stored_as_its_dip_direction_and_the_strike_is_not_kept()
    {
        var (json, errors) = StructuralMeasurement.NormalizeProperties(
            """{"measurement_type":"bedding","strike":30,"dip":40,"note":"n"}""");

        errors.ShouldBeEmpty();
        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.TryGetProperty("strike", out _).ShouldBeFalse();
        doc.RootElement.GetProperty("dip_direction").GetDouble().ShouldBe(120);
        doc.RootElement.GetProperty("dip").GetDouble().ShouldBe(40);
        doc.RootElement.GetProperty("note").GetString().ShouldBe("n");
    }

    [Fact]
    public void A_strike_that_contradicts_the_dip_direction_beside_it_is_refused()
    {
        StructuralMeasurement.NormalizeProperties("""{"strike":30,"dip_direction":120,"dip":40}""").Errors.ShouldBeEmpty();
        StructuralMeasurement.NormalizeProperties("""{"strike":30,"dip_direction":300,"dip":40}""").Errors.ShouldNotBeEmpty();
        StructuralMeasurement.NormalizeProperties("""{"strike":"thirty","dip":40}""").Errors.ShouldNotBeEmpty();
        StructuralMeasurement.NormalizeProperties("""{"strike":400,"dip":40}""").Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_document_without_a_strike_is_left_as_it_came_except_a_full_turn()
    {
        const string plain = """{"measurement_type":"joint","dip_direction":45,"dip":80}""";
        StructuralMeasurement.NormalizeProperties(plain).Json.ShouldBe(plain);
        StructuralMeasurement.NormalizeProperties("not json").Json.ShouldBe("not json");

        var (json, _) = StructuralMeasurement.NormalizeProperties("""{"dip_direction":360,"dip":10}""");
        JsonDocument.Parse(json!).RootElement.GetProperty("dip_direction").GetDouble().ShouldBe(0);
    }

    [Fact]
    public void A_stored_orientation_is_read_only_when_it_is_whole_and_in_range()
    {
        StructuralMeasurement.ReadOrientation("""{"dip_direction":120,"dip":40}""").ShouldBe(new PlaneOrientation(120, 40));
        StructuralMeasurement.ReadOrientation("""{"dip_direction":120}""").ShouldBeNull();
        StructuralMeasurement.ReadOrientation("""{"dip_direction":120,"dip":95}""").ShouldBeNull();
        StructuralMeasurement.ReadOrientation("""{"dip_direction":"120","dip":40}""").ShouldBeNull();
        StructuralMeasurement.ReadOrientation("{}").ShouldBeNull();
    }

    [Fact]
    public void The_seeded_schema_requires_the_type_and_orientation_and_lists_the_closed_types()
    {
        using var schema = JsonDocument.Parse(StructuralMeasurement.PropertiesSchemaJson());
        schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["measurement_type", "dip_direction", "dip"], ignoreOrder: true);
        schema.RootElement.GetProperty("properties").GetProperty("measurement_type").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["bedding", "joint", "fault", "cleavage", "fracture", "other"]);
        schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .ShouldBe(["measurement_type", "dip_direction", "dip", "note", "measured_on"], ignoreOrder: true);
    }
}
