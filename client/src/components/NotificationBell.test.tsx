// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';

/**
 * The badge is exercised against a real query cache and a real invalidation rather than against a
 * stubbed hook. Marking everything read has to move the number, and the only thing that makes it
 * move is that the mutation invalidates a prefix the count's key actually sits under — a mistake
 * that a mocked hook returning whatever the test wants would hide completely.
 */
const get = vi.fn();
const post = vi.fn();

vi.mock('../api/client.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client.ts')>()),
  api: { GET: get, POST: post },
}));

const { useMarkAllNotificationsRead, useMarkNotificationRead } = await import('../api/hooks.ts');
const { default: NotificationBell } = await import('./NotificationBell.tsx');

function answer<T>(data: T) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) });
}

/** The bell, plus both acts that are supposed to move it, sharing one cache. */
function Header() {
  const markAllRead = useMarkAllNotificationsRead();
  const markRead = useMarkNotificationRead();
  return (
    <>
      <NotificationBell />
      <button type="button" onClick={() => markAllRead.mutate()}>
        mark all read
      </button>
      <button type="button" onClick={() => markRead.mutate(41)}>
        mark one read
      </button>
    </>
  );
}

function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
  return render(<Header />, { wrapper });
}

/**
 * What the badge is showing, or nothing if it is showing nothing.
 *
 * Not simply whether the element is there: antd keeps it in the document through a leave
 * animation that never finishes where there are no transitions, and marks it `data-show="false"`
 * for the duration. A test that asked only whether the node existed would report a cleared badge
 * as still showing its old number for ever.
 */
function badgeShows(): string | null {
  const sup = document.querySelector('.ant-badge-count');
  if (sup === null || sup.getAttribute('data-show') === 'false') return null;
  return sup.getAttribute('title');
}

beforeEach(() => {
  get.mockReset();
  post.mockReset();
  post.mockImplementation(() => Promise.resolve({ response: new Response(null, { status: 204 }) }));
});
afterEach(cleanup);

describe('the notification bell', () => {
  it('shows how many are waiting', async () => {
    get.mockImplementation(() => answer({ unread: 3 }));
    show();

    await waitFor(() => expect(badgeShows()).toBe('3'));
    expect(get).toHaveBeenCalledWith('/api/v1/notifications/unread-count');
  });

  it('draws no badge at all when there is nothing waiting', async () => {
    get.mockImplementation(() => answer({ unread: 0 }));
    show();

    // Waiting on the answer rather than on the absence, so this cannot pass by looking before
    // the count has arrived: an empty inbox must look like a plain bell, not like a bell
    // reporting a zero, and it must still look like that once the number is known.
    await waitFor(() => expect(get).toHaveBeenCalled());
    await waitFor(() => expect(badgeShows()).toBeNull());
    expect(screen.queryByText('0')).toBeNull();
  });

  it('clears the moment everything is marked read, without waiting out the interval', async () => {
    get.mockImplementation(() => answer({ unread: 2 }));
    show();
    await waitFor(() => expect(badgeShows()).toBe('2'));

    // What the server would answer next: the act emptied the inbox.
    get.mockImplementation(() => answer({ unread: 0 }));
    fireEvent.click(screen.getByRole('button', { name: 'mark all read' }));

    await waitFor(() => expect(post).toHaveBeenCalledWith('/api/v1/notifications/read-all'));
    await waitFor(() => expect(badgeShows()).toBeNull());
  });

  it('counts down when a single line is opened, by the same prefix', async () => {
    get.mockImplementation(() => answer({ unread: 2 }));
    show();
    await waitFor(() => expect(badgeShows()).toBe('2'));

    get.mockImplementation(() => answer({ unread: 1 }));
    fireEvent.click(screen.getByRole('button', { name: 'mark one read' }));

    // The other path to the same number: opening one line on the inbox page has to move the
    // header too, and it is a different mutation reaching the count through the shared root.
    await waitFor(() =>
      expect(post).toHaveBeenCalledWith('/api/v1/notifications/{id}/read', {
        params: { path: { id: 41 } },
      }),
    );
    await waitFor(() => expect(badgeShows()).toBe('1'));
  });

  it('offers the way to the inbox rather than an opinion about it', async () => {
    get.mockImplementation(() => answer({ unread: 5 }));
    show();

    // A count and a destination is the whole of it: no wording that asks to be acted on, and
    // nothing that opens by itself. A permanently non-zero bell is an expected state for somebody
    // who reads everything by email, so it must not read as an unfinished chore.
    const bell = await screen.findByRole('button', { name: 'Notifications' });
    expect(bell).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
