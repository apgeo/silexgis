// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { LibraryPhotograph } from '../../api/hooks.ts';
import LibraryPhotoGrid from './LibraryPhotoGrid.tsx';

/**
 * A page of a neighbouring library's photographs, as tiles.
 *
 * The load-bearing case is the last one: a picture that failed is never asked for again, not even
 * after the grid is thrown away and drawn afresh. Against one of the two products, a picture
 * request whose original cannot be resolved is what marks the file missing over there, so a grid
 * that forgot on every remount would be a deletion loop rather than a slow page.
 *
 * Every identifier, hash and title below is invented.
 */

const template =
  '/api/v1/photo-libraries/photoprism/thumbnails/{reference}?size={size}&token=invented';

const photographs: LibraryPhotograph[] = [
  {
    photographId: 'psinvented1',
    reference: 'aa11bb22cc33',
    title: 'An invented picture',
    takenAt: '2026-02-03T04:05:06Z',
    kind: null,
  },
  {
    photographId: 'psinvented2',
    reference: 'bb22cc33dd44',
    title: null,
    takenAt: null,
    kind: null,
  },
];

function grid(over: Partial<Parameters<typeof LibraryPhotoGrid>[0]> = {}) {
  return render(
    <LibraryPhotoGrid
      source="photoprism"
      photographs={photographs}
      pictureUrlTemplate={template}
      onOpen={vi.fn()}
      emptyText="nothing here"
      {...over}
    />,
  );
}

afterEach(cleanup);

describe('a page of a neighbouring library', () => {
  it('draws one tile per photograph, through this application rather than from the library', () => {
    grid();

    // By the tile's own mark rather than by role: the mark this falls back to is an icon, and
    // the library it is drawn from gives that a picture role too — so a test counting roles
    // would count the fallbacks as pictures and pass while nothing was ever asked for.
    const pictures = screen.getAllByTestId('library-photo-tile-picture');
    expect(pictures).toHaveLength(2);
    expect(pictures[0].getAttribute('src')).toBe(
      '/api/v1/photo-libraries/photoprism/thumbnails/aa11bb22cc33?size=small&token=invented',
    );

    // Small, never the original: a page of sixty photographs at full size is hundreds of megabytes
    // fetched through this application from somebody else's container.
    for (const picture of pictures) {
      expect(picture.getAttribute('src')).toContain('size=small');
      expect(picture.getAttribute('loading')).toBe('lazy');
    }
  });

  /**
   * A photograph the library kept no title for is still a photograph, and the tile says so in
   * words rather than showing an empty caption that would read as a title nobody wrote.
   */
  it('names a photograph the library gave no title', () => {
    grid();

    expect(screen.getByText('An invented picture')).toBeInTheDocument();
    expect(screen.getByText('Untitled')).toBeInTheDocument();
  });

  /**
   * A null template is this library's pictures being stopped, published by the server. Every tile
   * falls back to a mark and asks for nothing — the listing is fine, and drawing it as broken
   * would be this application inventing an outage.
   */
  it('asks for no picture at all when the library must not be asked for one', () => {
    grid({ pictureUrlTemplate: null });

    expect(screen.queryAllByTestId('library-photo-tile-picture')).toHaveLength(0);
    expect(screen.getAllByTestId('library-photo-tile-fallback')).toHaveLength(2);
  });

  it('opens the photograph it was clicked on, by the library\'s name for it', () => {
    const onOpen = vi.fn();
    grid({ onOpen });

    fireEvent.click(screen.getByRole('button', { name: 'An invented picture' }));

    // The photograph's own identifier, never the picture's — on this product the second is a hash
    // of the bytes and names no photograph at all.
    expect(onOpen).toHaveBeenCalledWith('psinvented1');
  });

  it('says so rather than drawing nothing when the library holds nothing matching', () => {
    grid({ photographs: [] });

    expect(screen.getByText('nothing here')).toBeInTheDocument();
    expect(screen.queryByTestId('library-photo-grid')).not.toBeInTheDocument();
  });

  /**
   * The one that matters. A failed picture falls back to its mark, and the failure is remembered
   * outside the component — so a grid unmounted and drawn again does not ask a second time.
   */
  it('never asks again for a picture that failed, even after the grid is drawn afresh', () => {
    const first = grid();

    fireEvent.error(screen.getAllByTestId('library-photo-tile-picture')[0]);

    // One tile falls back and the other is untouched: a picture this library could not produce
    // says nothing about the rest of what it holds.
    expect(screen.getAllByTestId('library-photo-tile-fallback')).toHaveLength(1);
    expect(screen.getAllByTestId('library-photo-tile-picture')).toHaveLength(1);

    first.unmount();
    grid();

    expect(screen.getAllByTestId('library-photo-tile-fallback')).toHaveLength(1);

    const survivors = screen.getAllByTestId('library-photo-tile-picture');
    expect(survivors).toHaveLength(1);
    expect(survivors[0].getAttribute('src')).toContain('bb22cc33dd44');
  });
});
