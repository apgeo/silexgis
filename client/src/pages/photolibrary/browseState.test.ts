// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import type { LibraryPhotographPage, LibraryPhotographSearchPage } from '../../api/hooks.ts';
import { browseState, countLine, pagingOf, searchWording, stepPage } from './browseState.ts';

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

  return { ...rest, matching: 'text', items, ...over };
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
        error: new ApiError(400, 'photo_library.rejected'),
        page: undefined,
      }),
    ).toBe('silent');
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
    });

    expect(searchWording('meaning')).toEqual({
      placeholder: 'libraryPhotos.search.placeholderMeaning',
      explains: 'libraryPhotos.search.byMeaning',
    });
  });
});
