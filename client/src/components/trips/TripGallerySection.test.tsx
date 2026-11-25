// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

/**
 * A trip's photographs and the albums about it: what is drawn once both reads answer, what is
 * said when either fails, and making the trip's album — opened on success, its refusal said in
 * words. The trip, its photographs and its albums are invented.
 */

const createAlbum = vi.fn();
const refetchPhotos = vi.fn();
const refetchAlbums = vi.fn();
let photosRead: Record<string, unknown>;
let albumsRead: Record<string, unknown>;
let documents = 'read, write';

vi.mock('../../api/hooks.ts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/hooks.ts')>();
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: { domains: { documents } } }),
    usePhotos: () => photosRead,
    useAlbums: () => albumsRead,
    useCreateAlbum: () => ({ mutateAsync: createAlbum, isPending: false }),
  };
});

// The grid and the viewer have tests of their own; here only which photographs they are given.
vi.mock('../gallery/PhotoGrid.tsx', () => ({
  default: ({ photos }: { photos: { documentId: string; title: string }[] }) => (
    <ul data-testid="grid">
      {photos.map((photo) => (
        <li key={photo.documentId}>{photo.title}</li>
      ))}
    </ul>
  ),
}));
vi.mock('../gallery/Lightbox.tsx', () => ({ default: () => null }));

const { default: TripGallerySection } = await import('./TripGallerySection.tsx');

const t = i18n.getFixedT('en');

function photo(id: string, title: string) {
  return {
    documentId: id,
    title,
    thumbnailUrl: null,
    previewUrl: null,
    width: 10,
    height: 10,
    credit: { caption: null, photographerName: null, licenceCode: null, placeName: null },
    originalName: `${id}.jpg`,
    contentUrl: null,
    mayDownloadOriginal: false,
    locationRemoved: false,
    photo: null,
  };
}

function answered(items: unknown[], refetch: () => void) {
  return { data: { items }, isPending: false, isError: false, error: null, refetch };
}

function failed(error: unknown, refetch: () => void) {
  return { data: undefined, isPending: false, isError: true, error, refetch };
}

function show() {
  render(
    <App>
      <MemoryRouter initialEntries={['/trips/trip-1']}>
        <Routes>
          <Route path="/trips/trip-1" element={<TripGallerySection tripId="trip-1" tripTitle="Invented descent" />} />
          <Route path="/albums/:id" element={<div data-testid="album-page" />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  createAlbum.mockReset();
  refetchPhotos.mockReset();
  refetchAlbums.mockReset();
  documents = 'read, write';
  photosRead = answered([photo('p1', 'Invented shaft'), photo('p2', 'Invented gallery')], refetchPhotos);
  albumsRead = answered(
    [{ id: 'a1', title: 'Invented album', coverThumbnailUrl: null, photoCount: 2 }],
    refetchAlbums,
  );
});
afterEach(cleanup);

describe('TripGallerySection', () => {
  it("draws the trip's photographs and the albums about it", () => {
    show();
    expect(screen.getByTestId('grid')).toHaveTextContent('Invented shaft');
    expect(screen.getByTestId('grid')).toHaveTextContent('Invented gallery');
    expect(screen.getByRole('link', { name: 'Invented album' })).toHaveAttribute('href', '/albums/a1');
    expect(screen.queryByTestId('trip-photographs-load-failed')).toBeNull();
    expect(screen.queryByTestId('trip-albums-load-failed')).toBeNull();
  });

  it('names a failed read of the photographs instead of saying there are none, and reads again on Retry', () => {
    photosRead = failed(new TypeError('Failed to fetch'), refetchPhotos);
    show();
    expect(screen.getByTestId('trip-photographs-load-failed')).toHaveTextContent(t('trips.photographsLoadFailed'));
    expect(screen.queryByText(t('trips.noPhotographs'))).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetchPhotos).toHaveBeenCalledTimes(1);
    expect(refetchAlbums).not.toHaveBeenCalled();
  });

  it('names a failed read of the albums and keeps the photographs that were read', () => {
    albumsRead = failed(new ApiError(503, undefined, 'Unavailable'), refetchAlbums);
    show();
    expect(screen.getByTestId('trip-albums-load-failed')).toHaveTextContent(t('trips.albumsLoadFailed'));
    expect(screen.getByTestId('grid')).toHaveTextContent('Invented shaft');
    fireEvent.click(screen.getByRole('button', { name: t('common.retry') }));
    expect(refetchAlbums).toHaveBeenCalledTimes(1);
  });

  it("makes the trip's album, private and named after the trip, and opens it", async () => {
    createAlbum.mockResolvedValue({ id: 'new-album' });
    show();
    fireEvent.click(screen.getByTestId('trip-new-album'));

    expect(await screen.findByTestId('album-page')).toBeInTheDocument();
    expect(createAlbum).toHaveBeenCalledWith({
      title: 'Invented descent',
      visibility: 'private',
      subjectEntityType: 'tripLog',
      subjectEntityId: 'trip-1',
    });
  });

  it('says why making the album was refused, and stays on the trip', async () => {
    createAlbum
      .mockRejectedValueOnce(new ApiError(403, 'access.create_forbidden', 'Forbidden.'))
      .mockRejectedValueOnce(new ApiError(404, 'album.subject_not_found', 'Not found.'));
    show();

    fireEvent.click(screen.getByTestId('trip-new-album'));
    expect(await screen.findByText(t('gallery.createForbidden'))).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('trip-new-album'));
    expect(await screen.findByText(t('gallery.subjectGone'))).toBeInTheDocument();

    await waitFor(() => expect(createAlbum).toHaveBeenCalledTimes(2));
    expect(screen.queryByTestId('album-page')).toBeNull();
    expect(screen.getByTestId('trip-gallery')).toBeInTheDocument();
  });

  it('offers no album to a reader who may not write documents', () => {
    documents = 'read';
    show();
    expect(screen.getByTestId('grid')).toBeInTheDocument();
    expect(screen.queryByTestId('trip-new-album')).toBeNull();
  });
});
