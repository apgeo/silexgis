// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Surveys;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a survey compiler printed while it ran, read out of a log a real compilation actually
/// wrote.
///
/// <para>
/// This is the acceptance test for consuming the log reader at all. The parsing belongs to the
/// library that owns the compiler's other formats and is tested there; what is asserted here is
/// what this application depends on — that the loop-error table survives the crossing into this
/// project's own shape with its figures intact, that a run which stopped partway is recorded as a
/// stopped run rather than as a survey with no loops, and that text which is not a log at all is
/// refused instead of being reported as a failed compilation.
/// </para>
///
/// <para>
/// The fixture is a genuine compilation of a genuine cave with every cave and station identifier
/// replaced. The figures are the real ones, which is the point: a table of invented loop errors
/// would agree with any reader, including a broken one.
/// </para>
/// </summary>
public class SurveyCompilationLogTests
{
    private const string FixtureName = "therion-compiler.log";

    private static readonly SurveyCompilationLogReader Reader = new();

    private static string LogText() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName));

    /// <summary>
    /// The same log cut off partway through, the way a run that died leaves one: the step it was in
    /// never printed that it finished, and nothing after it was ever written.
    /// </summary>
    private static string HaltedLogText()
    {
        var lines = LogText().ReplaceLineEndings("\n").Split('\n');
        var stopped = Array.FindIndex(lines, l => l.StartsWith("calculating station coordinates", StringComparison.Ordinal));
        stopped.ShouldBeGreaterThan(0, "the fixture no longer contains the step this test cuts at");

        lines[stopped] = lines[stopped].Replace(" done", string.Empty, StringComparison.Ordinal);
        return string.Join('\n', lines[..(stopped + 1)]);
    }

    [Fact]
    public void A_compilers_log_is_recognised_by_its_contents_and_prose_is_not()
    {
        var log = LogText();

        Reader.LooksLikeCompilationLog(FixtureName, log).ShouldBeTrue();

        // The contents are what decide it. A log renamed on the way in is still a log.
        Reader.LooksLikeCompilationLog(null, log).ShouldBeTrue();
        Reader.LooksLikeCompilationLog("notes.txt", log).ShouldBeTrue();

        // And a file named like a log is not one. This text is invented for the test.
        const string notALog = """
            Trip notes, 3 March.
            Rigged the second pitch, surveyed to the sump and out.
            compilation of the sketch still to do.
            """;
        Reader.LooksLikeCompilationLog("therion.log", notALog).ShouldBeFalse();
        Reader.LooksLikeCompilationLog(FixtureName, string.Empty).ShouldBeFalse();
        Reader.LooksLikeCompilationLog(FixtureName, null).ShouldBeFalse();
    }

    [Fact]
    public void Text_that_is_not_a_compilers_log_is_refused_rather_than_read_as_a_failed_run()
    {
        // A reader that answered "failed compilation" here would put a quality claim on a survey
        // nobody ever compiled: no log at all and a compilation that failed are different facts,
        // and only one of them is evidence about the survey.
        Should.Throw<SurveySourceException>(() => Reader.Read("notes.txt", "not a log at all"));
        Should.Throw<SurveySourceException>(() => Reader.Read(FixtureName, string.Empty));
        Should.Throw<SurveySourceException>(() => Reader.Read(FixtureName, null));

        // The positive case, in the same test: a real log is read.
        Reader.Read(FixtureName, LogText()).LoopErrors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_loop_error_table_is_read_row_for_row_in_the_compilers_own_order()
    {
        var report = Reader.Read(FixtureName, LogText());

        report.LoopErrors.Count.ShouldBe(26);
        report.LoopErrors.Select(l => l.Ordinal).ShouldBe(Enumerable.Range(0, 26));

        var worstRatio = report.LoopErrors[0];
        worstRatio.RelativeErrorPercent.ShouldBe(87.41, 1e-9);
        worstRatio.AbsoluteErrorM.ShouldBe(5.7, 1e-9);
        worstRatio.TotalLengthM.ShouldBe(6.5, 1e-9);
        worstRatio.StationCount.ShouldBe(2);
        worstRatio.ErrorXM.ShouldBe(4.9, 1e-9);
        worstRatio.ErrorYM.ShouldBe(2.7, 1e-9);
        worstRatio.ErrorZM.ShouldBe(0.7, 1e-9);

        // The station sequence is kept as the compiler wrote it: a chain of survey-qualified names.
        worstRatio.Stations.ShouldContain(" - ");
        worstRatio.Stations.Trim().ShouldBe(worstRatio.Stations);
        report.LoopErrors.ShouldAllBe(l => l.Stations.Length > 0);
    }

    [Fact]
    public void A_loops_ratio_and_its_distance_are_two_different_answers()
    {
        var report = Reader.Read(FixtureName, LogText());

        // The two measures do not rank the loops the same way, which is why both are stored and
        // why nothing may rank by one while labelling it the other. In this survey the second loop
        // the compiler listed closes to a worse ratio than the seventh, while the seventh misses by
        // the greater distance — a long loop can be off by metres and still be the better survey.
        var ratioWorse = report.LoopErrors[1];
        var distanceWorse = report.LoopErrors[6];

        ratioWorse.RelativeErrorPercent.ShouldBeGreaterThan(distanceWorse.RelativeErrorPercent);
        ratioWorse.AbsoluteErrorM.ShouldBeLessThan(distanceWorse.AbsoluteErrorM);
        ratioWorse.TotalLengthM.ShouldBeLessThan(distanceWorse.TotalLengthM);

        // Ordering by the two measures genuinely disagrees over the table as a whole.
        var byRatio = report.LoopErrors.OrderByDescending(l => l.RelativeErrorPercent).Select(l => l.Ordinal);
        var byDistance = report.LoopErrors.OrderByDescending(l => l.AbsoluteErrorM).Select(l => l.Ordinal);
        byRatio.ShouldNotBe(byDistance);
    }

    [Fact]
    public void A_run_that_stopped_partway_is_read_as_failed_at_the_step_it_stopped_in()
    {
        var halted = Reader.Read(FixtureName, HaltedLogText());

        halted.Outcome.ShouldBe(SurveyCompilationOutcome.Failed);
        halted.IncompleteStage.ShouldBe("calculating station coordinates");
        halted.CompilationSeconds.ShouldBeNull();

        // It reports no loops because it never got as far as reporting any — which is a different
        // claim from a survey that has none, and is why the outcome is stored beside the table.
        halted.LoopErrors.ShouldBeEmpty();

        // The same log, whole: it finished, so nothing is named as the step it stopped in.
        var finished = Reader.Read(FixtureName, LogText());
        finished.Outcome.ShouldBe(SurveyCompilationOutcome.Succeeded);
        finished.IncompleteStage.ShouldBeNull();
        finished.LoopErrors.Count.ShouldBe(26);
    }

    [Fact]
    public void The_run_that_produced_the_figures_can_be_named()
    {
        var report = Reader.Read(FixtureName, LogText());

        // Enough of the run to trace a figure back to the compilation that produced it.
        report.CompilerVersion.ShouldBe("5.5.7+dev");
        report.CompilerReleaseDate.ShouldBe("2021-02-06");
        report.CompilationSeconds.ShouldBe(13);
        report.ErrorCount.ShouldBe(0);
        report.AverageLoopErrorPercent!.Value.ShouldBe(1.49, 1e-9);

        // This run printed no survey-statistics block, so the figures that come from it are not
        // known — which is not the same as their being zero, and is stored as null for that reason.
        report.LoopCount.ShouldBeNull();
        report.TotalLengthM.ShouldBeNull();
        report.TotalLengthAdjustedM.ShouldBeNull();
    }
}
