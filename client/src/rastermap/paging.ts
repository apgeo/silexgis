// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResLink } from '../api/hooks.ts';

/** One page of the links-for-target answer, as the endpoint shapes it. */
export interface LinkPage {
  items: ResLink[];
  totalItems: number;
  pageSize: number;
}

/**
 * How many links one request asks for — the same bound the links panel and the station
 * pictures use for a single page.
 */
export const RASTER_LINKS_PAGE_SIZE = 200;

/**
 * Every link incident to a target, gathered across however many pages it takes.
 *
 * The station-pictures read stops at one page and accepts the truncation; this one must
 * not. A well-pinned cave holds one link per (station, map) pair, so hundreds of links on
 * one model is the feature working as designed — and a fold fed one page of them would
 * silently drop the stations that happened to sort last, drawing a map that looks finished
 * and is not.
 *
 * The page count is decided from the first answer and not revised: a link created while
 * the pages are being walked is the next refetch's business. A later page arriving empty
 * ends the walk early — the set shrank underneath us, and looping on emptiness would ask
 * forever for pages that no longer exist.
 */
export async function allLinksForTarget(
  fetchPage: (page: number) => Promise<LinkPage>,
): Promise<ResLink[]> {
  const first = await fetchPage(1);
  const all = [...first.items];

  // A server bug answering a non-positive page size leaves no way to know how many pages
  // exist; one page is then the honest answer rather than the start of an endless walk.
  if (first.pageSize <= 0) {
    return all;
  }
  const totalPages = Math.ceil(first.totalItems / first.pageSize);

  for (let page = 2; page <= totalPages; page++) {
    const next = await fetchPage(page);
    if (next.items.length === 0) {
      break;
    }
    all.push(...next.items);
  }

  return all;
}
