// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CalendarFeedList } from '../../api/hooks.ts';

/**
 * The card that hands out calendar subscription addresses.
 *
 * What is under test is the shape the credential forces on the screen: an address is shown once,
 * from the mint answer, and the list the server keeps never carries one; the card is absent while
 * the installation offers no feeds; and a withdrawn address keeps its row but loses its control.
 *
 * Every address below is invented.
 */

const mintMutate = vi.fn();
const revokeMutate = vi.fn();
let list: CalendarFeedList = { enabled: true, feeds: [] };

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useCalendarFeeds: () => ({ data: list, isPending: false }),
    useMintCalendarFeed: () => ({ mutateAsync: mintMutate, isPending: false }),
    useRevokeCalendarFeed: () => ({ mutateAsync: revokeMutate, isPending: false }),
  };
});

const { default: CalendarFeedCard } = await import('./CalendarFeedCard.tsx');

const mintedUrl = 'http://localhost:8080/api/v1/calendar/feed/not-a-real-token.ics';

function show() {
  return render(
    <App>
      <CalendarFeedCard />
    </App>,
  );
}

beforeEach(() => {
  mintMutate.mockReset();
  revokeMutate.mockReset();
  list = { enabled: true, feeds: [] };
});

afterEach(cleanup);

describe('the calendar feed card', () => {
  it('is absent while the installation offers no feeds, even when addresses were minted before', () => {
    // The gate was switched off after an address was handed out: the row still exists on the
    // server, but a card that lists it would invite a revoke on something already dead and a
    // mint that can only be refused.
    list = {
      enabled: false,
      feeds: [{ id: 'f1', label: 'Phone', createdAt: '2026-10-01T10:00:00Z', revokedAt: null }],
    };

    show();

    expect(screen.queryByText('Calendar feed')).toBeNull();
    expect(screen.queryByText('Phone')).toBeNull();
  });

  it('shows the minted address once, from the mint answer, and never from the list', async () => {
    mintMutate.mockResolvedValue({
      id: 'f2',
      label: 'Phone',
      url: mintedUrl,
      createdAt: '2026-10-05T10:00:00Z',
    });

    show();

    expect(screen.queryByText(mintedUrl)).toBeNull();
    fireEvent.change(screen.getByLabelText('Name for this address'), { target: { value: 'Phone' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create a feed address' }));

    expect(await screen.findByText(mintedUrl)).toBeTruthy();
    expect(mintMutate).toHaveBeenCalledWith('Phone');
    // The one-time warning stands beside the address, not in a toast that would already be gone.
    expect(screen.getByText(/shown only once/)).toBeTruthy();
  });

  it('sends no label when the name is left blank, so the server stores null rather than an empty string', async () => {
    mintMutate.mockResolvedValue({ id: 'f3', label: null, url: mintedUrl, createdAt: '2026-10-05T10:00:00Z' });

    show();
    fireEvent.click(screen.getByRole('button', { name: 'Create a feed address' }));

    await waitFor(() => expect(mintMutate).toHaveBeenCalledWith(null));
  });

  it('lists an address by its name and date, offers a revoke on a live one and none on a withdrawn one', () => {
    list = {
      enabled: true,
      feeds: [
        { id: 'live', label: 'Phone', createdAt: '2026-10-01T10:00:00Z', revokedAt: null },
        { id: 'gone', label: null, createdAt: '2026-09-01T10:00:00Z', revokedAt: '2026-09-02T10:00:00Z' },
      ],
    };

    show();

    expect(screen.getByText('Phone')).toBeTruthy();
    expect(screen.getByText('Unnamed address')).toBeTruthy();
    expect(screen.getByText('Active')).toBeTruthy();
    expect(screen.getByText('Revoked')).toBeTruthy();
    // One control for two rows: the withdrawn address has nothing left to withdraw.
    expect(screen.getAllByRole('button', { name: 'Revoke' })).toHaveLength(1);
    // Nothing in the list is an address: the server lists metadata only, and the card adds none.
    expect(screen.queryByText(/calendar\/feed\//)).toBeNull();
  });

  it('withdraws an address after the confirmation', async () => {
    revokeMutate.mockResolvedValue(undefined);
    list = {
      enabled: true,
      feeds: [{ id: 'live', label: 'Phone', createdAt: '2026-10-01T10:00:00Z', revokedAt: null }],
    };

    show();
    fireEvent.click(screen.getByRole('button', { name: 'Revoke' }));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(revokeMutate).toHaveBeenCalledWith('live'));
  });
});
