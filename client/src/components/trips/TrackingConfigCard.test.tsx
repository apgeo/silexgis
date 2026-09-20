// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TrackingState, TripCalloutState } from '../../api/hooks.ts';

const MODEL = '44444444-4444-4444-4444-444444444444';
/** The corrected survey a watch gets re-pointed at while the party is underground. */
const OTHER_MODEL = '55555555-5555-5555-5555-555555555555';
/** A survey whose import failed: on the cave's list, with a name and a date, holding no stations. */
const FAILED_MODEL = '66666666-6666-6666-6666-666666666666';

/**
 * A survey as the cave's list describes one — a line plot read right through, unless said
 * otherwise. The two every other test in this file uses are exactly that.
 */
const survey = (id: string, name: string, overrides: Record<string, string> = {}) => ({
  id,
  caveId: 'cave-1',
  name,
  status: 'ready',
  format: 'survex3d',
  ...overrides,
});

/**
 * What the cave's survey list answers, set per test. Held in a variable rather than baked into the
 * mock for the same reason `coarse` below is: the cases worth proving are lists this card has to
 * narrow, and narrowing is only provable against a list that has something in it to leave out.
 */
let models: ReturnType<typeof survey>[] = [];
let modelsPending = false;

const setTracking = vi.fn();
const createTeam = vi.fn();
const renameTeam = vi.fn();
const deleteTeam = vi.fn();

vi.mock('../../api/hooks.ts', async () => ({
  // The rule that decides whether anybody can be put on a survey comes from the module itself
  // rather than being restated here. Stubbed, this file would prove the card asks *a* question and
  // prove nothing about the answer — and the answer is the whole of the change.
  surveyModelCanPlaceACaver: (
    await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts')
  ).surveyModelCanPlaceACaver,
  // And which of the three reasons it is, when it cannot — same reasoning, and the same module:
  // the card names the reason in the sentence it prints, so a stub here would let it print any
  // reason at all and still pass.
  surveyModelPlacingObstacle: (
    await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts')
  ).surveyModelPlacingObstacle,
  useSetTripTracking: () => ({ mutateAsync: setTracking, isPending: false }),
  useCreateTrackingTeam: () => ({ mutateAsync: createTeam, isPending: false }),
  useRenameTrackingTeam: () => ({ mutateAsync: renameTeam, isPending: false }),
  useDeleteTrackingTeam: () => ({ mutateAsync: deleteTeam, isPending: false }),
  useSurveyModelsForCaves: () => ({ data: models, isPending: modelsPending }),
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
    surveyModelMissing: false,
    referenceStationName: 'E0',
    depthFilter: ['main'],
    armedAt: '2026-09-12T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    publishesRealNames: true,
    // Not published. The pair travels together — "published" and "until when" are one answer —
    // and a test about a published trip says both.
    publishedAt: null,
    publishedUntil: null,
    teams: [],
    participants: [],
    ...overrides,
  };
}

/**
 * The card as the trip tab mounts it.
 *
 * The callout is a parameter rather than a constant because it is a claim this card makes about
 * the trip: "the callout arranged for this trip" is only true of a trip that has one. `none` is
 * the default deliberately — a trip with no callout arranged is the case the scope statement was
 * most wrong about, so it is the case every other test in this file is read against.
 */
function show(
  tracking: TrackingState = state(),
  canEdit = true,
  calloutState: TripCalloutState = 'none',
) {
  return render(
    <App>
      <TrackingConfigCard
        tripLogId="trip-1"
        caveIds={['cave-1']}
        tracking={tracking}
        canEdit={canEdit}
        calloutState={calloutState}
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
  models = [survey(MODEL, 'Main survey'), survey(OTHER_MODEL, 'Corrected survey')];
  modelsPending = false;
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
  /**
   * The failure this prevents is a watch that looks like it is working.
   *
   * Deleting the survey a watch is armed on leaves the state Armed, leaves entries, exits and
   * notes landing, and leaves nobody placeable — and the tag over all of that still reads "Armed"
   * in the same blue it reads on a watch that is fine. The card says what that word is now worth,
   * beside the word itself.
   */
  it('marks a watch whose survey has been deleted, and leaves an ordinary one alone', () => {
    const { unmount } = show(state({ surveyModelMissing: true }));
    expect(screen.getByTestId('trip-tracking-model-missing-tag')).toHaveTextContent(
      'Survey deleted',
    );
    unmount();

    // The twin: the same armed watch with its survey still here says nothing of the sort, so the
    // mark means the condition and not merely "this watch is armed".
    show(state({ surveyModelMissing: false }));
    expect(screen.queryByTestId('trip-tracking-model-missing-tag')).not.toBeInTheDocument();
  });

  /**
   * Re-pointing an armed watch at a corrected survey is allowed, and must not be silent.
   *
   * Every report already on the log names the survey it was made against, so the moment the watch
   * moves, those places stop being drawable and the party comes off the model — which, with
   * nothing said, reads as a watch that has stopped working.
   */
  it('says when places on the watch were reported against a different survey', () => {
    const placed = {
      caverId: 'caver-1',
      teamId: null,
      lastKind: 'atStation' as const,
      lastRecordedAt: '2026-09-12T09:00:00Z',
      positionRecordedAt: '2026-09-12T09:00:00Z',
      stationName: 'p.g.7',
      depthM: null,
      positionSurveyModelId: MODEL,
      in: true,
      out: false,
      label: null,
      publishedAs: null,
    };

    const { unmount } = show(
      state({ participants: [{ ...placed, positionSurveyModelId: 'another-model' }] }),
    );
    expect(screen.getByTestId('trip-tracking-positions-other-model')).toHaveTextContent(
      'Some places were reported on a different survey',
    );
    unmount();

    // The twin, and the one that matters: a watch whose places were measured in the survey it is
    // on says nothing, so this warning cannot be read as decoration on every tracked trip.
    show(state({ participants: [placed] }));
    expect(screen.queryByTestId('trip-tracking-positions-other-model')).not.toBeInTheDocument();
  });

  /**
   * The half of a mid-trip survey swap that was silent, and whose bill arrives during a callout.
   *
   * The reference station and the depth filter name stations of the survey in use, so the server
   * clears both when the survey changes and nothing new is given in the same act — rightly, since
   * those names mean nothing, or somewhere else, in another survey. What that leaves is a watch
   * with no datum: the next depth report is refused, "the depth datum cannot be established", to
   * somebody who has just relayed a depth over a phone and has no idea that changing the survey is
   * why. So it is said before the act, on the form where the act is taken.
   */
  it('says the datum and the filter will go before a survey swap takes them', async () => {
    show();
    expect(screen.queryByTestId('trip-tracking-model-swap-clears-datum')).not.toBeInTheDocument();

    const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
    await act(async () => {
      fireEvent.mouseDown(model);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('Corrected survey'));
    });

    expect(screen.getByTestId('trip-tracking-model-swap-clears-datum')).toHaveTextContent(
      'Saving this will clear the depth datum and the station filter',
    );
  });

  it('says nothing about a datum when the same save carries a new one', async () => {
    // Typing a reference station in the same act is how a coordinator moves the datum across
    // deliberately: the server resolves it against the survey being chosen and nothing is lost,
    // so a warning here would be crying wolf over the correct way of doing it.
    show();

    const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
    await act(async () => {
      fireEvent.mouseDown(model);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('Corrected survey'));
    });
    fireEvent.change(screen.getByTestId('trip-tracking-reference'), { target: { value: 'Q9' } });

    expect(screen.queryByTestId('trip-tracking-model-swap-clears-datum')).not.toBeInTheDocument();
  });

  it('says nothing about a datum on a watch that has none to lose', async () => {
    show(state({ referenceStationName: null, depthFilter: [] }));

    const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
    await act(async () => {
      fireEvent.mouseDown(model);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('Corrected survey'));
    });

    expect(screen.queryByTestId('trip-tracking-model-swap-clears-datum')).not.toBeInTheDocument();
  });

  it('says out loud, once it has happened, that the datum went with the survey', async () => {
    // The warning above is seen only by whoever was looking at the form, and what was lost is not
    // visible in the answer: an empty datum box reads as one nobody ever filled in.
    setTracking.mockResolvedValue(
      state({ surveyModelId: OTHER_MODEL, referenceStationName: null, depthFilter: [] }),
    );
    show();

    const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
    await act(async () => {
      fireEvent.mouseDown(model);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('Corrected survey'));
    });
    fireEvent.click(screen.getByTestId('trip-tracking-save'));

    expect(
      await screen.findByText(/depth datum and the station filter were cleared/),
    ).toBeInTheDocument();
  });

  /**
   * A refusal this card can be answered with and had no words for.
   *
   * The server refuses moving an *armed* watch to a survey of another cave. Without a sentence of
   * its own the card fell back to "could not be saved" — which invites a retry of the very act
   * that was refused on purpose, on the one surface where a vague answer is read as news about
   * where people are.
   */
  it('says why a survey of another cave was refused, rather than that a save failed', async () => {
    const { ApiError } = await import('../../api/client.ts');
    setTracking.mockRejectedValue(new ApiError(409, 'tracking.model_other_cave'));
    show();

    const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
    await act(async () => {
      fireEvent.mouseDown(model);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('Corrected survey'));
    });
    fireEvent.click(screen.getByTestId('trip-tracking-save'));

    expect(await screen.findByText(/belongs to a different cave/)).toBeInTheDocument();
    expect(screen.queryByText('The operation failed. Please try again.')).toBeNull();
  });

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

  /**
   * What this surface claims to be, which until now it did not say at all.
   *
   * A live list of people underground, in a caving application, reads as a safety net. This one is
   * not: it is connected to the overdue callout by nothing, so a club can run a whole watch on a
   * party that nothing in the installation will ever notice is missing. The card is where somebody
   * turns a watch on, so the card is where the scope has to be stated.
   */
  describe('what a watch is, and what it is not', () => {
    it('states that it records reports and raises no alarm, and names the thing that does', () => {
      show();

      const said = screen.getByTestId('trip-tracking-scope');
      // The claim itself, not merely a box in the right place: a banner that said something else
      // would pass a test that only asked whether a banner was rendered.
      expect(said).toHaveTextContent(/does not raise an alarm/i);
      expect(said).toHaveTextContent(/nothing here will notice and nobody will be told/i);
      // And where to go for the thing that does, because a scope statement with no destination
      // tells somebody they are unprotected and leaves them there.
      expect(said).toHaveTextContent(/callout/i);
    });

    /**
     * Said twice on purpose, and this is the half that earns it. On a phone the box above is
     * several screens up by the time a thumb reaches the button, so the moment of the act is the
     * one moment the distinction can still change what somebody does.
     */
    it('says it again under the button that starts a watch', () => {
      show(state({ state: 'off', armedAt: null }));

      expect(screen.getByTestId('trip-tracking-arm-scope')).toHaveTextContent(
        /does not arrange a callout/i,
      );
    });

    // The twin of the above: once a watch is running, that button is not the one that starts one,
    // and repeating the line beside Save would be noise under a box that has already said it.
    it('does not repeat itself beside Save once a watch is running', () => {
      show();

      expect(screen.queryByTestId('trip-tracking-arm-scope')).not.toBeInTheDocument();
      // The standing statement is still there — the line went, the claim did not.
      expect(screen.getByTestId('trip-tracking-scope')).toBeInTheDocument();
    });

    // Whoever is reading a live watch is entitled to know what it is doing on their behalf, whether
    // or not they are the person who may change it.
    it('states it to a reader who may not edit the watch at all', () => {
      show(state(), false);

      expect(screen.getByTestId('trip-tracking-scope')).toHaveTextContent(/does not raise an alarm/i);
    });

    /**
     * And the half of that statement that is a claim about *this trip* rather than about the
     * product: where the thing that does raise an alarm is.
     *
     * A callout is arranged on the trip, not here, and plenty of trips have none. Told "the
     * callout arranged for this trip, higher up this page", a reader of a trip without one is sent
     * to a panel that rendered nothing — and for a reader who may not edit the trip it renders
     * nothing at all, so there is literally nothing up there to find. Retiring one imaginary safety
     * net by naming a second is the one thing this statement must not do.
     */
    it('names the callout only when the trip has one arranged', () => {
      show(state(), true, 'armed');

      expect(screen.getByTestId('trip-tracking-scope')).toHaveTextContent(
        /callout arranged for this trip/i,
      );
    });

    // A callout that has been stood down was still arranged, and the panel that holds it is still
    // drawn — so it is still there to be pointed at.
    it('counts a callout that was stood down as one the trip has', () => {
      show(state(), true, 'stoodDown');

      expect(screen.getByTestId('trip-tracking-scope')).toHaveTextContent(
        /callout arranged for this trip/i,
      );
    });

    // The twin, and the case the statement was wrong about: says the trip has none, rather than
    // pointing up the page at one.
    it('says a trip with no callout has none, instead of pointing at one', () => {
      show(state(), true, 'none');

      const said = screen.getByTestId('trip-tracking-scope');
      expect(said).toHaveTextContent(/no callout is arranged for this trip/i);
      expect(said).not.toHaveTextContent(/the callout arranged for this trip/i);
    });

    /**
     * The reader this matters most to. With no callout arranged and no right to edit the trip, the
     * callout panel higher up the page renders nothing whatsoever — so a sentence sending them
     * there sends them to blank page.
     */
    it('tells a reader who may not edit that this trip has no callout', () => {
      show(state(), false, 'none');

      const said = screen.getByTestId('trip-tracking-scope');
      expect(said).toHaveTextContent(/no callout is arranged for this trip/i);
      expect(said).not.toHaveTextContent(/the callout arranged for this trip/i);
    });

    // The line under the arming button carries the act, so it too has to know whether the act has
    // already been done.
    it('offers to arrange a callout under the arming button when the trip has none', () => {
      show(state({ state: 'off', armedAt: null }), true, 'none');

      expect(screen.getByTestId('trip-tracking-arm-scope')).toHaveTextContent(
        /arrange a callout on this trip as well/i,
      );
    });

    // The twin: nobody is sent to arrange a second callout beside the one that is already there.
    it('does not send somebody to arrange a callout the trip already has', () => {
      show(state({ state: 'off', armedAt: null }), true, 'armed');

      const line = screen.getByTestId('trip-tracking-arm-scope');
      expect(line).toHaveTextContent(/does not change the callout already arranged/i);
      expect(line).not.toHaveTextContent(/as well/i);
    });
  });

  /**
   * A watch cannot be armed over a survey nobody can be placed on.
   *
   * A reported place is the name of a station. A survey whose import failed holds none, so a watch
   * pointed at one accepts every report and places nobody, for as long as the party is underground —
   * and it is on the cave's list looking exactly like one that worked.
   */
  describe('surveys nobody can be placed on', () => {
    it('leaves a survey whose import failed out of the chooser, and keeps the ones that worked', async () => {
      models = [
        survey(MODEL, 'Main survey'),
        survey(FAILED_MODEL, 'Half-imported survey', { status: 'failed' }),
      ];
      show(state({ state: 'off', surveyModelId: null, armedAt: null }));

      const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
      await act(async () => {
        fireEvent.mouseDown(model);
      });

      // The positive half, and it is not decoration: a filter that dropped everything would pass
      // the absence below on its own.
      expect(await screen.findByTitle('Main survey')).toBeInTheDocument();
      expect(screen.queryByTitle('Half-imported survey')).not.toBeInTheDocument();
    });

    // The same rule, the other reason a survey holds no stations. A file of cave walls is turned
    // into a picture and never yields a station, so it can no more carry a position than a failed
    // import can.
    it('leaves a file of cave walls out of the chooser too', async () => {
      models = [
        survey(MODEL, 'Main survey'),
        survey(FAILED_MODEL, 'Cave walls', { format: 'stl' }),
      ];
      show(state({ state: 'off', surveyModelId: null, armedAt: null }));

      const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
      await act(async () => {
        fireEvent.mouseDown(model);
      });

      expect(await screen.findByTitle('Main survey')).toBeInTheDocument();
      expect(screen.queryByTitle('Cave walls')).not.toBeInTheDocument();
    });

    /**
     * Hiding a survey from the chooser does not unpoint a watch already on one — armed before this
     * rule existed, or armed on a reading since replaced by one that failed. Silence there is the
     * worse half of the same failure: nothing is drawn, the party never appears, and every report
     * still succeeds.
     */
    it('says so beside the state when the watch is already on one', () => {
      models = [survey(FAILED_MODEL, 'Half-imported survey', { status: 'failed' })];
      show(state({ surveyModelId: FAILED_MODEL }));

      expect(screen.getByTestId('trip-tracking-model-unplaceable-tag')).toBeInTheDocument();
      expect(screen.getByTestId('trip-tracking-model-unplaceable')).toHaveTextContent(
        /holds no stations/i,
      );
    });

    /**
     * Three different surveys hold no stations and the server refuses none of them, so one
     * sentence cannot cover all three — and the sentence that was covering all three was the one
     * about a failed import.
     *
     * <b>A file of cave walls converted perfectly.</b> Told its import did not come through and to
     * import it again, its owner re-imports a mesh, gets a mesh, and gets no station — for ever,
     * because a wall mesh never yields one. The advice that fits it is to choose a line plot.
     */
    it('tells a watch on a file of cave walls that it is cave walls, not that an import failed', () => {
      models = [survey(FAILED_MODEL, 'Cave walls', { format: 'stl' })];
      show(state({ surveyModelId: FAILED_MODEL }));

      const said = screen.getByTestId('trip-tracking-model-unplaceable');
      expect(said).toHaveTextContent(/file of cave walls/i);
      expect(said).not.toHaveTextContent(/import did not come through/i);
      expect(said).not.toHaveTextContent(/import that survey again/i);
    });

    /**
     * And a reading still running has not failed — it is a job in progress, and waiting is the
     * whole of what it needs. Told an import did not come through while the import is running is
     * the same statement made false by the clock.
     */
    it('tells a watch on a survey still being read to wait, not that an import failed', () => {
      models = [survey(FAILED_MODEL, 'Arriving survey', { status: 'processing' })];
      show(state({ surveyModelId: FAILED_MODEL }));

      const said = screen.getByTestId('trip-tracking-model-unplaceable');
      expect(said).toHaveTextContent(/still being read/i);
      expect(said).not.toHaveTextContent(/import did not come through/i);
      expect(said).not.toHaveTextContent(/import that survey again/i);
    });

    // The twin that keeps the two above from being a way of never saying anything: the survey that
    // really did fail is still told exactly that, and still told the act that answers it.
    it('tells a watch on a failed import exactly that, and what to do about it', () => {
      models = [survey(FAILED_MODEL, 'Half-imported survey', { status: 'failed' })];
      show(state({ surveyModelId: FAILED_MODEL }));

      const said = screen.getByTestId('trip-tracking-model-unplaceable');
      expect(said).toHaveTextContent(/import did not come through/i);
      expect(said).toHaveTextContent(/import that survey again/i);
    });

    /**
     * What the Survey field shows once the survey it holds is no longer among the things offered.
     *
     * The chooser looks its own value up among its options to find a label and falls back to the
     * value itself when it finds none — so narrowing the list turned this field into a bare
     * identifier on exactly the watches this change exists to rescue. The co-ordinator who opens
     * this form does so because they have just been told the survey cannot place anybody; the field
     * naming that survey must name it.
     */
    it('still names the survey the watch is on, though it is no longer offered', () => {
      models = [
        survey(MODEL, 'Main survey'),
        survey(FAILED_MODEL, 'Half-imported survey', { status: 'failed' }),
      ];
      show(state({ surveyModelId: FAILED_MODEL }));

      const field = screen.getByTestId('trip-tracking-model');
      expect(field).toHaveTextContent('Half-imported survey');
      expect(field).not.toHaveTextContent(FAILED_MODEL);
    });

    // And it says why it cannot be used, where it is read — otherwise it is a name in a chooser
    // that looks exactly like a working choice.
    it('marks that survey in the field with the reason it cannot be used', () => {
      models = [survey(FAILED_MODEL, 'Half-imported survey', { status: 'failed' })];
      show(state({ surveyModelId: FAILED_MODEL }));

      expect(screen.getByTestId('trip-tracking-model')).toHaveTextContent(
        /import did not come through/i,
      );
    });

    /**
     * Named, but not on offer. The narrowing is the whole point of the change, so the survey the
     * watch is stuck on must not become choosable again by being put back in the list to carry
     * its name.
     */
    it('does not offer that survey as a choice anybody can make afresh', async () => {
      models = [
        survey(MODEL, 'Main survey'),
        survey(FAILED_MODEL, 'Half-imported survey', { status: 'failed' }),
      ];
      show(state({ surveyModelId: FAILED_MODEL }));

      const model = within(screen.getByTestId('trip-tracking-model')).getByRole('combobox');
      await act(async () => {
        fireEvent.mouseDown(model);
      });

      // Asked by the name the chooser announces rather than by the text it paints: what decides
      // whether this row can be taken is what the control tells the person reading it, and the
      // painted row carries only the identifier.
      const stuck = await screen.findByRole('option', { name: /Half-imported survey/ });
      const usable = await screen.findByRole('option', { name: 'Main survey' });
      expect(stuck).toHaveAttribute('aria-disabled', 'true');
      // The positive twin, without which a chooser that disabled everything would pass.
      expect(usable).not.toHaveAttribute('aria-disabled', 'true');
    });

    /**
     * An empty chooser over a cave whose readings are still running is a chooser that fills itself
     * in — the list keeps asking while anything on it is unsettled. Telling its owner the imports
     * failed sends them to re-queue a job that was about to finish.
     */
    it('says the chooser will fill itself in when a survey is still being read', () => {
      models = [survey(FAILED_MODEL, 'Arriving survey', { status: 'pending' })];
      show(state({ state: 'off', surveyModelId: null, armedAt: null }));

      const said = screen.getByTestId('trip-tracking-no-placeable-model');
      expect(said).toHaveTextContent(/still being read/i);
      expect(said).not.toHaveTextContent(/import did not come through/i);
    });

    // The twin: a watch on a survey that was read right through says none of it.
    it('says none of that when the watch is on a survey that was read through', () => {
      show();

      expect(screen.queryByTestId('trip-tracking-model-unplaceable-tag')).not.toBeInTheDocument();
      expect(screen.queryByTestId('trip-tracking-model-unplaceable')).not.toBeInTheDocument();
    });

    /**
     * Nothing is known about a survey until its list arrives. Claiming this one in the meantime
     * would put "nobody can be placed" on screen for every watch on every first render, which is
     * the way a true warning gets trained out of people.
     */
    it('claims nothing while the survey list is still arriving', () => {
      models = [];
      modelsPending = true;
      show(state({ surveyModelId: FAILED_MODEL }));

      expect(screen.queryByTestId('trip-tracking-model-unplaceable-tag')).not.toBeInTheDocument();
    });

    /**
     * Narrowing the chooser turns a cave whose readings all failed into no choices at all, and an
     * empty dropdown under "Choose a survey" reads as a cave with no surveys — a different problem
     * with a different answer.
     */
    it('says why the chooser is empty when every survey of the cave failed', () => {
      models = [survey(FAILED_MODEL, 'Half-imported survey', { status: 'failed' })];
      show(state({ state: 'off', surveyModelId: null, armedAt: null }));

      expect(screen.getByTestId('trip-tracking-no-placeable-model')).toHaveTextContent(
        /import did not come through/i,
      );
    });

    // The twin: a cave with one usable survey has a working chooser and is told nothing.
    it('says nothing about an empty chooser when a usable survey is there', () => {
      models = [
        survey(MODEL, 'Main survey'),
        survey(FAILED_MODEL, 'Half-imported survey', { status: 'failed' }),
      ];
      show(state({ state: 'off', surveyModelId: null, armedAt: null }));

      expect(screen.queryByTestId('trip-tracking-no-placeable-model')).not.toBeInTheDocument();
    });

    // A cave with nothing uploaded at all is the ordinary case, not a fault, and the existing help
    // text under the chooser already covers it.
    it('says nothing about an empty chooser when the cave has no surveys at all', () => {
      models = [];
      show(state({ state: 'off', surveyModelId: null, armedAt: null }));

      expect(screen.queryByTestId('trip-tracking-no-placeable-model')).not.toBeInTheDocument();
    });
  });

});
