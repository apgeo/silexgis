// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Api.Features.Statistics;
using SilexGis.Infrastructure.Statistics;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a request may write where a measure is asked for.
///
/// <para>
/// The vocabulary on the wire is the set of declared names and nothing else. Everything refused
/// below is a spelling the framework's own text-to-enumeration parse accepts, and each one that
/// got through would publish a contract nobody meant to: the underlying numbers become an accepted
/// address for a measure, and renumbering the enumeration then silently re-points every client
/// that started using them.
/// </para>
/// </summary>
public class RegistryMeasureNameTests
{
    [Theory]
    [InlineData("surveyedLength", RegistryMeasure.SurveyedLength)]
    [InlineData("SurveyedLength", RegistryMeasure.SurveyedLength)]
    [InlineData("  depth  ", RegistryMeasure.Depth)]
    [InlineData("RAMIFICATIONINDEX", RegistryMeasure.RamificationIndex)]
    public void A_declared_name_is_read_whatever_case_it_was_written_in(
        string written, RegistryMeasure expected)
    {
        RegistryMeasures.TryParse(written, out var measure).ShouldBeTrue(written);
        measure.ShouldBe(expected);
    }

    [Theory]

    // The underlying value, bare and behind either sign. A guard that looked only at the first
    // character saw the first of these and not the other two, so `+2` addressed a measure by its
    // number after all.
    [InlineData("2")]
    [InlineData("+2")]
    [InlineData("-1")]
    [InlineData(" +8 ")]

    // A comma-separated list, which that parse combines by bit and answers with whichever measure
    // the combination lands on — here depth (2) with the height above the entrance (3), which is
    // 3 again, so the request would have been answered as a measure it never named.
    [InlineData("Depth,PositiveDepth")]

    // And the ordinary refusals: a measure this registry does not publish, and no measure at all.
    // Altitude is deliberately not a name — a height above sea level is a coordinate.
    [InlineData("altitude")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_that_is_not_a_declared_name_is_refused(string? written)
    {
        RegistryMeasures.TryParse(written, out _).ShouldBeFalse(written ?? "(null)");
    }

    [Fact]
    public void Every_declared_measure_answers_to_the_name_the_answer_gives_it()
    {
        // The names the API publishes are the names it accepts back. A caller copying a measure out
        // of one answer into the next request is the ordinary way this route is used, and a name
        // that round-tripped in only one direction would break exactly that.
        foreach (var declared in Enum.GetValues<RegistryMeasure>())
        {
            RegistryMeasures.TryParse(declared.ToString(), out var measure)
                .ShouldBeTrue(declared.ToString());
            measure.ShouldBe(declared);
        }
    }
}
