// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Tests.Support;

/// <summary>
/// Where a test class puts the files it scratches: under the build output, never under the system
/// temporary directory.
/// </summary>
/// <remarks>
/// On the machine this suite is developed on, the system temporary directory is a 16 GB tmpfs —
/// memory. Eighty classes writing uploads, rasters and key rings into it took that memory from
/// every other process on the box, and when it filled, the shell's own output capture broke and a
/// running suite failed wholesale at login while the root disk reported tens of gigabytes free.
/// The build output is on disk, is reclaimed by <c>dotnet clean</c>, and is where the factory
/// already keeps each class's file store. A script test over the test sources keeps the system
/// directory out of them.
/// </remarks>
internal static class TestScratch
{
    /// <summary>A directory that exists, under the test assembly's own output.</summary>
    public static string Root { get; } =
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "test-data")).FullName;
}
