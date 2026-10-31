// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Tests;

public sealed class EntranceAltitudesTests
{
    [Fact]
    public void A_gap_inside_the_tolerance_agrees_and_one_outside_it_disagrees()
    {
        var close = EntranceAltitudes.Compare(1015m, 1000d);
        close.Verdict.ShouldBe(EntranceAltitudeVerdict.Agrees);
        close.DifferenceM.ShouldBe(15d);
        close.Disagrees.ShouldBeFalse();

        var far = EntranceAltitudes.Compare(940m, 1000d);
        far.Verdict.ShouldBe(EntranceAltitudeVerdict.Disagrees);
        far.DifferenceM.ShouldBe(-60d);
        far.AbsoluteDifferenceM.ShouldBe(60d);
        far.Disagrees.ShouldBeTrue();
    }

    [Fact]
    public void Exactly_the_tolerance_is_still_agreement()
    {
        EntranceAltitudes.Compare(1000m + (decimal)EntranceAltitudes.ToleranceM, 1000d)
            .Verdict.ShouldBe(EntranceAltitudeVerdict.Agrees);
    }

    [Fact]
    public void A_missing_side_is_never_compared_and_never_read_as_nought()
    {
        var unrecorded = EntranceAltitudes.Compare(null, 1000d);
        unrecorded.Verdict.ShouldBe(EntranceAltitudeVerdict.NotRecorded);
        unrecorded.DifferenceM.ShouldBeNull();

        var unmodelled = EntranceAltitudes.Compare(1000m, null);
        unmodelled.Verdict.ShouldBe(EntranceAltitudeVerdict.NotModelled);
        unmodelled.DifferenceM.ShouldBeNull();

        // A recorded sea level is a recorded altitude.
        EntranceAltitudes.Compare(0m, 100d).Verdict.ShouldBe(EntranceAltitudeVerdict.Disagrees);

        EntranceAltitudes.Compare(1000m, double.NaN).Verdict.ShouldBe(EntranceAltitudeVerdict.NotModelled);
    }
}
