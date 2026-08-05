// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Documents;

/// <summary>
/// What a comment on a document may say, who may change it, and what replying to one
/// means — as pure rules, so the answer is the same wherever it is asked. Endpoints load
/// state, ask here, and persist; nothing re-derives these answers locally.
/// <para>
/// Reading is deliberately absent from this class. A comment is readable exactly when its
/// document is, which is a question for the document access rules and must stay a single
/// question: a comment that answered it separately would become a way of learning that a
/// document exists without being allowed to see it.
/// </para>
/// </summary>
public static class DocumentCommentRules
{
    /// <summary>
    /// Cap on a comment body. Long enough for a real remark about a document, short enough
    /// that the table stays a discussion rather than a second place documents are stored.
    /// </summary>
    public const int MaxBodyLength = 4000;

    /// <summary>
    /// The body as it should be stored: surrounding whitespace removed, everything else
    /// left exactly as typed. Null when what remains is nothing at all.
    /// </summary>
    public static string? Normalize(string? body)
    {
        var trimmed = body?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Everything wrong with a proposed body — empty when it is acceptable. Measured after
    /// normalizing, so trailing whitespace can neither smuggle a body past the emptiness
    /// check nor push a legitimate one past the cap.
    /// </summary>
    public static IReadOnlyList<string> ValidateBody(string? body)
    {
        var normalized = Normalize(body);
        if (normalized is null)
        {
            return ["a comment says something"];
        }

        return normalized.Length > MaxBodyLength
            ? [$"a comment is at most {MaxBodyLength} characters"]
            : [];
    }

    /// <summary>
    /// Whether <paramref name="callerId"/> may rewrite this comment. Only its author may:
    /// an administrator can remove a remark that should not stand, but putting different
    /// words in somebody else's mouth is not a moderation power this system grants. An
    /// authorless comment — its account deleted — can no longer be edited by anyone.
    /// </summary>
    public static bool MayEdit(DocumentComment comment, Guid callerId)
    {
        ArgumentNullException.ThrowIfNull(comment);
        return comment.AuthorId is { } author && author != Guid.Empty && author == callerId;
    }

    /// <summary>
    /// Whether <paramref name="callerId"/> may remove this comment: its author, or a full
    /// administrator. Deliberately nobody else — in particular, holding rights over the
    /// document a remark sits on is not moderation of the remark. Whoever uploaded a
    /// document holds every right over it, so admitting document-deleters here would make
    /// every uploader the moderator of what is said about their own upload, which is
    /// exactly the power an author-or-administrator rule is meant to withhold.
    /// </summary>
    public static bool MayDelete(DocumentComment comment, Guid callerId, bool isFullAdmin)
    {
        ArgumentNullException.ThrowIfNull(comment);
        return isFullAdmin || MayEdit(comment, callerId);
    }

    /// <summary>
    /// Whether a new comment may reply to <paramref name="parent"/> on
    /// <paramref name="documentId"/>. Threads are one level deep, so a reply to a reply is
    /// refused rather than quietly flattened, and a parent belonging to another document is
    /// refused because a thread that spanned two documents would be readable by the readers
    /// of either.
    /// </summary>
    public static bool MayReplyTo(DocumentComment parent, Guid documentId)
    {
        ArgumentNullException.ThrowIfNull(parent);
        return parent.ParentId is null && parent.DocumentId == documentId;
    }
}
