// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// A test that builds a scene in a real Blender, on a machine that has one. Elsewhere it is
/// reported skipped, saying why — unless <c>SILEXGIS_TEST_BLENDER=require</c> is set, and then a
/// missing program is a failure, so that a gate naming these tests cannot pass by not running them.
/// The variable may instead hold the path of the Blender to use, for checking against a version
/// other than the one on the search path.
/// </summary>
/// <remarks>
/// The application itself never starts Blender — it writes a bundle for somebody else to render,
/// and a rule in the architecture tests keeps the code that would start one out of the build.
/// These tests start it themselves, from the test project, to read back what the bundle builds.
/// </remarks>
public sealed class BlenderFactAttribute : FactAttribute
{
    public const string Variable = "SILEXGIS_TEST_BLENDER";

    public BlenderFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (Executable is null && !Required)
        {
            Skip = "Needs blender on the search path (set " + Variable + "=require to make its absence a failure, or to a Blender's path to use that one).";
        }
    }

    private static string? Setting => Environment.GetEnvironmentVariable(Variable);

    private static bool Required => string.Equals(Setting, "require", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Blender these tests run, or null when there is none.</summary>
    public static string? Executable =>
        Setting is { Length: > 0 } named && !Required
            ? (File.Exists(named) ? named : null)
            : ExecutableSearch.OnPath("blender");
}
