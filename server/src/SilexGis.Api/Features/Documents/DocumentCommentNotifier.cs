// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Api.Common;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Notifications;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Documents;

/// <summary>
/// Tells the two people a new remark concerns: whoever it answers, and whoever owns the thing
/// it was left on.
/// </summary>
/// <remarks>
/// <para>
/// <b>No message here carries a word of what was said.</b> A body is free text somebody typed,
/// and a message leaves the installation entirely — once it is in a mailbox it obeys none of the
/// rules that decided who could read it. So each message names the document, links to it, and
/// lets the reader open it and see for themselves. Declaring no placeholder the text could
/// arrive in is what makes that structural rather than a habit.
/// </para>
/// <para>
/// Who is told is implicit and short: the remark this one answers, and the document's owner.
/// Those two candidates are named here rather than collected from a list somebody maintains,
/// so there is nothing to subscribe to and nothing to keep in step. Anyone else who has spoken
/// on the document finds a new remark by opening it; whether they should instead be told is a
/// question this does not settle either way.
/// </para>
/// <para>
/// Being named as one of those two is not evidence of being able to read the document now. A
/// grant can be withdrawn, an object made private, a caving group left, all long after the
/// remark that named somebody was written — so each candidate's own right to read is decided
/// again here, one account at a time, against their own access as it stands. That decision runs
/// through the document's own read walk, the same one the comment routes pass through, rather
/// than the shorter check that serves rows with no second band: a document reachable only
/// through something its file is attached to is genuinely readable, and telling its owner
/// nothing about their own upload would be wrong in the quiet direction.
/// </para>
/// <para>
/// Everything queues before the caller's own <c>SaveChangesAsync</c>, so a message exists only
/// if the remark it reports actually committed.
/// </para>
/// </remarks>
internal static class DocumentCommentNotifier
{
    /// <summary>
    /// Queues what one new remark should tell people, given the document it was left on and the
    /// remark it answers, if any.
    /// </summary>
    /// <param name="parent">The remark being answered, or null when this one starts a thread.</param>
    public static async Task PostedAsync(
        SilexGisDbContext db,
        IAccessService access,
        UserContext user,
        Document document,
        DocumentComment? parent,
        CancellationToken ct)
    {
        // Nobody is told about their own remark. An author whose account is gone leaves a null
        // behind, and there is no one to tell.
        var answered = parent?.AuthorId is { } author && author != Guid.Empty && author != user.UserId
            ? author
            : (Guid?)null;

        // A document whose owner is gone leaves an empty id behind rather than a null one. The
        // owner is dropped when they are also the person being answered: one remark is one
        // message, and of the two things it could be called, being answered is the nearer.
        var owner = document.OwnerUserId != Guid.Empty
                    && document.OwnerUserId != user.UserId
                    && document.OwnerUserId != answered
            ? document.OwnerUserId
            : (Guid?)null;

        if (answered is null && owner is null)
        {
            return;
        }

        // The file the document currently serves, which the read walk needs to answer whether
        // somebody reaches the document through something it is attached to. Read once, and only
        // once there is somebody whose access is worth deciding.
        var content = await DocumentQueries.CurrentFileAsync(db, document.Id, ct);

        Dictionary<Guid, string>? labels = null;

        async Task TellAsync(Guid recipient, NotificationCategory category, string templateKey)
        {
            var theirs = await AccessContextResolver.ResolveAsync(db, recipient, ct);
            if (!await DocumentAccessRules.CanReadAsync(db, access, theirs, document, content?.File, ct))
            {
                return;
            }

            labels ??= await ProfileDirectory.ResolveLabelsAsync(db, user, [user.UserId], ct);

            NotificationQueue.Enqueue(
                db,
                recipient,
                category,
                templateKey,
                new Dictionary<string, string>
                {
                    ["actorName"] = labels.GetValueOrDefault(user.UserId) ?? string.Empty,
                    ["documentTitle"] = document.Title,
                    ["url"] = $"/documents/{document.Id}",
                },
                // The title and the link above are what the document looked like when this was
                // written. Whether the reader may still be shown either is a question for the
                // moment they read it, and the target reference is the only thing that question
                // can be asked against.
                //
                // It names the document rather than the remark, and that is the deliberate
                // choice: a remark can be deleted outright by the person who wrote it, taking
                // its answers with it, and a reference to one would then point at nothing. The
                // document is where the reader was going anyway — the conversation is on it —
                // so a deleted remark leaves a message that still opens the right page, while a
                // deleted document degrades to the wording for a subject the reader can no
                // longer see, which is the honest thing to say about it.
                NotificationTargetKind.Document,
                document.Id);
        }

        if (answered is { } replyTo)
        {
            await TellAsync(replyTo, NotificationCategory.CommentReply, MessageTemplateCatalog.NotifyCommentReply);
        }

        if (owner is { } documentOwner)
        {
            await TellAsync(documentOwner, NotificationCategory.CommentOnMine, MessageTemplateCatalog.NotifyCommentOnMine);
        }
    }
}
