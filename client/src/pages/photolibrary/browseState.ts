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
  | 'credentialRefused'
  | 'notUnderstood'
  | 'searchTooLong'
  | 'pageTooDeep'
  | 'tripNotFound'
  | 'tripWindowUnusable'
  | 'silent'
  | 'empty'
  | 'endOfList'
  | 'photographs';

/**
 * The refusals and failures this screen tells apart, by the stable code each carries.
 *
 * <p>
 * Read from the code rather than matched out of the sentence beside it, which is prose written for
 * a person and may be reworded or translated without anything here noticing — and never from the
 * status, which is the same for most of these: a library that refused this installation's
 * credential and a library that is stopped both reach the browser as a 503.
 * </p>
 * <p>
 * That last pair is the whole reason this list is longer than one entry. "The library did not
 * answer; it may be stopped, or still starting up" is the right sentence for exactly one of these
 * and sends an administrator to restart a healthy container for the other, whose fix is to issue a
 * new credential. The far side answering something this build cannot read is a third thing again —
 * a contract that changed, or a defect on this side — and reading as an outage it would be looked
 * for in the wrong place entirely.
 * </p>
 */
const Codes = {
  /** This application would not put the words to any library: they were longer than it will send. */
  searchTooLong: 'photo_library.search_too_long',
  /** A page further into the library than this installation will ask for. */
  pageTooDeep: 'photo_library.page_too_deep',
  /**
   * The trip whose days were asked about is not there, or is not this account's to read. One code
   * for both, as everywhere a row is read by identifier: which of the two it is would itself be a
   * fact about the trip.
   */
  tripNotFound: 'photo_library.trip_not_found',
  /** The trip is there and its dates do not describe a stretch of time worth asking about. */
  tripWindowUnusable: 'photo_library.trip_window_unusable',
  /** The library refused the credential this installation is configured with. */
  credentialRefused: 'photo_library.unauthorized',
  /** The library answered, and not with anything this build could read. */
  notUnderstood: 'photo_library.rejected',
} as const;

export interface BrowseStateInput {
  /** Nothing has arrived yet and nothing failed. */
  isPending: boolean;
  /** What the request failed with, or null.  */
  error: unknown;
  /** The page in hand, which may be the previous one while the next arrives. */
  page: LibraryPage | undefined;
}

/**
 * Which of these this is.
 *
 * <p>
 * A failure wins over a page in hand, and that is deliberate: while paging keeps the previous page
 * on screen, a page that failed to turn must not read as the library answering. The screen then
 * shows the failure instead of the grid, because what is still in hand is the answer to a page the
 * reader has already left, and leaving it drawn under a heading that says nothing about which page
 * it is would be the same conflation from the other direction.
 * </p>
 * <p>
 * Every state below the first two is here because collapsing it into "the library did not answer"
 * sends somebody to the wrong place: to a container that is running for a credential that was
 * refused, to a library for a question this application declined to put to it, and to a fault
 * nobody has for a request this application built out of range.
 * </p>
 */
export function browseState({ isPending, error, page }: BrowseStateInput): BrowseState {
  if (error) {
    if (error instanceof ApiError && (error.status === 403 || error.status === 401)) {
      // A right this account does not have, which is nothing to do with the library.
      return 'refused';
    }

    if (error instanceof ApiError) {
      switch (error.code) {
        // The library was never asked. A search that outgrew what this installation will put in a
        // request to a neighbour arrives here from an address somebody was given rather than from
        // the box, and a screen reporting "the library did not answer" for a question nobody put
        // to it sends a reader to look at a container that is fine.
        case Codes.searchTooLong:
          return 'searchTooLong';
        // Nor was it asked for this one: a page beginning further into the library than this
        // installation will ask for. Also not reachable from the controls, which step one page at
        // a time.
        case Codes.pageTooDeep:
          return 'pageTooDeep';
        // Nor was it asked for these two. Both are about this installation's own record of a trip
        // and neither is about the library at all, so neither may read as the library being down:
        // one sends somebody to a trip they cannot see, the other to a trip whose dates need
        // correcting, and "the library did not answer" sends them to a container that is fine.
        case Codes.tripNotFound:
          return 'tripNotFound';
        case Codes.tripWindowUnusable:
          return 'tripWindowUnusable';
        // It answered, and refused the credential this whole installation reaches it with. The
        // fix is a new credential, and no amount of restarting produces one.
        case Codes.credentialRefused:
          return 'credentialRefused';
        // It answered something this build could not read, which is a contract that moved or a
        // defect on this side — and in neither case a library that is down.
        case Codes.notUnderstood:
          return 'notUnderstood';
        default:
          break;
      }
    }

    return 'silent';
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

/**
 * @param withinATripWindow Whether the listing was narrowed to the days one trip was out. It
 *   changes what the total is a count of and therefore which sentence may be written over it: the
 *   number a library states beside a narrowed listing counts what it holds <em>in that window</em>,
 *   and "of 412 photographs the library holds" written over it would tell a reader their club owns
 *   four hundred photographs when it owns forty thousand. Neither number describes the caller —
 *   every account reaches a library through one credential belonging to the installation, so there
 *   is one answer and everybody gets it.
 */
export function countLine(page: LibraryPage, withinATripWindow = false): CountLine | null {
  const shown = page.items.length;
  if (shown === 0) {
    return null;
  }

  if (searched(page)) {
    // A ranking and a set of matches are counted in the same numbers and mean different things, so
    // they are not described in the same sentence. "There are more" over an ordering of the whole
    // library reads as "more matched", and nothing matched: the ordering simply continues, and it
    // continues for as long as the library holds anything — so a reader can page for ever under a
    // sentence that sounds like a promise of relevance. Said as what it is instead.
    const ranked = page.matching === 'meaning';

    return {
      key: ranked
        ? page.hasMore
          ? 'libraryPhotos.search.showingRankedMore'
          : 'libraryPhotos.search.showingRanked'
        : page.hasMore
          ? 'libraryPhotos.search.showingMore'
          : 'libraryPhotos.search.showing',
      values: { count: shown },
    };
  }

  if (withinATripWindow) {
    return page.total === null
      ? { key: 'libraryPhotos.trip.showingUnknownTotal', values: { count: shown } }
      : { key: 'libraryPhotos.trip.showingOf', values: { shown, total: page.total } };
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
  /** What an answer with nothing in it means, which is not the same thing for the two products. */
  empty: string;
  /** What a search that failed means, which is also not the same thing for the two products. */
  silent: string;
}

export function searchWording(matching: LibrarySearchMatching): SearchWording {
  return matching === 'meaning'
    ? {
        placeholder: 'libraryPhotos.search.placeholderMeaning',
        explains: 'libraryPhotos.search.byMeaning',
        // An ordering of the whole library always has a front, so an answer with nothing at the
        // front of it is not a statement about the words at all: it means the library ordered
        // nothing, which is what a library whose picture recognition is switched off does. Nothing
        // here claims to know that it is switched off — that is readable only by an administrator
        // of that product — but sending a reader away to think of better words would be the one
        // reading the answer rules out.
        empty: 'libraryPhotos.search.noRanking',
        // And a search that failed against such a library has a second ordinary cause the browse
        // route does not have, so the sentence names both rather than the one that sends an
        // operator to restart a container that is running perfectly.
        silent: 'libraryPhotos.search.silentByMeaning',
      }
    : {
        placeholder: 'libraryPhotos.search.placeholderText',
        explains: 'libraryPhotos.search.textOnly',
        empty: 'libraryPhotos.search.empty',
        silent: 'libraryPhotos.browse.silent',
      };
}

/**
 * What the empty grid says it is, which is four different facts drawn the same way.
 *
 * A page past the end of a listing that holds plenty; a library that holds nothing at all; a
 * search that matched nothing, which is about the words rather than about the library; and an
 * ordering with nothing in it, which is about neither and means the library ranked nothing. The
 * last one is arrived at from the words alone in the fifth case below: a search whose text was
 * reduced to nothing this product could search for was never put to any library.
 */
export function emptyMessage(
  state: BrowseState,
  page: LibraryPage | undefined,
  wording: SearchWording,
  withinATripWindow = false,
): string {
  if (state === 'endOfList') {
    return 'libraryPhotos.browse.pastEnd';
  }

  if (page && searched(page)) {
    return page.searched === '' ? 'libraryPhotos.search.nothingLeft' : wording.empty;
  }

  // A fifth fact, and it is the one the trip panel is for: the library answered, it holds plenty,
  // and none of it was taken while this trip was out. "This library holds nothing matching" said
  // over that would be a claim about the library rather than about the days.
  return withinATripWindow ? 'libraryPhotos.trip.nothingTaken' : 'libraryPhotos.browse.empty';
}

/**
 * The one thing said above the grid when something went wrong, and how loudly.
 *
 * <p>
 * Here rather than as five conditions in the middle of markup, because which sentence a failure
 * gets is the whole of what this screen owes an operator and it is decided by two facts — the
 * state, and which of the two questions was being asked. A wrong sentence here does not look like
 * a defect: it looks like a working screen describing a different fault, and somebody spends an
 * afternoon on the container it named.
 * </p>
 */
export interface BrowseProblem {
  key: string;
  values?: Record<string, number>;
  kind: 'error' | 'warning' | 'info';
}

export function problemOf(
  state: BrowseState,
  searching: boolean,
  wording: SearchWording,
  maxSearchLength: number,
): BrowseProblem | null {
  switch (state) {
    case 'refused':
      return { key: 'libraryPhotos.refusals.notAllowed', kind: 'error' };
    case 'credentialRefused':
      return { key: 'libraryPhotos.health.credentialRefused', kind: 'error' };
    case 'notUnderstood':
      return { key: 'libraryPhotos.health.unreadable', kind: 'warning' };
    case 'searchTooLong':
      // Nothing is wrong with anything: the box is still there to clear, and the library was never
      // asked. The number is the server's own, so the sentence cannot outlive the limit it states.
      return {
        key: 'libraryPhotos.search.tooLong',
        values: { count: maxSearchLength },
        kind: 'info',
      };
    case 'pageTooDeep':
      return { key: 'libraryPhotos.browse.pageTooDeep', kind: 'info' };
    case 'tripNotFound':
      // Nothing is wrong with the library, and nothing was asked of it. A reader looking at a trip
      // they may not read is the ordinary way here, so this is stated rather than alarming.
      return { key: 'libraryPhotos.trip.notFound', kind: 'info' };
    case 'tripWindowUnusable':
      return { key: 'libraryPhotos.trip.datesUnusable', kind: 'warning' };
    case 'silent':
      // A failed search of a library that ranks by meaning has a cause a listing cannot have, and
      // the sentence for it names both possibilities instead of the one that sends an operator to
      // a healthy container.
      return { key: searching ? wording.silent : 'libraryPhotos.browse.silent', kind: 'warning' };
    default:
      return null;
  }
}
