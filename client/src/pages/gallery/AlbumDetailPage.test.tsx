// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { afterRejectionsAreReported } from '../../testEventLoop.ts';

const state = vi.hoisted(() => ({
  setCover: vi.fn(),
  /** What the album's own read failed with, when a test makes it fail. */
  albumError: null as unknown,
  photosFailed: false,
  /** The album's pictures; emptied by the test about an empty album. */
  photoIds: ['p1'] as string[],
  writes: true,
  refetchAlbum: vi.fn(),
  refetchPhotos: vi.fn(),
}));

/** An invented picture, flattened by the page to what the grid draws. */
function photo(documentId: string) {
  return {
    documentId,
    title: documentId,
    thumbnailUrl: null,
    previewUrl: null,
    width: 100,
    height: 100,
    credit: { caption: null, photographerName: null, licenceCode: null, placeName: null },
    originalName: `${documentId}.jpg`,
    contentUrl: `/c/${documentId}`,
    mayDownloadOriginal: true,
    locationRemoved: false,
    photo: null,
  };
}

vi.mock('../../api/hooks.ts', async (importActual) => {
  const actual = await importActual<typeof import('../../api/hooks.ts')>();
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: { domains: { documents: state.writes ? 'read,write' : 'read' } } }),
    useAlbum: () =>
      state.albumError
        ? { data: undefined, isPending: false, isError: true, error: state.albumError, refetch: state.refetchAlbum }
        : {
            data: { id: 'album-1', title: 'Sample album', description: null, coverDocumentId: null },
            isPending: false,
            isError: false,
            refetch: state.refetchAlbum,
          },
    usePhotos: () =>
      state.photosFailed
        ? { data: undefined, isPending: false, isError: true, refetch: state.refetchPhotos }
        : {
            data: {
              items: state.photoIds.map(photo),
              page: 1,
              pageSize: 500,
              totalItems: state.photoIds.length,
            },
            isPending: false,
            isError: false,
            refetch: state.refetchPhotos,
          },
    useReorderAlbum: () => ({ mutateAsync: vi.fn() }),
    useSetAlbumCover: () => ({ mutateAsync: state.setCover }),
    useRemoveAlbumItem: () => ({ mutateAsync: vi.fn() }),
  };
});

// The grid is proved on its own; here it only places each picture's controls.
vi.mock('../../components/gallery/PhotoGrid.tsx', () => ({
  default: (props: { photos: Array<{ documentId: string }>; renderExtra?: (p: { documentId: string }) => ReactNode }) => (
    <div>{props.photos.map((p) => <div key={p.documentId}>{props.renderExtra?.(p)}</div>)}</div>
  ),
}));
vi.mock('../../components/gallery/Lightbox.tsx', () => ({ default: () => null }));
vi.mock('../../components/gallery/PhotoCreditDrawer.tsx', () => ({ default: () => null }));

const { default: AlbumDetailPage } = await import('./AlbumDetailPage.tsx');

const unhandled = vi.fn();
beforeEach(() => {
  state.setCover.mockReset();
  state.albumError = null;
  state.photosFailed = false;
  state.photoIds = ['p1'];
  state.writes = true;
  state.refetchAlbum.mockReset();
  state.refetchPhotos.mockReset();
  unhandled.mockReset();
  process.on('unhandledRejection', unhandled);
});
afterEach(() => {
  process.off('unhandledRejection', unhandled);
  cleanup();
});

function draw() {
  render(
    <MemoryRouter initialEntries={['/albums/album-1']}>
      <App>
        <Routes>
          <Route path="/albums/:id" element={<AlbumDetailPage />} />
        </Routes>
      </App>
    </MemoryRouter>,
  );
}

describe('AlbumDetailPage', () => {
  it('offers a way to add pictures from an empty album, to somebody who may add them', () => {
    state.photoIds = [];
    draw();
    const add = screen.getByTestId('album-add-photos');
    expect(add).toHaveAttribute('href', '/gallery');
    expect(add).toHaveTextContent('Add photographs');
  });

  it('offers no such way to a reader who may not add pictures', () => {
    state.photoIds = [];
    state.writes = false;
    draw();
    expect(screen.queryByTestId('album-add-photos')).toBeNull();
  });

  it('asks the server to make a picture the cover', () => {
    state.setCover.mockResolvedValue(undefined);
    draw();
    fireEvent.click(screen.getByRole('button', { name: 'Use as the cover' }));
    expect(state.setCover).toHaveBeenCalledWith({ id: 'album-1', documentId: 'p1' });
  });

  it('says when making a picture the cover failed, instead of letting the refusal go unseen', async () => {
    state.setCover.mockRejectedValue(new Error('API error 500'));
    draw();
    fireEvent.click(screen.getByRole('button', { name: 'Use as the cover' }));

    expect(await screen.findByText('The operation failed. Please try again.')).toBeTruthy();
    await afterRejectionsAreReported();
    expect(unhandled).not.toHaveBeenCalled();
  });
});

describe('reads of the album that did not land', () => {
  it('says a failed read of the album failed, with Retry, rather than that it is forbidden', () => {
    state.albumError = new TypeError('offline');
    draw();
    expect(screen.getByTestId('read-failed')).toBeTruthy();
    expect(screen.queryByText(i18n.t('admin.forbidden'))).toBeNull();
    fireEvent.click(screen.getByTestId('read-failed-retry'));
    expect(state.refetchAlbum).toHaveBeenCalledTimes(1);
  });

  it('says an album that is not there is not there, rather than that it is forbidden', () => {
    state.albumError = new ApiError(404, 'album.not_found');
    draw();
    expect(screen.getByText(i18n.t('gallery.albumNotFound'))).toBeTruthy();
    expect(screen.queryByText(i18n.t('admin.forbidden'))).toBeNull();
  });

  it('says a failed read of the pictures failed, rather than drawing an empty album', () => {
    state.photosFailed = true;
    draw();
    expect(screen.getByTestId('album-photos-load-failed')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(state.refetchPhotos).toHaveBeenCalledTimes(1);
  });
});
