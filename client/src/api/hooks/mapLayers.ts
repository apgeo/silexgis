// SPDX-License-Identifier: AGPL-3.0-or-later
import { useQuery } from '@tanstack/react-query';
import { api } from '../client.ts';
import { unwrap } from './shared.ts';
import { queryKeys } from './queryKeys.ts';
import type { GeofileInfo, RasterMapInfo } from './geodata.ts';

/** The largest page the server hands out; the walk below asks for it to keep the round trips few. */
const LAYER_PAGE_SIZE = 500;

/**
 * A ceiling on how many pages one reading walks, so a catalogue that keeps growing while it is read
 * cannot hold the map waiting for ever. Far above any collection a layer panel can usefully list.
 */
const MAX_PAGES = 20;

/** One page of a paged list, as every paged route answers. */
interface Page<T> {
  items: T[];
  pageSize: number;
  totalItems: number;
}

/**
 * Every row of a paged list, page after page until the total is reached. The map offers each
 * drawable file as a layer, and a list cut at one page lost the oldest ones from the layer list
 * for good once an installation held more than fitted on it. A page that fails fails the whole
 * reading, rather than answering with a short list that looks complete.
 */
export async function readAllPages<T>(read: (page: number) => Promise<Page<T>>): Promise<T[]> {
  const all: T[] = [];
  for (let page = 1; ; page++) {
    const answer = await read(page);
    all.push(...answer.items);
    // An empty or short page ends the walk even when the total says otherwise: rows deleted
    // while the pages were being turned would otherwise keep it asking for pages past the end.
    if (
      answer.items.length === 0 ||
      answer.items.length < answer.pageSize ||
      all.length >= answer.totalItems ||
      page >= MAX_PAGES
    ) {
      return all;
    }
  }
}

/**
 * The imported vector files this caller may see — every one, not a first page — which the map,
 * the 3D view and the flyby panels offer as layers. The server keeps only the imported ones
 * before it counts, so files still importing or failed never take a place on a page.
 */
export function useGeofileLayers() {
  return useQuery({
    queryKey: queryKeys.geofileLayers,
    queryFn: () =>
      readAllPages<GeofileInfo>((page) =>
        unwrap(api.GET('/api/v1/geofiles', { params: { query: { ready: true, page, pageSize: LAYER_PAGE_SIZE } } })),
      ),
  });
}

/**
 * The georeferenced maps and map sheets ready to draw that this caller may see — every one. Read
 * again every eight minutes, because each carries a signed address for its picture that lives ten.
 */
export function useRasterMapLayers() {
  return useQuery({
    queryKey: queryKeys.rasterMapLayers,
    queryFn: () =>
      readAllPages<RasterMapInfo>((page) =>
        unwrap(
          api.GET('/api/v1/georeferenced-maps', { params: { query: { ready: true, page, pageSize: LAYER_PAGE_SIZE } } }),
        ),
      ),
    refetchInterval: 8 * 60_000,
  });
}
