// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using Shouldly;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// Every write that holds a versioned row also ends the hold.
/// </summary>
/// <remarks>
/// A hold opens the request's transaction and keeps it until the handler commits through the
/// hold's answer. A handler that saves under the hold and never ends it is rolled back when the
/// hold is disposed, while its caller is answered as though the write had landed. Disposal cannot
/// refuse that loudly — it also runs while another exception is unwinding, and a throw there would
/// replace the real failure — so the mistake is caught here, in the source, where it is cheapest.
/// A file is counted as a whole: each hold it takes needs an answer or a commit beside it.
/// </remarks>
public class VersionHoldGuardTests
{
    private static readonly Regex Holds = new(@"\bConcurrency\.HoldIfMatchAsync\(", RegexOptions.Compiled);
    private static readonly Regex Ends = new(@"\bhold\.(?:AnswerAsync|CommitAsync)\(", RegexOptions.Compiled);

    [Fact]
    public void Every_file_that_takes_a_version_hold_ends_each_one()
    {
        var holding = new List<string>();
        var offenders = new List<string>();
        foreach (var (file, source) in TripSoftDeleteGuardTests.Sources())
        {
            var code = CodeOnly(source);
            var holds = Holds.Matches(code).Count;
            if (holds == 0)
            {
                continue;
            }

            holding.Add(file);
            var ends = Ends.Matches(code).Count;
            if (ends < holds)
            {
                offenders.Add($"{file}: {holds} holds, {ends} answers or commits");
            }
        }

        // The fixture half: the scan reads the writes known to hold a version. A scan that found
        // none would pass for ever.
        holding.ShouldContain("server/src/SilexGis.Api/Features/Caves/CaveEndpoints.cs");
        holding.ShouldContain("server/src/SilexGis.Api/Features/Features/FeatureEndpoints.cs");

        offenders.ShouldBeEmpty(
            "a write that holds a versioned row must end with hold.AnswerAsync or hold.CommitAsync, "
            + "or what it saved is rolled back when the hold is disposed: " + string.Join("; ", offenders));
    }

    /// <summary>The source with its comments taken out, so a hold named in prose is not counted.</summary>
    private static string CodeOnly(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return string.Join('\n', withoutBlocks.Split('\n').Select(line =>
        {
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            return comment >= 0 ? line[..comment] : line;
        }));
    }
}
