// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import { unwrap } from './shared.ts';
import type { GeofileInfo, RasterMapInfo } from './geodata.ts';

/** The largest page the list routes answer. */
const PAGE_SIZE = 500;

/**
 * A ceiling on how many pages one reading walks, so a catalogue that keeps growing while it is read
 * cannot hold the map waiting for ever. Far above any collection a layer panel can usefully list.
 */
const MAX_PAGES = 20;

/**
 * Every row of a paged list, page by page. The map offers each imported file and each ready
 * picture as a layer, and the list routes cannot narrow by state, so a single page would quietly
 * leave out whatever sorted after its end — a picture that is ready and simply never offered.
 */
export async function readEveryPage<T>(
  readPage: (page: number) => Promise<{ items: T[]; totalItems: number }>,
): Promise<{ items: T[]; totalItems: number }> {
  const first = await readPage(1);
  const items = [...first.items];
  const pages = Math.min(Math.ceil(first.totalItems / PAGE_SIZE), MAX_PAGES);
  for (let page = 2; page <= pages; page++) {
    const next = await readPage(page);
    if (next.items.length === 0) {
      break;
    }
    items.push(...next.items);
  }
  return { items, totalItems: first.totalItems };
}

/** Every geofile this reader may see, for the map's layer list. */
export function useEveryGeofile() {
  return useQuery({
    // Under the geofile lists' own prefix, so whatever refreshes them after a write refreshes this.
    queryKey: ['geofiles', 'list', 'every'] as const,
    queryFn: (): Promise<{ items: GeofileInfo[]; totalItems: number }> =>
      readEveryPage((page) =>
        unwrap(api.GET('/api/v1/geofiles', { params: { query: { page, pageSize: PAGE_SIZE } } })),
      ),
  });
}

/** Every georeferenced map this reader may see, for the map's and the 3D view's layer lists. */
export function useEveryRasterMap() {
  return useQuery({
    queryKey: ['raster-maps', 'list', 'every'] as const,
    queryFn: (): Promise<{ items: RasterMapInfo[]; totalItems: number }> =>
      readEveryPage((page) =>
        unwrap(api.GET('/api/v1/georeferenced-maps', { params: { query: { page, pageSize: PAGE_SIZE } } })),
      ),
    // The signed picture addresses expire after ten minutes; read them again before they do.
    refetchInterval: 8 * 60_000,
  });
}
