// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Tests;

/// <summary>
/// The installation's metric coordinate system, and the refusal to start without a usable one.
///
/// <para>
/// This setting is only ever read while answering a question in metres, which may be a rare route.
/// A wrong value would therefore sit unnoticed until somebody asked how close two caves come to
/// each other — and would then be reported as a projection failure rather than as a mistyped
/// setting. So the value is checked while the application starts, and these tests are about that
/// check rather than about any query.
/// </para>
/// </summary>
public class SpatialOptionsTests
{
    private static SpatialOptionsValidator Validator() => new(new ProjCrsRegistry());

    [Fact]
    public void The_default_working_system_resolves()
    {
        // The shipped default has to be a code the bundled projection database actually knows;
        // if it ever stops being one, every installation that never set the value fails to start.
        var result = Validator().Validate(null, new SpatialOptions());

        result.Succeeded.ShouldBeTrue(result.FailureMessage);
    }

    [Theory]
    [InlineData(32634)] // UTM 34N — western Romania
    [InlineData(32635)] // UTM 35N — eastern Romania
    [InlineData(25832)] // ETRS89 / UTM 32N — an installation in central Europe
    public void A_projected_system_an_installation_might_choose_resolves(int epsg)
    {
        var result = Validator().Validate(null, new SpatialOptions { WorkingSrid = epsg });

        result.Succeeded.ShouldBeTrue(result.FailureMessage);
    }

    [Fact]
    public void A_code_the_projection_database_does_not_know_refuses_to_start()
    {
        var result = Validator().Validate(null, new SpatialOptions { WorkingSrid = 999_999 });

        result.Failed.ShouldBeTrue();
        // The message has to name the setting, because the reader is somebody looking at a
        // container that will not come up, not somebody reading a stack trace.
        result.FailureMessage.ShouldContain("WorkingSrid");
        result.FailureMessage.ShouldContain("999999");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_value_that_is_not_a_code_at_all_refuses_to_start(int epsg)
    {
        var result = Validator().Validate(null, new SpatialOptions { WorkingSrid = epsg });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("positive");
    }

    [Fact]
    public void The_transform_fragment_never_spells_the_code_itself()
    {
        // The rule the helper exists for: a query carrying a literal EPSG code keeps carrying it
        // after an installation changes the setting, and the resulting numbers stay plausible
        // while being measured in the wrong place.
        var fragment = SpatialSql.ToWorking("f.geom");

        fragment.ShouldBe("ST_Transform(f.geom, @workingSrid)");
        fragment.ShouldNotContain("326");
    }

    [Fact]
    public void The_metre_test_stays_in_the_stored_system_so_the_existing_index_answers_it()
    {
        // This is the pre-filter that makes the projected transform affordable. If it ever starts
        // transforming instead, every metric query goes back to scanning the table.
        var fragment = SpatialSql.WithinMetres("a.geom", "b.geom", "metres");

        fragment.ShouldContain("::geography");
        fragment.ShouldNotContain("ST_Transform");
    }

    [Fact]
    public void The_altitude_test_asks_whether_there_are_altitudes_and_not_whether_there_is_room_for_them()
    {
        var sql = SpatialSql.HasAltitudes("f.geom");

        // Counting the dimensions is the tempting test and is the wrong one: a survey drawn in
        // plan is stored with a third ordinate of zero so it can be held and drawn like any other
        // shape, so the count says three for line work that recorded no depth at all. The values
        // have to be looked at, or every vertical figure derived from a drawing is a flat cave
        // invented out of the storage format.
        sql.ShouldContain("ST_NDims");
        sql.ShouldContain("ST_ZMin");
        sql.ShouldContain("ST_ZMax");
        sql.ShouldContain("<> 0");
    }
}
