// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';
import type { LibraryPhotographPage } from '../../api/hooks.ts';

/**
 * What a page of a neighbouring library is currently able to say.
 *
 * <p>
 * Four answers rather than one absence, because collapsing them is the characteristic failure of
 * this integration: <b>a grid still filling</b>, <b>a library that holds nothing matching</b>, <b>a
 * library that did not answer</b> and <b>an account that may not look</b> all draw an empty page,
 * and only the first of them means "wait". Somebody has spent an afternoon on a library that was
 * working every time these have been rendered as one.
 * </p>
 */
export type BrowseState =
  | 'loading'
  | 'refused'
  | 'searchUnsupported'
  | 'silent'
  | 'empty'
  | 'photographs';

/**
 * The refusal a library that does not match text answers words with.
 *
 * Read from the refusal's own code rather than matched out of its sentence, which is prose written
 * for a person and may be reworded or translated without anything here noticing.
 */
const TextSearchUnsupported = 'photo_library.text_search_unsupported';

export interface BrowseStateInput {
  /** Nothing has arrived yet and nothing failed. */
  isPending: boolean;
  /** What the request failed with, or null.  */
  error: unknown;
  /** The page in hand, which may be the previous one while the next arrives. */
  page: LibraryPhotographPage | undefined;
}

/**
 * Which of the four this is.
 *
 * <p>
 * A failure wins over a page in hand, and that is deliberate: while paging keeps the previous
 * page on screen, a page that failed to turn must not read as the library answering. What the
 * screen then shows is the words for the failure above whatever is still drawn, which is the
 * honest description of exactly that situation.
 * </p>
 * <p>
 * A refusal is told apart from a silence because the two send a reader somewhere completely
 * different: one is a right they do not have, and the other is a container somebody has to look
 * at. Read from the refusal's status rather than from its sentence, which is prose written for a
 * person and may be reworded without anything here noticing.
 * </p>
 * <p>
 * And both are told apart from a question this application declined to put to the library at all,
 * which is the one state where nothing is wrong with anything.
 * </p>
 */
export function browseState({ isPending, error, page }: BrowseStateInput): BrowseState {
  if (error) {
    if (error instanceof ApiError && error.code === TextSearchUnsupported) {
      // The library was never asked. Words carried in the address outlive the box they were typed
      // into — switching library keeps them — and a screen reporting "the library did not answer"
      // for a question nobody put to it sends a reader to look at a container that is fine.
      return 'searchUnsupported';
    }

    return error instanceof ApiError && (error.status === 403 || error.status === 401)
      ? 'refused'
      : 'silent';
  }

  if (isPending || !page) {
    return 'loading';
  }

  return page.items.length === 0 ? 'empty' : 'photographs';
}

/**
 * What the paging controls may offer.
 *
 * <p>
 * The whole of the difficulty is the total. One of the two products states how many it holds and
 * the other publishes no way to ask, so a control built on a total would work against one library
 * and silently offer nothing against the other. What is always knowable is whether there is a page
 * behind this one — the library says so directly — and that is what the next control follows.
 * </p>
 */
export interface BrowsePaging {
  hasPrevious: boolean;
  hasNext: boolean;
  /**
   * How many the library says it holds, or null when it does not say. Null is shown as an unknown
   * total rather than as a number this application worked out from the pages it has seen: a count
   * assembled by paging is wrong the moment somebody adds a picture, and a reader cannot tell it
   * from a fact.
   */
  total: number | null;
  /** How many are on this page. Always knowable, and never confused with the total. */
  shown: number;
}

export function pagingOf(page: LibraryPhotographPage | undefined): BrowsePaging {
  if (!page) {
    return { hasPrevious: false, hasNext: false, total: null, shown: 0 };
  }

  return {
    hasPrevious: page.page > 1,
    hasNext: page.hasMore,
    total: page.total,
    shown: page.items.length,
  };
}

/**
 * The page a step lands on, never below the first.
 *
 * A step past the end is refused here rather than by the control being disabled, so that the two
 * cannot disagree: a control enabled by a stale answer would otherwise ask the library for a page
 * it has already said is not there.
 */
export function stepPage(current: number, direction: -1 | 1, paging: BrowsePaging): number {
  if (direction === 1) {
    return paging.hasNext ? current + 1 : current;
  }

  return Math.max(1, current - 1);
}
