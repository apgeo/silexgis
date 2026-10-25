// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The copy of an uploaded survey the compiler reads. The survey text is shaped like TopoDroid's
/// own Therion export (its default options: sketches and fix written as comments), with invented
/// stations and an invented position.
/// </summary>
public class TherionCompileCopyTests
{
    private const string TopoDroidDefault = """
        encoding utf-8
        survey inventata -title "inventata"
          centerline
            cs long-lat
            # fix 0 24.000000 46.000000 500 # 5.0
            date 2026.10.25
            # team Test A.; Test B.
            0 1 5.20 30.0 -10.0
          endcenterline

          # input "inventata-1p.th2"
          # input "inventata-1s.th2"

          # map m1p -projection plan
          #   inventata-1p
          # endmap
          # map m1s -projection extended
          #   inventata-1s
          # endmap

        endsurvey
        """;

    [Fact]
    public void Sketches_that_were_uploaded_are_read_and_drawn_and_the_commented_fix_made_live()
    {
        var copy = TherionCompileCopy.ForCompile(TopoDroidDefault, ["inventata-1p.th2", "inventata-1s.th2"]);

        copy.SketchesDrawn.ShouldBe(["inventata-1p.th2", "inventata-1s.th2"]);
        copy.FixUncommented.ShouldBeTrue();
        var lines = copy.Text.Split('\n').Select(l => l.Trim()).ToList();
        lines.ShouldContain("input \"inventata-1p.th2\"");
        lines.ShouldContain("input \"inventata-1s.th2\"");
        lines.ShouldContain("map m1p -projection plan");
        lines.ShouldContain("inventata-1p");
        lines.ShouldContain("map m1s -projection extended");
        lines.Count(l => l == "endmap").ShouldBe(2);
        lines.ShouldContain("fix 0 24.000000 46.000000 500 # 5.0");

        // Only those lines: the party stays a comment, as TopoDroid wrote it.
        lines.ShouldContain("# team Test A.; Test B.");
    }

    [Fact]
    public void A_sketch_that_did_not_come_keeps_its_input_and_its_map_commented()
    {
        var copy = TherionCompileCopy.ForCompile(TopoDroidDefault, ["inventata-1p.th2"]);

        copy.SketchesDrawn.ShouldBe(["inventata-1p.th2"]);
        var lines = copy.Text.Split('\n').Select(l => l.Trim()).ToList();
        lines.ShouldContain("input \"inventata-1p.th2\"");
        lines.ShouldContain("# input \"inventata-1s.th2\"");
        lines.ShouldContain("map m1p -projection plan");
        lines.ShouldContain("# map m1s -projection extended");
        lines.ShouldContain("#   inventata-1s");
    }

    [Fact]
    public void Without_sketches_the_survey_compiles_as_its_centreline()
    {
        var copy = TherionCompileCopy.ForCompile(TopoDroidDefault, []);

        copy.SketchesDrawn.ShouldBeEmpty();
        copy.Text.ShouldNotContain("\n  input ");
        copy.Text.ShouldNotContain("\n  map ");
        copy.FixUncommented.ShouldBeTrue();
    }

    [Fact]
    public void Uncommented_map_commands_are_left_as_they_are()
    {
        var uncommented = TopoDroidDefault
            .Replace("# input", "input", StringComparison.Ordinal)
            .Replace("# map", "map", StringComparison.Ordinal)
            .Replace("#   inventata", "  inventata", StringComparison.Ordinal)
            .Replace("# endmap", "endmap", StringComparison.Ordinal);

        var copy = TherionCompileCopy.ForCompile(uncommented, ["inventata-1p.th2", "inventata-1s.th2"]);

        copy.SketchesDrawn.ShouldBe(["inventata-1p.th2", "inventata-1s.th2"]);
        copy.Text.ShouldBe(uncommented.Replace("# fix", "fix", StringComparison.Ordinal).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void A_live_fix_is_never_doubled_and_no_fix_is_made_live_without_a_coordinate_system()
    {
        var georeferenced = TopoDroidDefault.Replace("# fix 0", "fix 0", StringComparison.Ordinal)
            + "\n# fix 1 24.1 46.1 510\n";
        TherionCompileCopy.ForCompile(georeferenced, []).FixUncommented.ShouldBeFalse();
        TherionCompileCopy.ForCompile(georeferenced, []).Text.ShouldContain("# fix 1 24.1 46.1 510");

        var local = TopoDroidDefault.Replace("cs long-lat", "# cs long-lat", StringComparison.Ordinal);
        var copy = TherionCompileCopy.ForCompile(local, []);
        copy.FixUncommented.ShouldBeFalse();
        copy.Text.ShouldContain("# fix 0 24.000000 46.000000 500");
    }

    [Fact]
    public void The_comma_below_letters_are_written_as_cedillas_in_the_copy()
    {
        TherionCompileCopy.Normalise("Peștera Țarina ȘȚ ă â î").ShouldBe("Peştera Ţarina ŞŢ ă â î");
        TherionCompileCopy.ForCompile("survey a -title \"Peștera\"\nendsurvey", []).Text
            .ShouldBe("survey a -title \"Peştera\"\nendsurvey");
    }

    [Fact]
    public void A_file_reading_only_the_uploads_own_files_by_bare_name_passes()
    {
        var copy = TherionCompileCopy.ForCompile(TopoDroidDefault, ["inventata-1p.th2", "inventata-1s.th2"]);

        TherionCompileCopy.UnsafeReferences(
                "inventata.th", copy.Text, ["inventata.th", "inventata-1p.th2", "inventata-1s.th2"])
            .ShouldBeEmpty();
        TherionCompileCopy.UnsafeReferences("a.th", "input b\ninput \"c\"", ["a.th", "b.th", "c.th2"]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("input /etc/hostname", "input")]
    [InlineData("input \"/etc/hostname\"", "input")]
    [InlineData("input ../other/inventata-1p.th2", "input")]
    [InlineData("input sub/inventata-1p.th2", "input")]
    [InlineData("input C:inventata-1p.th2", "input")]
    [InlineData("input ..", "input")]
    [InlineData("input not-uploaded.th2", "input")]
    [InlineData("input", "input")]
    [InlineData("  SOURCE elsewhere.th", "source")]
    [InlineData("import /data/cave.3d -surveys use", "import")]
    [InlineData("load /etc/passwd", "load")]
    [InlineData("include /etc/passwd", "include")]
    [InlineData("scrap s1 -projection plan -sketch /etc/shadow 0 0", "-sketch")]
    public void A_file_naming_anything_else_is_refused_by_line_and_command(string line, string command)
    {
        var refused = TherionCompileCopy.UnsafeReferences(
            "inventata.th", "encoding utf-8\n" + line + "\n", ["inventata.th", "inventata-1p.th2"]);

        refused.ShouldBe([new TherionUnsafeReference("inventata.th", 2, command)]);
    }

    [Fact]
    public void Commented_reads_are_not_read_by_the_compiler_and_are_not_refused()
    {
        TherionCompileCopy.UnsafeReferences(
                "inventata.th", "# input /etc/hostname\nsurvey a # input /etc/passwd\nendsurvey", ["inventata.th"])
            .ShouldBeEmpty();

        // A '#' inside quotes is not a comment, so what follows it is still read.
        TherionCompileCopy.UnsafeReferences("inventata.th", "input \"#/etc/hostname\"", ["inventata.th"])
            .ShouldHaveSingleItem().Command.ShouldBe("input");
    }

    [Theory]
    [InlineData(0, false, 3, SurveyCompileVerdict.Succeeded)]
    [InlineData(0, false, 1, SurveyCompileVerdict.Succeeded)]
    [InlineData(0, false, 0, SurveyCompileVerdict.NothingProduced)]
    [InlineData(0, true, 3, SurveyCompileVerdict.CompilerFailed)]
    [InlineData(1, false, 3, SurveyCompileVerdict.CompilerFailed)]
    [InlineData(-1, false, 0, SurveyCompileVerdict.CompilerFailed)]
    public void A_compile_counts_when_the_compiler_finished_cleanly_and_wrote_something_asked_for(
        int exitCode, bool logSaysFailed, int produced, SurveyCompileVerdict verdict) =>
        SurveyCompileOutcome.Judge(exitCode, logSaysFailed, produced).ShouldBe(verdict);

    [Fact]
    public void A_compiled_run_says_how_much_came_out_and_whether_it_was_drawn()
    {
        SurveyPipelineNotes.Compiled(3, 4, sketchesDrawn: true).ShouldBe("Compiled: 3 of 4 outputs produced and stored.");
        SurveyPipelineNotes.Compiled(4, 4, sketchesDrawn: false).ShouldContain("centreline only");
    }
}
