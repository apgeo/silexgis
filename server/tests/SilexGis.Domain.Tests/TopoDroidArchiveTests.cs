// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Reading a TopoDroid survey archive's manifest and dump. Every line here is invented, shaped
/// exactly as TopoDroid 6.5.34 writes them.
/// </summary>
public class TopoDroidArchiveTests
{
    // The survey row as TopoDroid writes it, missing comma between the last two values included.
    private const string SurveyRow =
        "INSERT into surveys values( 1, \"pestera_inventata\", \"2026.10.25\", \"Test A.; Test B.\", 1.2500, \"Invented sample\", \"0\", 1, 0, 0, 0 0 );";

    private const string FixedRow =
        "INSERT into fixeds values( 1, 1, \"0\", 24.0000000, 46.0000000, 541.33, 500.00, \"invented\", 0, 2, \"\", 0.0000000, 0.0000000, 0.0, 2, 2, 0.0000, 5.0, -1.0, 1.000000, 1.000000 );";

    [Fact]
    public void A_manifest_names_the_version_the_database_and_the_survey()
    {
        var manifest = TopoDroidArchive.ReadManifest("6.5.34 605034\n60\npestera_inventata\n2026.10.25\n", out var problem);

        problem.ShouldBeNull();
        manifest.ShouldBe(new TopoDroidManifest("6.5.34", 605034, 60, "pestera_inventata"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("6.5.34 605034\n60\n")]
    [InlineData("6.5.34 605034\nsixty\nsurvey\n")]
    [InlineData("6.5.34 605034\n20\nsurvey\n")]
    [InlineData("\n60\nsurvey\n")]
    [InlineData("6.5.34\n60\n \n")]
    public void A_manifest_that_is_incomplete_or_too_old_is_refused_with_a_reason(string text)
    {
        TopoDroidArchive.ReadManifest(text, out var problem).ShouldBeNull();
        problem.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_newer_database_version_is_read_by_its_stable_leading_columns()
    {
        TopoDroidArchive.ReadManifest("7.0.0 700000\n75\ns\n", out _)!.DatabaseVersion.ShouldBe(75);
    }

    [Fact]
    public void The_dump_gives_the_surveys_date_party_declination_and_fixed_stations()
    {
        var facts = TopoDroidArchive.ReadDump(
        [
            SurveyRow,
            "INSERT into plots values( 1, 1, \"1p\", 1, 0, \"0\", \"\", -78.90, -74.29, 34.13, 0.00 );",
            FixedRow,
        ])!;

        facts.Name.ShouldBe("pestera_inventata");
        facts.Date.ShouldBe(new DateOnly(2026, 10, 25));
        facts.Team.ShouldBe("Test A.; Test B.");
        facts.Declination.ShouldBe(1.25);
        facts.Comment.ShouldBe("Invented sample");
        facts.FixedRowCount.ShouldBe(1);
        facts.FixedPoints.ShouldHaveSingleItem().ShouldBe(new TopoDroidFixedPoint("0", 24, 46, 541.33, 500));
    }

    [Fact]
    public void Escaped_quotes_come_back_and_blank_values_read_as_absent()
    {
        var facts = TopoDroidArchive.ReadDump(
            ["INSERT into surveys values( 1, \"s\", \"\", \"Ion \u001bIonel\u001b A.\", x, \"\", \"0\" );"])!;

        facts.Team.ShouldBe("Ion \"Ionel\" A.");
        facts.Date.ShouldBeNull();
        facts.Declination.ShouldBeNull();
        facts.Comment.ShouldBeNull();
        facts.FixedPoints.ShouldBeEmpty();
    }

    [Fact]
    public void A_fixed_row_that_is_not_a_position_on_the_globe_is_counted_and_not_kept()
    {
        var facts = TopoDroidArchive.ReadDump(
        [
            SurveyRow,
            "INSERT into fixeds values( 1, 1, \"0\", 240.0, 46.0, 0, 0 );",
            "INSERT into fixeds values( 1, 2, \"1\", 24.0 );",
        ])!;

        facts.FixedRowCount.ShouldBe(2);
        facts.FixedPoints.ShouldBeEmpty();
    }

    [Fact]
    public void A_dump_with_no_survey_row_reads_as_nothing()
    {
        TopoDroidArchive.ReadDump([FixedRow, "-- a comment", ""]).ShouldBeNull();
    }

    [Fact]
    public void Values_are_split_on_commas_spaces_or_both_with_strings_kept_whole()
    {
        TopoDroidArchive.Values("INSERT into t values( 1, \"a, b\", 2 3,4 , 'c d' );")
            .ShouldBe(["1", "a, b", "2", "3", "4", "c d"]);
        TopoDroidArchive.Values("no parenthesis").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("manifest", true)]
    [InlineData("photo/0001.jpg", true)]
    [InlineData("../evil", false)]
    [InlineData("photo/../../evil", false)]
    [InlineData("photo\\..\\..\\evil", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:/Windows/evil", false)]
    [InlineData("", false)]
    public void An_entry_name_that_climbs_out_of_the_archive_is_unsafe(string name, bool safe)
    {
        TopoDroidArchive.IsSafeEntryName(name).ShouldBe(safe);
    }

    [Theory]
    [InlineData("survey.sql", true)]
    [InlineData("Other.SQL", true)]
    [InlineData("photo/x.sql", false)]
    [InlineData("survey.txt", false)]
    public void The_dump_is_a_sql_file_at_the_archives_root(string name, bool dump)
    {
        TopoDroidArchive.IsDump(name).ShouldBe(dump);
    }
}

public class SurveyPipelineProgressTests
{
    [Fact]
    public void Each_stage_owns_its_band_of_the_run_in_order()
    {
        var stages = Enum.GetValues<SurveyUploadStage>();
        SurveyPipelineProgress.Band(stages[0]).Start.ShouldBe(0);
        SurveyPipelineProgress.Band(stages[^1]).End.ShouldBe(100);
        for (var i = 1; i < stages.Length; i++)
        {
            SurveyPipelineProgress.Band(stages[i]).Start.ShouldBe(SurveyPipelineProgress.Band(stages[i - 1]).End);
        }
    }

    [Fact]
    public void A_stage_reporting_outside_its_own_range_stays_inside_its_band()
    {
        SurveyPipelineProgress.Overall(SurveyUploadStage.Compile, 50).ShouldBe(52);
        SurveyPipelineProgress.Overall(SurveyUploadStage.Compile, -10).ShouldBe(25);
        SurveyPipelineProgress.Overall(SurveyUploadStage.Compile, 400).ShouldBe(80);
    }

    [Fact]
    public void A_run_that_does_not_compile_says_why_and_one_that_can_compile_goes_on()
    {
        SurveyPipelineNotes.WithoutCompiling(compileAsked: false, compilerAvailable: false).ShouldBe(SurveyPipelineNotes.NotAsked);
        SurveyPipelineNotes.WithoutCompiling(compileAsked: false, compilerAvailable: true).ShouldBe(SurveyPipelineNotes.NotAsked);
        SurveyPipelineNotes.WithoutCompiling(compileAsked: true, compilerAvailable: false).ShouldBe(SurveyPipelineNotes.NoCompiler);
        SurveyPipelineNotes.WithoutCompiling(compileAsked: true, compilerAvailable: true).ShouldBeNull();
    }
}
