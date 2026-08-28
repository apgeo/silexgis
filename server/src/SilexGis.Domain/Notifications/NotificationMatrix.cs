// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Notifications;

/// <summary>
/// What a person has actually chosen for one category on one channel, once every rule that can
/// override a stored row has been applied.
/// </summary>
/// <remarks>
/// <para>
/// One home for the whole question, because it is asked from four places — the settings page
/// reading the matrix back, the settings page writing it, routing deciding whether a delivery row
/// exists, and the send path asking the same question again on the way out — and four copies of it
/// would drift. Pure, so the table of rules can be read and tested without a database.
/// </para>
/// <para>
/// The rules, in the order they apply and with the reason each exists:
/// </para>
/// <list type="number">
/// <item>
/// A channel outside what is usable is <see cref="NotificationChannelChoice.Off"/>, whatever is
/// stored. Usable is the category's shipped ceiling intersected with what this installation
/// actually has. A stored row for a channel that has since left either set is therefore inert
/// rather than an error — the row is simply worth nothing, which is what lets a ceiling be
/// narrowed without a data migration.
/// </item>
/// <item>
/// Otherwise the stored choice, or the documented default when nothing is stored. <b>Nothing
/// stored is the ordinary case, not "everything off"</b>: most accounts never open the settings
/// page.
/// </item>
/// <item>
/// A category nobody may switch off is on wherever it can reach for free, so a stored
/// <see cref="NotificationChannelChoice.Off"/> for one is ignored rather than obeyed — <b>on a
/// channel that charges per message it is obeyed</b>, and such a channel starts off. The rule
/// exists so nobody can silence a warning about their own account, and a channel the reader
/// already has costs them nothing to leave on; a channel billed to the operator is different in
/// kind. Forcing one on would manufacture two agreements nobody gave: the reader's, whose number
/// was confirmed to sign in with and never offered as a contact address, and the operator's, who
/// would be paying per message for every account that ever set up a second factor. So a safety
/// category may reach a phone, and does so when somebody asks for it.
/// </item>
/// <item>
/// A category that refuses to be held back, or a channel that cannot hold anything back, reads
/// <see cref="NotificationChannelChoice.Daily"/> as
/// <see cref="NotificationChannelChoice.Immediate"/>.
/// </item>
/// </list>
/// </remarks>
public static class NotificationMatrix
{
    /// <summary>
    /// The channels a category can really use here: its shipped ceiling, narrowed to what this
    /// installation has, narrowed again to the channels this installation has agreed to pay for.
    /// <b>The one place any narrowing is intersected in</b> — which is why the ceiling is a set
    /// rather than a flag per channel.
    /// </summary>
    /// <param name="paidChannelsAllowed">
    /// The channels that cost money and that this installation has switched on. Defaulted to none
    /// on purpose: a caller that has not asked the administrator's answer gets the answer that
    /// spends nothing, so forgetting to pass it makes a paid channel unreachable rather than
    /// silently billable.
    /// </param>
    public static NotificationChannelKind Usable(
        NotificationCategory category,
        NotificationChannelKind installed,
        NotificationChannelKind paidChannelsAllowed = NotificationChannelKind.None) =>
        NotificationCategories.Ceiling(category)
        & installed
        & ~(NotificationChannelKinds.Paid & ~paidChannelsAllowed);

    /// <summary>
    /// What one cell of the matrix is worth. <paramref name="stored"/> is the row the user has
    /// saved, or <see langword="null"/> when they have saved none.
    /// </summary>
    public static NotificationChannelChoice Resolve(
        NotificationCategory category,
        NotificationChannelKind channel,
        NotificationChannelChoice? stored,
        NotificationChannelKind installed,
        NotificationChannelKind paidChannelsAllowed = NotificationChannelKind.None)
    {
        if (!NotificationChannelKinds.IsSingle(channel) ||
            (Usable(category, installed, paidChannelsAllowed) & channel) != channel)
        {
            return NotificationChannelChoice.Off;
        }

        var choice = stored ?? NotificationCategories.Default(category, channel);

        if (choice is NotificationChannelChoice.Off &&
            !NotificationCategories.IsUserConfigurable(category) &&
            !Charges(channel))
        {
            choice = NotificationChannelChoice.Immediate;
        }

        if (choice is NotificationChannelChoice.Daily &&
            (NotificationCategories.IsAlwaysImmediate(category) ||
             !NotificationChannelKinds.CanDefer(channel)))
        {
            choice = NotificationChannelChoice.Immediate;
        }

        return choice;
    }

    /// <summary>
    /// Whether a choice may be stored for this cell at all — what a write has to refuse rather
    /// than quietly rewrite, so somebody who asked for something impossible is told so.
    /// </summary>
    public static bool CanChoose(
        NotificationCategory category,
        NotificationChannelKind channel,
        NotificationChannelChoice choice,
        NotificationChannelKind installed,
        NotificationChannelKind paidChannelsAllowed = NotificationChannelKind.None)
    {
        if (!NotificationChannelKinds.IsSingle(channel) ||
            (Usable(category, installed, paidChannelsAllowed) & channel) != channel)
        {
            return false;
        }

        if (choice is NotificationChannelChoice.Off &&
            !NotificationCategories.IsUserConfigurable(category) &&
            !Charges(channel))
        {
            return false;
        }

        return choice is not NotificationChannelChoice.Daily ||
            (NotificationChannelKinds.CanDefer(channel) &&
             !NotificationCategories.IsAlwaysImmediate(category));
    }

    /// <summary>
    /// Whether this channel bills the installation for every message it carries. The one thing
    /// that stops "nobody may switch this category off" from also meaning "and somebody else pays
    /// for it wherever it can reach".
    /// </summary>
    private static bool Charges(NotificationChannelKind channel) =>
        (NotificationChannelKinds.Paid & channel) == channel;

    /// <summary>
    /// Whether a category reaches its reader nowhere at all. A legitimate state — somebody may
    /// choose to hear nothing about something — but never one to arrive at by accident, so the
    /// settings page has to be able to say it plainly.
    /// </summary>
    public static bool ReachesNobody(
        NotificationCategory category,
        Func<NotificationChannelKind, NotificationChannelChoice> cell,
        NotificationChannelKind installed,
        NotificationChannelKind paidChannelsAllowed = NotificationChannelKind.None) =>
        NotificationChannelKinds
            .Split(Usable(category, installed, paidChannelsAllowed))
            .All(channel => cell(channel) is NotificationChannelChoice.Off);
}
