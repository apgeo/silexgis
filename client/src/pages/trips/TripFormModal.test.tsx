// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import dayjs from 'dayjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo, TripLogWrite } from '../../api/hooks.ts';

const createTrip = vi.fn();
const createPlan = vi.fn();
const updateTrip = vi.fn();
// What the server says a plan's audience would be, as the plan-default read answers it.
const planDefault = vi.fn();
// Where this caller may record a trip, as the capabilities answer says it.
const door = vi.fn();

/** Somebody who holds the right to record trips over the domain as such — nearly everybody. */
const holdsTheRight = { canCreate: true, unbound: true, cavingGroups: [] };
const silex = { id: 'g-1', name: 'Silex' };
const avenul = { id: 'g-2', name: 'Avenul' };

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: [] }),
  useTripTypes: () => ({ data: [{ id: 1, code: 'survey', name: 'Survey / mapping', isSeeded: true }] }),
  useTripParticipantRoles: () => ({
    data: [
      { id: 1, code: 'participant', name: 'Participant', isSeeded: true, sortOrder: 10 },
      { id: 3, code: 'leader', name: 'Leader', isSeeded: true, sortOrder: 30 },
    ],
  }),
  useSearch: () => ({ data: undefined }),
  useCreateDoor: () => door(),
  useCreateTripLog: () => ({ mutateAsync: createTrip, isPending: false }),
  useCreateTripPlan: () => ({ mutateAsync: createPlan, isPending: false }),
  useTripPlanDefault: (enabled: boolean) => (enabled ? planDefault() : { data: undefined }),
  useUpdateTripLog: () => ({ mutateAsync: updateTrip, isPending: false }),
}));

const { default: TripFormModal } = await import('./TripFormModal.tsx');

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return {
    id: 'trip-1',
    title: 'Digging weekend',
    tripTypeId: null,
    tripDate: '2026-03-14',
    tripDateEnd: null,
    entryTime: null,
    exitTime: null,
    description: null,
    results: null,
    weatherConditions: null,
    locationText: null,
    organizingCavingGroupId: null,
    geom: null,
    caveIds: [],
    participants: [],
    proposers: [],
    cavingGroupId: null,
    visibility: 'private',
    ...overrides,
  } as TripLogInfo;
}

/** The write body the modal handed its mutation, once the save has been awaited. */
async function savedBody(mutation: typeof createTrip): Promise<TripLogWrite> {
  fireEvent.click(screen.getByRole('button', { name: 'OK' }));
  await vi.waitFor(() => expect(mutation).toHaveBeenCalled());
  const call = mutation.mock.calls[0][0] as TripLogWrite | { body: TripLogWrite };
  return 'body' in call ? call.body : call;
}

function show(subject: TripLogInfo | null, intent?: 'report' | 'plan') {
  return render(
    <App>
      <TripFormModal open trip={subject} intent={intent} onClose={() => {}} />
    </App>,
  );
}

// Every test that does not say otherwise is about somebody who holds the right outright, which
// is the caller this form was written for and whose form must not have changed.
beforeEach(() => {
  door.mockReset().mockReturnValue(holdsTheRight);
});

describe('TripFormModal dates', () => {
  beforeEach(() => {
    createTrip.mockReset().mockResolvedValue({ id: 'new-trip' });
    updateTrip.mockReset().mockResolvedValue({ id: 'trip-1' });
  });
  afterEach(cleanup);

  it('saves a trip whose date control was never touched', async () => {
    // The create form pre-fills today at both ends of the range. A trip is routinely logged by
    // typing a title and accepting everything else, so a default that fails the field's own
    // required rule would make the form unsubmittable without the user knowing why.
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });

    const body = await savedBody(createTrip);
    expect(body.tripDate).toBe(dayjs().format('YYYY-MM-DD'));
    expect(body.tripDateEnd).toBeNull();
  });

  it('keeps the second day of a trip that ran on past the first', async () => {
    show(trip({ tripDateEnd: '2026-03-16' }));

    const body = await savedBody(updateTrip);
    expect(body.tripDate).toBe('2026-03-14');
    expect(body.tripDateEnd).toBe('2026-03-16');
  });

  it('sends the number of places somebody typed, and an empty box as no limit at all', async () => {
    // A limit is what makes the people past it a waiting list rather than everybody who said
    // yes, so somewhere has to be able to set one — and clearing it has to mean a trip that
    // turns nobody away, not a value silently left as it was.
    show(trip({ maxParticipants: 8 } as Partial<TripLogInfo>));
    expect(screen.getByTestId('trip-max-participants')).toHaveValue('8');

    fireEvent.change(screen.getByTestId('trip-max-participants'), { target: { value: '6' } });
    expect((await savedBody(updateTrip)).maxParticipants).toBe(6);

    cleanup();
    updateTrip.mockClear();
    show(trip({ maxParticipants: 8 } as Partial<TripLogInfo>));
    fireEvent.change(screen.getByTestId('trip-max-participants'), { target: { value: '' } });
    expect((await savedBody(updateTrip)).maxParticipants).toBeNull();
  });

  it('carries the meeting point through a save that never touched it', async () => {
    // The meeting point is a column like the sketch, written straight through with null meaning
    // "cleared", so a save that only corrects a title must send it back — otherwise correcting a
    // title erases where the party was told to be, and nothing on screen says it happened.
    const meetingGeom = {
      type: 'Point',
      coordinates: [25.44, 45.53],
    } as unknown as TripLogInfo['meetingGeom'];
    show(trip({ meetingGeom }));

    const body = await savedBody(updateTrip);
    expect(body.meetingGeom).toEqual(meetingGeom);
  });

  it('sends no meeting point for a trip that states none', async () => {
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });

    expect((await savedBody(createTrip)).meetingGeom).toBeNull();
  });

  it('drops the meeting point when it is cleared, and leaves the sketch alone', async () => {
    // Two maps stand on this form and each clears only its own: a driver that reached for "the
    // clear button" would be reaching for whichever rendered first.
    const geom = { type: 'Point', coordinates: [25.6, 45.65] } as unknown as TripLogInfo['geom'];
    const meetingGeom = {
      type: 'Point',
      coordinates: [25.44, 45.53],
    } as unknown as TripLogInfo['meetingGeom'];
    show(trip({ geom, meetingGeom }));

    fireEvent.click(screen.getByTestId('trip-meeting-geometry-clear'));

    const body = await savedBody(updateTrip);
    expect(body.meetingGeom).toBeNull();
    expect(body.geom).toEqual(geom);
  });

  it('carries a sketch the editor never touched through a save', async () => {
    // The form owns the geometry now; an edit that changes only the title must not drop the shape.
    const geom = { type: 'Point', coordinates: [25.6, 45.65] } as unknown as TripLogInfo['geom'];
    show(trip({ geom }));

    const body = await savedBody(updateTrip);
    expect(body.geom).toEqual(geom);
  });

  it('sends no sketch for a trip that has none', async () => {
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });

    const body = await savedBody(createTrip);
    expect(body.geom).toBeNull();
  });

  it('drops the sketch when it is cleared', async () => {
    const geom = { type: 'Point', coordinates: [25.6, 45.65] } as unknown as TripLogInfo['geom'];
    show(trip({ geom }));

    fireEvent.click(screen.getByTestId('trip-geometry-clear'));

    const body = await savedBody(updateTrip);
    expect(body.geom).toBeNull();
  });

  it('carries the measured facts through a save that never showed them', async () => {
    // Every write sets all of them, and this form offers none of them yet, so blanks would
    // unmeasure a trip whose title somebody corrected. The incident flag is the one that would
    // hurt most: a form that quietly sends false says nothing went wrong on a trip where
    // something did, and nothing on screen would have said so.
    show(
      trip({
        depthReachedM: 218,
        lengthSurveyedM: 412.5,
        surveyStations: 47,
        ropeMetres: 260,
        hadIncident: true,
      }),
    );
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Digging weekend (2)' } });

    const body = await savedBody(updateTrip);
    expect(body.depthReachedM).toBe(218);
    expect(body.lengthSurveyedM).toBe(412.5);
    expect(body.surveyStations).toBe(47);
    expect(body.ropeMetres).toBe(260);
    expect(body.hadIncident).toBe(true);
  });

  it("keeps the trip's room for people through a save that never showed it", async () => {
    // The write sets the whole trip, and this form draws no control for the limit, so leaving it
    // out of the body clears it. Nothing would look wrong afterwards — an unlimited trip is what
    // no limit means — while everybody who was waiting for a place is silently on the trip.
    show(trip({ maxParticipants: 8 }));
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Digging weekend (4)' } });

    const body = await savedBody(updateTrip);
    expect(body.maxParticipants).toBe(8);
  });

  it('mentions no section at all, rather than three empty ones', async () => {
    // Absent and empty are different answers on the wire: absent leaves the stored section
    // alone, empty clears it. This form draws none of the three, so a title correction here
    // must not empty what a trip found, needed and learned — and echoing them back instead
    // would be worse still, because that re-measures each one against the purpose's schema as
    // it now stands, so an old report could fail on a section nobody opened.
    show(
      trip({
        fieldData: { water_level: 'high' },
        logistics: { permit_reference: 'RO-2026-14' },
        safety: { incident_severity: 'near_miss' },
      }),
    );
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Digging weekend (3)' } });

    const body = await savedBody(updateTrip);
    expect(body.fieldData).toBeNull();
    expect(body.logistics).toBeNull();
    expect(body.safety).toBeNull();
  });

  it('says nothing went wrong on a trip nobody has measured', async () => {
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });

    const body = await savedBody(createTrip);
    expect(body.depthReachedM).toBeNull();
    expect(body.surveyStations).toBeNull();
    expect(body.hadIncident).toBe(false);
  });

  it('sends back the job, the times and the note it never showed', async () => {
    // The form shows a name and nothing else, and a write replaces the whole roster. So a row it
    // loaded has to go back carrying what it was carrying: dropping it would retype the trip's
    // leader as an ordinary attendee and throw away the hours somebody recorded, as the price of
    // correcting a title.
    show(
      trip({
        participants: [
          {
            caverId: 'caver-1',
            name: 'Ana Ionescu',
            userId: null,
            roleId: 3,
            entryTime: '09:00:00',
            exitTime: '13:15:00',
            note: 'Turned back at the pitch head.',
          },
        ],
      }),
    );
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Digging weekend (4)' } });

    const body = await savedBody(updateTrip);
    expect(body.participants).toEqual([
      {
        caverId: 'caver-1',
        newCaverName: null,
        roleId: 3,
        entryTime: '09:00:00',
        exitTime: '13:15:00',
        note: 'Turned back at the pitch head.',
      },
    ]);
  });

  it('names no job for somebody typed in, so the server reads them as simply there', async () => {
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });
    fireEvent.click(screen.getByRole('button', { name: /Add participant/ }));
    fireEvent.change(screen.getByPlaceholderText('Participant name'), { target: { value: 'Guest Caver' } });

    const body = await savedBody(createTrip);
    expect(body.participants).toEqual([
      { caverId: null, newCaverName: 'Guest Caver', roleId: null, entryTime: null, exitTime: null, note: null },
    ]);
  });

  it('correcting a misspelled name means the corrected name, not the person it was typed over', async () => {
    // The row loaded pointing at somebody. Sending that reference back beside the corrected
    // text would store nothing at all: the server reads the reference and ignores the name, so
    // the misspelling would survive every attempt to fix it, silently and without an error.
    show(
      trip({
        participants: [
          { caverId: 'caver-1', name: 'Ana Popscu', userId: null, roleId: 1, entryTime: null, exitTime: null, note: null },
        ],
      }),
    );
    fireEvent.change(screen.getByDisplayValue('Ana Popscu'), { target: { value: 'Ana Popescu' } });

    const body = await savedBody(updateTrip);
    expect(body.participants[0]).toMatchObject({ caverId: null, newCaverName: 'Ana Popescu' });
  });

  it('keeps the person a row was loaded for when its name is typed back as it was', async () => {
    // The other half of the same rule, and the reason it is not "any keystroke detaches the
    // row": a name shown for somebody who holds an account is their profile's, which need not
    // be the name the roster holds — so detaching on a keystroke that changed nothing would
    // quietly make a second person out of one.
    show(
      trip({
        participants: [
          { caverId: 'caver-1', name: 'Ana Popescu', userId: null, roleId: 1, entryTime: null, exitTime: null, note: null },
        ],
      }),
    );
    const field = screen.getByDisplayValue('Ana Popescu');
    fireEvent.change(field, { target: { value: 'Ana Pope' } });
    fireEvent.change(field, { target: { value: 'Ana Popescu' } });

    const body = await savedBody(updateTrip);
    expect(body.participants[0]).toMatchObject({ caverId: 'caver-1', newCaverName: null });
  });

  it('records the job a person did and the hours they were down without asking for either', async () => {
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Pitch rigging' } });
    fireEvent.click(screen.getByRole('button', { name: /Add participant/ }));
    fireEvent.change(screen.getByPlaceholderText('Participant name'), { target: { value: 'Guest Caver' } });
    // The row is a name until somebody asks it for more — which is the whole point of the
    // control, so the test opens the extra fields the same way a person has to.
    fireEvent.click(screen.getByRole('button', { name: /Role, times and note/ }));
    fireEvent.change(screen.getByPlaceholderText('Note'), { target: { value: 'Turned back at the pitch head.' } });

    const body = await savedBody(createTrip);
    expect(body.participants[0]).toMatchObject({
      caverId: null,
      newCaverName: 'Guest Caver',
      note: 'Turned back at the pitch head.',
    });
  });

  it('saves the job picked in a row’s own details, not the role of having merely been there', async () => {
    // The picker is the only field in the row whose value is chosen rather than typed, and it
    // was the only one not covered here: the note and the hours were, so a save that dropped
    // the job alone went out looking entirely healthy. The server reads a missing job as
    // "simply there", so the loss is silent — the trip's leader is stored as an attendee and
    // nothing anywhere says so.
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Pitch rigging' } });
    fireEvent.click(screen.getByRole('button', { name: /Add participant/ }));
    fireEvent.change(screen.getByPlaceholderText('Participant name'), { target: { value: 'Guest Caver' } });
    fireEvent.click(screen.getByRole('button', { name: /Role, times and note/ }));

    // Scoped to the row that was opened: the form draws several selects of its own, and the
    // trip's type is a different question from this person's job.
    const details = screen.getByTestId('roster-row-details');
    fireEvent.mouseDown(within(details).getByRole('combobox'));
    fireEvent.click(await screen.findByText('Leader'));

    const body = await savedBody(createTrip);
    expect(body.participants[0]).toMatchObject({ newCaverName: 'Guest Caver', roleId: 3 });
  });

  it('leaves a one-day trip without an end date rather than a range of itself', async () => {
    // Editing a single-day trip fills both ends of the picker with the same day; storing that
    // as an end date would make the trip read as "14/03/2026 – 14/03/2026" everywhere after.
    show(trip());

    const body = await savedBody(updateTrip);
    expect(body.tripDate).toBe('2026-03-14');
    expect(body.tripDateEnd).toBeNull();
  });
});

/**
 * The same form on its other door. A plan is the same trip in every respect but the audience it
 * gets when the author names none, and that rule lives on the server: what the form owes it is to
 * say in advance who that will be, to leave an audience the author did not choose unstated so the
 * rule can fire, and to post through the door the rule is behind.
 */
describe('TripFormModal as the plan door', () => {
  beforeEach(() => {
    createTrip.mockReset().mockResolvedValue({ id: 'new-trip' });
    createPlan.mockReset().mockResolvedValue({ id: 'new-plan' });
    planDefault.mockReset().mockReturnValue({
      data: { visibility: 'cavingGroup', cavingGroupId: 'g-1', cavingGroupName: 'Silex' },
    });
  });
  afterEach(cleanup);

  it('says it is planning a trip, and names the group that will read it', () => {
    show(null, 'plan');

    expect(screen.getByRole('dialog', { name: 'Plan a trip' })).toBeInTheDocument();
    expect(screen.getByTestId('trip-plan-audience').textContent).toContain('Silex');
  });

  it('pre-selects the audience the server answered with', () => {
    show(null, 'plan');

    // The select draws its chosen option's label; "Caving group" is the answer and "Private"
    // is what a report would have shown here.
    const audience = screen.getByLabelText('Visibility').closest('.ant-select');
    expect(audience?.textContent).toContain('Caving group');
  });

  it('posts through the plan door with an untouched audience left unstated', async () => {
    show(null, 'plan');
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Spring recce' } });

    const body = await savedBody(createPlan);
    expect(createTrip).not.toHaveBeenCalled();
    // Null, not "cavingGroup": a stated caving-group audience with no group behind it would be
    // an audience of nobody. Unstated, the server supplies the audience and binds the group.
    expect(body.visibility).toBeNull();
    expect(body.cavingGroupId).toBeNull();
  });

  it('sends an audience the author changed as a stated one', async () => {
    show(null, 'plan');
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Open day' } });
    fireEvent.mouseDown(screen.getByLabelText('Visibility'));
    fireEvent.click(await screen.findByText('Public'));

    const body = await savedBody(createPlan);
    expect(body.visibility).toBe('public');
  });

  it('says the plan starts private when the author is in no single group', () => {
    planDefault.mockReturnValue({
      data: { visibility: 'private', cavingGroupId: null, cavingGroupName: null },
    });
    show(null, 'plan');

    expect(screen.getByTestId('trip-plan-audience').textContent).toContain('starts private');
  });

  it('leaves the report door exactly as it was', async () => {
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });

    expect(screen.getByRole('dialog', { name: 'New trip log' })).toBeInTheDocument();
    expect(screen.queryByTestId('trip-plan-audience')).toBeNull();
    const body = await savedBody(createTrip);
    expect(createPlan).not.toHaveBeenCalled();
    // A report's audience is always stated, private unless chosen otherwise.
    expect(body.visibility).toBe('private');
  });
});

/**
 * The same form for somebody whose right to record trips reaches only through a caving group: a
 * club's own rules let its members record the club's trips and nothing wider. The server accepts
 * a trip from them only when it belongs to such a group — left unbound, as a report always was,
 * it is refused — so the form binds it: to the one group there is, saying so, or to the one the
 * author chooses from exactly the groups the server named.
 */
describe('TripFormModal for somebody who may record trips only for a caving group', () => {
  const byOneClub = { canCreate: true, unbound: false, cavingGroups: [silex] };
  const bySeveralClubs = { canCreate: true, unbound: false, cavingGroups: [avenul, silex] };

  beforeEach(() => {
    createTrip.mockReset().mockResolvedValue({ id: 'new-trip' });
    createPlan.mockReset().mockResolvedValue({ id: 'new-plan' });
    updateTrip.mockReset().mockResolvedValue({ id: 'trip-1' });
    planDefault.mockReset().mockReturnValue({ data: undefined });
  });
  afterEach(cleanup);

  it('binds a report to the one group, says so by name, and keeps the narrowest audience', async () => {
    door.mockReturnValue(byOneClub);
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });

    // Said before the trip exists, and the group is named rather than called "your group".
    expect(screen.getByTestId('trip-owning-group').textContent).toContain(
      'You record trips for Silex; this one will belong to it.',
    );
    // One group is nothing to choose between.
    expect(screen.queryByTestId('trip-owning-group-choice')).toBeNull();

    const body = await savedBody(createTrip);
    expect(createPlan).not.toHaveBeenCalled();
    // The binding is what the server admits the trip on; the audience beside it stays the
    // narrowest there is, as a report's always was.
    expect(body.cavingGroupId).toBe('g-1');
    expect(body.visibility).toBe('private');
  });

  it('says what belonging to the group means, which the visibility control cannot', () => {
    door.mockReturnValue(byOneClub);
    show(null);

    // "Private" beside a trip that belongs to a club does not keep the club out: what its
    // members may do with the club's trips is the club's rules' to say.
    expect(screen.getByTestId('trip-owning-group').textContent).toContain(
      "decided by the group's own rules, not by the visibility chosen here",
    );
  });

  it('binds a plan to the one group, shared with it, and states both rather than leaving them to the rule', async () => {
    door.mockReturnValue(byOneClub);
    show(null, 'plan');
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Spring recce' } });

    const audience = screen.getByLabelText('Visibility').closest('.ant-select');
    expect(audience?.textContent).toContain('Caving group');
    expect(screen.getByTestId('trip-plan-audience').textContent).toContain('Silex');
    expect(screen.getByTestId('trip-owning-group').textContent).toContain('Silex');

    const body = await savedBody(createPlan);
    expect(createTrip).not.toHaveBeenCalled();
    // Stated, not null. Unstated, the server's rule would decide — and it binds a plan only for
    // somebody in exactly one group, which this form cannot know: the list it holds is the
    // groups the caller may create in, not the groups they are in.
    expect(body.visibility).toBe('cavingGroup');
    expect(body.cavingGroupId).toBe('g-1');
    // So the read that describes an unstated plan is not asked for at all.
    expect(planDefault).not.toHaveBeenCalled();
  });

  it('keeps the trip in its group when the author changes who else may read it', async () => {
    door.mockReturnValue(byOneClub);
    show(null, 'plan');
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Open day' } });
    fireEvent.mouseDown(screen.getByLabelText('Visibility'));
    fireEvent.click(await screen.findByText('Public'));

    const body = await savedBody(createPlan);
    // A chosen audience with the group dropped would be exactly the unbound trip the server
    // refuses this caller.
    expect(body.visibility).toBe('public');
    expect(body.cavingGroupId).toBe('g-1');
  });

  it('asks which group when there are several, offers exactly those, and takes no answer for none', async () => {
    door.mockReturnValue(bySeveralClubs);
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });

    // Nothing is guessed between them, so there is no sentence claiming a group either.
    expect(screen.queryByTestId('trip-owning-group')).toBeNull();

    // Pressing OK without choosing is refused on the field, and nothing is sent.
    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(await screen.findByText('Choose the caving group this trip belongs to.')).toBeInTheDocument();
    expect(createTrip).not.toHaveBeenCalled();

    // The choice is the server's list and nothing else: a group left out of it is one the
    // server would refuse, so it is not there to pick.
    fireEvent.mouseDown(screen.getByLabelText('Belongs to'));
    await screen.findByTitle('Silex');
    // The list as drawn. antd builds a select's options only once it is opened, and this is the
    // only one this test opens, so every option in the document is one of this control's.
    const offered = Array.from(
      document.querySelectorAll('.ant-select-item-option-content'),
      (option) => option.textContent,
    );
    expect(offered).toEqual(['Avenul', 'Silex']);
    fireEvent.click(screen.getByTitle('Silex'));

    const body = await savedBody(createTrip);
    expect(body.cavingGroupId).toBe('g-1');
    expect(body.visibility).toBe('private');
  });

  it('asks which group for a plan too, and shares the plan with the one chosen', async () => {
    door.mockReturnValue(bySeveralClubs);
    show(null, 'plan');
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Spring recce' } });

    const audience = screen.getByLabelText('Visibility').closest('.ant-select');
    expect(audience?.textContent).toContain('Caving group');
    // No group is named before one is chosen: the sentence is about the group the plan will
    // belong to, which is the choice just below it.
    expect(screen.getByTestId('trip-plan-audience').textContent).toContain(
      'the caving group it belongs to',
    );

    fireEvent.mouseDown(screen.getByLabelText('Belongs to'));
    fireEvent.click(await screen.findByTitle('Avenul'));

    const body = await savedBody(createPlan);
    expect(body.visibility).toBe('cavingGroup');
    expect(body.cavingGroupId).toBe('g-2');
  });

  it('leaves somebody who holds the right outright alone, whatever clubs they are in', async () => {
    // The right over trips as such, and a club besides: nothing makes this caller file their
    // work under the club, so the form is the one it always was — no sentence, no choice, a
    // report bound to nothing and a plan left to the server's rule.
    door.mockReturnValue({ canCreate: true, unbound: true, cavingGroups: [silex] });
    planDefault.mockReturnValue({
      data: { visibility: 'cavingGroup', cavingGroupId: 'g-1', cavingGroupName: 'Silex' },
    });
    show(null);
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Quick look' } });

    expect(screen.queryByTestId('trip-owning-group')).toBeNull();
    expect(screen.queryByTestId('trip-owning-group-choice')).toBeNull();
    const report = await savedBody(createTrip);
    expect(report.cavingGroupId).toBeNull();
    expect(report.visibility).toBe('private');

    cleanup();
    show(null, 'plan');
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Spring recce' } });
    const plan = await savedBody(createPlan);
    expect(plan.visibility).toBeNull();
    expect(plan.cavingGroupId).toBeNull();
  });

  it('does not move an existing trip between groups, and asks nothing about one', async () => {
    // Editing is a different question from creating: the trip has the group it has, and what
    // this caller may do to it was decided on the trip itself before the form opened.
    door.mockReturnValue(bySeveralClubs);
    show(trip({ cavingGroupId: 'g-9', visibility: 'cavingGroup' }));
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Digging weekend (5)' } });

    expect(screen.queryByTestId('trip-owning-group')).toBeNull();
    expect(screen.queryByTestId('trip-owning-group-choice')).toBeNull();
    const body = await savedBody(updateTrip);
    expect(body.cavingGroupId).toBe('g-9');
    expect(body.visibility).toBe('cavingGroup');
  });
});
