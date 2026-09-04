// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Trips;

/// <summary>
/// Tells the people a trip write has newly put on a trip that they are on it.
/// </summary>
/// <remarks>
/// Behind an interface rather than written inline, because the message carries the acting
/// person's display label and the rule about which label a given reader may be shown belongs
/// with the surfaces that render people, not with the code that writes trips. The write core
/// therefore states <em>that</em> somebody should be told and leaves <em>what they are told</em>
/// to whoever composed the request.
/// </remarks>
public interface ITripRosterAnnouncer
{
    /// <summary>
    /// Announces a roster addition. Called before the caller's save, so a message exists only if
    /// the write it describes actually commits; implementations queue rather than send.
    /// </summary>
    Task AnnounceAsync(TripLog trip, IReadOnlyList<Guid> newlyNamedUserIds, CancellationToken ct);
}

/// <summary>Whether a trip write tells the people it newly named.</summary>
/// <remarks>
/// The silent value exists for bulk loading, and it is an argument rather than a second write
/// path on purpose: a thousand trips recorded from a club's old spreadsheet must not send a
/// thousand messages, and a quiet copy of the creation code bolted on beside this one is how the
/// two drift apart until only one of them still applies a rule.
/// </remarks>
public enum TripWriteNotice
{
    /// <summary>Tell the newly named people, which is what a person editing a trip means.</summary>
    Announce = 0,

    /// <summary>Tell nobody, whatever the trip's state would otherwise allow.</summary>
    Silent = 1,
}
