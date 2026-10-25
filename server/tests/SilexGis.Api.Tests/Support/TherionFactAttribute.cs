// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// A test that runs the real Therion compiler in its sandbox, on a machine that has both. Elsewhere
/// it is reported skipped, saying why — unless <c>SILEXGIS_TEST_THERION=require</c> is set, and
/// then a missing program is a failure: a gate that names these tests sets it, so that they can
/// never pass there by not running.
/// </summary>
public sealed class TherionFactAttribute : FactAttribute
{
    public const string RequireVariable = "SILEXGIS_TEST_THERION";

    public TherionFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        var required = string.Equals(Environment.GetEnvironmentVariable(RequireVariable), "require", StringComparison.OrdinalIgnoreCase);
        if (!required && (!OperatingSystem.IsLinux() || ExecutableSearch.OnPath("therion") is null || ExecutableSearch.OnPath("bwrap") is null))
        {
            Skip = "Needs therion and bwrap on the search path (set " + RequireVariable + "=require to make their absence a failure).";
        }
    }
}
