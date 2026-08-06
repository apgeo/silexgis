// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a comment on a document may say, who may change it, and what replying means.
/// Every refusal here is asserted next to the permission it is the absence of, so a rule
/// that stopped granting anything at all would fail rather than look stricter.
/// </summary>
public class DocumentCommentRulesTests
{
    private static DocumentComment Comment(Guid? authorId = null, Guid? documentId = null, Guid? parentId = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId ?? Guid.CreateVersion7(),
            ParentId = parentId,
            AuthorId = authorId,
            Body = "The sump siphons after rain.",
        };

    [Fact]
    public void A_body_is_stored_as_typed_with_only_its_surrounding_whitespace_removed()
    {
        DocumentCommentRules.Normalize("  two   spaces inside  \r\n").ShouldBe("two   spaces inside");
        DocumentCommentRules.Normalize("   ").ShouldBeNull();
        DocumentCommentRules.Normalize(null).ShouldBeNull();
    }

    [Fact]
    public void A_comment_has_to_say_something_but_an_ordinary_remark_passes()
    {
        DocumentCommentRules.ValidateBody("Page 3 has the wrong bearing.").ShouldBeEmpty();
        DocumentCommentRules.ValidateBody("   \t \n ").ShouldNotBeEmpty();
        DocumentCommentRules.ValidateBody(string.Empty).ShouldNotBeEmpty();
    }

    [Fact]
    public void The_cap_is_measured_after_trimming_so_padding_neither_smuggles_nor_evicts()
    {
        var atCap = new string('x', DocumentCommentRules.MaxBodyLength);
        DocumentCommentRules.ValidateBody(atCap).ShouldBeEmpty();
        DocumentCommentRules.ValidateBody("   " + atCap + "   ").ShouldBeEmpty();
        DocumentCommentRules.ValidateBody(atCap + "x").ShouldNotBeEmpty();
    }

    [Fact]
    public void Only_the_author_rewrites_a_comment_and_an_administrator_does_not()
    {
        var author = Guid.CreateVersion7();
        var somebodyElse = Guid.CreateVersion7();
        var comment = Comment(author);

        DocumentCommentRules.MayEdit(comment, author).ShouldBeTrue();
        DocumentCommentRules.MayEdit(comment, somebodyElse).ShouldBeFalse();
        // Being able to remove a remark is not being able to reword it.
        DocumentCommentRules.MayDelete(comment, somebodyElse, isFullAdmin: true).ShouldBeTrue();
    }

    [Fact]
    public void A_comment_whose_author_account_is_gone_can_no_longer_be_edited_by_anyone()
    {
        var orphaned = Comment(authorId: null);
        var stillOwned = Comment(authorId: Guid.CreateVersion7());

        DocumentCommentRules.MayEdit(orphaned, Guid.CreateVersion7()).ShouldBeFalse();
        DocumentCommentRules.MayEdit(orphaned, Guid.Empty).ShouldBeFalse();
        DocumentCommentRules.MayEdit(stillOwned, stillOwned.AuthorId!.Value).ShouldBeTrue();
    }

    [Fact]
    public void Posting_asks_for_an_account_and_for_nothing_else()
    {
        // Any signed-in member may join the discussion on a document they can open: posting
        // is not a right that has to be granted, so the rule admits an ordinary account the
        // same as the one that uploaded the file.
        DocumentCommentRules.MayPost(Guid.CreateVersion7()).ShouldBeTrue();
        // The single refusal is a caller with no account behind them, because a remark is
        // stored attributed and audited under a named account.
        DocumentCommentRules.MayPost(Guid.Empty).ShouldBeFalse();
    }

    [Fact]
    public void Deletion_reaches_the_author_and_the_full_administrator_and_nobody_else()
    {
        var author = Guid.CreateVersion7();
        var stranger = Guid.CreateVersion7();
        var comment = Comment(author);

        DocumentCommentRules.MayDelete(comment, author, isFullAdmin: false).ShouldBeTrue();
        DocumentCommentRules.MayDelete(comment, stranger, isFullAdmin: true).ShouldBeTrue();
        // Rights over the document a remark sits on are not moderation of the remark: the
        // caller who uploaded a document holds every right over it, and that must not turn
        // into the power to erase what other members said about it.
        DocumentCommentRules.MayDelete(comment, stranger, isFullAdmin: false).ShouldBeFalse();
    }

    [Fact]
    public void A_reply_answers_a_thread_starter_on_the_same_document_and_never_another_reply()
    {
        var documentId = Guid.CreateVersion7();
        var starter = Comment(documentId: documentId);
        var reply = Comment(documentId: documentId, parentId: starter.Id);
        var elsewhere = Comment(documentId: Guid.CreateVersion7());

        DocumentCommentRules.MayReplyTo(starter, documentId).ShouldBeTrue();
        // Threads are one level deep, and a thread may not straddle two documents.
        DocumentCommentRules.MayReplyTo(reply, documentId).ShouldBeFalse();
        DocumentCommentRules.MayReplyTo(elsewhere, documentId).ShouldBeFalse();
    }
}
