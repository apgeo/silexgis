// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The rule that separates a measured wall distance from a file saying nothing was measured, and
/// the numbers behind the cross-section vocabulary.
///
/// <para>
/// This rule is worth its own tests because getting it wrong is invisible. A negative sentinel
/// stored as a measurement is a negative passage width, and everything computed from it — a mean
/// width, a cross-sectional area, a volume — comes out as a number of the right magnitude and the
/// wrong sign, or of the wrong magnitude and the right sign, and nothing about the result says a
/// file was misread.
/// </para>
/// </summary>
public class SurveyDimensionsTests
{
    [Fact]
    public void A_measured_distance_is_kept_exactly()
    {
        SurveyDimensions.Measured(2.5).ShouldBe(2.5);
        SurveyDimensions.Measured(0.01).ShouldBe(0.01);
        SurveyDimensions.Measured(137.25).ShouldBe(137.25);
    }

    [Fact]
    public void A_station_hard_against_the_wall_measured_zero_and_that_is_a_measurement()
    {
        // Zero is the one value where "not measured" and "measured" are easy to conflate, and they
        // are different statements: the surveyor stood against the wall. It survives as zero.
        SurveyDimensions.Measured(0).ShouldBe(0);
    }

    [Theory]
    // What the format that writes metres directly puts in an unmeasured field.
    [InlineData(-1.0)]
    // What the format that stores centimetres in a signed integer produces once its all-bits-set
    // field has been divided by a hundred. A rule testing for a particular sentinel value would
    // keep this one as a real wall a centimetre away.
    [InlineData(-0.01)]
    [InlineData(-0.0001)]
    [InlineData(-4000.0)]
    public void A_negative_distance_is_the_file_saying_it_was_not_measured(double stored)
    {
        SurveyDimensions.Measured(stored).ShouldBeNull();
    }

    [Fact]
    public void An_unmeasured_distance_is_absent_rather_than_zero()
    {
        // Stated separately from the null assertion above because zero is the substitution that
        // would pass every other check: it is non-negative, it is finite, and it renders as a
        // perfectly ordinary number.
        var absent = SurveyDimensions.Measured(-1.0);

        absent.ShouldBeNull();
        absent.ShouldNotBe(0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_that_is_not_a_number_is_not_a_measurement(double stored)
    {
        // Neither format is documented as writing one, so a value like this is a file this
        // application cannot read a distance out of. It must not be stored, because arithmetic
        // over it spreads from the one station to every figure computed across the whole cave.
        SurveyDimensions.Measured(stored).ShouldBeNull();
    }

    [Fact]
    public void A_reading_with_nothing_measured_carries_no_fact()
    {
        SurveyDimensions.AnyMeasured(null, null, null, null).ShouldBeFalse();
    }

    [Fact]
    public void A_reading_with_one_wall_measured_is_still_a_reading()
    {
        SurveyDimensions.AnyMeasured(1.5, null, null, null).ShouldBeTrue();
        SurveyDimensions.AnyMeasured(null, null, null, 0).ShouldBeTrue();
    }

    [Fact]
    public void Section_shapes_keep_the_numbers_rows_were_written_with()
    {
        // Stored values. Renumbering one would reclassify every reading already extracted, and an
        // oval passage recorded as a tunnel is not something a later reader can detect.
        ((short)SurveySectionShape.Oval).ShouldBe((short)1);
        ((short)SurveySectionShape.Square).ShouldBe((short)2);
        ((short)SurveySectionShape.Diamond).ShouldBe((short)3);
        ((short)SurveySectionShape.Tunnel).ShouldBe((short)4);
    }

    [Fact]
    public void The_section_vocabulary_has_no_word_for_the_file_saying_nothing()
    {
        // Absence is the column being null. A zero member would give the same fact two storable
        // forms, and every reader would have to know both — including the ones written after the
        // person who chose it has forgotten.
        Enum.GetValues<SurveySectionShape>().ShouldNotContain(shape => (short)shape == 0);
        Enum.IsDefined((SurveySectionShape)0).ShouldBeFalse();
    }

    [Fact]
    public void Width_and_height_need_both_of_their_walls()
    {
        var both = Reading(left: 1.5, right: 2.0, up: 3.0, down: 0.5);
        both.WidthM.ShouldBe(3.5);
        both.HeightM.ShouldBe(3.5);

        // Half a measurement is not a width. Treating a missing wall as zero would report this
        // passage as 1.5 m wide when what is known is that it is at least 1.5 m wide.
        var half = Reading(left: 1.5, right: null, up: null, down: 0.5);
        half.WidthM.ShouldBeNull();
        half.HeightM.ShouldBeNull();
    }

    [Fact]
    public void A_reading_from_the_format_that_names_no_leg_carries_neither_leg_nor_shape()
    {
        var reading = Reading(left: 1.0, right: 1.0, up: 1.0, down: 1.0);

        reading.ShotId.ShouldBeNull();
        reading.Section.ShouldBeNull();
    }

    private static SurveyLrud Reading(double? left, double? right, double? up, double? down) => new()
    {
        SurveyModelId = Guid.NewGuid(),
        StationName = "main.1",
        LeftM = left,
        RightM = right,
        UpM = up,
        DownM = down,
    };
}
