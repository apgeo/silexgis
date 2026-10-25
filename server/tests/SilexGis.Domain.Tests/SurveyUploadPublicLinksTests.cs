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

    private static string[] States(SurveyUploadStatus status, SurveyUploadStage? stage, bool compile) =>
        [.. SurveyUploadPublicLinks.StageStates(status, stage, compile).Select(s => s.State.ToString().ToLowerInvariant())];
}
