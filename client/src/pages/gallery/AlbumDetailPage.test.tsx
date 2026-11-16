// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const state = vi.hoisted(() => ({ setCover: vi.fn() }));

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
    useCapabilities: () => ({ data: { domains: { documents: 'read,write' } } }),
    useAlbum: () => ({
      data: { id: 'album-1', title: 'Sample album', description: null, coverDocumentId: null },
      isPending: false,
    }),
    usePhotos: () => ({
      data: { items: [photo('p1')], page: 1, pageSize: 500, totalItems: 1 },
      isPending: false,
      refetch: vi.fn(),
    }),
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
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(unhandled).not.toHaveBeenCalled();
  });
});
