// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { PhotoQueryParams } from '../../api/hooks.ts';

const { photosSpy, canRead } = vi.hoisted(() => ({ photosSpy: vi.fn(), canRead: { value: true } }));

vi.mock('../../api/hooks.ts', () => ({
  useCan: () => canRead.value,
  usePhotos: (params: PhotoQueryParams, enabled: boolean) => photosSpy(params, enabled),
}));

vi.mock('../../components/gallery/PhotoGrid.tsx', () => ({
  default: ({ photos }: { photos: { documentId: string }[] }) => (
    <div data-testid="photo-grid">{photos.length} pictures</div>
  ),
}));
vi.mock('../../components/gallery/Lightbox.tsx', () => ({ default: () => null }));
vi.mock('../../components/gallery/photoView.ts', () => ({
  viewPhoto: (photo: { documentId: string }) => photo,
}));

const { default: ExpeditionPhotosTab } = await import('./ExpeditionPhotosTab.tsx');

function show() {
  return render(
    <MemoryRouter>
      <ExpeditionPhotosTab expeditionId="camp-1" />
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  canRead.value = true;
  photosSpy.mockReset().mockReturnValue({ data: { items: [] }, isPending: false });
});

describe('the camp photographs tab', () => {
  it('asks the server the camp question once, and never assembles the trips itself', () => {
    show();

    // One request, narrowed by the camp: which trips the pictures come from is the server's
    // answer, decided through the reader's own visibility, and not something this tab computes.
    expect(photosSpy).toHaveBeenCalled();
    const [params, enabled] = photosSpy.mock.calls[0] as [PhotoQueryParams, boolean];
    expect(params.expeditionId).toBe('camp-1');
    expect(params.tripLogId).toBeUndefined();
    expect(enabled).toBe(true);
  });

  it('draws the grid with its caveat, and leads to the gallery narrowed the same way', () => {
    photosSpy.mockReturnValue({
      data: { items: [{ documentId: 'p1' }, { documentId: 'p2' }] },
      isPending: false,
    });
    show();

    expect(screen.getByTestId('photo-grid').textContent).toBe('2 pictures');
    expect(screen.getByText(/The photographs you may see/)).toBeTruthy();
    expect(screen.getByRole('link').getAttribute('href')).toBe('/gallery?expeditionId=camp-1');
  });

  it('says there is nothing to show, in words that admit it is one reader\'s answer', () => {
    show();
    expect(screen.getByText(/that you may read/)).toBeTruthy();
  });

  it('draws nothing at all for somebody who may not read documents', () => {
    canRead.value = false;
    show();
    expect(screen.queryByTestId('expedition-photos-tab')).toBeNull();
    // Not even the request: the server would refuse it.
    expect(photosSpy.mock.calls[0][1]).toBe(false);
  });
});
