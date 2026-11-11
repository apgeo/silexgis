// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Metadata;

namespace SilexGis.Api.Tests;

/// <summary>
/// Every scan step's parameter schema is one the installation's validator can evaluate, and it
/// judges parameters the way a recipe relies on: defaults pass, a stranger is refused. The
/// domain holds the schema as text and cannot check this itself.
/// </summary>
public sealed class ScanStepSchemaTests
{
    private readonly JsonSchemaPropertiesValidator validator = new();

    [Fact]
    public void Every_registered_step_has_a_schema_the_validator_can_evaluate()
    {
        foreach (var step in ScanSteps.All)
        {
            validator.ValidateSchema(step.ParameterSchemaJson).ShouldBeEmpty(step.Id);
            validator.Validate(step.ParameterSchemaJson, "{}").ShouldBeEmpty(step.Id);
            // The values an upload is given when nobody chooses: the schema's own defaults,
            // written out, which the schema has to accept back.
            validator.Validate(step.ParameterSchemaJson, ScanSteps.DefaultParameters(step).GetRawText()).ShouldBeEmpty(step.Id);
        }
    }

    [Fact]
    public void Tiling_takes_its_own_parameters_and_refuses_one_nobody_defined()
    {
        var tile = ScanSteps.Find(Py3dtilesTileStep.StepId)!;
        validator.Validate(tile.ParameterSchemaJson, """{"specVersion":"1.0","rgb":false,"intensity":true}""").ShouldBeEmpty();
        validator.Validate(tile.ParameterSchemaJson, """{"specVersion":"1.1"}""").ShouldNotBeEmpty();
        validator.Validate(tile.ParameterSchemaJson, """{"rgb":"yes"}""").ShouldNotBeEmpty();
        validator.Validate(tile.ParameterSchemaJson, """{"voxelM":0.05}""").ShouldNotBeEmpty();

        var inspect = ScanSteps.Find(PointCloudInspectStep.StepId)!;
        validator.Validate(inspect.ParameterSchemaJson, """{"anything":1}""").ShouldNotBeEmpty();
    }
}
