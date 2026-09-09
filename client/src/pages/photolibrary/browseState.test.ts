// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import type { LibraryPhotographPage, LibraryPhotographSearchPage } from '../../api/hooks.ts';
import {
  browseState,
  countLine,
  emptyMessage,
  pagingOf,
  problemOf,
  searchWording,
  stepPage,
} from './browseState.ts';

/**
 * What a page of a neighbouring library is able to say, and what its paging may offer.
 *
 * Both are pure and both are here rather than inside the page, because both are decisions this
 * screen would otherwise make in the middle of markup, where the only way to check them is to
 * render and look. Every identifier and title below is invented.
 */

const page = (over: Partial<LibraryPhotographPage> = {}): LibraryPhotographPage => ({
  source: 'photoprism',
  libraryName: 'PhotoPrism',
  items: [
    {
      photographId: 'psinvented1',
      reference: 'aa11bb22cc33',
      title: 'An invented picture',
      takenAt: '2026-02-03T04:05:06Z',
      kind: null,
    },
  ],
  page: 1,
  pageSize: 60,
  total: null,
  hasMore: false,
  pageSizeCapped: false,
  picturesAvailable: true,
  pictureUrlTemplate: '/api/v1/photo-libraries/photoprism/thumbnails/{reference}?size={size}&token=x',
  readAt: '2026-02-03T04:05:06Z',
  ...over,
});

/** The other shape the same grid draws: one page of what a library made of somebody's words. */
const answer = (
  over: Partial<LibraryPhotographSearchPage> = {},
): LibraryPhotographSearchPage => {
  const { items, total: _total, ...rest } = page();
  void _total;

  return { ...rest, matching: 'text', searched: 'rope', items, ...over };
};

describe('what a page of a neighbouring library can say', () => {
  /**
   * The whole point of the state. Three of these draw an empty screen and mean completely
   * different things, and rendering them as one is why somebody spends an afternoon debugging a
   * library that was working.
   */
  it('tells a page still filling from one that holds nothing and one that did not answer', () => {
    expect(browseState({ isPending: true, error: null, page: undefined })).toBe('loading');
    expect(browseState({ isPending: false, error: null, page: page({ items: [] }) })).toBe('empty');
    expect(browseState({ isPending: false, error: new Error('no'), page: undefined })).toBe('silent');
    expect(browseState({ isPending: false, error: null, page: page() })).toBe('photographs');
  });

  /**
   * A right nobody has is not a library that is down. One sends a reader to whoever grants rights
   * here and the other sends somebody to look at a container, so they are told apart by the
   * refusal's status rather than by its sentence.
   */
  it('tells a refusal from a silence', () => {
    expect(
      browseState({ isPending: false, error: new ApiError(403, 'photo_library.forbidden'), page: undefined }),
    ).toBe('refused');
    expect(
      browseState({ isPending: false, error: new ApiError(401), page: undefined }),
    ).toBe('refused');
    expect(
      browseState({
        isPending: false,
        error: new ApiError(503, 'photo_library.unavailable'),
        page: undefined,
      }),
    ).toBe('silent');
  });

  /**
   * A search this application declined to put to the library is not the library failing. It
   * arrives from an address somebody was handed rather than from the box, which stops short of the
   * length — and a screen reporting "the library did not answer" for a question nobody asked sends
   * somebody to look at a container that is fine.
   */
  it('tells a question that was never asked from a library that did not answer', () => {
    expect(
      browseState({
        isPending: false,
        error: new ApiError(400, 'photo_library.search_too_long'),
        page: undefined,
      }),
    ).toBe('searchTooLong');

    // The control: another refusal with the same status is still a silence, so this is keyed off
    // the code and not off "a request that failed with 400".
    expect(
      browseState({
        isPending: false,
        error: new ApiError(400, 'photo_library.unavailable'),
        page: undefined,
      }),
    ).toBe('silent');
  });

  /**
   * The three failures that all arrive as the same status and mean completely different things.
   *
   * Both endpoints answer every failure of a neighbouring library with the same code, so what
   * separates them is the code each carries. A credential the far side refused rendered as "it may
   * be stopped, or still starting up" is the inverse of the failure this split exists to prevent:
   * an administrator restarts a container that is running perfectly, and the fix — issuing a new
   * credential — is the one thing the sentence did not mention.
   */
  it('tells a refused credential from an unreadable answer from a library that is down', () => {
    expect(
      browseState({
        isPending: false,
        error: new ApiError(503, 'photo_library.unauthorized'),
        page: undefined,
      }),
    ).toBe('credentialRefused');

    expect(
      browseState({
        isPending: false,
        error: new ApiError(503, 'photo_library.rejected'),
        page: undefined,
      }),
    ).toBe('notUnderstood');

    expect(
      browseState({
        isPending: false,
        error: new ApiError(503, 'photo_library.unavailable'),
        page: undefined,
      }),
    ).toBe('silent');
  });

  /**
   * A page this application declined to ask for is not a library that failed. Reachable only from
   * an address somebody typed, and reported as the neighbour being down it would send an operator
   * to look for a fault in a container that answered nothing because nothing was sent to it.
   */
  it('tells a page it would not ask for from a library that did not answer', () => {
    expect(
      browseState({
        isPending: false,
        error: new ApiError(400, 'photo_library.page_too_deep'),
        page: undefined,
      }),
    ).toBe('pageTooDeep');
  });

  /**
   * A page that failed to turn must not read as the library answering, even though the previous
   * page is still on screen — which is what turning a page while keeping the grid drawn produces.
   */
  it('reports a failure even while a previous page is still in hand', () => {
    expect(browseState({ isPending: false, error: new Error('no'), page: page() })).toBe('silent');
  });
});

/**
 * A page past the end of the listing is not an empty library.
 *
 * Reached in the ordinary course of things: one of the two products can only say "there may be
 * more", because all it knows is that the page it sent came back full — so a library holding an
 * exact multiple of the page size offers one page too many every time. Told that its library holds
 * nothing matching, a club with thousands of photographs goes looking for a fault nobody has.
 */
describe('a page past the end of the listing', () => {
  it('is told apart from a library that holds nothing matching', () => {
    expect(browseState({ isPending: false, error: null, page: page({ page: 2, items: [] }) }))
      .toBe('endOfList');

    // The first page with nothing on it is the other thing entirely: nothing here matches.
    expect(browseState({ isPending: false, error: null, page: page({ page: 1, items: [] }) }))
      .toBe('empty');
  });
});

describe('what the paging may offer', () => {
  /**
   * A next page follows what the library said, never a total: one of the two products states how
   * many it holds and the other publishes no way to ask, so a control built on a total would work
   * against one library and silently offer nothing against the other.
   */
  it('offers the next page from what the library said rather than from a count', () => {
    expect(pagingOf(page({ total: null, hasMore: true }), 1).hasNext).toBe(true);
    expect(pagingOf(page({ total: 412, hasMore: false }), 1).hasNext).toBe(false);
  });

  it('offers a previous page only after the first', () => {
    expect(pagingOf(page({ page: 1 }), 1).hasPrevious).toBe(false);
    expect(pagingOf(page({ page: 2 }), 2).hasPrevious).toBe(true);
  });

  /**
   * The count of what is on screen and the size of the library are separate numbers, and the
   * unknown one stays unknown. A page that put the first where the second belongs would tell a
   * reader the club has sixty photographs.
   */
  it('keeps what is shown apart from what the library holds', () => {
    const known = pagingOf(page({ total: 412 }), 1);
    expect(known.shown).toBe(1);
    expect(known.total).toBe(412);

    const unknown = pagingOf(page({ total: null }), 1);
    expect(unknown.shown).toBe(1);
    expect(unknown.total).toBeNull();
  });

  /**
   * While a page is being turned the answer on screen is the previous one, kept there on purpose so
   * the grid does not empty and refill. What that answer says about paging is about the page the
   * reader has already left — so a next control built from it lets a second step land one page past
   * the end of the listing, where the screen has nothing to draw and says the library holds nothing.
   */
  it('offers neither step while the answer in hand is not the answer to the question', () => {
    const turning = pagingOf(page({ page: 2, hasMore: true }), 3);

    expect(turning.current).toBe(false);
    expect(turning.hasNext).toBe(false);
    expect(turning.hasPrevious).toBe(false);

    // What is drawn is still described, because it is still on the screen.
    expect(turning.shown).toBe(1);

    // The control: the same answer, once it is the answer to the question being asked.
    const arrived = pagingOf(page({ page: 2, hasMore: true }), 2);
    expect(arrived.current).toBe(true);
    expect(arrived.hasNext).toBe(true);
    expect(arrived.hasPrevious).toBe(true);
  });

  it('says nothing at all before an answer has arrived', () => {
    expect(pagingOf(undefined, 1)).toEqual({
      hasPrevious: false,
      hasNext: false,
      total: null,
      shown: 0,
      current: false,
    });
  });

  /**
   * A step past the end is refused where the step is taken, not only by a disabled control: a
   * control left enabled by a stale answer would otherwise ask the library for a page it has
   * already said is not there.
   */
  it('refuses a step past either end', () => {
    const last = pagingOf(page({ page: 3, hasMore: false }), 3);
    expect(stepPage(3, 1, last)).toBe(3);
    expect(stepPage(3, -1, last)).toBe(2);

    const first = pagingOf(page({ page: 1, hasMore: true }), 1);
    expect(stepPage(1, -1, first)).toBe(1);
    expect(stepPage(1, 1, first)).toBe(2);
  });
});

/**
 * The line above the grid, which is the whole honesty of this screen in one sentence.
 *
 * The listing and the search may say different things about themselves and the difference is not
 * cosmetic: a listing can state how many the library holds, and a search cannot state how many
 * match, because neither product counts that. Writing "of 4 312" over a search would be a number
 * this application invented.
 */
describe('what the line above the grid says', () => {
  it('states the library size only where the library states it', () => {
    expect(countLine(page({ total: 412 }))).toEqual({
      key: 'libraryPhotos.browse.showingOf',
      values: { shown: 1, total: 412 },
    });

    expect(countLine(page({ total: null }))).toEqual({
      key: 'libraryPhotos.browse.showingUnknownTotal',
      values: { count: 1 },
    });
  });

  /**
   * A search says how many came back and whether there are more, and never a total. Both products
   * are like this for different reasons — one ranks its whole library and so has no set of matches
   * to count, the other counts only the page it has just sent — so there is no branch here where a
   * search acquires a number.
   */
  it('never puts a total over a search, whichever way the library matched', () => {
    expect(countLine(answer({ matching: 'text', hasMore: false }))).toEqual({
      key: 'libraryPhotos.search.showing',
      values: { count: 1 },
    });

    expect(countLine(answer({ matching: 'meaning', hasMore: true }))).toEqual({
      key: 'libraryPhotos.search.showingRankedMore',
      values: { count: 1 },
    });
  });

  /**
   * An ordering that continues is not a set of matches that continues, and the two must not be
   * described by one sentence. The library that ranks puts everything it holds in the ordering, so
   * "there are more" is true of every page until the reader has walked the whole library — read as
   * "more matched", it promises relevance the far side never claimed.
   */
  it('says a ranking continues rather than that more matched', () => {
    expect(countLine(answer({ matching: 'meaning', hasMore: false }))).toEqual({
      key: 'libraryPhotos.search.showingRanked',
      values: { count: 1 },
    });

    // The control: the product that does match text says the other thing, because for it there is
    // a set of matches and the library really is saying it holds more of them.
    expect(countLine(answer({ matching: 'text', hasMore: true }))).toEqual({
      key: 'libraryPhotos.search.showingMore',
      values: { count: 1 },
    });
  });

  /** Nothing to count is said inside the empty grid, and "showing 0" over it says it worse. */
  it('says nothing at all over a page with nothing on it', () => {
    expect(countLine(page({ items: [] }))).toBeNull();
    expect(countLine(answer({ items: [] }))).toBeNull();
  });

  /**
   * A search carries no total, and the paging must not report that as a total nobody stated: both
   * arrive as null and the line above the grid is what says which silence it is.
   */
  it('reports no total for a search rather than an unknown one', () => {
    const paging = pagingOf(answer({ hasMore: true }), 1);

    expect(paging.total).toBeNull();
    expect(paging.shown).toBe(1);
    expect(paging.hasNext).toBe(true);
  });
});

/**
 * What a person is invited to type.
 *
 * The two products answer a different question, and a box that invited a description of a
 * photograph over a library which can only look words up would be making a promise the far side
 * cannot keep: somebody types what they remember seeing, nothing comes back, and what they conclude
 * is that the library is empty.
 */
describe('how a search box describes itself', () => {
  it('asks for words of a library that matches words, and for a description of one that does not', () => {
    expect(searchWording('text')).toEqual({
      placeholder: 'libraryPhotos.search.placeholderText',
      explains: 'libraryPhotos.search.textOnly',
      empty: 'libraryPhotos.search.empty',
      silent: 'libraryPhotos.browse.silent',
    });

    expect(searchWording('meaning')).toEqual({
      placeholder: 'libraryPhotos.search.placeholderMeaning',
      explains: 'libraryPhotos.search.byMeaning',
      empty: 'libraryPhotos.search.noRanking',
      silent: 'libraryPhotos.search.silentByMeaning',
    });
  });

  /**
   * Nothing at the front of an ordering is not a statement about the words.
   *
   * The library that ranks puts everything it holds in order of closeness, so the ordering always
   * has a front: an answer with nothing in it means the library ordered nothing, which is what a
   * library with its picture recognition switched off does. Told that it is about the words, a
   * reader spends the afternoon on better words for a library that was never looking at pictures.
   */
  it('does not blame the words for an ordering that ranked nothing', () => {
    expect(emptyMessage('empty', answer({ items: [], matching: 'meaning' }), searchWording('meaning')))
      .toBe('libraryPhotos.search.noRanking');

    expect(emptyMessage('empty', answer({ items: [], matching: 'text' }), searchWording('text')))
      .toBe('libraryPhotos.search.empty');
  });

  /**
   * Words that were reduced to nothing were put to nobody, so the empty grid says that rather than
   * reporting an answer no library gave.
   */
  it('says when there were no words left to ask anybody about', () => {
    expect(
      emptyMessage('empty', answer({ items: [], searched: '' }), searchWording('text')),
    ).toBe('libraryPhotos.search.nothingLeft');
  });

  /** A listing keeps its own two sentences, neither of which is a search's. */
  it('keeps a listing past its end apart from a library that holds nothing', () => {
    expect(emptyMessage('endOfList', page({ items: [], page: 2 }), searchWording('text')))
      .toBe('libraryPhotos.browse.pastEnd');
    expect(emptyMessage('empty', page({ items: [] }), searchWording('text')))
      .toBe('libraryPhotos.browse.empty');
  });
});

/**
 * The one sentence a failure gets.
 *
 * Which one is decided by the state and by which of the two questions was being put, and a wrong
 * choice here does not look like a defect: it looks like a working screen describing a fault
 * somebody else has, and the afternoon goes on the container it named.
 */
describe('what a failure is told to the reader as', () => {
  const text = searchWording('text');
  const meaning = searchWording('meaning');

  it('names the credential when the library refused it, and the container only when it is silent', () => {
    expect(problemOf('credentialRefused', false, text, 200)?.key)
      .toBe('libraryPhotos.health.credentialRefused');
    expect(problemOf('notUnderstood', false, text, 200)?.key)
      .toBe('libraryPhotos.health.unreadable');
    expect(problemOf('silent', false, text, 200)?.key).toBe('libraryPhotos.browse.silent');
    expect(problemOf('refused', false, text, 200)?.key).toBe('libraryPhotos.refusals.notAllowed');
  });

  /**
   * A failed search of a library that ranks by meaning has an ordinary cause a listing cannot
   * have — its picture recognition being switched off — so the sentence names both possibilities
   * rather than the one that sends an operator to a healthy container.
   */
  it('names both causes when a search by meaning fails', () => {
    expect(problemOf('silent', true, meaning, 200)?.key)
      .toBe('libraryPhotos.search.silentByMeaning');

    // The control: the same failure of a listing has only the one cause, so it keeps the one
    // sentence.
    expect(problemOf('silent', false, meaning, 200)?.key).toBe('libraryPhotos.browse.silent');
  });

  /**
   * The length in the sentence is the server's, not a copy kept here. A screen holding its own copy
   * goes on stating the old number the day the server's moves, on the one surface whose argument is
   * that its numbers can be checked.
   */
  it('states the length the server refused with', () => {
    expect(problemOf('searchTooLong', true, text, 120)).toEqual({
      key: 'libraryPhotos.search.tooLong',
      values: { count: 120 },
      kind: 'info',
    });
  });

  it('says nothing at all while there is nothing wrong', () => {
    expect(problemOf('photographs', false, text, 200)).toBeNull();
    expect(problemOf('loading', false, text, 200)).toBeNull();
    expect(problemOf('empty', false, text, 200)).toBeNull();
    expect(problemOf('endOfList', false, text, 200)).toBeNull();
  });
});

describe('a listing narrowed to the days one trip was out', () => {
  /**
   * The number beside a narrowed listing counts what the library holds *in that window*, and the
   * sentence over it has to say so. "Of 412 photographs the library holds" written over a weekend's
   * worth would tell a reader their club owns four hundred photographs when it owns forty thousand
   * — and neither number describes the reader, because every account reaches a library through one
   * credential belonging to the whole installation.
   */
  it('counts what the library holds from those days, not what it holds altogether', () => {
    expect(countLine(page({ total: 12 }), true)).toEqual({
      key: 'libraryPhotos.trip.showingOf',
      values: { shown: 1, total: 12 },
    });

    expect(countLine(page({ total: null }), true)).toEqual({
      key: 'libraryPhotos.trip.showingUnknownTotal',
      values: { count: 1 },
    });

    // The control: the same page, not narrowed, keeps the sentence about the whole library.
    expect(countLine(page({ total: 12 }))?.key).toBe('libraryPhotos.browse.showingOf');
  });

  /**
   * The fact this panel exists to state. A library that answered, holds plenty, and holds nothing
   * from those days is not a library holding nothing — and only the first of those is about the
   * trip.
   */
  it('says nothing was taken then rather than that the library is empty', () => {
    expect(emptyMessage('empty', page({ items: [] }), searchWording('text'), true)).toBe(
      'libraryPhotos.trip.nothingTaken',
    );

    expect(emptyMessage('empty', page({ items: [] }), searchWording('text'))).toBe(
      'libraryPhotos.browse.empty',
    );
  });

  /**
   * A page past the end of the answer is still a page past the end. The trip changes what an empty
   * first page means and changes nothing about a step too far.
   */
  it('leaves a page past the end saying what it already said', () => {
    expect(
      emptyMessage('endOfList', page({ items: [], page: 2 }), searchWording('text'), true),
    ).toBe('libraryPhotos.browse.pastEnd');
  });

  /**
   * Two refusals this panel can meet that the library's own page cannot, and neither is about the
   * library. Read as "the library did not answer", one of them sends somebody to restart a
   * container over a trip they are not allowed to see, and the other over a mistyped date.
   */
  it('tells a refusal about the trip apart from a library that did not answer', () => {
    const notFound = new ApiError(404, 'photo_library.trip_not_found');
    const unusable = new ApiError(400, 'photo_library.trip_window_unusable');

    expect(browseState({ isPending: false, error: notFound, page: undefined })).toBe('tripNotFound');
    expect(browseState({ isPending: false, error: unusable, page: undefined })).toBe(
      'tripWindowUnusable',
    );

    expect(problemOf('tripNotFound', false, searchWording('text'), 200)).toEqual({
      key: 'libraryPhotos.trip.notFound',
      kind: 'info',
    });

    expect(problemOf('tripWindowUnusable', false, searchWording('text'), 200)).toEqual({
      key: 'libraryPhotos.trip.datesUnusable',
      kind: 'warning',
    });
  });
});
