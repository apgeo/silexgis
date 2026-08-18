// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ActivityState } from '../../api/hooks.ts';

const move = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useMoveTripLog: () => ({ mutateAsync: move, isPending: false, variables: undefined }),
}));

const { default: TripStateControl } = await import('./TripStateControl.tsx');

function show(state: ActivityState, canEdit = true) {
  return render(
    <App>
      <TripStateControl tripId="trip-1" state={state} canEdit={canEdit} />
    </App>,
  );
}

// Anchored at the end: an antd icon labels itself, so each button's accessible name carries
// the icon's own word ahead of the button's.
const publishButton = () => screen.queryByRole('button', { name: /Publish$/ });
const draftButton = () => screen.queryByRole('button', { name: /Back to draft$/ });
const doneButton = () => screen.queryByRole('button', { name: /Mark as done$/ });
const callOffButton = () => screen.queryByRole('button', { name: /Call it off$/ });

beforeEach(() => {
  move.mockReset().mockResolvedValue({});
});

afterEach(cleanup);

describe('TripStateControl', () => {
  it('offers nothing to somebody who may not write the trip', () => {
    // The server refuses regardless; showing a control that always fails is the thing
    // being avoided here, not the enforcement.
    show('draft', false);

    expect(publishButton()).toBeNull();
    expect(draftButton()).toBeNull();
    expect(doneButton()).toBeNull();
    expect(callOffButton()).toBeNull();
  });

  it('offers every move a draft may make, and each names the state it moves to', () => {
    // Recording a trip as having happened and calling one off were legal the whole time and
    // reachable through no button at all, because two named verbs can only ever offer two moves.
    show('draft');

    expect(publishButton()).not.toBeNull();
    expect(doneButton()).not.toBeNull();
    expect(callOffButton()).not.toBeNull();
    expect(draftButton()).toBeNull();
  });

  it('offers the reverse once a trip has gone out, and nothing that would end it from there', () => {
    show('published');

    expect(draftButton()).not.toBeNull();
    expect(publishButton()).toBeNull();
    expect(doneButton()).toBeNull();
    // De-announcing a trip and declaring it never happened are two decisions; neither is
    // offered as a shortcut to the other.
    expect(callOffButton()).toBeNull();
  });

  it('offers a finished trip its announcement and the way back to the workshop', () => {
    show('done');

    expect(publishButton()).not.toBeNull();
    expect(draftButton()).not.toBeNull();
    expect(callOffButton()).toBeNull();
  });

  it('lets a cancelled trip be taken back to draft but not announced from there', () => {
    show('cancelled');

    expect(draftButton()).not.toBeNull();
    expect(publishButton()).toBeNull();
    expect(doneButton()).toBeNull();
  });

  it('offers nothing for a state a trip cannot hold, rather than a button that always fails', () => {
    // The vocabulary is shared with activities that plan, so a value outside the trip's own
    // table can be handed to this control before anything here knows what to do with it.
    show('planned');

    expect(screen.queryAllByRole('button')).toHaveLength(0);
  });

  it('confirms before publishing, because the notification cannot be recalled', async () => {
    show('draft');

    fireEvent.click(publishButton()!);
    expect(move).not.toHaveBeenCalled();

    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await vi.waitFor(() => expect(move).toHaveBeenCalledWith({ id: 'trip-1', state: 'published' }));
  });

  it('confirms before calling a trip off, because it reads as an ending', async () => {
    show('draft');

    fireEvent.click(callOffButton()!);
    expect(move).not.toHaveBeenCalled();

    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await vi.waitFor(() => expect(move).toHaveBeenCalledWith({ id: 'trip-1', state: 'cancelled' }));
  });

  it('takes a trip back without a confirmation, since nothing is sent and nothing is lost', async () => {
    show('published');

    fireEvent.click(draftButton()!);

    await vi.waitFor(() => expect(move).toHaveBeenCalledWith({ id: 'trip-1', state: 'draft' }));
  });

  it('reports a refusal rather than leaving the state looking changed', async () => {
    move.mockRejectedValue(new Error('conflict'));
    show('published');

    fireEvent.click(draftButton()!);

    await vi.waitFor(() =>
      expect(screen.getByText('The operation failed. Please try again.')).toBeInTheDocument(),
    );
  });
});
