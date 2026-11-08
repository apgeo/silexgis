// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The odometry schema derived from an invented default dump: each key typed by its default, the
/// machine- and display-specific sections left out, text fixed, and a non-finite default kept out
/// of the editable set.
/// </summary>
public sealed class HdmappingParameterSchemaTests
{
    private const string Dump = """
        {
          "version_info": { "software_version": "9.9.9" },
          "paths": { "current_output_dir": "/work" },
          "misc": { "clear_color": [0.1, 0.2] },
          "loose_key": 4,
          "control": { "iterations": 30, "on": true, "resolution": 0.3, "label": "fixed", "weights": [1, 2.5] },
          "filtering": { "ceiling": "inf", "floor": 0.5 }
        }
        """;

    [Fact]
    public void Each_key_is_typed_by_its_default_and_the_non_parameter_sections_are_left_out()
    {
        using var dump = JsonDocument.Parse(Dump);
        using var schema = JsonDocument.Parse(HdmappingParameterSchema.StepSchemaJson(dump.RootElement));
        var values = schema.RootElement.GetProperty("properties").GetProperty("values");
        var sections = values.GetProperty("properties");

        sections.EnumerateObject().Select(p => p.Name).ShouldBe(["control", "filtering"]);
        var control = sections.GetProperty("control").GetProperty("properties");
        control.GetProperty("iterations").GetProperty("type").GetString().ShouldBe("integer");
        control.GetProperty("on").GetProperty("type").GetString().ShouldBe("boolean");
        control.GetProperty("resolution").GetProperty("type").GetString().ShouldBe("number");
        control.GetProperty("label").GetProperty("const").GetString().ShouldBe("fixed");
        control.GetProperty("weights").GetProperty("items").GetProperty("type").GetString().ShouldBe("number");
        sections.GetProperty("control").GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        values.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();

        var ceiling = sections.GetProperty("filtering").GetProperty("properties").GetProperty("ceiling");
        ceiling.GetProperty("type").GetString().ShouldBe("number");
        ceiling.TryGetProperty("default", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_editable_defaults_hold_only_parameters_with_finite_values()
    {
        using var dump = JsonDocument.Parse(Dump);
        var editable = HdmappingParameterSchema.EditableDefaults(dump.RootElement);

        editable.EnumerateObject().Select(p => p.Name).ShouldBe(["control", "filtering"]);
        editable.GetProperty("filtering").TryGetProperty("ceiling", out _).ShouldBeFalse();
        editable.GetProperty("filtering").GetProperty("floor").GetDouble().ShouldBe(0.5);
        editable.GetProperty("control").GetProperty("iterations").GetInt32().ShouldBe(30);
    }
}
