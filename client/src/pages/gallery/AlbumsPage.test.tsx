// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { DOCUMENT_VISIBILITIES } from '../../components/attachments/visibilityChoices.ts';
import AlbumsPage from './AlbumsPage.tsx';

const state = vi.hoisted(() => ({
  documents: 'read,write,share' as string,
  albums: { data: undefined as unknown, isPending: false, isError: false, refetch: vi.fn() },
  deleteAlbum: vi.fn(),
  revoke: vi.fn(),
  share: vi.fn(),
  create: vi.fn(),
}));

vi.mock('../../api/hooks.ts', async (importActual) => {
  const actual = await importActual<typeof import('../../api/hooks.ts')>();
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: { domains: { documents: state.documents } } }),
    useAlbums: () => state.albums,
    useCreateAlbum: () => ({ mutateAsync: state.create, isPending: false }),
    useUpdateAlbum: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useDeleteAlbum: () => ({ mutateAsync: state.deleteAlbum, isPending: false }),
    useShareAlbum: () => ({ mutateAsync: state.share, isPending: false }),
    useRevokeAlbumShare: () => ({ mutateAsync: state.revoke, isPending: false }),
  };
});

function album(id: string, title: string, hasActiveShare = false) {
  return {
    id,
    title,
    description: null,
    coverDocumentId: null,
    coverThumbnailUrl: null,
    subjectEntityType: null,
    subjectEntityId: null,
    visibility: 'authenticated',
    cavingGroupId: null,
    ownerUserId: 'u',
    photoCount: 3,
    hasActiveShare,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <App>
        <AlbumsPage />
      </App>
    </MemoryRouter>,
  );
}

const unhandled = vi.fn();
beforeEach(() => {
  state.documents = 'read,write,share';
  state.albums = {
    data: { items: [album('a1', 'Pestera 1 survey'), album('a2', 'Camp week', true)] },
    isPending: false,
    isError: false,
    refetch: vi.fn(),
  };
  state.deleteAlbum.mockReset();
  state.revoke.mockReset();
  state.share.mockReset();
  state.create.mockReset();
  unhandled.mockReset();
  process.on('unhandledRejection', unhandled);
});
afterEach(() => {
  process.off('unhandledRejection', unhandled);
  cleanup();
});

describe('AlbumsPage', () => {
  it('offers each card action as a named, focusable button', () => {
    renderPage();
    const card = screen.getByText('Pestera 1 survey').closest('.ant-card') as HTMLElement;

    // Bare icons with a click handler could not be reached from the keyboard or named by a
    // screen reader; these are buttons with names of their own.
    for (const name of ['Edit album', 'Make a share link', 'Delete album']) {
      const button = within(card).getByRole('button', { name });
      expect(button.tagName).toBe('BUTTON');
    }

    const shared = screen.getByText('Camp week').closest('.ant-card') as HTMLElement;
    expect(within(shared).getByRole('button', { name: 'Revoke the share link' })).toBeTruthy();
  });

  it('says when deleting an album failed, instead of doing nothing', async () => {
    state.deleteAlbum.mockRejectedValue(new Error('API error 404'));
    renderPage();
    const card = screen.getByText('Pestera 1 survey').closest('.ant-card') as HTMLElement;

    fireEvent.click(within(card).getByRole('button', { name: 'Delete album' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText('The deletion failed. Please try again.')).toBeTruthy();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(unhandled).not.toHaveBeenCalled();
  });

  it.each([
    ['confirms a revoked link', true, 'Share link revoked.'],
    ['says when revoking a link failed', false, 'The operation failed. Please try again.'],
  ])('%s', async (_name, succeeds, said) => {
    if (succeeds) {
      state.revoke.mockResolvedValue(undefined);
    } else {
      state.revoke.mockRejectedValue(new Error('API error 500'));
    }
    renderPage();
    const shared = screen.getByText('Camp week').closest('.ant-card') as HTMLElement;

    fireEvent.click(within(shared).getByRole('button', { name: 'Revoke the share link' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText(said)).toBeTruthy();
    expect(state.revoke).toHaveBeenCalledWith('a2');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(unhandled).not.toHaveBeenCalled();
  });

  /** Both cards' action buttons, by name, as drawn for the capability string given. */
  function cardActions(documents: string) {
    cleanup();
    state.documents = documents;
    renderPage();
    const card = screen.getByText('Pestera 1 survey').closest('.ant-card') as HTMLElement;
    const shared = screen.getByText('Camp week').closest('.ant-card') as HTMLElement;
    const names = (element: HTMLElement) =>
      within(element).queryAllByRole('button').map((button) => button.getAttribute('aria-label'));
    return { card: names(card), shared: names(shared) };
  }

  it('shows the share button to whoever the server lets mint a link, and nothing else to them', () => {
    // The allowed caller first, in the same test: were the actions drawn for nobody, the
    // absences below would prove nothing about the rule they are meant to test.
    const writer = cardActions('read,write,share');
    expect(writer.card).toEqual(expect.arrayContaining(['Edit album', 'Make a share link', 'Delete album']));
    expect(writer.shared).toContain('Revoke the share link');

    // Minting is allowed on the documents domain's share right alone; editing, deleting and
    // revoking need write on the album.
    const sharer = cardActions('read,share');
    expect(sharer.card).toContain('Make a share link');
    expect(sharer.card).not.toContain('Edit album');
    expect(sharer.card).not.toContain('Delete album');
    expect(sharer.shared).not.toContain('Revoke the share link');
  });

  it('shows no card actions to a reader with neither right', () => {
    const writer = cardActions('read,write,share');
    expect(writer.card).toEqual(expect.arrayContaining(['Edit album', 'Make a share link', 'Delete album']));
    expect(writer.shared).toContain('Revoke the share link');

    const reader = cardActions('read');
    expect(reader.card).toEqual([]);
    expect(reader.shared).toEqual([]);
  });

  it('says the albums could not be loaded, not that there are none, and loads them again', () => {
    state.albums = { data: undefined, isPending: false, isError: true, refetch: vi.fn() };
    renderPage();

    expect(screen.queryByText('No albums yet.')).toBeNull();
    expect(screen.getByText('The albums could not be loaded.')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(state.albums.refetch).toHaveBeenCalled();
  });

  it('offers exactly the audiences the API knows, each named in both languages', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /New album/ }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.mouseDown(within(dialog).getByRole('combobox'));

    await waitFor(() => expect(document.querySelectorAll('.ant-select-item-option').length).toBe(4));
    const offered = Array.from(document.querySelectorAll('.ant-select-item-option')).map((option) =>
      option.getAttribute('title'),
    );
    expect(offered).toEqual(
      DOCUMENT_VISIBILITIES.map((value) => (en.caves.visibilityValues as Record<string, string>)[value]),
    );
    for (const value of DOCUMENT_VISIBILITIES) {
      expect((ro.caves.visibilityValues as Record<string, string>)[value]).toBeTruthy();
    }
    expect([...DOCUMENT_VISIBILITIES].sort()).toEqual(['authenticated', 'cavingGroup', 'private', 'public']);
  });
});
