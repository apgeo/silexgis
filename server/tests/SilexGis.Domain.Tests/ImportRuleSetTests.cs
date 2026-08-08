// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which rule set applies to whom, what makes a document savable, and that the shipped set
/// actually says what the taxonomies can answer.
/// </summary>
public class ImportRuleSetTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Club = Guid.NewGuid();
    private static readonly Guid OtherClub = Guid.NewGuid();

    private static TermRuleSet Set(
        TermRuleScope scope, bool isDefault, Guid? group = null, bool seeded = false, Guid? owner = null) =>
        new()
        {
            Name = $"{scope}{(isDefault ? " default" : string.Empty)}",
            Scope = scope,
            OwnerUserId = scope == TermRuleScope.User ? owner ?? User : null,
            CavingGroupId = group,
            IsDefault = isDefault,
            IsSeeded = seeded,
            Rules = "{}",
        };

    [Fact]
    public void Resolution_walks_outwards_from_the_person_to_the_installation()
    {
        var own = Set(TermRuleScope.User, isDefault: true);
        var club = Set(TermRuleScope.CavingGroup, isDefault: true, group: Club);
        var installation = Set(TermRuleScope.Installation, isDefault: true);
        var shipped = Set(TermRuleScope.Installation, isDefault: false, seeded: true);

        TermRuleSetRules.Resolve([shipped, installation, club, own], User, [Club]).ShouldBe(own);
        TermRuleSetRules.Resolve([shipped, installation, club], User, [Club]).ShouldBe(club);
        TermRuleSetRules.Resolve([shipped, installation], User, [Club]).ShouldBe(installation);
        TermRuleSetRules.Resolve([shipped], User, [Club]).ShouldBe(shipped);
        TermRuleSetRules.Resolve([], User, [Club]).ShouldBeNull();
    }

    [Fact]
    public void Somebody_elses_default_is_not_yours()
    {
        var theirs = Set(TermRuleScope.User, isDefault: true, owner: Guid.NewGuid());
        var installation = Set(TermRuleScope.Installation, isDefault: true);

        TermRuleSetRules.Resolve([theirs, installation], User, []).ShouldBe(installation);
    }

    [Fact]
    public void A_member_of_two_clubs_gets_the_same_answer_every_time()
    {
        // Ordered by the caller's own group list rather than by whichever row came back first:
        // an answer that moved between requests would make an import propose differently for
        // no reason anybody could see.
        var first = Set(TermRuleScope.CavingGroup, isDefault: true, group: Club);
        var second = Set(TermRuleScope.CavingGroup, isDefault: true, group: OtherClub);

        TermRuleSetRules.Resolve([second, first], User, [Club, OtherClub]).ShouldBe(first);
        TermRuleSetRules.Resolve([first, second], User, [OtherClub, Club]).ShouldBe(second);
    }

    [Fact]
    public void A_group_the_caller_does_not_belong_to_contributes_nothing()
    {
        var elsewhere = Set(TermRuleScope.CavingGroup, isDefault: true, group: OtherClub);
        var installation = Set(TermRuleScope.Installation, isDefault: true);

        TermRuleSetRules.Resolve([elsewhere, installation], User, [Club]).ShouldBe(installation);
    }

    [Fact]
    public void The_shipped_set_is_a_valid_document_and_every_rule_can_be_evaluated()
    {
        TermRuleValidation.Validate(TermRuleSeeds.DefaultDocument).ShouldBeEmpty();
        TermRuleSeeds.Default.ShouldNotBeEmpty();
        TermRuleSeeds.Default.Select(r => r.Id).Distinct().Count().ShouldBe(TermRuleSeeds.Default.Count);
    }

    [Theory]
    [InlineData("Peștera Ursilor", ImportTargetKind.Cave, "cave")]
    [InlineData("P. Ursilor", ImportTargetKind.Cave, "cave")]
    [InlineData("Avenul din Șesuri", ImportTargetKind.Cave, "pit")]
    [InlineData("Izbucul Tăuz", ImportTargetKind.SurfaceFeature, "water_flow")]
    [InlineData("Ponorul Bătrân", ImportTargetKind.SurfaceFeature, "water_flow")]
    [InlineData("Dolina mare", ImportTargetKind.SurfaceFeature, "sinkhole")]
    [InlineData("Bear Cave", ImportTargetKind.Cave, "cave")]
    [InlineData("Cold Spring", ImportTargetKind.SurfaceFeature, "water_flow")]
    public void The_shipped_rules_propose_what_a_caver_would(string name, ImportTargetKind kind, string code)
    {
        var proposal = TermRuleEvaluator.Evaluate(TermRuleSeeds.Default, new CandidateText(name, null), []);

        proposal.Winner.ShouldNotBeNull();
        proposal.Winner.Rule.Target.ShouldBe(kind);
        (proposal.Winner.Rule.CaveTypeCode ?? proposal.Winner.Rule.FeatureTypeCode).ShouldBe(code);
    }

    [Theory]
    [InlineData("Parcare")]
    [InlineData("Cabana")]
    [InlineData("Start traseu")]
    public void The_shipped_rules_claim_nothing_they_should_not(string name)
    {
        // The negative half. A rule set that claimed everything would pass every test above
        // and produce a registry full of car parks.
        TermRuleEvaluator.Evaluate(TermRuleSeeds.Default, new CandidateText(name, null), [])
            .Winner.ShouldBeNull();
    }

    [Fact]
    public void A_document_is_refused_for_the_things_that_would_make_it_unrunnable()
    {
        var duplicateIds = new TermRuleDocument
        {
            Rules =
            [
                Valid("a") with { Id = "same" },
                Valid("b") with { Id = "same" },
            ],
        };
        TermRuleValidation.Validate(duplicateIds)
            .ShouldContain(e => e.Contains("used more than once"));

        var badRegex = new TermRuleDocument
        {
            Rules = [Valid("a") with { MatchMode = TermMatchMode.Regex, Terms = Terms("[unclosed") }],
        };
        TermRuleValidation.Validate(badRegex).ShouldContain(e => e.Contains("regular expression"));

        var noTerms = new TermRuleDocument
        {
            Rules = [Valid("a") with { Terms = new Dictionary<string, IReadOnlyList<string>>() }],
        };
        TermRuleValidation.Validate(noTerms).ShouldContain(e => e.Contains("no terms"));

        var noFields = new TermRuleDocument
        {
            Rules = [Valid("a") with { MatchName = false, MatchDescription = false }],
        };
        TermRuleValidation.Validate(noFields).ShouldContain(e => e.Contains("reads no field"));
    }

    [Fact]
    public void A_rule_may_only_name_the_taxonomy_its_target_uses()
    {
        // A rule somebody re-pointed and half-edited would otherwise create caves of a type
        // nobody chose.
        var confused = new TermRuleDocument
        {
            Rules = [Valid("a") with { Target = ImportTargetKind.Cave, FeatureTypeCode = "sinkhole" }],
        };
        TermRuleValidation.Validate(confused).ShouldContain(e => e.Contains("names an entrance or feature type"));

        var typeless = new TermRuleDocument
        {
            Rules = [Valid("a") with { Target = ImportTargetKind.SurfaceFeature, FeatureTypeCode = null }],
        };
        TermRuleValidation.Validate(typeless).ShouldContain(e => e.Contains("without saying which kind"));
    }

    private static TermRule Valid(string id) => new()
    {
        Id = id,
        Name = id,
        Terms = Terms("peșteră"),
        Target = ImportTargetKind.Cave,
        CaveTypeCode = "cave",
    };

    private static Dictionary<string, IReadOnlyList<string>> Terms(params string[] terms) =>
        new() { ["ro"] = terms };
}
