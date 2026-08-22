// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * How the header's unread count learns that it has changed.
 *
 * The installation chooses it, not this client: the server answers with the transport it has been
 * configured for, and the header follows. One value is implemented — `poll`, the count asked for
 * again on a timer. The name of a pushed stream exists alongside it so that a live transport
 * arrives as a second arm here and at the one hook that reads this, rather than as a rewrite of
 * the header.
 */
export type InboxTransport = 'poll' | 'sse';

/**
 * What the header does before the server has told it anything, and what an installation that has
 * chosen nothing gets. Polling is the safe answer in both cases: it is the transport that works
 * with no further machinery, so a count is never left with nothing to move it.
 */
export const defaultInboxTransport: InboxTransport = 'poll';

/** Whether a transport the server named is one this client can act on. */
export function isInboxTransport(value: unknown): value is InboxTransport {
  return value === 'poll' || value === 'sse';
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
