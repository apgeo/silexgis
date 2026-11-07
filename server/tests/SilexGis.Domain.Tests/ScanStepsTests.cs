// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

public class ScanStepsTests
{
    [Fact]
    public void Every_step_has_a_distinct_lower_case_dotted_id_and_is_found_by_it()
    {
        ScanSteps.All.ShouldNotBeEmpty();
        ScanSteps.All.Select(s => s.Id).ShouldBeUnique();
        foreach (var step in ScanSteps.All)
        {
            step.Id.ShouldMatch("^[a-z0-9]+\\.[a-z0-9]+$");
            step.DisplayName.ShouldNotBeNullOrWhiteSpace();
            step.Tool.ShouldNotBeNullOrWhiteSpace();
            ScanSteps.Find(step.Id).ShouldBeSameAs(step);
        }

        ScanSteps.Find("nobody.knows").ShouldBeNull();
        ScanSteps.Find("").ShouldBeNull();
    }

    [Fact]
    public void The_header_check_runs_in_the_api_and_tiling_in_the_worker()
    {
        var inspect = ScanSteps.Find(PointCloudInspectStep.StepId).ShouldNotBeNull();
        inspect.WhereItRuns.ShouldBe(ScanStepLocation.Api);
        inspect.Tool.ShouldBe(ScanSteps.ApiTool);
        inspect.Inputs.ShouldBe([ScanArtifactKind.PointCloudLaz]);

        var tile = ScanSteps.Find(Py3dtilesTileStep.StepId).ShouldNotBeNull();
        tile.WhereItRuns.ShouldBe(ScanStepLocation.Worker);
        tile.Tool.ShouldBe("py3dtiles");
        tile.Inputs.ShouldContain(ScanArtifactKind.PointCloudLaz);
        tile.Outputs.ShouldBe([ScanArtifactKind.PointCloudTiles]);
    }

    /// <summary>
    /// The schema library lives a layer up; here the schema is held to the shape a form generator
    /// and the hash rely on: an object with named properties, each with a default, and nothing
    /// else allowed. A schema that breaks the library's rules is caught where the library is.
    /// </summary>
    [Fact]
    public void Every_parameter_schema_is_a_closed_object_whose_properties_all_have_defaults()
    {
        foreach (var step in ScanSteps.All)
        {
            using var schema = JsonDocument.Parse(step.ParameterSchemaJson);
            var root = schema.RootElement;
            root.GetProperty("type").GetString().ShouldBe("object", step.Id);
            root.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse(step.Id);
            foreach (var property in root.GetProperty("properties").EnumerateObject())
            {
                property.Value.TryGetProperty("default", out _).ShouldBeTrue($"{step.Id}: {property.Name} has no default");
                property.Value.TryGetProperty("type", out _).ShouldBeTrue($"{step.Id}: {property.Name} has no type");
            }
        }
    }
}

public class SurveyModelKindsTests
{
    [Fact]
    public void Every_format_belongs_to_exactly_one_kind_and_that_kind_lists_it()
    {
        foreach (var format in Enum.GetValues<SurveyModelFormat>())
        {
            var kind = SurveyModelKinds.Of(format);
            SurveyModelKinds.FormatsOf(kind).ShouldContain(format);
            Enum.GetValues<SurveyModelKind>().Where(k => k != kind)
                .ShouldAllBe(other => !SurveyModelKinds.FormatsOf(other).Contains(format));
        }
    }

    [Fact]
    public void Point_clouds_are_a_kind_of_their_own_and_do_not_take_the_line_plots_mark()
    {
        SurveyModelKinds.Of(SurveyModelFormat.Laz).ShouldBe(SurveyModelKind.PointCloud);
        SurveyModelKinds.Of(SurveyModelFormat.Las).ShouldBe(SurveyModelKind.PointCloud);
        SurveyModelKinds.FormatsOf(SurveyModelKind.LinePlot).ShouldBe([SurveyModelFormat.Lox, SurveyModelFormat.Survex3d]);
        SurveyModelKinds.FormatsOf(SurveyModelKind.WallMesh).ShouldBe([SurveyModelFormat.Stl]);
    }
}

public class PointCloudFactsTests
{
    [Fact]
    public void The_facts_of_a_header_round_trip_through_json_and_say_whether_the_file_is_placed()
    {
        var header = new LasHeader(
            1, 4, 0, "scanner", "writer 2.0", new DateOnly(2026, 11, 7),
            375, 1000, 2, 6, true, 30, 0, 123_456_789,
            new LasVector3(0.001, 0.001, 0.001), new LasVector3(100, 200, 300),
            new LasVector3(-5, -6, -7), new LasVector3(5, 6, 7),
            [], new LasCoordinateSystem(LasCrsKind.Projected, 3844, null, null), true);

        var facts = PointCloudFacts.FromHeader(header);
        facts.PointCount.ShouldBe(123_456_789);
        facts.LasVersion.ShouldBe("1.4");
        facts.Compressed.ShouldBeTrue();
        facts.HasRgb.ShouldBeFalse();
        facts.HasIntensity.ShouldBeNull();
        facts.IsGeoreferenced.ShouldBeTrue();
        facts.Crs!.HorizontalEpsg.ShouldBe(3844);
        facts.Min.ShouldBe([-5d, -6d, -7d]);

        var read = PointCloudFacts.FromJson(facts.ToJson()).ShouldNotBeNull();
        read.ShouldBe(facts with { Scale = read.Scale, Offset = read.Offset, Min = read.Min, Max = read.Max });
        read.Max.ShouldBe([5d, 6d, 7d]);
        read.TileBytes.ShouldBeNull();
        facts.ToJson().ShouldNotContain("tileBytes");
    }

    [Fact]
    public void A_header_without_a_coordinate_system_gives_unplaced_facts()
    {
        var header = new LasHeader(
            1, 2, 0, "", "", null, 227, 227, 0, 1, false, 28, 10, null,
            new LasVector3(0.01, 0.01, 0.01), default, default, new LasVector3(1, 1, 1), [], null, false);
        var facts = PointCloudFacts.FromHeader(header);
        facts.IsGeoreferenced.ShouldBeFalse();
        facts.Crs.ShouldBeNull();
        facts.GeneratingSoftware.ShouldBeNull();
        PointCloudFacts.FromJson(null).ShouldBeNull();
        PointCloudFacts.FromJson("not json").ShouldBeNull();
    }
}
