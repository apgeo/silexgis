// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;
using Shouldly;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// The smoke test a product sent back from another computer passes before it is kept, on invented
/// files written to a scratch directory.
/// </summary>
public sealed class ScanExternalOutputCheckTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"silexgis-external-check-{Guid.NewGuid():N}");

    public ScanExternalOutputCheckTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("0.0 1.5 2.5 3.5\n0.1 1.6 2.6 3.6\n")]
    [InlineData("timestamp,x,y,z\r\n0,1,2,3\r\n")]
    [InlineData("0;1;2")]
    public async Task A_text_table_is_a_trajectory(string text) =>
        (await PointCloudProcessing.ExternalOutputProblemAsync(ScanArtifactKind.Trajectory, Write(Encoding.ASCII.GetBytes(text)), TestContext.Current.CancellationToken))
            .ShouldBeNull();

    [Fact]
    public async Task A_first_line_longer_than_the_block_read_is_not_a_trajectory()
    {
        // Twenty megabytes with a separator but no line end: refused from the first 64 KiB, never read whole.
        var bytes = new byte[20 * 1024 * 1024];
        Array.Fill(bytes, (byte)'7');
        bytes[3] = (byte)' ';
        (await PointCloudProcessing.ExternalOutputProblemAsync(ScanArtifactKind.Trajectory, Write(bytes), TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n1 2 3\n")]
    [InlineData("nocolumns\n")]
    public async Task A_file_without_columns_on_its_first_line_is_not_a_trajectory(string text) =>
        (await PointCloudProcessing.ExternalOutputProblemAsync(ScanArtifactKind.Trajectory, Write(Encoding.ASCII.GetBytes(text)), TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

    private string Write(byte[] bytes)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
