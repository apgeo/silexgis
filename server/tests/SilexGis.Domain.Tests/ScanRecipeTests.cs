// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

public class ScanRecipeTests
{
    [Fact]
    public void The_same_values_in_another_key_order_hash_the_same()
    {
        var a = ScanRecipes.ParamsHash("""{"rgb": true, "intensity": false, "specVersion": "1.0"}""");
        var b = ScanRecipes.ParamsHash("""{"specVersion":"1.0","intensity":false,"rgb":true}""");
        b.ShouldBe(a);
        a.Length.ShouldBe(64);
        a.ShouldBe(a.ToLowerInvariant());
    }

    [Fact]
    public void Key_order_does_not_matter_at_any_depth_and_whitespace_never_does()
    {
        var a = ScanRecipes.ParamsHash("""{ "voxel": { "sizeM": 0.05, "unit": "m" }, "keep": [1, 2, 3] }""");
        var b = ScanRecipes.ParamsHash("""{"keep":[1,2,3],"voxel":{"unit":"m","sizeM":0.05}}""");
        b.ShouldBe(a);
    }

    [Fact]
    public void A_number_spelt_another_way_is_the_same_number()
    {
        ScanRecipes.ParamsHash("""{"n": 1}""").ShouldBe(ScanRecipes.ParamsHash("""{"n": 1.0}"""));
        ScanRecipes.ParamsHash("""{"n": 100}""").ShouldBe(ScanRecipes.ParamsHash("""{"n": 1e2}"""));
        ScanRecipes.ParamsHash("""{"n": 0.5}""").ShouldBe(ScanRecipes.ParamsHash("""{"n": 5e-1}"""));
    }

    [Fact]
    public void A_different_value_a_different_key_or_a_reordered_list_is_a_different_request()
    {
        var reference = ScanRecipes.ParamsHash("""{"rgb": true, "keep": [1, 2]}""");
        ScanRecipes.ParamsHash("""{"rgb": false, "keep": [1, 2]}""").ShouldNotBe(reference);
        ScanRecipes.ParamsHash("""{"rgba": true, "keep": [1, 2]}""").ShouldNotBe(reference);
        ScanRecipes.ParamsHash("""{"rgb": true, "keep": [2, 1]}""").ShouldNotBe(reference);
        ScanRecipes.ParamsHash("""{"rgb": true}""").ShouldNotBe(reference);
        ScanRecipes.ParamsHash("""{"rgb": "true", "keep": [1, 2]}""").ShouldNotBe(reference);
    }

    [Fact]
    public void An_empty_object_has_one_hash_and_a_step_without_parameters_carries_it()
    {
        var step = ScanRecipes.Step(PointCloudInspectStep.StepId);
        step.ParamsHash.ShouldBe(ScanRecipes.ParamsHash("{}"));
        step.ParamsHash.ShouldBe(ScanRecipes.ParamsHash("  { }  "));
        step.Parameters.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public void A_step_at_its_defaults_carries_every_value_of_its_schema_and_hashes_unlike_an_empty_object()
    {
        var tile = ScanRecipes.StepWithDefaults(ScanSteps.Find(Py3dtilesTileStep.StepId)!);
        tile.StepId.ShouldBe(Py3dtilesTileStep.StepId);
        tile.Parameters.GetProperty("specVersion").GetString().ShouldBe("1.0");
        tile.Parameters.GetProperty("rgb").GetBoolean().ShouldBeTrue();
        tile.Parameters.GetProperty("intensity").GetBoolean().ShouldBeTrue();
        tile.Parameters.EnumerateObject().Count().ShouldBe(3);
        tile.ParamsHash.ShouldBe(ScanRecipes.ParamsHash("""{"intensity":true,"rgb":true,"specVersion":"1.0"}"""));
        tile.ParamsHash.ShouldNotBe(ScanRecipes.ParamsHash("{}"));

        // The schema's defaults are the values, not a reference to the schema: written out and
        // read back, the step says the same values whatever the schema says later.
        var reread = ScanRecipes.FromOptionsJson(ScanRecipes.ToOptionsJson(new ScanRecipe([tile]))).ShouldNotBeNull();
        reread.Steps.Single().Parameters.GetProperty("rgb").GetBoolean().ShouldBeTrue();
        reread.Steps.Single().ParamsHash.ShouldBe(tile.ParamsHash);

        // A step whose schema defines no parameter resolves to the empty object, as before.
        var inspect = ScanRecipes.StepWithDefaults(ScanSteps.Find(PointCloudInspectStep.StepId)!);
        inspect.Parameters.EnumerateObject().Count().ShouldBe(0);
        inspect.ParamsHash.ShouldBe(ScanRecipes.ParamsHash("{}"));
    }

    [Fact]
    public void A_recipe_written_into_the_options_reads_back_as_it_was_written()
    {
        using var tileParameters = JsonDocument.Parse("""{"rgb": false, "intensity": true, "specVersion": "1.0"}""");
        var recipe = new ScanRecipe(
        [
            ScanRecipes.Step(PointCloudInspectStep.StepId),
            ScanRecipes.Step(Py3dtilesTileStep.StepId, tileParameters.RootElement),
        ]);

        var options = ScanRecipes.ToOptionsJson(recipe);
        options.ShouldContain("\"recipe\"");

        var read = ScanRecipes.FromOptionsJson(options).ShouldNotBeNull();
        read.Steps.Count.ShouldBe(2);
        read.Steps[0].StepId.ShouldBe(PointCloudInspectStep.StepId);
        read.Steps[1].StepId.ShouldBe(Py3dtilesTileStep.StepId);
        read.Steps[1].ParamsHash.ShouldBe(recipe.Steps[1].ParamsHash);
        read.Steps[1].Parameters.GetProperty("rgb").GetBoolean().ShouldBeFalse();
        // The hash stored beside the values is the hash of the values, after a round trip too.
        ScanRecipes.ParamsHash(read.Steps[1].Parameters).ShouldBe(read.Steps[1].ParamsHash);
    }

    [Fact]
    public void The_step_to_run_again_from_and_the_display_choice_survive_the_options_round_trip()
    {
        var tile = ScanRecipes.Step(Py3dtilesTileStep.StepId);
        var recipe = new ScanRecipe([ScanRecipes.Step(PointCloudEstimateStep.StepId), tile], RerunFrom: Py3dtilesTileStep.StepId, DisplayChosen: true);
        recipe.MayReuse(PointCloudEstimateStep.StepId).ShouldBeTrue();
        recipe.MayReuse(Py3dtilesTileStep.StepId).ShouldBeFalse();

        var read = ScanRecipes.FromOptionsJson(ScanRecipes.ToOptionsJson(recipe)).ShouldNotBeNull();
        read.RerunFrom.ShouldBe(Py3dtilesTileStep.StepId);
        read.DisplayChosen.ShouldBeTrue();
        read.MayReuse(Py3dtilesTileStep.StepId).ShouldBeFalse();

        // Neither is written when it says nothing, and neither is read into something.
        var plain = ScanRecipes.FromOptionsJson(ScanRecipes.ToOptionsJson(new ScanRecipe([tile]))).ShouldNotBeNull();
        plain.RerunFrom.ShouldBeNull();
        plain.DisplayChosen.ShouldBeFalse();
        plain.MayReuse(Py3dtilesTileStep.StepId).ShouldBeTrue();
    }

    [Fact]
    public void Options_of_another_shape_or_none_carry_no_recipe()
    {
        ScanRecipes.FromOptionsJson(null).ShouldBeNull();
        ScanRecipes.FromOptionsJson("").ShouldBeNull();
        ScanRecipes.FromOptionsJson("{}").ShouldBeNull();
        // A survey archive's run options: compile, outputs, map scale, and no recipe.
        ScanRecipes.FromOptionsJson("""{"compile":true,"outputs":["lox"],"mapScale":500}""").ShouldBeNull();
        ScanRecipes.FromOptionsJson("""{"recipe": 4}""").ShouldBeNull();
        ScanRecipes.FromOptionsJson("""{"recipe": {"steps": [{"stepId": "", "parameters": {}, "paramsHash": ""}]}}""").ShouldBeNull();
        ScanRecipes.FromOptionsJson("not json").ShouldBeNull();
    }
}
