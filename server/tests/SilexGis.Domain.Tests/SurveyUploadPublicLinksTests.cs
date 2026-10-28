// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

public class SurveyUploadPublicLinksTests
{
    [Fact]
    public void A_link_is_made_only_when_every_condition_holds()
    {
        SurveyUploadPublicLinks.Refusal(true, true, true, false).ShouldBeNull();
        SurveyUploadPublicLinks.Refusal(true, true, true, true).ShouldBe(SurveyUploadPublicLinks.ProtectedCave);
        SurveyUploadPublicLinks.Refusal(true, true, false, false).ShouldBe(SurveyUploadPublicLinks.NoShareRight);
        SurveyUploadPublicLinks.Refusal(true, false, true, false).ShouldBe(SurveyUploadPublicLinks.AccountOff);
        SurveyUploadPublicLinks.Refusal(false, true, true, false).ShouldBe(SurveyUploadPublicLinks.InstallationOff);
    }

    [Fact]
    public void The_reason_given_is_the_first_one_a_person_could_act_on()
    {
        SurveyUploadPublicLinks.Refusal(false, false, false, true).ShouldBe(SurveyUploadPublicLinks.InstallationOff);
        SurveyUploadPublicLinks.Refusal(true, false, false, true).ShouldBe(SurveyUploadPublicLinks.AccountOff);
        SurveyUploadPublicLinks.Refusal(true, true, false, true).ShouldBe(SurveyUploadPublicLinks.NoShareRight);
    }

    [Fact]
    public void A_protected_cave_gets_a_link_only_with_the_explicit_acknowledgement()
    {
        SurveyUploadPublicLinks.Refusal(true, true, true, true, protectedAcknowledged: false)
            .ShouldBe(SurveyUploadPublicLinks.ProtectedCave);
        SurveyUploadPublicLinks.Refusal(true, true, true, true, protectedAcknowledged: true).ShouldBeNull();

        // The acknowledgement answers the cave's protection only: it never stands in for the
        // installation, the account or the right to share.
        SurveyUploadPublicLinks.Refusal(false, true, true, true, true).ShouldBe(SurveyUploadPublicLinks.InstallationOff);
        SurveyUploadPublicLinks.Refusal(true, false, true, true, true).ShouldBe(SurveyUploadPublicLinks.AccountOff);
        SurveyUploadPublicLinks.Refusal(true, true, false, true, true).ShouldBe(SurveyUploadPublicLinks.NoShareRight);
    }

    [Fact]
    public void At_completion_the_upload_s_own_choice_comes_before_the_account_preference()
    {
        // Silent upload: the preference decides, either way.
        Decide(accountOn: true, wanted: null).Refusal.ShouldBeNull();
        Decide(accountOn: false, wanted: null).Refusal.ShouldBe(SurveyUploadPublicLinks.AccountOff);

        // An upload that says no gets none although the preference says yes, and says why.
        Decide(accountOn: true, wanted: false).Refusal.ShouldBe(SurveyUploadPublicLinks.NotWanted);

        // An upload that asks gets one although the preference says no.
        Decide(accountOn: false, wanted: true).Refusal.ShouldBeNull();

        // Neither can open a link the installation does not make, nor give a share right.
        Decide(installationOn: false, wanted: true).Refusal.ShouldBe(SurveyUploadPublicLinks.InstallationOff);
        Decide(installationOn: false, wanted: false).Refusal.ShouldBe(SurveyUploadPublicLinks.InstallationOff);
        Decide(wanted: true, mayShare: false).Refusal.ShouldBe(SurveyUploadPublicLinks.NoShareRight);
    }

    [Fact]
    public void At_completion_the_audience_is_the_upload_s_else_the_account_default()
    {
        Decide(accountAudience: SurveyUploadLinkAudience.Public, audience: null)
            .Audience.ShouldBe(SurveyUploadLinkAudience.Public);
        Decide(accountAudience: SurveyUploadLinkAudience.Organization, audience: null)
            .Audience.ShouldBe(SurveyUploadLinkAudience.Organization);
        Decide(accountAudience: SurveyUploadLinkAudience.Organization, audience: SurveyUploadLinkAudience.Public)
            .Audience.ShouldBe(SurveyUploadLinkAudience.Public);
        Decide(accountAudience: SurveyUploadLinkAudience.Public, audience: SurveyUploadLinkAudience.Organization)
            .Audience.ShouldBe(SurveyUploadLinkAudience.Organization);
    }

    [Fact]
    public void At_completion_a_protected_cave_is_linked_only_when_the_upload_acknowledged_it()
    {
        var refused = Decide(caveProtected: true, allowProtected: false);
        refused.Refusal.ShouldBe(SurveyUploadPublicLinks.ProtectedCave);
        refused.ProtectedAcknowledged.ShouldBeFalse();

        var acknowledged = Decide(caveProtected: true, allowProtected: true);
        acknowledged.Refusal.ShouldBeNull();
        acknowledged.ProtectedAcknowledged.ShouldBeTrue();

        // An acknowledgement given for a cave that is not protected records nothing: the link is an
        // ordinary one, and closes like one if the cave is protected later.
        var ordinary = Decide(caveProtected: false, allowProtected: true);
        ordinary.Refusal.ShouldBeNull();
        ordinary.ProtectedAcknowledged.ShouldBeFalse();

        // No acknowledgement is recorded on a link that is not made.
        Decide(caveProtected: true, allowProtected: true, mayShare: false).ProtectedAcknowledged.ShouldBeFalse();
        Decide(caveProtected: true, allowProtected: true, wanted: false).ProtectedAcknowledged.ShouldBeFalse();
    }

    [Fact]
    public void An_ordinary_link_closes_when_its_cave_is_protected_and_an_acknowledged_one_does_not()
    {
        SurveyUploadPublicLinks.Opens(caveProtected: false, protectedAcknowledged: false).ShouldBeTrue();
        SurveyUploadPublicLinks.Opens(caveProtected: true, protectedAcknowledged: false).ShouldBeFalse();
        SurveyUploadPublicLinks.Opens(caveProtected: true, protectedAcknowledged: true).ShouldBeTrue();
        SurveyUploadPublicLinks.Opens(caveProtected: false, protectedAcknowledged: true).ShouldBeTrue();
    }

    [Fact]
    public void An_organization_link_admits_only_a_signed_in_caller_and_a_public_one_anybody()
    {
        SurveyUploadPublicLinks.Admits(SurveyUploadLinkAudience.Public, signedIn: false).ShouldBeTrue();
        SurveyUploadPublicLinks.Admits(SurveyUploadLinkAudience.Public, signedIn: true).ShouldBeTrue();
        SurveyUploadPublicLinks.Admits(SurveyUploadLinkAudience.Organization, signedIn: false).ShouldBeFalse();
        SurveyUploadPublicLinks.Admits(SurveyUploadLinkAudience.Organization, signedIn: true).ShouldBeTrue();
    }

    [Fact]
    public void A_token_is_long_random_and_looked_up_only_by_its_hash()
    {
        var token = SurveyUploadPublicLinks.NewToken();
        token.Length.ShouldBe(43);
        SurveyUploadPublicLinks.NewToken().ShouldNotBe(token);
        SurveyUploadPublicLinks.Hash(token).ShouldNotContain(token);
        SurveyUploadPublicLinks.Hash(token).ShouldBe(SurveyUploadPublicLinks.Hash(token));
        SurveyUploadPublicLinks.IsPlausible(token).ShouldBeTrue();
        SurveyUploadPublicLinks.IsPlausible("").ShouldBeFalse();
        SurveyUploadPublicLinks.IsPlausible(null).ShouldBeFalse();
        SurveyUploadPublicLinks.IsPlausible(new string('A', SurveyUploadPublicLinks.MaxTokenLength + 1)).ShouldBeFalse();
        SurveyUploadLinks.PublicPage(token).ShouldBe($"/p/survey/{token}");
    }

    [Fact]
    public void Stage_states_follow_where_the_run_stands()
    {
        States(SurveyUploadStatus.Running, SurveyUploadStage.Compile, compile: true)
            .ShouldBe(["done", "done", "done", "active", "pending", "pending"]);
        States(SurveyUploadStatus.Failed, SurveyUploadStage.Compile, compile: true)
            .ShouldBe(["done", "done", "done", "failed", "pending", "pending"]);
        States(SurveyUploadStatus.Succeeded, SurveyUploadStage.Read, compile: true)
            .ShouldBe(["done", "done", "done", "done", "done", "done"]);
        States(SurveyUploadStatus.Succeeded, SurveyUploadStage.Unpack, compile: false)
            .ShouldBe(["done", "done", "skipped", "skipped", "skipped", "skipped"]);
        States(SurveyUploadStatus.Queued, null, compile: true)
            .ShouldBe(["pending", "pending", "pending", "pending", "pending", "pending"]);
        States(SurveyUploadStatus.Running, null, compile: true)
            .ShouldBe(["active", "pending", "pending", "pending", "pending", "pending"]);
    }

    private static SurveyUploadLinkDecision Decide(
        bool installationOn = true,
        bool accountOn = true,
        SurveyUploadLinkAudience accountAudience = SurveyUploadLinkAudience.Public,
        bool? wanted = null,
        SurveyUploadLinkAudience? audience = null,
        bool allowProtected = false,
        bool mayShare = true,
        bool caveProtected = false) =>
        SurveyUploadPublicLinks.AtCompletion(
            installationOn, accountOn, accountAudience, wanted, audience, allowProtected, mayShare, caveProtected);

    private static string[] States(SurveyUploadStatus status, SurveyUploadStage? stage, bool compile) =>
        [.. SurveyUploadPublicLinks.StageStates(status, stage, compile).Select(s => s.State.ToString().ToLowerInvariant())];
}
