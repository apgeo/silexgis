// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A profile's values laid over a step's defaults, the odometry set completed with the dumped
/// defaults, and the built-ins' definitions — against invented defaults only.
/// </summary>
public sealed class ProcessingProfileValuesTests
{
    private static readonly JsonElement Dumped = Json("""
        {
          "version_info": { "software_version": "9.9.9" },
          "control": { "iterations": 30, "resolution": 0.3, "window": [1, 2, 3] },
          "filter": { "min_distance": 0.5 }
        }
        """);

    [Fact]
    public void Values_are_laid_over_the_defaults_key_by_key_and_a_list_is_replaced_whole()
    {
        var laid = ProcessingProfileValues.LayOver(Dumped, Json("""{"control":{"iterations":12,"window":[9]},"extra":{"k":1}}"""));

        laid.GetProperty("control").GetProperty("iterations").GetInt32().ShouldBe(12);
        laid.GetProperty("control").GetProperty("resolution").GetDouble().ShouldBe(0.3);
        laid.GetProperty("control").GetProperty("window").GetArrayLength().ShouldBe(1);
        laid.GetProperty("filter").GetProperty("min_distance").GetDouble().ShouldBe(0.5);
        laid.GetProperty("version_info").GetProperty("software_version").GetString().ShouldBe("9.9.9");
        laid.GetProperty("extra").GetProperty("k").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void An_odometry_set_is_completed_with_the_dump_and_a_whole_one_is_left_alone()
    {
        var empty = Recipe("""{"values":{}}""");
        JsonElement.DeepEquals(PointCloudRecipes.CompletedOdometryValues(empty, Dumped)!.Value, Dumped).ShouldBeTrue();

        var partial = Recipe("""{"values":{"control":{"iterations":12}}}""");
        var completed = PointCloudRecipes.CompletedOdometryValues(partial, Dumped)!.Value;
        completed.GetProperty("control").GetProperty("iterations").GetInt32().ShouldBe(12);
        completed.GetProperty("filter").GetProperty("min_distance").GetDouble().ShouldBe(0.5);

        var whole = PointCloudRecipes.WithOdometryValues(partial, completed);
        PointCloudRecipes.CompletedOdometryValues(whole, Dumped).ShouldBeNull();
        PointCloudRecipes.CompletedOdometryValues(new ScanRecipe([ScanRecipes.Step(PointCloudInspectStep.StepId)]), Dumped).ShouldBeNull();
    }

    [Fact]
    public void An_odometry_set_completed_from_the_editable_defaults_holds_no_machine_section_and_no_non_finite_value()
    {
        var dump = Json("""
            {
              "version_info": { "software_version": "9.9.9" },
              "paths": { "work": "/x" },
              "control": { "iterations": 30, "resolution": 0.3, "missing": "nan", "limit": "inf" }
            }
            """);
        var completed = PointCloudRecipes.CompletedOdometryValues(
            Recipe("""{"values":{"control":{"iterations":12}}}"""), HdmappingParameterSchema.EditableDefaults(dump))!.Value;

        completed.TryGetProperty("version_info", out _).ShouldBeFalse();
        completed.TryGetProperty("paths", out _).ShouldBeFalse();
        completed.GetProperty("control").GetProperty("iterations").GetInt32().ShouldBe(12);
        completed.GetProperty("control").TryGetProperty("missing", out _).ShouldBeFalse();
        completed.GetProperty("control").TryGetProperty("limit", out _).ShouldBeFalse();
    }

    [Fact]
    public void Replacing_a_steps_parameters_hashes_them_afresh()
    {
        var recipe = Recipe("""{"values":{}}""");
        var changed = PointCloudRecipes.WithStepParameters(recipe, HdmappingOdometryStep.StepId, Json("""{"values":{"control":{"iterations":1}}}"""));

        var step = changed.Steps.Single(s => s.StepId == HdmappingOdometryStep.StepId);
        step.ParamsHash.ShouldBe(ScanRecipes.ParamsHash(step.Parameters));
        step.ParamsHash.ShouldNotBe(recipe.Steps.Single(s => s.StepId == HdmappingOdometryStep.StepId).ParamsHash);
        changed.Steps.Single(s => s.StepId == PointCloudInspectStep.StepId)
            .ShouldBe(recipe.Steps.Single(s => s.StepId == PointCloudInspectStep.StepId));
    }

    [Fact]
    public void The_built_ins_are_distinct_sets_in_the_shape_a_recipe_stores()
    {
        BuiltInProcessingProfiles.All.Select(p => p.Id).Distinct().Count().ShouldBe(BuiltInProcessingProfiles.All.Count);
        BuiltInProcessingProfiles.All.Select(p => p.Name).Distinct().Count().ShouldBe(BuiltInProcessingProfiles.All.Count);
        foreach (var builtIn in BuiltInProcessingProfiles.All.Where(p => p.StepId == HdmappingOdometryStep.StepId))
        {
            Json(builtIn.ValuesJson).GetProperty("values").ValueKind.ShouldBe(JsonValueKind.Object);
        }

        // A surface profile is the step's own parameters, a plain object of keys its schema names.
        var surface = new MeshSurfaceStep();
        var known = Json(surface.ParameterSchemaJson).GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        var surfaces = BuiltInProcessingProfiles.All.Where(p => p.StepId == MeshSurfaceStep.StepId).ToList();
        surfaces.Select(p => p.Id).ShouldBe([BuiltInProcessingProfiles.SurfaceDefaultsId, BuiltInProcessingProfiles.SurfaceQuickLookId]);
        foreach (var builtIn in surfaces)
        {
            Json(builtIn.ValuesJson).EnumerateObject().Select(p => p.Name).ShouldAllBe(name => known.Contains(name));
        }

        BuiltInProcessingProfiles.All.Select(p => p.StepId).Distinct().ShouldBe([HdmappingOdometryStep.StepId, MeshSurfaceStep.StepId], ignoreOrder: true);

        Json(BuiltInProcessingProfiles.All.Single(p => p.Id == BuiltInProcessingProfiles.HdmappingDefaultsId).ValuesJson)
            .GetProperty("values").EnumerateObject().ShouldBeEmpty();
        BuiltInProcessingProfiles.IsBuiltIn(BuiltInProcessingProfiles.FastPreviewId).ShouldBeTrue();
        BuiltInProcessingProfiles.IsBuiltIn(Guid.NewGuid()).ShouldBeFalse();
    }

    private static ScanRecipe Recipe(string odometryParameters) => new(
    [
        ScanRecipes.Step(PointCloudInspectStep.StepId),
        ScanRecipes.Step(HdmappingOdometryStep.StepId, Json(odometryParameters)),
    ]);

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
