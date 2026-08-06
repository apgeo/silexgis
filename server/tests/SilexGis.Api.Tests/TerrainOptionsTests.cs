// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Domain.Geo;

namespace SilexGis.Api.Tests;

/// <summary>
/// The description an operator gives of their elevation model, and what it resolves to.
///
/// <para>
/// Everything here exists because getting it wrong is invisible. An installation whose terrain is
/// described with the wrong vertical datum draws every cave about forty metres off its hillside,
/// with no error, no warning on screen and nothing in the data to blame; an installation that
/// names a directory that is not there draws a globe with no ground on it. So the resolution is
/// asserted, and so is every configuration the startup log is expected to complain about.
/// </para>
/// </summary>
public class TerrainOptionsTests
{
    [Fact]
    public void An_installation_that_says_nothing_gets_no_terrain_and_no_complaints()
    {
        // The shipped state. `docker compose up` must stay exactly what it was: no elevation
        // server, no download, no pre-baking, and nothing published to the client.
        var options = new TerrainOptions();

        options.IsConfigured.ShouldBeFalse();
        options.ConfigurationWarnings().ShouldBeEmpty();
    }

    [Fact]
    public void Half_filled_terrain_settings_without_a_source_are_left_alone()
    {
        // The state an operator is in part way through setting one up. Complaining about it would
        // be noise in a log they are about to make correct.
        new TerrainOptions { HeightDatum = TerrainHeightDatum.Ellipsoidal, GeoidHeightM = 43 }
            .ConfigurationWarnings().ShouldBeEmpty();
    }

    [Fact]
    public void A_source_serving_sea_level_heights_asks_for_no_correction()
    {
        // Copernicus, and every other unconverted elevation model, holds heights above sea level
        // — the same kind of number a cave survey carries. Nothing has to move.
        var options = new TerrainOptions { Url = "/terrain/" };

        options.HeightDatum.ShouldBe(TerrainHeightDatum.Orthometric);
        options.SurveyHeightOffsetM.ShouldBe(0);
        options.ConfigurationWarnings().ShouldNotContain(w => w.Contains("GeoidHeightM is 0"));
    }

    [Fact]
    public void A_source_converted_when_it_was_baked_asks_for_the_local_undulation()
    {
        new TerrainOptions
        {
            Url = "/terrain/",
            HeightDatum = TerrainHeightDatum.Ellipsoidal,
            GeoidHeightM = 43.03,
        }.SurveyHeightOffsetM.ShouldBe(43.03);
    }

    [Fact]
    public void A_missing_trailing_slash_is_repaired_rather_than_left_to_fail_silently()
    {
        // A pyramid at /terrain is otherwise looked for at /layer.json, where the single-page
        // fallback answers 200 with the application's own HTML and the globe simply has no ground.
        new TerrainOptions { Url = "/terrain" }.ResolvedUrl.ShouldBe("/terrain/");
        new TerrainOptions { Url = "  /terrain/  " }.ResolvedUrl.ShouldBe("/terrain/");
    }

    [Fact]
    public void An_ellipsoidal_source_with_no_undulation_is_called_out()
    {
        // This is the forty-metre error, in the direction that is hardest to notice: the caves are
        // consistently below the ground rather than floating over it.
        var warnings = new TerrainOptions
        {
            Url = "/terrain/",
            HeightDatum = TerrainHeightDatum.Ellipsoidal,
            Attribution = "a credit",
        }.ConfigurationWarnings();

        warnings.ShouldContain(w => w.Contains("GeoidHeightM is 0"));
    }

    [Fact]
    public void An_undulation_that_will_never_be_used_is_called_out()
    {
        // The trap that killed the setting this replaces: a published knob that does nothing. An
        // operator sets it, sees no change, and stops believing the guide.
        var warnings = new TerrainOptions
        {
            Url = "/terrain/",
            GeoidHeightM = 43,
            Attribution = "a credit",
        }.ConfigurationWarnings();

        warnings.ShouldContain(w => w.Contains("GeoidHeightM is set"));
    }

    [Fact]
    public void A_source_with_no_credit_is_called_out()
    {
        new TerrainOptions { Url = "/terrain/" }
            .ConfigurationWarnings()
            .ShouldContain(w => w.Contains("Attribution is empty"));
    }

    [Fact]
    public void A_source_on_somebody_elses_host_is_called_out()
    {
        // Viewers' browsers fetch tiles directly, so this ends the property that nothing outside
        // the installation is contacted while a cave is being looked at — and it tells that host
        // which ground is being looked at.
        var warnings = new TerrainOptions
        {
            Url = "https://tiles.example.org/terrain/",
            Attribution = "a credit",
        }.ConfigurationWarnings();

        warnings.ShouldContain(w => w.Contains("tiles.example.org"));
    }

    [Fact]
    public void A_source_on_this_installation_is_not_called_out_for_being_elsewhere()
    {
        new TerrainOptions { Url = "/terrain/", Attribution = "a credit" }
            .ConfigurationWarnings()
            .ShouldBeEmpty();
    }
}
