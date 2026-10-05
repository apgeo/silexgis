// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Trips;
using Xunit;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which stored layout a write-up is built in when nobody named one: the trip purpose's own
/// layout first, the installation's chosen one next, and nothing — the shipped layout — last.
/// </summary>
/// <remarks>
/// Each test asserts the pick and what was passed over in the same body: a rule that always
/// answered the installation's choice would satisfy every "the default is used" assertion on its
/// own, and would read as a club that never bound a layout rather than as the broken thing it is.
/// </remarks>
public class ReportTemplateChoiceTests
{
    private static readonly Guid Installation = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SurveysOwn = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TrainingsOwn = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private const long Survey = 7;
    private const long Training = 8;
    private const long Dig = 9;

    private static readonly ReportTemplateCandidate[] Stored =
    [
        new(Installation, null, true),
        new(SurveysOwn, Survey, false),
        new(TrainingsOwn, Training, false),
    ];

    [Fact]
    public void A_purposes_own_layout_wins_over_the_installations_choice()
    {
        ReportTemplateChoice.Pick(Stored, Survey).ShouldBe(SurveysOwn);
        ReportTemplateChoice.Pick(Stored, Training).ShouldBe(TrainingsOwn);
    }

    [Fact]
    public void A_purpose_with_no_layout_of_its_own_gets_the_installations_choice()
    {
        ReportTemplateChoice.Pick(Stored, Dig).ShouldBe(Installation);
        // And a trip recorded under no purpose, or a camp, the same.
        ReportTemplateChoice.Pick(Stored, null).ShouldBe(Installation);
    }

    [Fact]
    public void Another_purposes_layout_is_never_borrowed()
    {
        // No installation choice at all: the dig falls through to the shipped layout rather than
        // being written up as a survey.
        ReportTemplateCandidate[] ownOnly = [new(SurveysOwn, Survey, false)];
        ReportTemplateChoice.Pick(ownOnly, Dig).ShouldBeNull();
        ReportTemplateChoice.Pick(ownOnly, Survey).ShouldBe(SurveysOwn);
    }

    [Fact]
    public void With_nothing_stored_the_answer_is_the_shipped_layout()
    {
        ReportTemplateChoice.Pick([], Survey).ShouldBeNull();
        ReportTemplateChoice.Pick([], null).ShouldBeNull();
    }

    [Fact]
    public void A_purposes_layout_the_installation_also_chose_serves_every_other_purpose_too()
    {
        // The administrator marked the survey layout as the installation's choice as well: it is
        // the survey's own by binding and everybody else's by choice.
        ReportTemplateCandidate[] both = [new(SurveysOwn, Survey, true), new(TrainingsOwn, Training, false)];
        ReportTemplateChoice.Pick(both, Survey).ShouldBe(SurveysOwn);
        ReportTemplateChoice.Pick(both, Dig).ShouldBe(SurveysOwn);
        ReportTemplateChoice.Pick(both, Training).ShouldBe(TrainingsOwn);
    }
}
