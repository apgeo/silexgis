// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import type { LibraryPhotographPage } from '../../api/hooks.ts';
import { browseState, pagingOf, stepPage } from './browseState.ts';

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
  textSearchSupported: true,
  picturesAvailable: true,
  pictureUrlTemplate: '/api/v1/photo-libraries/photoprism/thumbnails/{reference}?size={size}&token=x',
  readAt: '2026-02-03T04:05:06Z',
  ...over,
});

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
   * Words this application declined to put to the library are not the library failing. They reach
   * a library that cannot match them by outliving the box they were typed into — the address keeps
   * them when the reader switches library — and a screen reporting "the library did not answer"
   * for a question nobody asked sends somebody to look at a container that is fine.
   */
  it('tells a question that was never asked from a library that did not answer', () => {
    expect(
      browseState({
        isPending: false,
        error: new ApiError(400, 'photo_library.text_search_unsupported'),
        page: undefined,
      }),
    ).toBe('searchUnsupported');

    // The control: another refusal with the same status is still a silence, so this is keyed off
    // the code and not off "a request that failed with 400".
    expect(
      browseState({
        isPending: false,
        error: new ApiError(400, 'photo_library.search_too_long'),
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

describe('what the paging may offer', () => {
  /**
   * A next page follows what the library said, never a total: one of the two products states how
   * many it holds and the other publishes no way to ask, so a control built on a total would work
   * against one library and silently offer nothing against the other.
   */
  it('offers the next page from what the library said rather than from a count', () => {
    expect(pagingOf(page({ total: null, hasMore: true })).hasNext).toBe(true);
    expect(pagingOf(page({ total: 412, hasMore: false })).hasNext).toBe(false);
  });

  it('offers a previous page only after the first', () => {
    expect(pagingOf(page({ page: 1 })).hasPrevious).toBe(false);
    expect(pagingOf(page({ page: 2 })).hasPrevious).toBe(true);
  });

  /**
   * The count of what is on screen and the size of the library are separate numbers, and the
   * unknown one stays unknown. A page that put the first where the second belongs would tell a
   * reader the club has sixty photographs.
   */
  it('keeps what is shown apart from what the library holds', () => {
    const known = pagingOf(page({ total: 412 }));
    expect(known.shown).toBe(1);
    expect(known.total).toBe(412);

    const unknown = pagingOf(page({ total: null }));
    expect(unknown.shown).toBe(1);
    expect(unknown.total).toBeNull();
  });

  it('says nothing at all before an answer has arrived', () => {
    expect(pagingOf(undefined)).toEqual({
      hasPrevious: false,
      hasNext: false,
      total: null,
      shown: 0,
    });
  });

  /**
   * A step past the end is refused where the step is taken, not only by a disabled control: a
   * control left enabled by a stale answer would otherwise ask the library for a page it has
   * already said is not there.
   */
  it('refuses a step past either end', () => {
    const last = pagingOf(page({ page: 3, hasMore: false }));
    expect(stepPage(3, 1, last)).toBe(3);
    expect(stepPage(3, -1, last)).toBe(2);

    const first = pagingOf(page({ page: 1, hasMore: true }));
    expect(stepPage(1, -1, first)).toBe(1);
    expect(stepPage(1, 1, first)).toBe(2);
  });
});
