// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AlbumInfo } from '../../api/hooks.ts';

const { albumsSpy, capabilitiesSpy, createAlbumSpy } = vi.hoisted(() => ({
  albumsSpy: vi.fn(),
  capabilitiesSpy: vi.fn(),
  createAlbumSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useAlbums: (...args: unknown[]) => albumsSpy(...args),
  useCapabilities: () => capabilitiesSpy(),
  useCreateAlbum: () => ({ mutateAsync: createAlbumSpy, isPending: false }),
  hasAccessAction: (actions: string | undefined, action: string) =>
    (actions ?? '').split(',').includes(action),
}));

// Attachments are a shared section with tests of their own; this asserts only that the camp's
// own files hang off the camp, which is the wiring this page is responsible for.
vi.mock('../../components/attachments/AttachmentSection.tsx', () => ({
  default: ({ entityType, entityId }: { entityType: string; entityId: string }) => (
    <div data-testid="attachments">{`${entityType}:${entityId}`}</div>
  ),
}));

const { default: ExpeditionFilesTab } = await import('./ExpeditionFilesTab.tsx');

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

function album(): AlbumInfo {
  return {
    id: 'album-1',
    title: 'Bihor summer camp',
    photoCount: 4,
    coverThumbnailUrl: null,
  } as unknown as AlbumInfo;
}

function documents(actions: string) {
  capabilitiesSpy.mockReturnValue({ data: { domains: { documents: actions } } });
}

function show() {
  return render(
    <App>
      <MemoryRouter>
        <ExpeditionFilesTab expeditionId={CAMP} expeditionName="Bihor summer camp" canEdit />
      </MemoryRouter>
    </App>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  documents('read,write');
  albumsSpy.mockReturnValue({ data: { items: [album()], page: 1, pageSize: 100, totalItems: 1 } });
});

describe('what is filed against a camp', () => {
  it('hangs the files filed on the camp off the camp itself', () => {
    show();

    expect(screen.getByTestId('attachments').textContent).toBe(`expedition:${CAMP}`);
  });

  it('lists the albums that are about this camp', () => {
    show();

    // Filtered by the camp as the album's subject — the same question the trip gallery asks of
    // a trip, so a camp does not get a second way of deciding which albums are about it.
    expect(albumsSpy).toHaveBeenCalledWith(
      expect.objectContaining({ subjectEntityId: CAMP }),
      true,
    );
    expect(screen.getByText('Bihor summer camp')).toBeTruthy();
  });

  it('says an empty camp has no albums yet, rather than showing nothing at all', () => {
    albumsSpy.mockReturnValue({ data: { items: [], page: 1, pageSize: 100, totalItems: 0 } });
    show();

    expect(screen.getByText(/No albums about this camp yet/)).toBeTruthy();
  });

  it('offers the album button on the right to write documents, not the right to edit the camp', () => {
    // An album is separately governed content that happens to have a camp as its subject, so
    // somebody who may edit this camp and may not write documents is shown the albums and not
    // the button — and the server would refuse them in any case.
    documents('read');
    show();

    expect(screen.queryByTestId('expedition-new-album')).toBeNull();
    expect(screen.getByTestId('expedition-albums')).toBeTruthy();
  });

  it('shows no album section at all to somebody who may not read documents', () => {
    documents('');
    show();

    expect(screen.queryByTestId('expedition-albums')).toBeNull();
    // The positive case in the same breath: the camp's own attachments are governed by the camp
    // and stay, so this is a section disappearing rather than the tab going blank.
    expect(screen.getByTestId('attachments')).toBeTruthy();
  });
});
