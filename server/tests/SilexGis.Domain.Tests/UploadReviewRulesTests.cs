// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Who reads and decides held uploads, and the one exception that comes with reading them: held
/// lines are shown whole to exactly the accounts that may read the queue.
/// </summary>
public class UploadReviewRulesTests
{
    private static readonly Guid CallerId = Guid.CreateVersion7();

    private static AccessEntrySnapshot Entry(AccessEffect effect, AccessDomain domain, AccessAction actions) => new(
        1, Guid.CreateVersion7(), null, null, effect, domain, actions, AccessScopeKind.All,
        null, null, null, null);

    private static AccessContext With(params AccessEntrySnapshot[] entries) => new(CallerId, false, [], entries);

    [Fact]
    public void Reading_the_queue_is_seeing_held_lines_whole_and_neither_comes_with_any_other_right()
    {
        var reviewer = With(Entry(AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read));
        UploadReviewRules.MayRead(reviewer).ShouldBeTrue();
        UploadReviewRules.SeesHeldWhole(reviewer).ShouldBeTrue();
        UploadReviewRules.MayDecide(reviewer).ShouldBeFalse();

        // Every read and exact-position right over the content itself is not review.
        var everythingElse = With(
            Entry(AccessEffect.Allow, AccessDomain.Features, AccessAction.Read | AccessAction.ViewExactLocation),
            Entry(AccessEffect.Allow, AccessDomain.TripLogs, AccessAction.Read),
            Entry(AccessEffect.Allow, AccessDomain.Documents, AccessAction.Read));
        UploadReviewRules.MayRead(everythingElse).ShouldBeFalse();
        UploadReviewRules.SeesHeldWhole(everythingElse).ShouldBeFalse();

        UploadReviewRules.SeesHeldWhole(null).ShouldBeFalse();
        UploadReviewRules.SeesHeldWhole(With()).ShouldBeFalse();
    }

    [Fact]
    public void A_deny_of_reading_the_queue_takes_the_whole_view_with_it()
    {
        var denied = With(
            Entry(AccessEffect.Allow, AccessDomain.UploadReview, AccessAction.Read | AccessAction.Write),
            Entry(AccessEffect.Deny, AccessDomain.UploadReview, AccessAction.Read));
        UploadReviewRules.SeesHeldWhole(denied).ShouldBeFalse();
    }

    [Fact]
    public void A_full_administrator_reviews_and_sees_held_lines_whole()
    {
        var admin = new AccessContext(CallerId, true, [], []);
        UploadReviewRules.MayRead(admin).ShouldBeTrue();
        UploadReviewRules.MayDecide(admin).ShouldBeTrue();
        UploadReviewRules.SeesHeldWhole(admin).ShouldBeTrue();
    }
}
