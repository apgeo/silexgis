// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The place a new address takes: after the last one there is.
 *
 * Counting the addresses is not the same thing once one has been deleted — with 1 and 2 left after
 * the first was removed, a count gives the new one 2 as well, and two addresses sharing a place
 * may swap order from one read to the next.
 */
export function nextSortOrder(addresses: readonly { sortOrder: number }[]): number {
  return addresses.reduce((highest, address) => Math.max(highest, address.sortOrder + 1), 0);
}
