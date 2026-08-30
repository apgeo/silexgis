// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using Therion.Blender;
using Therion.Blender.Parsing;

namespace SilexGis.Api.Tests;

/// <summary>
/// The compiled survey formats, read out of a file a survey toolchain actually wrote.
///
/// <para>
/// This is the acceptance test for taking the readers as a dependency at all: the library that
/// supplies them keeps its own real-file corpus out of version control, so upstream's continuous
/// integration has never parsed a genuine export. Everything downstream — station rows, shot
/// flags, passage dimensions, every statistic computed from them — rests on the readers behaving
/// on real bytes rather than on constructed ones, so this project brings its own file and says
/// what it contains.
/// </para>
/// <para>
/// The fixture is the Survex export the browser tests already drive the 3D viewer with, linked
/// rather than copied so there is one file to keep true. It is a real cave in British national
/// grid coordinates, which is also why it is worth asserting the coordinate system on: a reader
/// that dropped it would still produce plausible-looking geometry, in the wrong place.
/// </para>
/// </summary>
public class SurveyFormatReaderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void A_survex_export_is_recognised_from_its_first_bytes()
    {
        var data = File.ReadAllBytes(FixturePath("P8_Master.3d"));

        CaveModelReader.Detect(data).ShouldBe(CaveSourceFormat.Survex3d);
    }

    [Fact]
    public void A_survex_export_yields_its_stations_shots_and_coordinate_system()
    {
        var model = CaveModelReader.Read(File.ReadAllBytes(FixturePath("P8_Master.3d")));

        model.SourceFormat.ShouldBe(CaveSourceFormat.Survex3d);
        model.Stations.Count.ShouldBe(393);
        model.Shots.Count.ShouldBe(389);

        // The export declares where its coordinates live. Nothing may assume metres about an
        // arbitrary origin for a file that says otherwise.
        model.CoordinateSystem.ShouldNotBeNullOrWhiteSpace();
        model.CoordinateSystem.ShouldContain("27700");
    }

    [Fact]
    public void Stations_carry_names_and_positions_rather_than_bare_coordinates()
    {
        var model = CaveModelReader.Read(File.ReadAllBytes(FixturePath("P8_Master.3d")));

        // Names are the identity these rows will be keyed on: the numeric id in the file is a
        // position in file order and is renumbered by every re-export.
        model.Stations.ShouldAllBe(s => !string.IsNullOrWhiteSpace(s.Name));
        model.Stations.Select(s => s.Name).Distinct().Count().ShouldBe(model.Stations.Count);

        var span = model.Stations.Max(s => s.Position.Z) - model.Stations.Min(s => s.Position.Z);
        span.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Passage_dimensions_are_present_and_keyed_by_station_name()
    {
        var model = CaveModelReader.Read(File.ReadAllBytes(FixturePath("P8_Master.3d")));

        // Cross-section runs are what passage-size work is later computed from. This format keys
        // them by station name, which is the shape the storage has to normalise towards.
        model.Passages.Count.ShouldBe(9);
        model.Passages.ShouldAllBe(p => p.Stations.Count > 0);
    }

    [Fact]
    public void Bytes_that_are_neither_format_are_refused_rather_than_guessed_at()
    {
        var notASurvey = "this is not a cave survey, it is a sentence"u8.ToArray();

        CaveModelReader.Detect(notASurvey).ShouldBe(CaveSourceFormat.Unknown);
        Should.Throw<CaveFileFormatException>(() => CaveModelReader.Read(notASurvey));
    }
}
