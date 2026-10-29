// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Expeditions;
using SilexGis.Domain.ResLinks;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Every shipped vocabulary keeps the codes it has already shipped, in the order it shipped them.
/// </summary>
/// <remarks>
/// A shipped row's display order is its position in its seed list, and the seeder never re-sorts
/// a row an installation already has. A code inserted in the middle therefore renumbers everything
/// after it on a fresh installation only, and nothing on screen says why two installations
/// disagree; a removed code leaves a row the list no longer protects. The lists below are what has
/// shipped. A new code goes at the end of its seed list, and then at the end of its line here —
/// a list may grow past what is pinned, never change inside it.
/// </remarks>
public class ShippedVocabularyOrderTests
{
    public static TheoryData<string, string[], string[]> Vocabularies() => new()
    {
        {
            "trip types",
            ["exploration", "survey", "maintenance", "training", "tourism", "rescue", "science", "other"],
            [.. TripTypeSeeds.All.Select(s => s.Code)]
        },
        {
            "trip participant roles",
            ["participant", "proposer", "leader", "driver", "surveyor", "photographer", "trainee",
                "instructor", "callout_contact"],
            [.. TripParticipantRoleSeeds.All.Select(s => s.Code)]
        },
        {
            "camp roster roles",
            ["member", "organiser", "cook", "base_camp", "driver", "medic", "equipment", "guest"],
            [.. ExpeditionRosterRoleSeeds.All.Select(s => s.Code)]
        },
        {
            "relation types",
            ["same-object", "related-to", "contains", "documents", "derived-from", "adjacent-to",
                "duplicate-of", "needs-clarification", "trip-work-area", "trip-objective", "trip-visited",
                "trip-surveyed", "trip-discovered", "trip-dug", "trip-photographed",
                "trip-searched-not-found", "trip-lead", "trip-follows-on-from", "text-of", "map-plan-of",
                "map-profile-of", "map-other-of", "map-station-point", "map-place-point"],
            [.. ResLinkRelationTypeSeeds.All.Select(s => s.Code)]
        },
        {
            "document kinds",
            ["survey_report", "trip_report", "map", "photo", "permit", "article", "correspondence", "other"],
            [.. DocumentTypeSeeds.All.Select(s => s.Code)]
        },
    };

    [Theory]
    [MemberData(nameof(Vocabularies))]
    public void A_shipped_list_only_ever_grows_at_its_end(string vocabulary, string[] shipped, string[] current)
    {
        current.Length.ShouldBeGreaterThanOrEqualTo(shipped.Length,
            $"a shipped code was removed from the {vocabulary}");
        current.Take(shipped.Length).ShouldBe(shipped,
            $"the {vocabulary} changed inside what has shipped; new codes go at the end, " +
            "because the seeder orders rows by position and never re-sorts an existing one");
    }

    [Fact]
    public void A_shipped_row_is_ordered_by_its_position_in_its_list()
    {
        ShippedVocabularyOrder.Of(0).ShouldBe(10);
        ShippedVocabularyOrder.Of(7).ShouldBe(80);
    }
}
