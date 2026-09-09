// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { LibraryAlbums } from '../../api/hooks.ts';
import LibraryAlbumChooser from './LibraryAlbumChooser.tsx';

/**
 * Choosing one of a neighbouring library's own albums.
 *
 * The load-bearing pair is "this library keeps no albums" against "this library did not answer
 * about them". Both leave a chooser with nothing to pick from, and they send a reader to entirely
 * different places — one to make an album they may already have, the other to a container that is
 * down. A control that renders them as one empty box is why somebody spends an afternoon on a
 * library that was working.
 *
 * Every identifier, title and count below is invented.
 */

const albums: LibraryAlbums = {
  source: 'photoprism',
  libraryName: 'PhotoPrism',
  items: [
    { albumId: 'asinvented000000one', title: 'An invented expedition', photographCount: 12, from: null, to: null },
    { albumId: 'asinvented000000two', title: null, photographCount: null, from: null, to: null },
  ],
  truncated: false,
  readAt: '2026-02-03T04:05:06Z',
};

function chooser(over: Partial<Parameters<typeof LibraryAlbumChooser>[0]> = {}) {
  return render(
    <LibraryAlbumChooser
      albums={albums}
      isPending={false}
      error={null}
      albumId={null}
      onChoose={vi.fn()}
      searching={false}
      {...over}
    />,
  );
}

afterEach(cleanup);

describe('an album chooser over a neighbouring library', () => {
  it('offers the albums the library listed, and nothing it did not', () => {
    chooser();

    const control = screen.getByTestId('library-photo-album');
    expect(control).toBeTruthy();
    expect(control.querySelector('.ant-select-disabled')).toBeNull();
  });

  /**
   * The pair this control exists to keep apart. Both draw an empty chooser, and the sentence in it
   * is the only thing a reader has to tell them by.
   */
  it('says a library with no albums differently from one that did not answer', () => {
    chooser({ albums: { ...albums, items: [] } });
    expect(screen.getByText('This library keeps no albums')).toBeTruthy();

    cleanup();

    chooser({ albums: undefined, error: new Error('no') });
    expect(screen.getByText('This library did not answer about its albums')).toBeTruthy();

    cleanup();

    // And a third: still filling, which is neither of those and means "wait".
    chooser({ albums: undefined, isPending: true });
    expect(screen.getByText("Reading this library's albums…")).toBeTruthy();
  });

  /**
   * A short list that does not say it is short is a wrong answer: a reader whose album is missing
   * concludes the library has lost it.
   */
  it('says when it is offering only the front of what the library keeps', () => {
    chooser({ albums: { ...albums, truncated: true } });

    expect(
      screen.getByText('Only the first 2 albums this library keeps are offered here.'),
    ).toBeTruthy();

    cleanup();

    chooser();
    expect(screen.queryByText(/Only the first/)).toBeNull();
  });

  /**
   * A search goes to the library whole — neither product will take a set of albums to search within
   * — so while words are on screen this narrows nothing. A control that sat there looking as though
   * it applied would be telling a reader their results were the album's.
   */
  it('says that a chosen album is not narrowing a search', () => {
    chooser({ searching: true, albumId: 'asinvented000000one' });

    expect(screen.getByText(/cannot be narrowed to an album/)).toBeTruthy();

    cleanup();

    // Not said when no album is chosen: there is nothing being ignored, so the line would be noise.
    chooser({ searching: true, albumId: null });
    expect(screen.queryByText(/cannot be narrowed to an album/)).toBeNull();
  });
});
