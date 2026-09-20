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

function view(canEdit = true, publishesRealNames = true, published = true) {
  return render(
    <App>
      <TrackingSharePanel
        tripLogId="trip-1"
        tripTitle="Peștera Demo Mare"
        canEdit={canEdit}
        publishesRealNames={publishesRealNames}
        published={published}
      />
    </App>,
  );
}

beforeEach(() => {
  shares = { data: [], error: null };
  mint.mockReset();
  revoke.mockReset();
  mint.mockResolvedValue({
    id: SHARE,
    token: TOKEN,
    createdAt: '2026-09-14T10:00:00Z',
    expiresAt: '2026-10-01T10:00:00Z',
  });
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

  /**
   * That a publication ends by itself, said before the button is pressed.
   *
   * <b>The failure it answers is the one revoking cannot.</b> The block this panel hands out puts
   * the token into a club's own article, which is indexed and archived — so an address handed over
   * in March goes on being fetchable from a page nobody has edited since, and taking the link back
   * relies on somebody remembering that the article exists. The page ends on its own now, and the
   * person about to paste it is the one who has to know that.
   */
  it('says a link ends on its own, and says so before there is one', () => {
    view();

    expect(screen.getByTestId('trip-tracking-publish-ends')).toHaveTextContent(
      /stops working when the watch is closed/,
    );
  });

  /**
   * That the party is told, said to the person who is about to publish.
   *
   * The other half of the naming disclosure: an installation publishes real names by default, and
   * the people named are the rest of the club. They are now told at the moment a link is minted,
   * and whoever mints it should know that before pressing rather than be surprised by a reply.
   */
  it('says everybody on the trip is told, beside the sentence about names', () => {
    view();

    expect(screen.getByTestId('trip-tracking-publish-tells-party')).toHaveTextContent(
      /Everybody on this trip who has an account is told/,
    );
  });

  /**
   * When the address stops working, said at the one moment the address exists.
   *
   * The token is shown exactly once, so this is the only screen on which somebody can write
   * "this link works until …" beside the thing they are pasting into a website.
   */
  it('says when the minted address stops working, beside the address', async () => {
    view();

    fireEvent.click(screen.getByTestId('trip-tracking-publish-mint'));

    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-publish-minted-expires')).toHaveTextContent(
        /stops working on/,
      ),
    );
    // The positive twin of the date being there at all: it is the server's answer rather than a
    // constant, so it has to be the date this mint came back with.
    expect(screen.getByTestId('trip-tracking-publish-minted-expires')).toHaveTextContent(/2026/);
  });

  /**
   * A link that has run out is not listed as live, and one that has not is.
   *
   * Without the second half this passes against a panel that lists nothing at all. The two rows
   * differ in one field, so what is being read is the expiry and not the presence of a row.
   */
  it('lists a link that is still within its window and drops one that has run out', () => {
    shares = {
      data: [
        {
          id: SHARE,
          createdBy: 'user-1',
          createdAt: '2026-09-14T10:00:00Z',
          revokedAt: null,
          // Well past: this test must not start failing on a particular Tuesday.
          expiresAt: '2020-01-01T00:00:00Z',
        },
      ],
      error: null,
    };
    const lapsed = view();
    expect(screen.queryByTestId(`trip-tracking-publish-share-${SHARE}`)).toBeNull();
    expect(screen.getByText(/This trip is not published/)).toBeInTheDocument();
    lapsed.unmount();

    shares = {
      data: [
        {
          id: SHARE,
          createdBy: 'user-1',
          createdAt: '2026-09-14T10:00:00Z',
          revokedAt: null,
          expiresAt: '2099-01-01T00:00:00Z',
        },
      ],
      error: null,
    };
    view();
    expect(screen.getByTestId(`trip-tracking-publish-share-${SHARE}`)).toBeInTheDocument();
    expect(screen.getByTestId(`trip-tracking-publish-expires-${SHARE}`)).toHaveTextContent(
      /Works until/,
    );
  });

  /**
   * What the page will show is said before anybody presses the button, and again beside the
   * address once there is one.
   *
   * The people named are not the person pressing the button — they are the rest of the club — so a
   * link that puts their names in front of the internet must not be minted by somebody who was
   * never told it would. Both sentences are asserted together with the other setting's, because a
   * notice that is always on screen says nothing: only the pair proves this one is reading the
   * installation's answer rather than reciting a constant.
   */
  it('says the page will name people, before the link exists and beside it', async () => {
    view(true, true);

    expect(screen.getByTestId('trip-tracking-publish-names')).toHaveTextContent(
      /publishes real names/,
    );
    fireEvent.click(screen.getByTestId('trip-tracking-publish-mint'));

    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-publish-minted-names')).toHaveTextContent(
        /real names/,
      ),
    );
  });

  it('says the opposite where the installation has turned names off', async () => {
    view(true, false);

    expect(screen.getByTestId('trip-tracking-publish-names')).toHaveTextContent(
      /Names are not published here/,
    );
    expect(screen.getByTestId('trip-tracking-publish-names')).toHaveTextContent(/Caver 1/);
    fireEvent.click(screen.getByTestId('trip-tracking-publish-mint'));

    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-publish-minted-names')).toHaveTextContent(
        /not people's names/,
      ),
    );
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
      data: [
        {
          id: SHARE,
          createdBy: 'user-1',
          createdAt: '2026-09-14T10:00:00Z',
          revokedAt: null,
          expiresAt: '2026-10-01T10:00:00Z',
        },
      ],
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
          expiresAt: '2026-10-01T10:00:00Z',
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
      data: [
        {
          id: SHARE,
          createdBy: 'user-1',
          createdAt: '2026-09-14T10:00:00Z',
          revokedAt: null,
          expiresAt: '2026-10-01T10:00:00Z',
        },
      ],
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
        {
          id: OLD,
          createdBy: 'user-1',
          createdAt: '2026-09-13T10:00:00Z',
          revokedAt: null,
          expiresAt: '2026-10-01T10:00:00Z',
        },
        {
          id: SHARE,
          createdBy: 'user-1',
          createdAt: '2026-09-14T10:00:00Z',
          revokedAt: null,
          expiresAt: '2026-10-01T10:00:00Z',
        },
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

  /**
   * A link is not called "Live" when nothing is open, and is when something is.
   *
   * <b>The failure: the ordinary ending of a publication was the one this list could not see.</b>
   * A row carries when it was created, whether it was revoked and when it runs out — and a
   * publication also ends when the watch closes, which is how nearly every real one ends, and when
   * the trip's cave stops being publishable. So the panel that is an administrator's only account
   * of what is published went on showing a blue "Live" and "Works until <a fortnight away>" while
   * every follower got a 404, and disagreed with the banner in the same tab, which does consult
   * the watch and disappears.
   *
   * Both halves in one test, over the same row: what differs between them is the server's answer
   * and nothing else, so what is read here is that answer rather than the presence of a row.
   */
  it('does not call a link live when the trip is no longer published', () => {
    shares = {
      data: [
        {
          id: SHARE,
          createdBy: 'user-1',
          createdAt: '2026-09-14T10:00:00Z',
          revokedAt: null,
          // Inside its own window: the only thing that has ended this publication is the trip.
          expiresAt: '2099-01-01T00:00:00Z',
        },
      ],
      error: null,
    };

    const open = view(true, true, true);
    expect(screen.getByTestId(`trip-tracking-publish-status-${SHARE}`)).toHaveTextContent('Live');
    expect(screen.getByTestId(`trip-tracking-publish-expires-${SHARE}`)).toHaveTextContent(
      /Works until/,
    );
    open.unmount();

    view(true, true, false);
    expect(screen.getByTestId(`trip-tracking-publish-status-${SHARE}`)).not.toHaveTextContent(
      'Live',
    );
    expect(screen.getByTestId(`trip-tracking-publish-expires-${SHARE}`)).toHaveTextContent(
      /opens nothing at the moment/,
    );
    // Still listed, and still revocable. A closed watch can be armed again and a protection can be
    // lifted, so this is the link somebody may well want to take back before it answers again —
    // and a row that had been hidden could not be taken back at all.
    expect(screen.getByTestId(`trip-tracking-publish-revoke-${SHARE}`)).toBeInTheDocument();
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
