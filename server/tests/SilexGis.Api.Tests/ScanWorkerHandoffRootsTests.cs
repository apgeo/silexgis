// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// The HDMapping worker on a hand-off directory of its own: each step's job is left with the worker
/// that runs it, and each announcement is believed only for that worker's steps. Invented files in
/// a scratch directory; no database.
/// </summary>
public sealed class ScanWorkerHandoffRootsTests : IDisposable
{
    private readonly string scratch = Path.Combine(TestScratch.Root, $"silexgis-handoff-roots-{Guid.NewGuid():N}");
    private readonly string tilerRoot;
    private readonly string hdmappingRoot;

    public ScanWorkerHandoffRootsTests()
    {
        tilerRoot = Path.Combine(scratch, "scans");
        hdmappingRoot = Path.Combine(scratch, "scans-hdmapping");
        Directory.CreateDirectory(tilerRoot);
        Directory.CreateDirectory(hdmappingRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ScanWorkerHandoff Handoff(string? hdmapping) => new(
        Options.Create(new ScanOptions { HandoffRoot = tilerRoot, HdmappingHandoffRoot = hdmapping ?? "", ProcessingEnabled = true }),
        TimeProvider.System,
        NullLogger<ScanWorkerHandoff>.Instance);

    [Fact]
    public async Task Each_step_runs_in_the_directory_of_the_worker_that_runs_it()
    {
        var handoff = Handoff(hdmappingRoot);
        handoff.Roots.ShouldBe([Path.GetFullPath(tilerRoot), Path.GetFullPath(hdmappingRoot)]);

        foreach (var step in new[] { HdmappingOdometryStep.StepId, HdmappingExportStep.StepId, PointCloudCheckStep.StepId })
        {
            var job = await handoff.BeginAsync(step, "u-" + step.Replace('.', '-'), TestContext.Current.CancellationToken);
            Path.GetDirectoryName(job).ShouldBe(Path.Combine(Path.GetFullPath(hdmappingRoot), ScanWorkerHandoff.JobsDirectory), step);
        }

        foreach (var step in new[] { PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId, MeshSurfaceStep.StepId, PointCloudRasterStep.StepId })
        {
            var job = await handoff.BeginAsync(step, "u-" + step.Replace('.', '-'), TestContext.Current.CancellationToken);
            Path.GetDirectoryName(job).ShouldBe(Path.Combine(Path.GetFullPath(tilerRoot), ScanWorkerHandoff.JobsDirectory), step);
        }
    }

    [Fact]
    public void Each_announcement_counts_only_for_its_own_workers_steps()
    {
        // Each worker also claims a step of the other's: neither claim is believed.
        Announce(tilerRoot, "tiler", PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId, HdmappingExportStep.StepId);
        Announce(hdmappingRoot, "hdmapping", HdmappingOdometryStep.StepId, PointCloudCheckStep.StepId, Py3dtilesTileStep.StepId);

        var announced = Handoff(hdmappingRoot).ReadCapabilities().ShouldNotBeNull();

        announced.Steps.Select(s => s.Id).Order().ShouldBe(
            new[] { PointCloudEstimateStep.StepId, Py3dtilesTileStep.StepId, HdmappingOdometryStep.StepId, PointCloudCheckStep.StepId }.Order());
        announced.Find(Py3dtilesTileStep.StepId)!.Version.ShouldBe("tiler-1");
        announced.Find(HdmappingOdometryStep.StepId)!.Version.ShouldBe("hdmapping-1");

        // Only the HDMapping worker has started: what it runs is there, and nothing else.
        File.Delete(Path.Combine(tilerRoot, ScanWorkerHandoff.CapabilitiesFile));
        Handoff(hdmappingRoot).ReadCapabilities().ShouldNotBeNull().Steps.Select(s => s.Id).Order()
            .ShouldBe(new[] { HdmappingOdometryStep.StepId, PointCloudCheckStep.StepId }.Order());
    }

    [Fact]
    public void The_odometry_defaults_are_read_from_the_hdmapping_workers_directory()
    {
        WriteDefaults(tilerRoot, "0.0.0-tiler-side");
        WriteDefaults(hdmappingRoot, "0.0.0-hdmapping-side");

        Handoff(hdmappingRoot).ReadOdometryDefaultsDocument().ShouldNotBeNull().Version.ShouldBe("0.0.0-hdmapping-side");
        // Without a directory of its own, the HDMapping worker shares the tiler's, as before.
        Handoff(null).ReadOdometryDefaultsDocument().ShouldNotBeNull().Version.ShouldBe("0.0.0-tiler-side");
        Handoff(null).Roots.ShouldBe([Path.GetFullPath(tilerRoot)]);
    }

    private static void Announce(string root, string worker, params string[] steps) =>
        File.WriteAllText(
            Path.Combine(root, ScanWorkerHandoff.CapabilitiesFile),
            JsonSerializer.Serialize(new { worker, steps = steps.Select(id => new { id, tool = "t", version = worker + "-1" }) }));

    private static void WriteDefaults(string root, string version)
    {
        Directory.CreateDirectory(Path.Combine(root, ScanWorkerHandoff.DefaultsDirectory));
        File.WriteAllText(
            Path.Combine(root, ScanWorkerHandoff.DefaultsDirectory, ScanWorkerHandoff.OdometryDefaultsFile),
            JsonSerializer.Serialize(new { step = "hdmapping.odometry", version, values = new { lidar_odometry_control = new { decimation = 0.01 } } }));
    }
}
