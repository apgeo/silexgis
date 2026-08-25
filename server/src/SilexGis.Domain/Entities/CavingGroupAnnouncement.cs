// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A notice somebody wrote to a whole caving group, recorded so it can be handed out.
/// </summary>
/// <remarks>
/// <para>
/// A row exists here <b>only for an announcement too large to hand out inside the request that
/// made it</b>. A small roster is written to directly and leaves nothing behind; a large one is
/// recorded once and expanded by a background pass, because one row per member written inside
/// somebody's HTTP call is a slow request and a long-held write transaction, and both get worse
/// exactly as the club gets bigger.
/// </para>
/// <para>
/// It carries what the message will say rather than a reference to it, because the wording, the
/// group's name and the sender's name are all frozen at the moment of sending. That is the same
/// rule every notification here follows: what a message says is what was true when it was sent,
/// and whether the reader may still be shown the group it names is asked again when they read it.
/// </para>
/// <para>
/// Which is also why it is deleted under the same retention window as the notifications it
/// produced, handed out or not. It holds a copy of the sender's words, and a copy that outlived
/// the installation's answer about how long what it tells people is kept would be a copy nobody
/// knew was there.
/// </para>
/// </remarks>
public class CavingGroupAnnouncement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CavingGroupId { get; set; }

    /// <summary>Who sent it. Never a recipient of their own announcement.</summary>
    public Guid SenderUserId { get; set; }

    /// <summary>The notice, in the sender's own words.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>What the group was called when this was sent.</summary>
    public string CavingGroupName { get; set; } = string.Empty;

    /// <summary>What the sender was called when this was sent.</summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>How many people it was meant for, counted when it was sent.</summary>
    public int RecipientCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the notifications for it were written, or null while it is still waiting.
    /// </summary>
    /// <remarks>
    /// The whole of what stops a roster being told twice. The queue this rides re-runs anything
    /// that was interrupted, from the beginning, so a pass that had already written half a club's
    /// notifications would write that half again. Stamping this in the same save as the
    /// notifications makes the expansion happen once or not at all, and a second pass over a
    /// stamped row does nothing.
    /// </remarks>
    public DateTimeOffset? ExpandedAt { get; set; }
}
