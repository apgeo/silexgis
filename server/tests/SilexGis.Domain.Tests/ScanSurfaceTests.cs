// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

public class ScanSurfaceTests
{
    [Fact]
    public void The_surface_step_runs_in_the_worker_and_writes_a_mesh_from_the_cloud_and_trajectory()
    {
        var step = ScanSteps.Find(MeshSurfaceStep.StepId).ShouldNotBeNull();
        step.WhereItRuns.ShouldBe(ScanStepLocation.Worker);
        step.Tool.ShouldBe("open3d");
        step.Inputs.ShouldBe([ScanArtifactKind.PointCloudLaz, ScanArtifactKind.Trajectory]);
        step.Outputs.ShouldBe([ScanArtifactKind.MeshStl]);

        var defaults = ScanSteps.DefaultParameters(step);
        defaults.GetProperty("voxelCm").GetDouble().ShouldBe(2);
        defaults.GetProperty("poissonDepth").GetInt32().ShouldBe(10);
        defaults.GetProperty("targetTriangles").GetInt32().ShouldBe(500_000);

        // No surface may be larger than the largest wall mesh a person may upload.
        using var schema = JsonDocument.Parse(step.ParameterSchemaJson);
        var most = schema.RootElement.GetProperty("properties").GetProperty("targetTriangles").GetProperty("maximum").GetInt64();
        most.ShouldBe(MeshSurfaceStep.MaxTriangles);
        (most * 50).ShouldBeLessThanOrEqualTo(100L * 1024 * 1024);
    }

    [Fact]
    public void Asking_for_a_surface_appends_the_step_after_the_rest_and_the_saved_recipe_drops_it_again()
    {
        var tiling = new ScanRecipe([
            ScanRecipes.StepWithDefaults(new PointCloudInspectStep()),
            ScanRecipes.StepWithDefaults(new Py3dtilesTileStep()),
        ], DisplayChosen: true);
        var request = new ScanSurfaceRequest(Guid.NewGuid(), Guid.NewGuid());

        var asked = PointCloudRecipes.WithSurface(tiling, request);
        asked.Steps.Select(s => s.StepId).ShouldBe([PointCloudInspectStep.StepId, Py3dtilesTileStep.StepId, MeshSurfaceStep.StepId]);
        asked.Surface.ShouldBe(request);
        asked.DisplayChosen.ShouldBeTrue();
        // Every step before the surface is reused: asking for one never tiles the cloud again.
        asked.MayReuse(Py3dtilesTileStep.StepId).ShouldBeTrue();
        asked.MayReuse(MeshSurfaceStep.StepId).ShouldBeFalse();

        // Asked twice, the recipe still has one surface step.
        PointCloudRecipes.WithSurface(asked, request).Steps.Count(s => s.StepId == MeshSurfaceStep.StepId).ShouldBe(1);

        // It survives the upload's options document, which carries it to the run.
        var stored = ScanRecipes.FromOptionsJson(ScanRecipes.ToOptionsJson(asked)).ShouldNotBeNull();
        stored.Surface.ShouldBe(request);
        stored.RerunFrom.ShouldBe(MeshSurfaceStep.StepId);

        var saved = PointCloudRecipes.WithoutSurface(stored);
        saved.Steps.Select(s => s.StepId).ShouldBe([PointCloudInspectStep.StepId, Py3dtilesTileStep.StepId]);
        saved.Surface.ShouldBeNull();
        saved.RerunFrom.ShouldBeNull();
        saved.DisplayChosen.ShouldBeTrue();
    }

    [Fact]
    public void Placing_a_surface_turns_its_corners_and_normals_into_east_north_up_by_the_placement()
    {
        // One triangle in the tile frame: at the zero point, one along +x, two along +y; normal up.
        var input = Stl([(0, 0, 1), (0, 0, 0), (1, 0, 0), (0, 2, 0)]);
        // The tiles' +Y points east: a quarter turn clockwise from north.
        var placement = ScanPlacement.Manual(25, 45, 300, 90);
        using var output = new MemoryStream();

        ScanSurfaceStl.Place(new MemoryStream(input), output, placement).ShouldBe(1);

        var written = output.ToArray();
        written.Length.ShouldBe(input.Length);
        written.AsSpan(0, 84).ToArray().ShouldBe(input.AsSpan(0, 84).ToArray());
        var floats = Floats(written);
        // Normal stays up.
        Near(floats[0..3], (0, 0, 1));
        // The tiles' +x is south when their +y is east; +y is east.
        Near(floats[3..6], (0, 0, 0));
        Near(floats[6..9], (0, -1, 0));
        Near(floats[9..12], (2, 0, 0));
    }

    [Fact]
    public void A_surface_cut_short_or_holding_nothing_is_refused()
    {
        var placement = ScanPlacement.Manual(25, 45, 300, 0);
        var whole = Stl([(0, 0, 1), (0, 0, 0), (1, 0, 0), (0, 1, 0)]);
        Should.Throw<InvalidDataException>(() => ScanSurfaceStl.Place(new MemoryStream(whole[..^10]), new MemoryStream(), placement));

        var empty = new byte[84];
        Should.Throw<InvalidDataException>(() => ScanSurfaceStl.Place(new MemoryStream(empty), new MemoryStream(), placement));
        Should.Throw<InvalidDataException>(() => ScanSurfaceStl.Place(new MemoryStream(new byte[20]), new MemoryStream(), placement));
    }

    private static byte[] Stl((double X, double Y, double Z)[] normalThenCorners)
    {
        var bytes = new byte[84 + 50];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), 1);
        for (var i = 0; i < 4; i++)
        {
            var at = bytes.AsSpan(84 + (i * 12));
            BinaryPrimitives.WriteSingleLittleEndian(at, (float)normalThenCorners[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(at[4..], (float)normalThenCorners[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(at[8..], (float)normalThenCorners[i].Z);
        }

        return bytes;
    }

    private static float[] Floats(byte[] stl) =>
        Enumerable.Range(0, 12).Select(i => BinaryPrimitives.ReadSingleLittleEndian(stl.AsSpan(84 + (i * 4)))).ToArray();

    private static void Near(float[] actual, (double X, double Y, double Z) expected)
    {
        actual[0].ShouldBe((float)expected.X, 1e-5f);
        actual[1].ShouldBe((float)expected.Y, 1e-5f);
        actual[2].ShouldBe((float)expected.Z, 1e-5f);
    }
}
