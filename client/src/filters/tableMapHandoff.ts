// SPDX-License-Identifier: AGPL-3.0-or-later
import { FILTER_PARAM, encodeFilter } from './url.ts';
import type { FilterDocument } from './types.ts';
import { worldBbox } from '../geo/coords.ts';

/**
 * Moving one question between a list and the map.
 *
 * A list and the map that show "the same rows" have to be asked the same thing, so a hand-off
 * carries the filter document itself rather than a description of it: the map runs the document
 * the list ran, and the list runs the document the map ran, each through the same server seam.
 *
 * The other direction adds a window. "What is in view" is decided by the server from the box the
 * request names — by the point each reader is shown for a protected cave, never by its stored
 * one — and never by the browser from the points it happens to have drawn: the browser's points
 * are clustered, capped and snapped, and a list derived from them would be a different list.
 */

/** The address parameter carrying the map's window on a list. */
export const WINDOW_PARAM = 'bbox';

/** The map's address for a list's filter: the list's document, or the plain map when it has none. */
export function mapAddressForList(token: string | undefined): string {
  if (token === undefined || token === '') {
    return '/map';
  }
  const params = new URLSearchParams();
  params.set(FILTER_PARAM, token);
  return `/map?${params.toString()}`;
}

/**
 * The cave registry's address for what the map is showing.
 *
 * `document` is the map's filter as a document — the one the map runs, or the saved filter's
 * document when the map runs a saved one — and `savedFilterId` names that saved filter so the
 * registry can show it as the one applied. `extent` is the map's ground box in degrees.
 */
export function registryAddressForMapView(
  extent: readonly [number, number, number, number],
  document: FilterDocument | null,
  savedFilterId?: string,
): string {
  const params = new URLSearchParams();
  if (document !== null) {
    params.set(FILTER_PARAM, encodeFilter(document));
    if (savedFilterId !== undefined) {
      params.set('saved', savedFilterId);
    }
  }
  params.set(WINDOW_PARAM, worldBbox(extent));
  return `/caves?${params.toString()}`;
}

/**
 * A window read back out of an address, or undefined when there is none or it is not a box.
 *
 * Only a well-formed box is passed on: four finite numbers inside the globe, west below east and
 * south below north. Anything else is dropped with the page saying so, rather than sent to be
 * refused or — worse — read as no window at all and answered with the whole registry.
 */
export function readWindow(raw: string | null): { bbox?: string; unreadable: boolean } {
  if (raw === null || raw === '') {
    return { unreadable: false };
  }
  const parts = raw.split(',').map((part) => Number(part));
  const [west, south, east, north] = parts;
  const valid = parts.length === 4
    && parts.every((value) => Number.isFinite(value))
    && west !== undefined && south !== undefined && east !== undefined && north !== undefined
    && west >= -180 && east <= 180 && south >= -90 && north <= 90
    && west < east && south < north;
  return valid ? { bbox: raw, unreadable: false } : { unreadable: true };
}
