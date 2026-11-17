// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;
using SilexGis.Infrastructure.Analysis;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// A test that runs the real WhiteboxTools program in its sandbox, on a machine that has it. The
/// program is the one <c>SILEXGIS_TEST_WHITEBOX</c> names by path, else the first
/// <c>whitebox_tools</c> on the search path. Elsewhere the test is reported skipped, saying why —
/// unless the variable is <c>require</c> (or names a path), and then a missing program is a
/// failure: a gate that provides the program sets it, so these tests can never pass there by not
/// running. <c>skip</c> says the machine makes no promise: the tests run where the program is
/// found and are skipped where it is not.
/// </summary>
public sealed class WhiteboxFactAttribute : FactAttribute
{
    public const string Variable = "SILEXGIS_TEST_WHITEBOX";

    private const string Unrequired = "skip";

    public WhiteboxFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        var said = Environment.GetEnvironmentVariable(Variable);
        var required = !string.IsNullOrWhiteSpace(said) && !string.Equals(said, Unrequired, StringComparison.OrdinalIgnoreCase);
        if (!required && (!OperatingSystem.IsLinux() || Program is null || ExecutableSearch.OnPath("bwrap") is null))
        {
            Skip = "Needs whitebox_tools and bwrap (set " + Variable + "=require, or to the program's path, to make their absence a failure).";
        }
    }

    /// <summary>The program these tests run, or null when there is none.</summary>
    public static string? Program
    {
        get
        {
            var said = Environment.GetEnvironmentVariable(Variable);
            return !string.IsNullOrWhiteSpace(said)
                && !string.Equals(said, "require", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(said, Unrequired, StringComparison.OrdinalIgnoreCase)
                ? ExecutableSearch.Resolve(said, SandboxedWhiteboxRunner.ProgramName)
                : ExecutableSearch.OnPath(SandboxedWhiteboxRunner.ProgramName);
        }
    }
}
