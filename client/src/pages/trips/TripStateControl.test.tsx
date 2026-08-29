// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ActivityState, Visibility } from '../../api/hooks.ts';

const move = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useMoveTripLog: () => ({ mutateAsync: move, isPending: false, variables: undefined }),
}));

const { default: TripStateControl } = await import('./TripStateControl.tsx');

function show(state: ActivityState, canEdit = true, visibility: Visibility = 'private') {
  return render(
    <App>
      <TripStateControl tripId="trip-1" state={state} visibility={visibility} canEdit={canEdit} />
    </App>,
  );
}

// Anchored at the end: an antd icon labels itself, so each button's accessible name carries
// the icon's own word ahead of the button's.
const publishButton = () => screen.queryByRole('button', { name: /Publish$/ });
const draftButton = () => screen.queryByRole('button', { name: /Back to draft$/ });
const doneButton = () => screen.queryByRole('button', { name: /Mark as done$/ });
const callOffButton = () => screen.queryByRole('button', { name: /Call it off$/ });
const floatButton = () => screen.queryByRole('button', { name: /Float it$/ });
const organiseButton = () => screen.queryByRole('button', { name: /Start organising$/ });
const goingAheadButton = () => screen.queryByRole('button', { name: /It is going ahead$/ });
const putBackButton = () => screen.queryByRole('button', { name: /Put it back$/ });
const newDateButton = () => screen.queryByRole('button', { name: /Settle a new date$/ });

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

  it('offers a draft both ways out: the ladder a plan climbs and the record of one already run', () => {
    show('draft');

    expect(floatButton()).not.toBeNull();
    expect(organiseButton()).not.toBeNull();
    expect(doneButton()).not.toBeNull();
    expect(publishButton()).not.toBeNull();
  });

  it('offers a floated idea the next rung, the way back and the way out', () => {
    // Before the planning states were admitted this rendered no control at all, so a trip that
    // reached one could be neither advanced nor abandoned from the page it was shown on.
    show('proposed');

    expect(organiseButton()).not.toBeNull();
    expect(draftButton()).not.toBeNull();
    expect(callOffButton()).not.toBeNull();
    // One rung at a time: being organised does not imply going ahead.
    expect(goingAheadButton()).toBeNull();
  });

  it('offers a trip being organised its confirmation, a postponement and both ways out', () => {
    show('planned');

    expect(goingAheadButton()).not.toBeNull();
    expect(putBackButton()).not.toBeNull();
    expect(draftButton()).not.toBeNull();
    expect(callOffButton()).not.toBeNull();
  });

  it('lets a confirmed trip be recorded, put back or called off the night before', () => {
    show('confirmed');

    expect(doneButton()).not.toBeNull();
    expect(putBackButton()).not.toBeNull();
    expect(callOffButton()).not.toBeNull();
    // Announcing is what a finished trip does, not a trip that has not happened yet.
    expect(publishButton()).toBeNull();
  });

  it('sends a postponed trip back to being organised rather than straight to going ahead', async () => {
    // The other state that rendered nothing at all. A new date has to be settled before
    // anybody is told the trip is on again.
    show('delayed');

    expect(newDateButton()).not.toBeNull();
    expect(goingAheadButton()).toBeNull();
    expect(draftButton()).not.toBeNull();

    fireEvent.click(newDateButton()!);
    await vi.waitFor(() => expect(move).toHaveBeenCalledWith({ id: 'trip-1', state: 'planned' }));
  });

  it('offers nothing for a value the trip table says nothing about', () => {
    // The enum is shared with activities that are not trips, so a value the trip's table says
    // nothing about can still be handed to this control.
    show('nonsense' as ActivityState);

    expect(screen.queryAllByRole('button')).toHaveLength(0);
  });

  it('confirms before publishing, because the notification cannot be recalled', async () => {
    show('draft');

    fireEvent.click(publishButton()!);
    expect(move).not.toHaveBeenCalled();

    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await vi.waitFor(() => expect(move).toHaveBeenCalledWith({ id: 'trip-1', state: 'published' }));
  });

  it('names who can read the trip in the question, and says announcing does not change it', async () => {
    // A plan is readable by the author's club from the moment it is made, and somebody about to
    // announce one is entitled to know that before the announcement rather than after it.
    show('draft', true, 'cavingGroup');

    fireEvent.click(publishButton()!);

    expect(await screen.findByText(/readable by your caving group/)).toBeInTheDocument();
    expect(screen.getByText(/does not change/)).toBeInTheDocument();
  });

  it('names the widest audience in the same words the question uses for the narrowest', async () => {
    show('draft', true, 'public');

    fireEvent.click(publishButton()!);

    expect(
      await screen.findByText(/anybody at all, including visitors without an account/),
    ).toBeInTheDocument();
  });

  it('sends the state and nothing else, so announcing cannot widen the audience', async () => {
    // The one thing this button might be expected to do and must never do: the request carries
    // the move and no audience at all, and the server leaves the stored one where it is.
    show('draft', true, 'cavingGroup');

    fireEvent.click(publishButton()!);
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(move).toHaveBeenCalledWith({ id: 'trip-1', state: 'published' }));
    expect(move).toHaveBeenCalledTimes(1);
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
