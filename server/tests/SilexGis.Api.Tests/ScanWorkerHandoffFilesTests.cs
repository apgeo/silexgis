// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// The application's side of the hand-off directory against a worker that does not play fair:
/// links where the application reads or writes, and files too large to be honest. Everything is
/// invented and written to a scratch directory; no database is needed.
/// </summary>
public sealed class ScanWorkerHandoffFilesTests : IDisposable
{
    private readonly string scratch = Path.Combine(TestScratch.Root, $"silexgis-handoff-files-{Guid.NewGuid():N}");
    private readonly string root;
    private readonly string outside;
    private readonly ScanWorkerHandoff handoff;

    public ScanWorkerHandoffFilesTests()
    {
        root = Path.Combine(scratch, "handoff");
        outside = Path.Combine(scratch, "application");
        Directory.CreateDirectory(Path.Combine(root, ScanWorkerHandoff.JobsDirectory));
        Directory.CreateDirectory(outside);
        handoff = new ScanWorkerHandoff(
            Options.Create(new ScanOptions { HandoffRoot = root, ProcessingEnabled = true, PickupSeconds = 5, JobTimeoutSeconds = 30 }),
            TimeProvider.System,
            NullLogger<ScanWorkerHandoff>.Instance)
        {
            PollInterval = TimeSpan.FromMilliseconds(20),
        };
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

    [Fact]
    public async Task An_input_is_not_copied_through_a_link_left_at_its_name()
    {
        var job = await handoff.BeginAsync("pointcloud.estimate", "job-link", TestContext.Current.CancellationToken);
        var target = Path.Combine(outside, "keys.json");
        File.WriteAllText(target, "kept as it was");
        File.CreateSymbolicLink(Path.Combine(job, "pointCloudLaz.laz"), target);
        var source = Path.Combine(scratch, "source.laz");
        File.WriteAllText(source, "the upload");

        await Should.ThrowAsync<ScanHandoffTamperedException>(
            ScanWorkerHandoff.CopyInAsync(job, source, "pointCloudLaz.laz", TestContext.Current.CancellationToken));
        File.ReadAllText(target).ShouldBe("kept as it was");

        // The same source copied into a job directory with nothing at the name arrives whole, so
        // the refusal above is the link's doing and not a copy that refuses everything.
        var plain = await handoff.BeginAsync("pointcloud.estimate", "job-plain-input", TestContext.Current.CancellationToken);
        await ScanWorkerHandoff.CopyInAsync(plain, source, "pointCloudLaz.laz", TestContext.Current.CancellationToken);
        File.ReadAllText(Path.Combine(plain, "pointCloudLaz.laz")).ShouldBe("the upload");
    }

    [Fact]
    public async Task Nothing_is_written_into_a_job_directory_that_has_become_a_link()
    {
        var job = await handoff.BeginAsync("pointcloud.estimate", "job-swapped", TestContext.Current.CancellationToken);
        Directory.Delete(job);
        Directory.CreateSymbolicLink(job, outside);
        var source = Path.Combine(scratch, "source.laz");
        File.WriteAllText(source, "the upload");

        await Should.ThrowAsync<ScanHandoffTamperedException>(
            ScanWorkerHandoff.CopyInAsync(job, source, "pointCloudLaz.laz", TestContext.Current.CancellationToken));
        await Should.ThrowAsync<ScanHandoffTamperedException>(
            ScanWorkerHandoff.IssueAsync(job, "pointcloud.estimate", JsonDocument.Parse("{}").RootElement, new Dictionary<string, string>(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Directory.EnumerateFileSystemEntries(outside).ShouldBeEmpty();

        // A job directory that is still a directory takes both the copy and the request.
        var plain = await handoff.BeginAsync("pointcloud.estimate", "job-plain-directory", TestContext.Current.CancellationToken);
        await ScanWorkerHandoff.CopyInAsync(plain, source, "pointCloudLaz.laz", TestContext.Current.CancellationToken);
        await ScanWorkerHandoff.IssueAsync(plain, "pointcloud.estimate", JsonDocument.Parse("{}").RootElement, new Dictionary<string, string>(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        File.ReadAllText(Path.Combine(plain, "pointCloudLaz.laz")).ShouldBe("the upload");
        File.Exists(Path.Combine(plain, ScanWorkerHandoff.RequestFile)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_result_that_is_a_link_or_too_large_reads_as_unreadable()
    {
        var linked = await handoff.BeginAsync("pointcloud.estimate", "job-linked-result", TestContext.Current.CancellationToken);
        var target = Path.Combine(outside, "settings.json");
        File.WriteAllText(target, """{"status":"ok","outputs":[],"reason":"read from the application's own disk"}""");
        File.CreateSymbolicLink(Path.Combine(linked, ScanWorkerHandoff.ResultFile), target);
        var fromLink = await handoff.WaitAsync(linked, (_, _, _) => Task.CompletedTask, TestContext.Current.CancellationToken);
        fromLink.Status.ShouldBe(ScanJobStatus.Unreadable);
        fromLink.Reason.ShouldNotContain("own disk");

        var swollen = await handoff.BeginAsync("pointcloud.estimate", "job-large-result", TestContext.Current.CancellationToken);
        var padding = new string(' ', 2 * 1024 * 1024);
        File.WriteAllText(Path.Combine(swollen, ScanWorkerHandoff.ResultFile), "{\"status\":\"ok\"," + padding + "\"outputs\":[]}");
        var large = await handoff.WaitAsync(swollen, (_, _, _) => Task.CompletedTask, TestContext.Current.CancellationToken);
        large.Status.ShouldBe(ScanJobStatus.Unreadable);
        large.Reason.ShouldContain("larger");
    }

    [Fact]
    public void The_log_tail_is_read_from_the_end_of_the_log_only()
    {
        var job = Path.Combine(root, ScanWorkerHandoff.JobsDirectory, "job-log");
        Directory.CreateDirectory(job);
        var log = new StringBuilder();
        log.Append(new string('x', 4 * 1024 * 1024)).Append('\n');
        for (var i = 1; i <= 40; i++)
        {
            log.Append("line ").Append(i).Append('\n');
        }

        File.WriteAllText(Path.Combine(job, ScanWorkerHandoff.LogFile), log.ToString());

        var tail = ScanHandoffFiles.TailLines(job, ScanWorkerHandoff.LogFile, 64 * 1024, 15);

        tail.Count.ShouldBe(15);
        tail[0].ShouldBe("line 26");
        tail[^1].ShouldBe("line 40");
    }

    [Fact]
    public void An_announcement_that_is_a_link_is_no_announcement()
    {
        var announcement = """{"worker":"elsewhere","steps":[{"id":"pointcloud.estimate","tool":"laspy","version":"1"}]}""";

        // The same announcement as a plain file in the hand-off directory is read, so the null
        // below is the link's doing and not a reader that answers null to everything.
        var plain = Path.Combine(root, ScanWorkerHandoff.CapabilitiesFile);
        File.WriteAllText(plain, announcement);
        var read = handoff.ReadCapabilities();
        read.ShouldNotBeNull();
        read.Worker.ShouldBe("elsewhere");
        read.Steps.Select(s => s.Id).ShouldBe(["pointcloud.estimate"]);
        File.Delete(plain);

        var target = Path.Combine(outside, "capabilities.json");
        File.WriteAllText(target, announcement);
        File.CreateSymbolicLink(plain, target);

        handoff.ReadCapabilities().ShouldBeNull();
    }
}
