// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Trips;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Who may put somebody on a trip's list, who may answer for whom, and what a note beside an
/// answer may say. Every refusal here is asserted next to the permission it is the absence of, so
/// a rule that stopped granting anything at all would fail rather than look stricter.
/// </summary>
public class TripInvitationRulesTests
{
    private static readonly Guid Caller = Guid.CreateVersion7();
    private static readonly Guid Somebody = Guid.CreateVersion7();

    [Fact]
    public void A_note_is_stored_as_typed_with_only_its_surrounding_whitespace_removed()
    {
        TripInvitationRules.Normalize("  can drive   four  \r\n").ShouldBe("can drive   four");
        TripInvitationRules.Normalize("   ").ShouldBeNull();
        TripInvitationRules.Normalize(null).ShouldBeNull();
    }

    /// <summary>
    /// An answer needs no words with it, so an empty note is acceptable where an empty comment
    /// body is not — but the cap is real, and it is measured after the whitespace comes off so
    /// trailing blanks can neither smuggle a long note past it nor push a legitimate one over.
    /// </summary>
    [Fact]
    public void A_note_may_be_absent_but_may_not_be_endless()
    {
        TripInvitationRules.ValidateNote(null).ShouldBeEmpty();
        TripInvitationRules.ValidateNote("   ").ShouldBeEmpty();
        TripInvitationRules.ValidateNote("Only if we start after nine.").ShouldBeEmpty();

        var full = new string('x', TripInvitationRules.MaxNoteLength);
        TripInvitationRules.ValidateNote(full).ShouldBeEmpty();
        TripInvitationRules.ValidateNote(full + "  ").ShouldBeEmpty();
        TripInvitationRules.ValidateNote(full + "x").ShouldNotBeEmpty();
    }

    /// <summary>
    /// The identity test the whole self branch rests on. A caver row carries no authorization
    /// meaning of its own, so being that person means holding the account it is linked to — and
    /// a person with no account is nobody, rather than being everybody who has no account.
    /// </summary>
    [Fact]
    public void The_person_an_answer_is_about_is_whoever_holds_the_account_it_is_linked_to()
    {
        TripInvitationRules.AnswersForSelf(Caller, Caller).ShouldBeTrue();
        TripInvitationRules.AnswersForSelf(Caller, Somebody).ShouldBeFalse();
        TripInvitationRules.AnswersForSelf(Caller, null).ShouldBeFalse();
        TripInvitationRules.AnswersForSelf(Guid.Empty, null).ShouldBeFalse();
        TripInvitationRules.AnswersForSelf(Guid.Empty, Guid.Empty).ShouldBeFalse();
    }

    /// <summary>
    /// Reading the plan is what admits somebody to it, and the route has already asked that
    /// question by the time this one is put — so what is left here is whose answer it is. A
    /// reader gives their own and nobody else's.
    /// </summary>
    [Fact]
    public void A_reader_answers_for_themselves_and_for_nobody_else()
    {
        TripInvitationRules.MayAnswerFor(Caller, Caller, mayWriteTrip: false, isFullAdmin: false)
            .ShouldBeTrue();
        TripInvitationRules.MayAnswerFor(Caller, Somebody, mayWriteTrip: false, isFullAdmin: false)
            .ShouldBeFalse();
        TripInvitationRules.MayAnswerFor(Caller, null, mayWriteTrip: false, isFullAdmin: false)
            .ShouldBeFalse();
    }

    /// <summary>
    /// Whoever runs the trip answers for anybody, because somebody has to be able to write down
    /// the answer of a member who telephoned. A full administrator may as well — and neither of
    /// those is a right an ordinary reader picks up by being on the list.
    /// </summary>
    [Fact]
    public void Whoever_may_write_the_trip_answers_for_anybody_and_so_does_an_administrator()
    {
        TripInvitationRules.MayAnswerFor(Caller, Somebody, mayWriteTrip: true, isFullAdmin: false)
            .ShouldBeTrue();
        TripInvitationRules.MayAnswerFor(Caller, Somebody, mayWriteTrip: false, isFullAdmin: true)
            .ShouldBeTrue();

        // Including for a person with no account at all, who has no other way of being answered
        // for and would otherwise be unrecordable.
        TripInvitationRules.MayAnswerFor(Caller, null, mayWriteTrip: true, isFullAdmin: false)
            .ShouldBeTrue();

        TripInvitationRules.MayAnswerFor(Caller, Somebody, mayWriteTrip: false, isFullAdmin: false)
            .ShouldBeFalse();
    }

    /// <summary>
    /// Nobody without an account answers for anyone, themselves included: every answer is
    /// attributed to a named account and audited under it, and an unattributable one is not a
    /// thing this system can store.
    /// </summary>
    [Fact]
    public void An_answer_with_no_account_behind_it_is_refused_however_much_else_is_held()
    {
        TripInvitationRules.MayAnswerFor(Guid.Empty, Somebody, mayWriteTrip: true, isFullAdmin: true)
            .ShouldBeFalse();
        TripInvitationRules.MayInvite(Guid.Empty, mayWriteTrip: true, isFullAdmin: true).ShouldBeFalse();

        TripInvitationRules.MayAnswerFor(Caller, Somebody, mayWriteTrip: true, isFullAdmin: true).ShouldBeTrue();
        TripInvitationRules.MayInvite(Caller, mayWriteTrip: true, isFullAdmin: true).ShouldBeTrue();
    }

    /// <summary>
    /// Editing the list is running the trip. There is no self branch here on purpose: putting
    /// yourself on a list is answering, and answering has its own rule and its own route.
    /// </summary>
    [Fact]
    public void Editing_the_list_takes_the_trip_and_a_plain_reader_does_not_have_it()
    {
        TripInvitationRules.MayInvite(Caller, mayWriteTrip: true, isFullAdmin: false).ShouldBeTrue();
        TripInvitationRules.MayInvite(Caller, mayWriteTrip: false, isFullAdmin: true).ShouldBeTrue();
        TripInvitationRules.MayInvite(Caller, mayWriteTrip: false, isFullAdmin: false).ShouldBeFalse();
    }
}
