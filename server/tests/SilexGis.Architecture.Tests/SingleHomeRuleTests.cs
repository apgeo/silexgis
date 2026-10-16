// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.RegularExpressions;
using Shouldly;

namespace SilexGis.Architecture.Tests;

/// <summary>
/// Disclosure questions that have one home stay in it: asked anywhere else, a second copy is free
/// to answer differently after the first is changed, and nothing would say so.
/// </summary>
public class SingleHomeRuleTests
{
    [Fact]
    public void Whether_people_may_be_read_at_all_is_asked_in_one_place()
    {
        // The domain-wide read over people (asked of no entry in particular) decides whether a
        // camp's roster, its write-up, a record's history and the relation chips may name anybody.
        // It is CaverDirectory.MayReadPeople; an installation that removes the right must find
        // people withheld everywhere at once.
        var asked = new Regex(@"AccessDomain\.Cavers,\s*AccessAction\.Read,\s*null\b");

        var elsewhere = TripSoftDeleteGuardTests.Sources()
            .Where(s => !s.File.EndsWith("/Common/CaverDirectory.cs", StringComparison.Ordinal))
            .Where(s => asked.IsMatch(s.Source))
            .Select(s => s.File)
            .ToList();

        elsewhere.ShouldBeEmpty("ask CaverDirectory.MayReadPeople instead of asking the evaluator directly");

        // The positive twin: the home itself asks it, so the scan above is looking at real text.
        TripSoftDeleteGuardTests.Sources()
            .Single(s => s.File.EndsWith("/Common/CaverDirectory.cs", StringComparison.Ordinal))
            .Source.ShouldMatch(asked.ToString());
    }

    [Fact]
    public void A_file_that_takes_the_injected_clock_reads_no_other()
    {
        // The clock is injected where a test reads a stamp back — a delivery's next attempt, a
        // job's retry delay, the moment a tracking report is judged to be in the future. A file
        // that takes it and also reads the wall clock directly has two clocks, and a test moving
        // one sees the other stay put: an assertion that passes or fails for a reason unrelated to
        // what it checks. The rest of the solution reads the wall clock and is left alone until a
        // test needs otherwise.
        var wall = new Regex(@"\bDateTime(Offset)?\.(UtcNow|Now)\b");
        var injected = new Regex(@"\bTimeProvider\b");

        var mixed = TripSoftDeleteGuardTests.Sources()
            .Where(s => injected.IsMatch(s.Source) && wall.IsMatch(s.Source))
            .Select(s => s.File)
            .ToList();

        mixed.ShouldBeEmpty("read the injected TimeProvider throughout a file that takes it");

        // The scan is looking at real text: files taking the clock exist.
        TripSoftDeleteGuardTests.Sources().Count(s => injected.IsMatch(s.Source)).ShouldBeGreaterThan(10);
    }
}
