// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace SilexGis.Api.Tests;

/// <summary>
/// A test class that is both <see cref="IAsyncLifetime"/> and <see cref="IDisposable"/> has its
/// <c>DisposeAsync</c> called and its <c>Dispose</c> not: xunit v3 disposes a test class one way,
/// asynchronously when it can. A <c>Dispose</c> left to the framework then never runs — the class's
/// application is never stopped, the fast test mode's shared application is never handed back, and
/// a run slows to a crawl leaking hosts. So <c>DisposeAsync</c> calls it, in every such class.
/// </summary>
public sealed class TestClassDisposalTests
{
    private static readonly Regex ClassDeclaration = new(
        @"^(?:public |internal )?(?:sealed |abstract )?(?:partial )?class (\w+)[^{]*", RegexOptions.Multiline);

    /// <summary>The class's own Dispose called as a statement — not some field's.</summary>
    private static readonly Regex OwnDispose = new(@"(?<![.\w])Dispose\(\);");

    [Fact]
    public void Every_class_with_both_disposals_runs_the_synchronous_one_from_the_asynchronous_one()
    {
        var root = Path.GetDirectoryName(SourcePath())!;
        var checkedClasses = 0;
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            var classes = ClassDeclaration.Matches(source);
            for (var i = 0; i < classes.Count; i++)
            {
                var declaration = classes[i].Value;
                if (!declaration.Contains("IAsyncLifetime", StringComparison.Ordinal)
                    || !declaration.Contains("IDisposable", StringComparison.Ordinal))
                {
                    continue;
                }

                var end = i + 1 < classes.Count ? classes[i + 1].Index : source.Length;
                var body = source[classes[i].Index..end];
                var start = body.IndexOf("ValueTask DisposeAsync()", StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                checkedClasses++;
                var close = body.IndexOf("\n    }", start, StringComparison.Ordinal);
                var method = close < 0 ? body[start..] : body[start..close];
                if (!OwnDispose.IsMatch(method))
                {
                    missing.Add($"{Path.GetFileName(file)}: {classes[i].Groups[1].Value}");
                }
            }
        }

        missing.ShouldBeEmpty("call Dispose() from DisposeAsync — xunit v3 calls only the asynchronous one");
        // The scan is looking at real classes: the suite has a great many of this shape.
        checkedClasses.ShouldBeGreaterThan(100);
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;
}
