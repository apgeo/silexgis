// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';

const detach = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useDetachTrackingPicture: () => ({ mutateAsync: detach, isPending: false }),
}));

const { default: TrackingMomentPictures } = await import('./TrackingMomentPictures.tsx');

function show() {
  return render(
    <App>
      <TrackingMomentPictures
        tripLogId="trip-1"
        pictures={[
          {
            at: Date.parse('2026-09-12T09:05:00Z'),
            caverId: null,
            documentId: 'doc-1',
            memberId: 'member-1',
            entry: { url: '/files/doc-1', caption: 'Pitch head' },
          },
        ]}
        loading={false}
        failed={false}
        canEdit
        nameOf={(caverId) => caverId}
        onAttach={vi.fn()}
      />
    </App>,
  );
}

/** Presses the photograph's own button, then the confirmation it opens. */
async function takeOff() {
  await act(async () => {
    fireEvent.click(screen.getByTestId('trip-tracking-picture-detach-member-1'));
  });
  // The confirmation's button is found inside its own bubble: the row button it belongs to has a
  // name too, and a search of the whole page for "OK" would be a search for whichever came first.
  const bubble = await screen.findByRole('tooltip');
  await act(async () => {
    fireEvent.click(within(bubble).getByRole('button', { name: 'OK' }));
  });
}

// In braces on purpose: a hook that returns a function has it run as that test's clean-up, and
// the mock is a function.
beforeEach(() => {
  detach.mockReset();
});
afterEach(cleanup);

describe('TrackingMomentPictures', () => {
  it('says a photograph somebody else already took off is gone, rather than that taking it off failed', async () => {
    detach.mockRejectedValue(new ApiError(404, 'tracking.picture_not_found'));
    show();
    await takeOff();

    expect(detach).toHaveBeenCalledWith({ tripLogId: 'trip-1', memberId: 'member-1' });
    expect(await screen.findByText(/no longer on this moment/)).toBeInTheDocument();
    expect(screen.queryByText('The photograph could not be taken off the moment.')).toBeNull();
  });

  it('keeps its general sentence for a failure that names nothing it has words for', async () => {
    detach.mockRejectedValue(new TypeError('Failed to fetch'));
    show();
    await takeOff();

    expect(await screen.findByText('The photograph could not be taken off the moment.')).toBeInTheDocument();
    expect(screen.queryByText(/no longer on this moment/)).toBeNull();
  });
});
