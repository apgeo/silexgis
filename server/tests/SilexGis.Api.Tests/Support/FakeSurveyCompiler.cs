// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// A survey compiler for tests: it says it is there, and when asked to compile writes the files it
/// was told to into the project directory with invented content, as a compiler would.
/// </summary>
internal sealed class FakeSurveyCompiler(params string[] outputs) : ISurveyCompiler
{
    public const string FakeVersion = "fake-compiler 1.0";

    private int calls;

    public bool IsAvailable => true;

    public string? Version => FakeVersion;

    /// <summary>How many compiles were asked of it.</summary>
    public int Calls => calls;

    public async Task<SurveyCompileResult> CompileAsync(
        SurveyCompileRequest request, IProgress<SurveyCompileProgress> progress, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        progress.Report(new SurveyCompileProgress("writing", 100));
        foreach (var output in outputs)
        {
            var path = Path.Combine(request.ProjectDirectory, output);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"invented {output}", ct);
        }

        return new SurveyCompileResult(true, 0, "compilation finished: invented", outputs, "invented tail");
    }
}
