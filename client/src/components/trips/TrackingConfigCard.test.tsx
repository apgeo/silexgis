// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TrackingState } from '../../api/hooks.ts';

const MODEL = '44444444-4444-4444-4444-444444444444';

const setTracking = vi.fn();
const createTeam = vi.fn();
const renameTeam = vi.fn();
const deleteTeam = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useSetTripTracking: () => ({ mutateAsync: setTracking, isPending: false }),
  useCreateTrackingTeam: () => ({ mutateAsync: createTeam, isPending: false }),
  useRenameTrackingTeam: () => ({ mutateAsync: renameTeam, isPending: false }),
  useDeleteTrackingTeam: () => ({ mutateAsync: deleteTeam, isPending: false }),
  useSurveyModelsForCaves: () => ({
    data: [{ id: MODEL, caveId: 'cave-1', name: 'Main survey' }],
    isPending: false,
  }),
  useCaveNames: () => new Map<string, string>(),
}));

// What decides how big every control on this card is drawn. Mocked rather than driven by a media
// query, as the rest of this application tests its finger layouts; false by default, which is the
// machine every other test in this file is being read on.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

const { default: TrackingConfigCard } = await import('./TrackingConfigCard.tsx');

function state(overrides: Partial<TrackingState> = {}): TrackingState {
  return {
    state: 'armed',
    surveyModelId: MODEL,
    referenceStationName: 'E0',
    depthFilter: ['main'],
    armedAt: '2026-09-12T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    publishesRealNames: true,
    teams: [],
    participants: [],
    ...overrides,
  };
}

function show(tracking: TrackingState = state(), canEdit = true) {
  return render(
    <App>
      <TrackingConfigCard
        tripLogId="trip-1"
        caveIds={['cave-1']}
        tracking={tracking}
        canEdit={canEdit}
        onStale={() => {}}
      />
    </App>,
  );
}

beforeEach(() => {
  setTracking.mockReset().mockResolvedValue(state());
  createTeam.mockReset().mockResolvedValue({ id: 'team-1', title: 'Team A' });
  renameTeam.mockReset().mockResolvedValue({ id: 'team-1', title: 'Team B' });
  deleteTeam.mockReset().mockResolvedValue(undefined);
  coarse = false;
});

afterEach(cleanup);

describe('TrackingConfigCard', () => {
  /**
   * The defect this prevents is silent and expensive. A co-ordinator who may run the trip but may
   * not be told the cave's exact position is sent no reference station at all — the field arrives
   * empty because it was withheld, not because none is set. A save that echoed the form back would
   * clear the stored one, and every depth reported afterwards would resolve against a different
   * datum with nothing on screen having said so.
   */
  it('sends nothing about a field nobody edited, so a save cannot clear what it was not shown', async () => {
    show(state({ surveyModelId: null, referenceStationName: null, depthFilter: [] }));

    fireEvent.click(screen.getByTestId('trip-tracking-save'));

    await waitFor(() => expect(setTracking).toHaveBeenCalledTimes(1));
    const sent = setTracking.mock.calls[0][0] as Record<string, unknown>;
    expect(sent).toMatchObject({ tripLogId: 'trip-1', state: 'armed' });
    expect(sent.surveyModelId).toBeUndefined();
    expect(sent.referenceStationName).toBeUndefined();
    expect(sent.depthFilter).toBeUndefined();
  });

  // The other direction, which the rule above must not swallow: emptying a field on purpose is a
  // statement, and it has to reach the server as one.
  it('clears the reference station when somebody empties it on purpose', async () => {
    show();

    fireEvent.change(screen.getByTestId('trip-tracking-reference'), { target: { value: '' } });
    fireEvent.click(screen.getByTestId('trip-tracking-save'));

    await waitFor(() => expect(setTracking).toHaveBeenCalledTimes(1));
    expect(setTracking.mock.calls[0][0]).toMatchObject({ referenceStationName: '' });
  });

  /**
   * Ending a watch is not an occasion to rewrite the vocabulary it was kept in. The reports
   * already on the log were resolved against this configuration, so a close that carried the form
   * with it would change what those depths meant after the fact.
   */
  it('closes the watch without carrying any configuration with it', async () => {
    show();

    fireEvent.change(screen.getByTestId('trip-tracking-reference'), { target: { value: 'Q9' } });
    fireEvent.click(screen.getByTestId('trip-tracking-close'));
    fireEvent.click(await screen.findByText('OK'));

    await waitFor(() => expect(setTracking).toHaveBeenCalledTimes(1));
    const sent = setTracking.mock.calls[0][0] as Record<string, unknown>;
    expect(sent).toMatchObject({ state: 'closed' });
    expect(sent.referenceStationName).toBeUndefined();
    expect(sent.surveyModelId).toBeUndefined();
  });

  // Arming carries the chosen survey with it, so starting a watch is one act. Split in two, the
  // ordinary path would be a refusal — a watch armed against no survey is one the server rejects.
  it('arms the watch and the survey it was chosen with in one act', async () => {
    show(state({ state: 'off', surveyModelId: null, referenceStationName: null, depthFilter: [] }));

    const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
    await act(async () => {
      fireEvent.mouseDown(model);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('Main survey'));
    });

    fireEvent.click(screen.getByTestId('trip-tracking-arm'));

    await waitFor(() => expect(setTracking).toHaveBeenCalledTimes(1));
    expect(setTracking.mock.calls[0][0]).toMatchObject({ state: 'armed', surveyModelId: MODEL });
  });

  it('offers a reader who may not write the trip nothing that writes it', () => {
    show(state({ teams: [{ id: 'team-1', title: 'Team A' }] }), false);

    expect(screen.queryByTestId('trip-tracking-save')).toBeNull();
    expect(screen.queryByTestId('trip-tracking-close')).toBeNull();
    expect(screen.queryByTestId('trip-tracking-team-submit')).toBeNull();
    // What the watch is set to is still said, because that is a fact about the trip.
    expect(screen.getByTestId('trip-tracking-teams')).toHaveTextContent('Team A');
  });

  /**
   * The read-side half of the same defect, and the one the merge semantics alone do not close.
   * A co-writer without the right to place the cave is sent all three configuration fields empty,
   * and drawn as blanks they say the opposite of what is true: that no survey and no datum are
   * set. The invitation to fill them in is the whole problem — the field is one keystroke and a
   * backspace away from clearing a datum this person was never shown.
   */
  it('draws a withheld setup as withheld rather than as one nobody has set', () => {
    show(
      state({
        positionsWithheld: true,
        surveyModelId: null,
        referenceStationName: null,
        depthFilter: [],
      }),
    );

    expect(screen.getByTestId('trip-tracking-config-withheld')).toHaveTextContent(
      'This tracking setup is not shown to you',
    );
    expect(screen.getByTestId('trip-tracking-reference')).toBeDisabled();
    expect(
      within(screen.getByTestId('trip-tracking-model')).getByRole('combobox'),
    ).toBeDisabled();
  });

  // Closing a watch and arming one again are not placing acts and stay available while the setup
  // is withheld: the server keeps what it holds for every field this card leaves out.
  it('still ends a withheld watch without rewriting the setup it was kept in', async () => {
    show(
      state({
        positionsWithheld: true,
        surveyModelId: null,
        referenceStationName: null,
        depthFilter: [],
      }),
    );

    fireEvent.click(screen.getByTestId('trip-tracking-close'));
    fireEvent.click(await screen.findByText('OK'));

    await waitFor(() => expect(setTracking).toHaveBeenCalledTimes(1));
    const sent = setTracking.mock.calls[0][0] as Record<string, unknown>;
    expect(sent).toMatchObject({ state: 'closed' });
    expect(sent.surveyModelId).toBeUndefined();
    expect(sent.referenceStationName).toBeUndefined();
    expect(sent.depthFilter).toBeUndefined();
  });

  // Replacing a setup somebody was not shown is still allowed — it is a real thing to want, and
  // the server checks the placing right on whatever survey is chosen. What it must not be is
  // accidental, so it is a deliberate act taken with the warning on screen.
  it('replaces a withheld setup only once somebody says that is what they mean', async () => {
    show(
      state({
        positionsWithheld: true,
        surveyModelId: null,
        referenceStationName: null,
        depthFilter: [],
      }),
    );

    fireEvent.click(screen.getByTestId('trip-tracking-config-replace'));
    expect(screen.getByTestId('trip-tracking-reference')).not.toBeDisabled();

    fireEvent.change(screen.getByTestId('trip-tracking-reference'), { target: { value: 'Q9' } });
    fireEvent.click(screen.getByTestId('trip-tracking-save'));

    await waitFor(() => expect(setTracking).toHaveBeenCalledTimes(1));
    expect(setTracking.mock.calls[0][0]).toMatchObject({ referenceStationName: 'Q9' });
  });

  it('adds a team by the title somebody typed', async () => {
    show();

    fireEvent.change(screen.getByTestId('trip-tracking-team-title'), {
      target: { value: 'Bottom team' },
    });
    fireEvent.click(screen.getByTestId('trip-tracking-team-submit'));

    await waitFor(() => expect(createTeam).toHaveBeenCalledTimes(1));
    expect(createTeam.mock.calls[0][0]).toMatchObject({
      tripLogId: 'trip-1',
      title: 'Bottom team',
    });
  });

  /**
   * <b>Chosen on the pointer and never on the width.</b> A phone held sideways reports 863px
   * across — a desk's worth of room — and still has nothing on it that can hit a 24px target, so
   * a card that sized its controls by how much room it had would leave that device exactly as it
   * was. Everything here is something somebody presses, so the branch is made once for the card
   * rather than control by control, which is how a surface ends up with its rarest button sized
   * for a finger and its commonest one left alone.
   */
  describe('drawn for a finger', () => {
    const LARGE = 'ant-btn-lg';

    it('sizes the acts that start and end a watch, and the fields they carry', () => {
      coarse = true;
      show();

      expect(screen.getByTestId('trip-tracking-save')).toHaveClass(LARGE);
      expect(screen.getByTestId('trip-tracking-close')).toHaveClass(LARGE);
      // The configuration fields matter as much as the buttons: the reference station is the datum
      // every depth on the log is resolved against, and it is typed into on a hillside.
      expect(screen.getByTestId('trip-tracking-reference')).toHaveClass('ant-input-lg');
      expect(screen.getByTestId('trip-tracking-model')).toHaveClass('ant-select-lg');
      expect(screen.getByTestId('trip-tracking-depth-filter')).toHaveClass('ant-select-lg');
    });

    it('sizes the teams editor, including the two buttons buried in a chip', () => {
      coarse = true;
      show(state({ teams: [{ id: 'team-1', title: 'Team A' }] }));

      expect(screen.getByTestId('trip-tracking-team-title')).toHaveClass('ant-input-lg');
      expect(screen.getByTestId('trip-tracking-team-submit')).toHaveClass(LARGE);
      // These two were the smallest controls on the card by some way, and the chip simply grows
      // to hold them rather than the targets being crammed into it.
      expect(screen.getByTestId('trip-tracking-team-rename-team-1')).toHaveClass(LARGE);
      expect(screen.getByTestId('trip-tracking-team-delete-team-1')).toHaveClass(LARGE);
    });

    it('offers a watch it may not read the same target on the button that unlocks it', () => {
      // The one act on this card that replaces a datum somebody was never shown. It is drawn small
      // on a desk because it lives inside a warning, and small is the whole problem on a phone.
      coarse = true;
      show(
        state({
          positionsWithheld: true,
          surveyModelId: null,
          referenceStationName: null,
          depthFilter: [],
        }),
      );

      expect(screen.getByTestId('trip-tracking-config-replace')).toHaveClass(LARGE);
    });

    it('leaves the card exactly as it was where there is a mouse', () => {
      show(state({ teams: [{ id: 'team-1', title: 'Team A' }] }));

      expect(screen.getByTestId('trip-tracking-save')).not.toHaveClass(LARGE);
      expect(screen.getByTestId('trip-tracking-reference')).not.toHaveClass('ant-input-lg');
      // Still the small link buttons the chip was designed around.
      expect(screen.getByTestId('trip-tracking-team-rename-team-1')).toHaveClass('ant-btn-sm');
      expect(screen.getByTestId('trip-tracking-team-delete-team-1')).toHaveClass('ant-btn-sm');
    });
  });

});
