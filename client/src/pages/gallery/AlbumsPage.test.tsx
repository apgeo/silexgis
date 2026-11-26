// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App } from 'antd';
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { ApiError } from '../../api/client.ts';
import { DOCUMENT_VISIBILITIES } from '../../components/attachments/visibilityChoices.ts';
import AlbumsPage from './AlbumsPage.tsx';
import { afterRejectionsAreReported } from '../../testEventLoop.ts';

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

function album(id: string, title: string, hasActiveShare = false, canShare = true) {
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
    canShare,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };
}

function OpenedAlbum() {
  return <div data-testid="opened-album">{useParams().id}</div>;
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/albums']}>
      <App>
        <Routes>
          <Route path="/albums" element={<AlbumsPage />} />
          <Route path="/albums/:id" element={<OpenedAlbum />} />
        </Routes>
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
  it('says an album with no photographs is empty, where its cover would be', () => {
    state.albums = {
      data: { items: [{ ...album('a1', 'Nothing yet'), photoCount: 0 }, { ...album('a2', 'Some'), photoCount: 4 }] },
      isPending: false,
      isError: false,
      refetch: vi.fn(),
    };
    renderPage();
    const empty = screen.getByText('Nothing yet').closest('.ant-card') as HTMLElement;
    expect(within(empty).getByTestId('album-cover-placeholder')).toHaveTextContent('No photographs yet');
    const some = screen.getByText('Some').closest('.ant-card') as HTMLElement;
    expect(within(some).getByTestId('album-cover-placeholder')).not.toHaveTextContent('No photographs yet');
  });

  it('opens the album it has just made, and says it was made', async () => {
    state.create.mockResolvedValue({ ...album('a9', 'New one'), id: 'a9' });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /New album/ }));
    fireEvent.change(await screen.findByLabelText('Title'), { target: { value: 'New one' } });
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));

    expect(await screen.findByTestId('opened-album')).toHaveTextContent('a9');
    expect(await screen.findByText('Album created.')).toBeInTheDocument();
  });

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
    expect(within(shared).getByRole('button', { name: 'Stop sharing' })).toBeTruthy();
  });

  it('says when deleting an album failed, instead of doing nothing', async () => {
    state.deleteAlbum.mockRejectedValue(new Error('API error 404'));
    renderPage();
    const card = screen.getByText('Pestera 1 survey').closest('.ant-card') as HTMLElement;

    fireEvent.click(within(card).getByRole('button', { name: 'Delete album' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText('The deletion failed. Please try again.')).toBeTruthy();
    await afterRejectionsAreReported();
    expect(unhandled).not.toHaveBeenCalled();
  });

  it.each([
    ['confirms a revoked link', true, en.gallery.shareRevoked],
    ['says when revoking a link failed', false, 'The operation failed. Please try again.'],
  ])('%s', async (_name, succeeds, said) => {
    if (succeeds) {
      state.revoke.mockResolvedValue(undefined);
    } else {
      state.revoke.mockRejectedValue(new Error('API error 500'));
    }
    renderPage();
    const shared = screen.getByText('Camp week').closest('.ant-card') as HTMLElement;

    fireEvent.click(within(shared).getByRole('button', { name: 'Stop sharing' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    expect(await screen.findByText(said)).toBeTruthy();
    expect(state.revoke).toHaveBeenCalledWith('a2');
    await afterRejectionsAreReported();
    expect(unhandled).not.toHaveBeenCalled();
  });

  /** Both cards' action buttons, by name, as drawn for the capability string and albums given. */
  function cardActions(documents: string, canShare = true) {
    cleanup();
    state.documents = documents;
    state.albums = {
      ...state.albums,
      data: { items: [album('a1', 'Pestera 1 survey', false, canShare), album('a2', 'Camp week', true, canShare)] },
    };
    renderPage();
    const card = screen.getByText('Pestera 1 survey').closest('.ant-card') as HTMLElement;
    const shared = screen.getByText('Camp week').closest('.ant-card') as HTMLElement;
    const names = (element: HTMLElement) =>
      within(element).queryAllByRole('button').map((button) => button.getAttribute('aria-label'));
    return { card: names(card), shared: names(shared) };
  }

  it('offers the share-link actions on the album\'s own share right, not on writing documents', () => {
    // The allowed caller first, in the same test: were the actions drawn for nobody, the
    // absences below would prove nothing about the rule they are meant to test.
    const writer = cardActions('read,write,share', true);
    expect(writer.card).toEqual(expect.arrayContaining(['Edit album', 'Make a share link', 'Delete album']));
    expect(writer.shared).toContain('Stop sharing');

    // A writer of documents the server says may not share this album gets neither link action:
    // minting and revoking both take the Share right on the album itself.
    const writerWithoutShare = cardActions('read,write,share', false);
    expect(writerWithoutShare.card).toEqual(expect.arrayContaining(['Edit album', 'Delete album']));
    expect(writerWithoutShare.card).not.toContain('Make a share link');
    expect(writerWithoutShare.shared).not.toContain('Stop sharing');
    expect(writerWithoutShare.shared).not.toContain('Make a share link');
  });

  it('lets whoever holds the album\'s share right mint and revoke its link, and nothing else', () => {
    const sharer = cardActions('read', true);
    expect(sharer.card).toEqual(['Make a share link']);
    expect(sharer.shared).toEqual(['Stop sharing']);

    const reader = cardActions('read', false);
    expect(reader.card).toEqual([]);
    expect(reader.shared).toEqual([]);
  });

  it('says that sharing an album takes the share right when the server refuses the link', async () => {
    state.share.mockResolvedValueOnce({ token: 'tok' });
    renderPage();
    const card = () => screen.getByText('Pestera 1 survey').closest('.ant-card') as HTMLElement;
    fireEvent.click(within(card()).getByRole('button', { name: 'Make a share link' }));
    expect(await screen.findByDisplayValue(/\/shared\/albums\/tok$/)).toBeTruthy();

    state.share.mockRejectedValueOnce(new ApiError(403, 'album.share_forbidden'));
    fireEvent.click(within(card()).getByRole('button', { name: 'Make a share link' }));
    expect(await screen.findByText(en.gallery.shareForbidden)).toBeTruthy();
    expect(ro.gallery.shareForbidden).toBeTruthy();
    expect(ro.gallery.shareForbidden).not.toBe(en.gallery.shareForbidden);
    await afterRejectionsAreReported();
    expect(unhandled).not.toHaveBeenCalled();
  });

  it('says in words that an album is shared, and stops sharing with a control that does not look like sharing', () => {
    renderPage();
    const shared = screen.getByText('Camp week').closest('.ant-card') as HTMLElement;
    const unshared = screen.getByText('Pestera 1 survey').closest('.ant-card') as HTMLElement;
    expect(within(shared).getByText(en.gallery.sharedByLink)).toBeTruthy();
    expect(within(unshared).queryByText(en.gallery.sharedByLink)).toBeNull();
    expect(ro.gallery.sharedByLink).not.toBe(en.gallery.sharedByLink);

    // The same glyph used to mean both "make a link" and "take it back"; the two must differ.
    const stop = within(shared).getByRole('button', { name: en.gallery.revokeShare });
    const make = within(unshared).getByRole('button', { name: en.gallery.shareAlbum });
    expect(stop.querySelector('.anticon-share-alt')).toBeNull();
    expect(make.querySelector('.anticon-share-alt')).not.toBeNull();
  });

  it('offers a Copy button beside the one-time link and says the link was copied', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    state.share.mockResolvedValueOnce({ token: 'tok' });
    renderPage();
    const card = screen.getByText('Pestera 1 survey').closest('.ant-card') as HTMLElement;
    fireEvent.click(within(card).getByRole('button', { name: en.gallery.shareAlbum }));
    fireEvent.click(await screen.findByRole('button', { name: en.gallery.copyLink }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(expect.stringMatching(/\/shared\/albums\/tok$/)));
    expect(await screen.findByText(en.gallery.linkCopied)).toBeTruthy();
  });

  it('explains a lost link without the word "mint"', () => {
    expect(en.gallery.shareOnce).not.toMatch(/\bmint/i);
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

describe('leaving the album dialog with typed work', () => {
  it('closes at once when nothing was typed', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /New album/ }));
    await screen.findByRole('dialog');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByLabelText('Title')).toBeNull());
    expect(screen.queryByText('Discard your changes?')).toBeNull();
  });

  it('asks before throwing a typed title away, and a click beside it does not close it', async () => {
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /New album/ }));
    const title = await screen.findByLabelText('Title');
    fireEvent.change(title, { target: { value: 'Winter camp' } });

    const wrap = document.querySelector('.ant-modal-wrap') as HTMLElement;
    fireEvent.mouseDown(wrap);
    fireEvent.mouseUp(wrap);
    fireEvent.click(wrap);
    expect(screen.getByLabelText('Title')).toHaveValue('Winter camp');

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect((await screen.findAllByText('Discard your changes?')).length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Title')).toHaveValue('Winter camp');
  });
});
