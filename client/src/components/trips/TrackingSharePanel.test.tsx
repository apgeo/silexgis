// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { fireEvent } from '@testing-library/dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripTrackingShare } from '../../api/hooks.ts';

const SHARE = '11111111-1111-1111-1111-111111111111';
const TOKEN = 'abcDEF-123_xyz';

const mint = vi.fn();
const revoke = vi.fn();
let shares: { data?: TripTrackingShare[]; error: unknown } = { data: [], error: null };

vi.mock('../../api/hooks.ts', () => ({
  useTripTrackingShares: () => shares,
  useMintTripTrackingShare: () => ({ mutateAsync: mint, isPending: false }),
  useRevokeTripTrackingShare: () => ({ mutateAsync: revoke, isPending: false }),
}));

vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => false }));

const { default: TrackingSharePanel } = await import('./TrackingSharePanel.tsx');

function view(canEdit = true) {
  return render(
    <App>
      <TrackingSharePanel tripLogId="trip-1" tripTitle="Peștera Demo Mare" canEdit={canEdit} />
    </App>,
  );
}

beforeEach(() => {
  shares = { data: [], error: null };
  mint.mockReset();
  revoke.mockReset();
  mint.mockResolvedValue({ id: SHARE, token: TOKEN, createdAt: '2026-09-14T10:00:00Z' });
  revoke.mockResolvedValue(undefined);
});

afterEach(cleanup);

describe('publishing a tracked trip', () => {
  it('is offered to nobody who cannot write the trip', () => {
    view(false);

    expect(screen.queryByTestId('trip-tracking-publish')).toBeNull();
    expect(screen.queryByTestId('trip-tracking-publish-mint')).toBeNull();
  });

  it('says plainly that an unpublished trip cannot be followed', () => {
    view();

    expect(screen.getByText(/This trip is not published/)).toBeInTheDocument();
  });

  it('shows the address and the website block together, once, when a link is minted', async () => {
    // Forced rather than chosen: the token exists in exactly one response and is stored only as a
    // hash, so there is no later moment at which either could be produced.
    view();
    fireEvent.click(screen.getByTestId('trip-tracking-publish-mint'));

    await waitFor(() => expect(screen.getByTestId('trip-tracking-publish-minted')).toBeVisible());
    expect(screen.getByText(/only time it is shown/)).toBeInTheDocument();

    const link = screen.getByTestId('trip-tracking-publish-link') as HTMLInputElement;
    expect(link.value).toBe(`${window.location.origin}/shared/trips/${TOKEN}`);

    const embed = screen.getByTestId('trip-tracking-publish-snippet') as HTMLTextAreaElement;
    expect(embed.value).toContain(`/shared/trips/${TOKEN}/embed`);
    expect(embed.value).toContain('aspect-ratio:');
  });

  it('captions the frame with the trip, since it will sit on somebody else’s page', () => {
    view();
    fireEvent.click(screen.getByTestId('trip-tracking-publish-mint'));

    return waitFor(() => {
      const embed = screen.getByTestId('trip-tracking-publish-snippet') as HTMLTextAreaElement;
      expect(embed.value).toContain('title="Peștera Demo Mare"');
    });
  });

  it('lists a live link without its token, because the list is answered without one', () => {
    shares = {
      data: [{ id: SHARE, createdBy: 'user-1', createdAt: '2026-09-14T10:00:00Z', revokedAt: null }],
      error: null,
    };
    view();

    expect(screen.getByTestId(`trip-tracking-publish-share-${SHARE}`)).toBeInTheDocument();
    expect(screen.getByTestId('trip-tracking-publish-list').textContent).not.toContain(TOKEN);
  });

  it('leaves a link somebody already took back off the list', () => {
    shares = {
      data: [
        {
          id: SHARE,
          createdBy: 'user-1',
          createdAt: '2026-09-14T10:00:00Z',
          revokedAt: '2026-09-14T11:00:00Z',
        },
      ],
      error: null,
    };
    view();

    expect(screen.queryByTestId(`trip-tracking-publish-share-${SHARE}`)).toBeNull();
    expect(screen.getByText(/This trip is not published/)).toBeInTheDocument();
  });

  it('takes a link back, and stops showing an address that now opens nothing', async () => {
    shares = {
      data: [{ id: SHARE, createdBy: 'user-1', createdAt: '2026-09-14T10:00:00Z', revokedAt: null }],
      error: null,
    };
    view();
    fireEvent.click(screen.getByTestId('trip-tracking-publish-mint'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-publish-link')).toBeVisible());

    fireEvent.click(screen.getByTestId(`trip-tracking-publish-revoke-${SHARE}`));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() =>
      expect(revoke).toHaveBeenCalledWith({ tripLogId: 'trip-1', shareId: SHARE }),
    );
    await waitFor(() => expect(screen.queryByTestId('trip-tracking-publish-link')).toBeNull());
  });

  it('keeps a link it just minted when a different one is taken back', async () => {
    // Rotation is what the panel invites: mint the new link, hand it out, then take the old one
    // back. Clearing the address on any revoke destroys the new token before it has been pasted
    // anywhere — the server keeps only a hash, so nothing can produce it again and the whole
    // exercise has to be repeated with a third link.
    const OLD = '99999999-9999-9999-9999-999999999999';
    shares = {
      data: [
        { id: OLD, createdBy: 'user-1', createdAt: '2026-09-13T10:00:00Z', revokedAt: null },
        { id: SHARE, createdBy: 'user-1', createdAt: '2026-09-14T10:00:00Z', revokedAt: null },
      ],
      error: null,
    };
    view();
    fireEvent.click(screen.getByTestId('trip-tracking-publish-mint'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-publish-link')).toBeVisible());

    fireEvent.click(screen.getByTestId(`trip-tracking-publish-revoke-${OLD}`));
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await waitFor(() => expect(revoke).toHaveBeenCalledWith({ tripLogId: 'trip-1', shareId: OLD }));
    const link = screen.getByTestId('trip-tracking-publish-link') as HTMLInputElement;
    expect(link.value).toBe(`${window.location.origin}/shared/trips/${TOKEN}`);
    expect(screen.getByTestId('trip-tracking-publish-snippet')).toBeVisible();
  });

  it('says which refusal a mint met, in words somebody can act on', async () => {
    const { ApiError } = await import('../../api/client.ts');
    mint.mockRejectedValue(new ApiError(409, 'tracking.publication_refused_protected'));
    view();

    fireEvent.click(screen.getByTestId('trip-tracking-publish-mint'));

    expect(await screen.findByText(/protected coordinates/)).toBeInTheDocument();
    expect(screen.queryByTestId('trip-tracking-publish-minted')).toBeNull();
  });

  it('does not draw a failed read of the list as a trip nobody published', async () => {
    shares = { data: undefined, error: new Error('nope') };
    view();

    expect(screen.getByText(/could not be read/)).toBeInTheDocument();
    expect(screen.queryByText(/This trip is not published/)).toBeNull();
  });
});
