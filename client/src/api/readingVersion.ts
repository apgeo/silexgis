// SPDX-License-Identifier: AGPL-3.0-or-later
import { replaceEqualDeep } from '@tanstack/react-query';

// The version each held reading was answered at, kept against the reading itself rather than
// against its address. The newest version filed for an address can be ahead of the copy a cache
// holds: a save files its own version the moment it answers, while the cache goes on holding the
// record from before the save until a re-read lands, and a read made outside the cache files a
// version without changing what the cache holds. A form filled from the cached copy and saved
// under that newer version would send the older values as if they had been read at it, and the
// server would take them over the change the version stands for.
const versionOfReading = new WeakMap<object, string>();

/** Keeps `response`'s version against `reading`, the values that answer carried. */
export function noteVersionOfReading(reading: unknown, response: Response): void {
  const etag = response.headers.get('ETag');
  if (etag && typeof reading === 'object' && reading !== null) {
    versionOfReading.set(reading, etag);
  }
}

/**
 * The version the values in `reading` were answered at, for a form filled from them to send as
 * its `If-Match` — or undefined where that reading carried none, in which case the transport sends
 * the newest version filed for the address.
 */
export function versionReadWith(reading: unknown): string | undefined {
  return typeof reading === 'object' && reading !== null ? versionOfReading.get(reading) : undefined;
}

/**
 * A query's structural sharing that keeps the version with the values the cache ends up holding.
 *
 * The cache does not hold the object a read returned: it keeps the old object where nothing
 * changed, and builds a new one where something did. Either way that object is what a screen
 * reads, so the version the read was answered at is moved onto it. Where the values are unchanged
 * the newer version speaks for them just as well.
 */
export function sharesKeepingVersion(previous: unknown, next: unknown): unknown {
  const shared = replaceEqualDeep(previous, next);
  const version = versionReadWith(next);
  if (version !== undefined && shared !== next && typeof shared === 'object' && shared !== null) {
    versionOfReading.set(shared, version);
  }
  return shared;
}
