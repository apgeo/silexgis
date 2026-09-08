// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';
import type {
  LibraryPhotographPage,
  LibraryPhotographSearchPage,
  LibrarySearchMatching,
} from '../../api/hooks.ts';

/**
 * What the grid is drawing: a page of the library, or a page of what the library made of somebody's
 * words.
 *
 * Two shapes rather than one with a flag, because they answer different questions and are allowed
 * to say different things about themselves. A listing can state how many the library holds; a
 * search cannot state how many match, because neither of the products behind this counts that —
 * one ranks everything it holds and so has no set of matches, and the other counts only the page it
 * has just sent.
 */
export type LibraryPage = LibraryPhotographPage | LibraryPhotographSearchPage;

/** Whether this answer came from a search, which is the only thing that carries how it matched. */
function searched(page: LibraryPage): page is LibraryPhotographSearchPage {
  return 'matching' in page;
}

/**
 * What a page of a neighbouring library is currently able to say.
 *
 * <p>
 * Several answers rather than one absence, because collapsing them is the characteristic failure
 * of this integration: <b>a grid still filling</b>, <b>a library that holds nothing matching</b>,
 * <b>a library that did not answer</b>, <b>an account that may not look</b> and <b>a page past the
 * end of the listing</b> all draw an empty page, and only the first of them means "wait". Somebody
 * has spent an afternoon on a library that was working every time these have been rendered as one.
 * </p>
 * <p>
 * The last of them is the one arrived at from the other direction, and it is not hypothetical: one
 * of the two products can only say "there may be more" — a page that came back full is all it
 * knows — so a library holding an exact multiple of the page size offers one page too many every
 * time. Rendered as "this library holds nothing matching", that tells a reader with thousands of
 * photographs in front of them that their library is empty.
 * </p>
 */
export type BrowseState =
  | 'loading'
  | 'refused'
  | 'searchTooLong'
  | 'silent'
  | 'empty'
  | 'endOfList'
  | 'photographs';

/**
 * The refusal this application answers with when it will not put a search to a library at all.
 *
 * Read from the refusal's own code rather than matched out of its sentence, which is prose written
 * for a person and may be reworded or translated without anything here noticing.
 */
const SearchTooLong = 'photo_library.search_too_long';

export interface BrowseStateInput {
  /** Nothing has arrived yet and nothing failed. */
  isPending: boolean;
  /** What the request failed with, or null.  */
  error: unknown;
  /** The page in hand, which may be the previous one while the next arrives. */
  page: LibraryPage | undefined;
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
    if (error instanceof ApiError && error.code === SearchTooLong) {
      // The library was never asked. A search that outgrew what this installation will put in a
      // request to a neighbour arrives here from an address somebody was given rather than from
      // the box, and a screen reporting "the library did not answer" for a question nobody put to
      // it sends a reader to look at a container that is fine.
      return 'searchTooLong';
    }

    return error instanceof ApiError && (error.status === 403 || error.status === 401)
      ? 'refused'
      : 'silent';
  }

  if (isPending || !page) {
    return 'loading';
  }

  if (page.items.length > 0) {
    return 'photographs';
  }

  // Nothing on a page after the first is the end of the listing, not an empty library: a library
  // that held nothing matching would have held nothing on the first page either. Both ways of
  // getting here are ordinary — a library whose size is an exact multiple of the page size always
  // offers one page too many, and a listing can also lose rows on the other side of the socket
  // between one page being drawn and the next being asked for.
  return page.page > 1 ? 'endOfList' : 'empty';
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
  /**
   * Whether the page in hand is the answer to the page being asked for.
   *
   * False while a page is being turned, because the answer still on screen is the previous one —
   * kept there on purpose, so the grid does not empty and refill.
   */
  current: boolean;
}

/**
 * @param asked The page the screen is currently asking for, which is not always the page in hand.
 *   Required rather than inferred, because inferring it is the defect: while a page is being
 *   turned the answer on screen is the previous one, and controls built from what <em>it</em> said
 *   describe a page the reader has already left. Two steps in quick succession then land one past
 *   the end of the listing on a next control the previous page enabled.
 */
export function pagingOf(
  page: LibraryPage | undefined,
  asked: number,
): BrowsePaging {
  if (!page) {
    return { hasPrevious: false, hasNext: false, total: null, shown: 0, current: false };
  }

  const current = page.page === asked;

  return {
    // Neither control is offered until the answer catches up with the question. What is on screen
    // meanwhile is the previous page, and stepping from a page that is not the one being drawn is
    // how a reader arrives somewhere neither of them describes.
    hasPrevious: current && page.page > 1,
    hasNext: current && page.hasMore,
    // A search carries no total and is not missing one: nothing counted what a sentence matched,
    // so there is no number to be unknown. Both arrive here as null and the line below the grid
    // says which of the two silences it is.
    total: searched(page) ? null : page.total,
    shown: page.items.length,
    current,
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

/**
 * The one line above the grid that says how much is being shown, and what that number is a count
 * of.
 *
 * <p>
 * Here rather than in the middle of markup because it is the whole honesty of this screen in four
 * branches, and because the branches are the kind that only ever get checked by rendering and
 * looking. Two of them are the listing's — the library states how many it holds, or it publishes
 * no way to ask — and two are the search's, where <b>no product states how many match</b>: one
 * orders everything it holds by closeness to the words and so has no set of matches to count, and
 * the other counts only the page it has just sent. So a search says how many came back and whether
 * there are more, and never a total, because a total there would be a number this application
 * invented and a reader cannot tell an invented number from a counted one.
 * </p>
 * <p>
 * Null when there is nothing on the page: what an empty grid is doing there is said inside it, and
 * "showing 0" above it would be a second, worse way of saying the same thing.
 * </p>
 */
export interface CountLine {
  key: string;
  values: Record<string, number>;
}

export function countLine(page: LibraryPage): CountLine | null {
  const shown = page.items.length;
  if (shown === 0) {
    return null;
  }

  if (searched(page)) {
    return {
      key: page.hasMore ? 'libraryPhotos.search.showingMore' : 'libraryPhotos.search.showing',
      values: { count: shown },
    };
  }

  return page.total === null
    ? { key: 'libraryPhotos.browse.showingUnknownTotal', values: { count: shown } }
    : { key: 'libraryPhotos.browse.showingOf', values: { shown, total: page.total } };
}

/**
 * What to invite somebody to type into the search box of a given library, and what to tell them
 * about the answer they will get.
 *
 * <p>
 * The two products answer a different question, and this is the one place that decides how each is
 * described. A box reading "describe the picture" over a library that matches words against titles
 * and captions is a promise the far side cannot keep: somebody types what they remember seeing,
 * nothing comes back, and what they conclude is that the library is empty rather than that they
 * asked the wrong kind of question.
 * </p>
 * <p>
 * Driven by what the server published about the product rather than by naming the products here,
 * so a third one added on the server arrives with wording rather than with a default that happens
 * to be wrong for it.
 * </p>
 */
export interface SearchWording {
  placeholder: string;
  explains: string;
}

export function searchWording(matching: LibrarySearchMatching): SearchWording {
  return matching === 'meaning'
    ? {
        placeholder: 'libraryPhotos.search.placeholderMeaning',
        explains: 'libraryPhotos.search.byMeaning',
      }
    : {
        placeholder: 'libraryPhotos.search.placeholderText',
        explains: 'libraryPhotos.search.textOnly',
      };
}
