// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The numbers behind the station and shot flag vocabularies, pinned, and the plain readers built
/// on top of them.
///
/// <para>
/// These are stored bit sets. A row records what the enum meant on the day it was written, and the
/// flags come from files written by two different toolchains that disagree about which bit means
/// what — a splay is bit 16 in one format and bit 4 in the other. That disagreement is why the
/// stored value is this application's own numbering rather than a cast of whatever number the file
/// reader produced, and this is the test that keeps the numbering from drifting. Renumbering a bit
/// would silently reclassify every extracted survey already in the database: shots that were
/// splays would become duplicates, and the statistics computed over them would still look
/// perfectly reasonable.
/// </para>
/// </summary>
public class SurveyGraphVocabularyTests
{
    [Fact]
    public void Station_flags_keep_the_bits_rows_were_written_with()
    {
        ((int)SurveyStationFlags.None).ShouldBe(0);
        ((int)SurveyStationFlags.Surface).ShouldBe(1);
        ((int)SurveyStationFlags.Underground).ShouldBe(2);
        ((int)SurveyStationFlags.Entrance).ShouldBe(4);
        ((int)SurveyStationFlags.Exported).ShouldBe(8);
        ((int)SurveyStationFlags.Fixed).ShouldBe(16);
        ((int)SurveyStationFlags.Continuation).ShouldBe(32);
        ((int)SurveyStationFlags.HasWalls).ShouldBe(64);
        ((int)SurveyStationFlags.Anonymous).ShouldBe(128);
        ((int)SurveyStationFlags.Wall).ShouldBe(256);
    }

    [Fact]
    public void Shot_flags_keep_the_bits_rows_were_written_with()
    {
        ((int)SurveyShotFlags.None).ShouldBe(0);
        ((int)SurveyShotFlags.Surface).ShouldBe(1);
        ((int)SurveyShotFlags.Duplicate).ShouldBe(2);
        ((int)SurveyShotFlags.Splay).ShouldBe(4);
        ((int)SurveyShotFlags.NotVisible).ShouldBe(8);
        ((int)SurveyShotFlags.NotLrud).ShouldBe(16);
    }

    [Fact]
    public void Every_flag_is_a_single_distinct_bit()
    {
        // A bit set whose members overlap, or whose members are not powers of two, stores answers
        // nobody can read back: setting one flag would set another, and a stored value would
        // decode to a combination that was never written.
        AssertDisjointSingleBits(Enum.GetValues<SurveyStationFlags>().Select(f => (int)f));
        AssertDisjointSingleBits(Enum.GetValues<SurveyShotFlags>().Select(f => (int)f));
    }

    [Fact]
    public void A_shot_reports_the_flags_the_file_set_on_it()
    {
        var shot = Shot(SurveyShotFlags.Splay | SurveyShotFlags.Surface);

        shot.IsSplay.ShouldBeTrue();
        shot.IsSurface.ShouldBeTrue();
        shot.IsDuplicate.ShouldBeFalse();
    }

    [Fact]
    public void A_shot_with_no_flags_is_an_ordinary_leg_of_the_traverse()
    {
        var shot = Shot(SurveyShotFlags.None);

        shot.IsSplay.ShouldBeFalse();
        shot.IsSurface.ShouldBeFalse();
        shot.IsDuplicate.ShouldBeFalse();
    }

    [Fact]
    public void A_shot_flagged_duplicate_is_not_thereby_a_splay()
    {
        // The two mean opposite things about whether the leg is passage: a duplicate leg is real
        // passage measured twice, a splay is not passage at all. They sit in adjacent bits, which
        // is exactly how a mapping mistake would go unnoticed.
        var shot = Shot(SurveyShotFlags.Duplicate);

        shot.IsDuplicate.ShouldBeTrue();
        shot.IsSplay.ShouldBeFalse();
    }

    [Fact]
    public void A_station_reports_the_flags_the_file_set_on_it()
    {
        var station = Station(SurveyStationFlags.Entrance | SurveyStationFlags.Fixed);

        station.IsEntrance.ShouldBeTrue();
        station.IsFixed.ShouldBeTrue();

        var ordinary = Station(SurveyStationFlags.Underground);
        ordinary.IsEntrance.ShouldBeFalse();
        ordinary.IsFixed.ShouldBeFalse();
    }

    private static void AssertDisjointSingleBits(IEnumerable<int> values)
    {
        var seen = 0;
        foreach (var value in values.Where(v => v != 0))
        {
            (value & (value - 1)).ShouldBe(0, $"{value} is not a single bit");
            (value & seen).ShouldBe(0, $"{value} overlaps a flag already defined");
            seen |= value;
        }
    }

    private static SurveyShot Shot(SurveyShotFlags flags) => new()
    {
        SurveyModelId = Guid.NewGuid(),
        Geom = new LineString([new CoordinateZ(25.0, 45.0, 700), new CoordinateZ(25.001, 45.0, 695)])
        {
            SRID = 4326,
        },
        LengthM = 78.5,
        Flags = flags,
    };

    private static SurveyStation Station(SurveyStationFlags flags) => new()
    {
        SurveyModelId = Guid.NewGuid(),
        Name = "1.4",
        Position = new Point(new CoordinateZ(25.0, 45.0, 700)) { SRID = 4326 },
        Flags = flags,
    };
}
