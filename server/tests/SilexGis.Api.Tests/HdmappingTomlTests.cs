// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Scans;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Tests;

/// <summary>
/// HDMapping's parameter files read and written, on a file written by hand in the layout the
/// program's dump has (a version table, a paths table, parameter sections, a display table). The
/// values are invented.
/// </summary>
public sealed class HdmappingTomlTests
{
    private const string Dump = """
        [version_info]
        software_version = "9.9.9"
        config_version = "1.0"

        [paths]
        current_output_dir = ""

        [lidar_odometry_control]
        max_iterations = 1000
        use_motion_from_previous_step = true
        resolution = 0.3
        sliding_window_trajectory_length_threshold = 200.0
        decimation = 1e-2
        threshold_nr_poses = 20
        label = "a \"quoted\" name"
        window = [1, 2, 3]

        [filter]
        ceiling = inf
        floor = -inf
        missing = nan

        [misc]
        clear_color = [0.45, 0.55, 0.6]
        """;

    [Fact]
    public void A_dump_reads_by_section_and_keeps_each_numbers_kind()
    {
        var values = HdmappingToml.Read(Dump);

        var control = values.GetProperty("lidar_odometry_control");
        control.GetProperty("max_iterations").GetRawText().ShouldBe("1000");
        control.GetProperty("sliding_window_trajectory_length_threshold").GetRawText().ShouldBe("200.0");
        ProcessingProfileImport.IsWhole(control.GetProperty("decimation")).ShouldBeFalse();
        control.GetProperty("use_motion_from_previous_step").GetBoolean().ShouldBeTrue();
        control.GetProperty("label").GetString().ShouldBe("a \"quoted\" name");
        control.GetProperty("window").GetArrayLength().ShouldBe(3);
        values.GetProperty("filter").GetProperty("ceiling").GetString().ShouldBe("inf");
        values.GetProperty("filter").GetProperty("floor").GetString().ShouldBe("-inf");
        values.GetProperty("filter").GetProperty("missing").GetString().ShouldBe("nan");
        values.GetProperty("version_info").GetProperty("software_version").GetString().ShouldBe("9.9.9");
    }

    [Fact]
    public void What_is_written_reads_back_to_the_same_values()
    {
        var dumped = HdmappingToml.Read(Dump);
        var imported = ProcessingProfileImport.Hdmapping(dumped, dumped, null, "9.9.9");
        imported.Report.Accepted.ShouldBeTrue();
        var sections = imported.Values.GetProperty("values");

        var text = HdmappingToml.Write(sections, dumped, new Dictionary<string, string> { ["software_version"] = "9.9.9" }, ["a heading"]);

        text.ShouldStartWith("# a heading\n");
        text.ShouldContain("[version_info]\nsoftware_version = \"9.9.9\"");
        text.ShouldNotContain("[paths]");
        text.ShouldNotContain("[misc]");
        var again = HdmappingToml.Read(text);
        var reimported = ProcessingProfileImport.Hdmapping(again, dumped, null, "9.9.9");
        reimported.Report.Accepted.ShouldBeTrue();
        reimported.Report.FileVersion.ShouldBe("9.9.9");
        reimported.Report.Defaulted.ShouldBeEmpty();
        JsonElement.DeepEquals(reimported.Values, imported.Values).ShouldBeTrue(text);
        foreach (var key in sections.GetProperty("lidar_odometry_control").EnumerateObject())
        {
            var read = again.GetProperty("lidar_odometry_control").GetProperty(key.Name);
            JsonElement.DeepEquals(read, key.Value).ShouldBeTrue(key.Name);
            if (key.Value.ValueKind == JsonValueKind.Number)
            {
                // The kind survives too: a fractional setting comes back with its fraction.
                ProcessingProfileImport.IsWhole(read).ShouldBe(ProcessingProfileImport.IsWhole(key.Value), key.Name);
            }
        }
    }

    [Fact]
    public void A_fractional_setting_whose_value_is_whole_is_still_written_with_a_fraction()
    {
        var dumped = HdmappingToml.Read(Dump);
        using var profile = JsonDocument.Parse("""{"lidar_odometry_control":{"resolution":1,"max_iterations":7}}""");

        var text = HdmappingToml.Write(profile.RootElement, dumped, null, []);

        text.ShouldContain("resolution = 1.0\n");
        text.ShouldContain("max_iterations = 7\n");
        ProcessingProfileImport.Hdmapping(HdmappingToml.Read(text), dumped, null, null).Report.Accepted.ShouldBeTrue();
    }

    [Fact]
    public void Text_that_is_not_TOML_is_refused_with_where()
    {
        var error = Should.Throw<FormatException>(() => HdmappingToml.Read("[lidar_odometry_control\nmax_iterations = "));
        error.Message.ShouldNotBeNullOrWhiteSpace();
    }
}
