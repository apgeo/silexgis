// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * How the header's unread count learns that it has changed.
 *
 * The server answers with the transport its build supports, and the header follows. One value is
 * implemented — `poll`, the count asked for again on a timer. A live transport would arrive as a
 * second member of this union and one arm at the hook that reads it, so every place that has to
 * decide what to do about it fails to compile until it does.
 */
export type InboxTransport = 'poll';

/**
 * What the header does before the server has told it anything, and what an installation that has
 * chosen nothing gets. Polling is the safe answer in both cases: it is the transport that works
 * with no further machinery, so a count is never left with nothing to move it.
 */
export const defaultInboxTransport: InboxTransport = 'poll';

/** Whether a transport the server named is one this client can act on. */
export function isInboxTransport(value: unknown): value is InboxTransport {
  return value === 'poll';
}

/**
 * How long the header waits before asking again while the transport is polling.
 *
 * A minute is the cost of one small request per signed-in tab per minute, and is the longest a
 * count may be stale for a person who is looking at the page. It is not the only thing keeping
 * the number honest: both ways of marking something read refresh it at once, and so does coming
 * back to the tab, so the interval only covers a notification arriving while somebody watches.
 */
export const inboxPollIntervalMs = 60_000;
