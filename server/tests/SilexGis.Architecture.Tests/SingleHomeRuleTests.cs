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
}
