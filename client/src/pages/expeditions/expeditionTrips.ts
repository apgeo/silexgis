// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * How many of a camp's trips its pages ask for at once.
 *
 * One number for every part of a camp's pages that lists its trips, so the parts share one
 * request and one answer rather than each asking for a slightly different page of the same list.
 */
export const EXPEDITION_TRIPS_PAGE_SIZE = 50;
