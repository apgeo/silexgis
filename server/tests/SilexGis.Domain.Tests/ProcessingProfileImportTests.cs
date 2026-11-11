// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Scans;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What an import makes of a parameter file, against invented defaults in the shape HDMapping's
/// dump has: sections of keys, a version table, a paths table, a display table and one default
/// that is not finite.
/// </summary>
public sealed class ProcessingProfileImportTests
{
    private static readonly JsonElement Defaults = Json("""
        {
          "version_info": { "software_version": "9.9.9", "config_version": "1.0" },
          "paths": { "current_output_dir": "" },
          "misc": { "clear_color": [0.4, 0.5, 0.6] },
          "lidar_odometry_control": {
            "max_iterations": 1000,
            "use_motion_from_previous_step": true,
            "resolution": 0.3,
            "label": "fixed",
            "window": [1, 2, 3]
          },
          "filter": { "ceiling": "inf", "min_distance": 0.5 }
        }
        """);

    [Fact]
    public void A_partial_file_is_taken_filled_from_the_defaults_and_cleaned_of_what_is_not_a_parameter()
    {
        var file = Json("""
            {
              "version_info": { "software_version": "9.8.0" },
              "paths": { "current_output_dir": "/home/somebody/run" },
              "misc": { "clear_color": [0, 0, 0] },
              "lidar_odometry_control": { "max_iterations": 50, "resolution": 0.5, "typo_key": 1 },
              "no_such_section": { "a": 1 }
            }
            """);

        var result = ProcessingProfileImport.Hdmapping(file, Defaults, null, "9.9.9");

        var report = result.Report;
        report.Accepted.ShouldBeTrue();
        report.Taken.ShouldBe(["lidar_odometry_control.max_iterations", "lidar_odometry_control.resolution"]);
        report.Unknown.ShouldBe(["lidar_odometry_control.typo_key", "no_such_section.a"]);
        report.DroppedSections.ShouldBe(["version_info", "paths", "misc"]);
        report.Defaulted.ShouldBe([
            "lidar_odometry_control.use_motion_from_previous_step",
            "lidar_odometry_control.label",
            "lidar_odometry_control.window",
            "filter.min_distance",
        ]);
        report.FileVersion.ShouldBe("9.8.0");
        report.VersionDiffers.ShouldBeTrue();

        var values = result.Values.GetProperty("values");
        values.TryGetProperty("paths", out _).ShouldBeFalse();
        values.TryGetProperty("misc", out _).ShouldBeFalse();
        values.TryGetProperty("version_info", out _).ShouldBeFalse();
        var control = values.GetProperty("lidar_odometry_control");
        control.GetProperty("max_iterations").GetInt32().ShouldBe(50);
        control.GetProperty("use_motion_from_previous_step").GetBoolean().ShouldBeTrue();
        control.TryGetProperty("typo_key", out _).ShouldBeFalse();

        // The non-finite default is left to the program: naming it would be refused by the run.
        values.GetProperty("filter").TryGetProperty("ceiling", out _).ShouldBeFalse();
        values.GetProperty("filter").GetProperty("min_distance").GetDouble().ShouldBe(0.5);
    }

    [Theory]
    [InlineData("""{"max_iterations": "many"}""", "must be a whole number")]
    [InlineData("""{"max_iterations": 5.0}""", "must be a whole number")]
    [InlineData("""{"max_iterations": 3000000000}""", "out of range")]
    [InlineData("""{"use_motion_from_previous_step": 1}""", "must be true or false")]
    [InlineData("""{"resolution": "fine"}""", "must be a number")]
    [InlineData("""{"resolution": "nan"}""", "must be a finite number")]
    [InlineData("""{"window": [1, 2.5]}""", "item 1 must be a whole number")]
    [InlineData("""{"window": 4}""", "must be a list")]
    public void A_value_the_program_would_misread_refuses_the_import_naming_the_key(string control, string reason)
    {
        var file = Json($$"""{"lidar_odometry_control": {{control}}}""");

        var report = ProcessingProfileImport.Hdmapping(file, Defaults, null, "9.9.9").Report;

        report.Accepted.ShouldBeFalse();
        var refusal = report.Refused.ShouldHaveSingleItem();
        refusal.Key.ShouldStartWith("lidar_odometry_control.");
        refusal.Reason.ShouldContain(reason);
    }

    [Fact]
    public void A_whole_number_for_a_fractional_setting_is_taken_and_text_settings_stay_the_programs()
    {
        var file = Json("""
            {
              "lidar_odometry_control": { "resolution": 1, "label": "mine" },
              "filter": { "ceiling": 12.5 }
            }
            """);

        var result = ProcessingProfileImport.Hdmapping(file, Defaults, null, null);

        result.Report.Accepted.ShouldBeTrue();
        result.Report.Fixed.ShouldBe(["lidar_odometry_control.label"]);
        result.Report.VersionDiffers.ShouldBeFalse();
        var values = result.Values.GetProperty("values");
        values.GetProperty("lidar_odometry_control").GetProperty("label").GetString().ShouldBe("fixed");
        values.GetProperty("filter").GetProperty("ceiling").GetDouble().ShouldBe(12.5);
    }

    [Fact]
    public void A_file_naming_its_own_version_is_compared_with_the_installed_one()
    {
        var same = ProcessingProfileImport.Hdmapping(Json("""{"version_info":{"software_version":"9.9.9"}}"""), Defaults, null, "9.9.9");
        same.Report.VersionDiffers.ShouldBeFalse();

        var stated = ProcessingProfileImport.Hdmapping(Json("{}"), Defaults, "9.0.0@abc", "9.9.9@def");
        stated.Report.FileVersion.ShouldBe("9.0.0@abc");
        stated.Report.VersionDiffers.ShouldBeTrue();
    }

    [Fact]
    public void A_flat_step_drops_undeclared_keys_and_fills_its_defaults()
    {
        var result = ProcessingProfileImport.Flat(
            Json("""{"voxelCm": 4, "colour": "red"}"""),
            Json("""{"method": "voxel", "voxelCm": 2, "everyNth": 2}"""),
            ["method", "voxelCm", "everyNth"],
            null,
            null);

        result.Report.Taken.ShouldBe(["voxelCm"]);
        result.Report.Unknown.ShouldBe(["colour"]);
        result.Report.Defaulted.ShouldBe(["method", "everyNth"]);
        result.Values.GetProperty("voxelCm").GetInt32().ShouldBe(4);
        result.Values.GetProperty("method").GetString().ShouldBe("voxel");

        var refused = result.Report.WithSchemaErrors(["/voxelCm: Value should be at most 1000"]);
        refused.Accepted.ShouldBeFalse();
        refused.Refused.ShouldHaveSingleItem().Key.ShouldBe("/voxelCm");
    }

    [Theory]
    [InlineData("0.106.0@abc123", "0.106.0")]
    [InlineData("0.106.0", "0.106.0")]
    [InlineData(null, null)]
    [InlineData("@abc", null)]
    public void The_software_version_is_the_part_before_the_build(string? toolVersion, string? expected) =>
        ProcessingProfileImport.SoftwareVersionOf(toolVersion).ShouldBe(expected);

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
