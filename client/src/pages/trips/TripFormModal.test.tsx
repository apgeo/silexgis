// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import dayjs from 'dayjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo, TripLogWrite } from '../../api/hooks.ts';

const createTrip = vi.fn();
const updateTrip = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: [] }),
  useTripTypes: () => ({ data: [{ id: 1, code: 'survey', name: 'Survey / mapping', isSeeded: true }] }),
  useSearch: () => ({ data: undefined }),
  useCreateTripLog: () => ({ mutateAsync: createTrip, isPending: false }),
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

function show(subject: TripLogInfo | null) {
  return render(
    <App>
      <TripFormModal open trip={subject} onClose={() => {}} />
    </App>,
  );
}

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

    fireEvent.click(screen.getByRole('button', { name: /Clear shape/ }));

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

  it('leaves a one-day trip without an end date rather than a range of itself', async () => {
    // Editing a single-day trip fills both ends of the picker with the same day; storing that
    // as an end date would make the trip read as "14/03/2026 – 14/03/2026" everywhere after.
    show(trip());

    const body = await savedBody(updateTrip);
    expect(body.tripDate).toBe('2026-03-14');
    expect(body.tripDateEnd).toBeNull();
  });
});
