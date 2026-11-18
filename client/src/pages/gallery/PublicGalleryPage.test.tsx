// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

/**
 * The two gallery pages a visitor without an account can open: the curated public gallery and an
 * album reached by its link.
 *
 * What each must never do is read a failure as an answer. A gallery that could not be read is not
 * an empty gallery, and an album the server could not serve is not a revoked link — saying either
 * sends the visitor away believing something false. The pictures below are invented.
 */

const state = vi.hoisted(() => ({
  publicPhotos: {} as Record<string, unknown>,
  sharedAlbum: {} as Record<string, unknown>,
}));

vi.mock('../../api/hooks.ts', () => ({
  usePublicPhotos: () => state.publicPhotos,
  useSharedAlbum: () => state.sharedAlbum,
}));

// The grid and the viewer have tests of their own; here only which pictures reach them matters.
vi.mock('../../components/gallery/PhotoGrid.tsx', () => ({
  default: ({ photos, emptyText }: { photos: { documentId: string; title: string }[]; emptyText?: string }) => (
    <ul data-testid="grid">
      {photos.length === 0 && emptyText ? <li>{emptyText}</li> : null}
      {photos.map((photo) => (
        <li key={photo.documentId}>{photo.title}</li>
      ))}
    </ul>
  ),
}));
vi.mock('../../components/gallery/Lightbox.tsx', () => ({ default: () => null }));

const { default: PublicGalleryPage, SharedAlbumPage } = await import('./PublicGalleryPage.tsx');

const t = i18n.getFixedT('en');
const refetch = vi.fn();

beforeEach(async () => {
  await i18n.changeLanguage('en');
  refetch.mockReset();
});
afterEach(cleanup);

function photo(id: string, title: string) {
  return { documentId: id, title };
}

function showGallery() {
  render(
    <MemoryRouter>
      <PublicGalleryPage />
    </MemoryRouter>,
  );
}

function showAlbum() {
  render(
    <MemoryRouter initialEntries={['/shared/albums/invented-token']}>
      <Routes>
        <Route path="/shared/albums/:token" element={<SharedAlbumPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('PublicGalleryPage', () => {
  it('shows what was published', () => {
    state.publicPhotos = {
      data: { items: [photo('a', 'Invented shaft'), photo('b', 'Invented lake')], page: 1, pageSize: 60, totalItems: 2 },
      isPending: false,
      isError: false,
      refetch,
    };
    showGallery();
    expect(screen.getByText('Invented shaft')).toBeTruthy();
    expect(screen.getByText('Invented lake')).toBeTruthy();
    expect(screen.queryByText(t('gallery.publicEmpty'))).toBeNull();
  });

  it('says nothing is published when the server answers with no pictures', () => {
    state.publicPhotos = {
      data: { items: [], page: 1, pageSize: 60, totalItems: 0 },
      isPending: false,
      isError: false,
      refetch,
    };
    showGallery();
    expect(screen.getByText(t('gallery.publicEmpty'))).toBeTruthy();
  });

  it('says the gallery could not be read, and offers to try again, rather than calling it empty', () => {
    state.publicPhotos = { data: undefined, isPending: false, isError: true, refetch };
    showGallery();
    expect(screen.queryByText(t('gallery.publicEmpty'))).toBeNull();
    expect(screen.getByText(t('common.loadFailed'))).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetch).toHaveBeenCalled();
  });
});

describe('SharedAlbumPage', () => {
  it('shows the album the link opens', () => {
    state.sharedAlbum = {
      data: { title: 'Invented expedition', description: 'Pictures from an invented trip.', photos: [photo('c', 'Invented pitch')] },
      isPending: false,
      isError: false,
      refetch,
    };
    showAlbum();
    expect(screen.getByText('Invented expedition')).toBeTruthy();
    expect(screen.getByText('Pictures from an invented trip.')).toBeTruthy();
    expect(screen.getByText('Invented pitch')).toBeTruthy();
  });

  it('reads a link the server does not know as one that opens nothing', () => {
    state.sharedAlbum = {
      data: undefined,
      isPending: false,
      isError: true,
      error: new ApiError(404, 'album_share.not_found', undefined),
      refetch,
    };
    showAlbum();
    expect(screen.getByText(t('gallery.linkNotFound'))).toBeTruthy();
    expect(screen.queryByRole('button', { name: t('common.retry') })).toBeNull();
  });

  it('does not call a link revoked when the server could not answer', () => {
    state.sharedAlbum = {
      data: undefined,
      isPending: false,
      isError: true,
      error: new ApiError(503, undefined, undefined),
      refetch,
    };
    showAlbum();
    expect(screen.queryByText(t('gallery.linkNotFound'))).toBeNull();
    expect(screen.getByText(t('common.loadFailed'))).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetch).toHaveBeenCalled();
  });
});
