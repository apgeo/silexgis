// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Notifications;

/// <summary>What asking for one dead delivery to be sent again did.</summary>
/// <remarks>
/// Four of these are refusals and two are acts. They are one vocabulary rather than an exception
/// per refusal because the caller has to tell an operator what happened either way: "it is on its
/// way again" and "the person it was for has since asked not to be told" are both answers, and
/// only one of them is good news.
/// </remarks>
public enum NotificationRetryOutcome
{
    /// <summary>No such delivery.</summary>
    NotFound,

    /// <summary>
    /// The delivery has not given up. Only a dead one may be put back: anything else is either
    /// already on its way or already gone, and forcing it would be a second copy of a message the
    /// recipient may already have.
    /// </summary>
    NotDead,

    /// <summary>
    /// Nothing here can carry this message: either no wording is registered under its key, or the
    /// wording that is registered was written for a transport this installation does not send on.
    /// Both are the reasons routing refused it in the first place, and both would refuse it again
    /// on the same line, so the refusal is the more useful answer — something has to change in the
    /// installation before the message can exist at all.
    /// </summary>
    TemplateUnknown,

    /// <summary>
    /// There is no longer anywhere to send it. The account went, or lost the address the channel
    /// needs; the delivery stays dead because no number of attempts can fix that.
    /// </summary>
    Unreachable,

    /// <summary>
    /// The recipient has since said they do not want this. The delivery is dropped rather than
    /// sent — the notification itself stays in their inbox, which is where somebody who switched a
    /// channel off reads it.
    /// </summary>
    Suppressed,

    /// <summary>Put back in the queue, due at the instant the result carries.</summary>
    Queued,
}
