// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Trips;

/// <summary>
/// Which trips a camp's surface log lists: the ones somebody at the surface is still answering
/// for, and the ones that came out so recently that "everybody is out" is still news.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the log is for.</b> A camp runs several parties on the same day, and the person at the
/// surface has one question — who is still underground — that until now took opening every trip
/// in turn. The log is that question answered once, for the whole camp. So what belongs on it is
/// decided by the watch and by nothing else about the trip: its dates, its lifecycle state and its
/// plan say what was intended, and the watch says what somebody is actually following.
/// </para>
/// <para>
/// <b>A watch that was closed stays for a while, and then leaves.</b> The moment a watch is closed
/// is the moment the row has its best thing to say, and a row that vanished at that instant would
/// leave the coordinator wondering whether the party came out or the screen lost it. But a camp
/// lasts weeks, and a log that kept every finished trip would bury the two parties still inside
/// under thirty that came out days ago. Hence a window, counted from the closing.
/// </para>
/// <para>
/// <b>The window is this log's own.</b> It is not the grace a published page is given after its
/// watch closes, although the two look alike: that one governs what people without an account may
/// read, and an installation that shortens it to nothing for privacy must not thereby empty the
/// coordinator's screen of the parties that have just come out.
/// </para>
/// <para>
/// <b>It watches no clock on the party's behalf.</b> Nothing here reads the hour a party planned
/// to be out by, and nothing here says a party is late: that is the callout's question, asked by
/// the callout's own rules. A trip is on the log because its watch is running or has just
/// stopped, whatever its plan said.
/// </para>
/// <para>
/// Pure, and asked on every read rather than stored: a verdict written down when a watch closed
/// would go on listing the trip after the window had passed.
/// </para>
/// </remarks>
public static class TripSurfaceLog
{
    /// <summary>
    /// Whether a trip's watch belongs on the surface log at this instant.
    /// </summary>
    /// <param name="state">
    /// The watch. <see cref="TripTrackingState.Armed"/> is always listed, however long ago it was
    /// armed — a watch nobody closed is exactly the row a coordinator must not lose.
    /// <see cref="TripTrackingState.Closed"/> is listed while <paramref name="recentlyClosed"/>
    /// has not yet passed since it closed, and only when it was once armed.
    /// <see cref="TripTrackingState.Off"/> never is: nobody was ever followed.
    /// </param>
    /// <param name="armedAt">
    /// When the watch was last armed, or nothing when it never was. A closed watch that was never
    /// armed is not a party that has just come out: it is how a recording brought in from a
    /// device afterwards is filed — closed at the moment of the import, about a day that may be
    /// long past, with every position in it reading as somebody still inside because a device
    /// records where people were and never that they left. Nobody at the surface followed that
    /// party, so it is off the log for the reason a watch that is off is. A running watch is not
    /// asked the question: it is listed whatever its record says about how it came to run.
    /// </param>
    /// <param name="closedAt">
    /// When the watch closed. A watch recorded as closed with no instant against it is read as
    /// closed long ago, never as closed just now: listing it would keep a row on the log forever
    /// on the strength of a missing value.
    /// </param>
    /// <param name="recentlyClosed">
    /// How long a closed watch stays. Zero — or less, which no installation can configure — lists
    /// running watches only.
    /// </param>
    /// <remarks>
    /// The far edge is exclusive: a watch closed exactly <paramref name="recentlyClosed"/> ago has
    /// left. Written as a difference of two instants rather than as an instant plus the window, so
    /// that a very long window is a long window and not an overflow.
    /// </remarks>
    public static bool Lists(
        DateTimeOffset now,
        TripTrackingState state,
        DateTimeOffset? armedAt,
        DateTimeOffset? closedAt,
        TimeSpan recentlyClosed) => state switch
        {
            TripTrackingState.Armed => true,
            TripTrackingState.Closed =>
                armedAt is not null
                && recentlyClosed > TimeSpan.Zero
                && closedAt is { } closed
                && now - closed < recentlyClosed,
            _ => false,
        };
}
