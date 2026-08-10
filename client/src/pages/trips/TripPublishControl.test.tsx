// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ActivityState } from '../../api/hooks.ts';

const publish = vi.fn();
const unpublish = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  usePublishTripLog: () => ({ mutateAsync: publish, isPending: false }),
  useUnpublishTripLog: () => ({ mutateAsync: unpublish, isPending: false }),
}));

const { default: TripPublishControl } = await import('./TripPublishControl.tsx');

function show(state: ActivityState, canEdit = true) {
  return render(
    <App>
      <TripPublishControl tripId="trip-1" state={state} canEdit={canEdit} />
    </App>,
  );
}

// Anchored at the end: an antd icon labels itself, so each button's accessible name carries
// the icon's own word ahead of the button's.
const publishButton = () => screen.queryByRole('button', { name: /Publish$/ });
const draftButton = () => screen.queryByRole('button', { name: /Back to draft$/ });

beforeEach(() => {
  publish.mockReset().mockResolvedValue({});
  unpublish.mockReset().mockResolvedValue({});
});

afterEach(cleanup);

describe('TripPublishControl', () => {
  it('offers nothing to somebody who may not write the trip', () => {
    // The server refuses regardless; showing a control that always fails is the thing
    // being avoided here, not the enforcement.
    show('draft', false);

    expect(publishButton()).toBeNull();
    expect(draftButton()).toBeNull();
  });

  it('offers publishing for a trip that has not gone out, and the reverse once it has', () => {
    const { unmount } = show('draft');
    expect(publishButton()).not.toBeNull();
    expect(draftButton()).toBeNull();
    unmount();

    show('published');
    expect(publishButton()).toBeNull();
    expect(draftButton()).not.toBeNull();
  });

  it('lets a cancelled trip be taken back to draft but not announced from there', () => {
    // Reinstating goes through the workshop: de-announcing a trip and declaring it never
    // happened are two decisions, so neither is offered as a shortcut to the other.
    show('cancelled');

    expect(publishButton()).toBeNull();
    expect(draftButton()).not.toBeNull();
  });

  it('confirms before publishing, because the notification cannot be recalled', async () => {
    show('draft');

    fireEvent.click(publishButton()!);
    expect(publish).not.toHaveBeenCalled();

    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await vi.waitFor(() => expect(publish).toHaveBeenCalledWith('trip-1'));
  });

  it('takes a trip back without a confirmation, since nothing is sent and nothing is lost', async () => {
    show('published');

    fireEvent.click(draftButton()!);

    await vi.waitFor(() => expect(unpublish).toHaveBeenCalledWith('trip-1'));
  });

  it('reports a refusal rather than leaving the state looking changed', async () => {
    unpublish.mockRejectedValue(new Error('conflict'));
    show('published');

    fireEvent.click(draftButton()!);

    await vi.waitFor(() =>
      expect(screen.getByText('The operation failed. Please try again.')).toBeInTheDocument(),
    );
  });
});
