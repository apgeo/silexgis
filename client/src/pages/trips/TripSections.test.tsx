// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo, TripLogWrite } from '../../api/hooks.ts';

const updateTrip = vi.fn();

const fieldDataSchema = JSON.stringify({
  type: 'object',
  properties: {
    conditions: { type: 'string', title: 'Conditions underground' },
    club_specific: { type: 'string', title: 'Whatever this club asks' },
  },
});
const safetySchema = JSON.stringify({
  type: 'object',
  properties: {
    incident_summary: { type: 'string', title: 'What happened' },
    incident_severity: {
      type: 'string',
      title: 'Severity',
      enum: ['near_miss', 'minor', 'serious', 'rescue'],
    },
  },
});
const logisticsSchema = JSON.stringify({
  type: 'object',
  properties: {
    permit_holder_caver_id: {
      type: 'string',
      title: 'Permit holder',
      pattern: '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
    },
  },
});
const ANA = '11111111-2222-3333-4444-555555555555';

vi.mock('../../api/hooks.ts', () => ({
  useTripTypes: () => ({
    data: [
      {
        id: 1,
        code: 'survey',
        name: 'Survey',
        isSeeded: true,
        fieldDataSchema,
        fieldDataSchemaVersion: 1,
        logisticsSchema,
        logisticsSchemaVersion: 1,
        safetySchema,
        safetySchemaVersion: 1,
      },
    ],
  }),
  useCavers: () => ({ data: [{ id: ANA, name: 'Ana Pop' }] }),
  useUpdateTripLog: () => ({ mutateAsync: updateTrip, isPending: false }),
}));

const { default: TripSections } = await import('./TripSections.tsx');

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return {
    id: 'trip-1',
    title: 'Digging weekend',
    tripTypeId: 1,
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
    depthReachedM: null,
    lengthSurveyedM: null,
    surveyStations: null,
    ropeMetres: null,
    hadIncident: false,
    fieldData: {},
    logistics: {},
    safety: {},
    ...overrides,
  } as unknown as TripLogInfo;
}

function show(subject: TripLogInfo, canEdit: boolean) {
  return render(
    <App>
      <TripSections trip={subject} canEdit={canEdit} />
    </App>,
  );
}

/** Opens a collapsed section by clicking its header. */
function openSection(label: string) {
  fireEvent.click(screen.getByText(label));
}

beforeEach(() => {
  updateTrip.mockReset();
  updateTrip.mockResolvedValue({});
});
afterEach(cleanup);

describe('TripSections', () => {
  it('labels a shipped field in the reader’s language and an installation’s own as its schema wrote it', () => {
    show(trip(), true);
    openSection('Field data');

    // The shipped code is translated through the ordinary wording, so a schema whose titles
    // were written in English does not put English on a screen in another language.
    expect(screen.getByText('Conditions underground')).toBeTruthy();
    // A code this client does not ship falls back to the title the schema itself carries —
    // the only wording an installation's own field has.
    expect(screen.getByText('Whatever this club asks')).toBeTruthy();
  });

  it('sends one section and leaves the other two absent, keeping keys the schema no longer knows', async () => {
    show(trip({ fieldData: { conditions: 'wet', retired_key: 'kept' } as never }), true);
    openSection('Field data');

    fireEvent.click(screen.getByTestId('trip-section-save-fieldData'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalled());

    const body = (updateTrip.mock.calls[0][0] as { body: TripLogWrite }).body;
    expect(body.fieldData).toEqual({ conditions: 'wet', retired_key: 'kept' });
    // Absent, not emptied: a surface that drew one section must not blank the two it never
    // showed, and must not re-measure them against a schema that may have moved since.
    expect(body.logistics).toBeNull();
    expect(body.safety).toBeNull();
    // Nor may saving a section disturb which caves the trip's roles name.
    expect(body.caveIds).toBeNull();
  });

  it("keeps the trip's room for people when a section is saved", async () => {
    // The section save sends the whole trip, and it draws no control for the limit, so omitting
    // it clears the trip's room for people. Nothing on screen would say so — an unlimited trip
    // is what no limit means — while everybody who was waiting for a place is silently on it.
    show(trip({ maxParticipants: 8, fieldData: { conditions: 'wet' } as never }), true);
    openSection('Field data');

    fireEvent.click(screen.getByTestId('trip-section-save-fieldData'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalled());

    const body = (updateTrip.mock.calls[0][0] as { body: TripLogWrite }).body;
    expect(body.maxParticipants).toBe(8);
  });

  it('shows a person the schema names by their name, and offers the roster rather than an identifier', () => {
    // Read-only first: an identifier is not a person, and a reader shown a raw one learns
    // nothing the field was recorded to tell them.
    show(trip({ logistics: { permit_holder_caver_id: ANA } as never }), false);
    openSection('Logistics');
    expect(screen.getByTestId('trip-section-value-permit_holder_caver_id').textContent).toBe('Ana Pop');
    cleanup();

    // And when writing: a chooser over the roster, so the identity field is answerable at all —
    // a text box demanding an identifier is answered by typing a name into the note beside it,
    // which is precisely the free text the roster reference exists to avoid.
    show(trip(), true);
    openSection('Logistics');
    const picker = screen.getByTestId('trip-section-field-permit_holder_caver_id');
    expect(picker.querySelector('input')).toBeTruthy();
    expect(picker.className).toContain('ant-select');
  });

  it('keeps what somebody is typing when the trip is re-read under them', async () => {
    // Every write to the trip re-reads it, and the answer arrives as a new object seconds
    // later. Somebody typing in the meantime must not have it taken off the screen — and, the
    // part that loses work silently, must not have the next save serialise the copy that came
    // back instead of what they wrote.
    const before = trip({ fieldData: { conditions: 'wet' } as never });
    const { rerender } = show(before, true);
    openSection('Field data');

    fireEvent.change(screen.getByTestId('trip-section-field-club_specific'), {
      target: { value: 'half a sentence' },
    });

    // The re-read landing: a different object, carrying what the server holds, which is the
    // trip without the half sentence in it.
    rerender(
      <App>
        <TripSections trip={trip({ fieldData: { conditions: 'wet' } as never })} canEdit />
      </App>,
    );

    expect(screen.getByTestId('trip-section-field-club_specific')).toHaveValue('half a sentence');

    fireEvent.click(screen.getByTestId('trip-section-save-fieldData'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalled());
    const body = (updateTrip.mock.calls[0][0] as { body: TripLogWrite }).body;
    expect(body.fieldData).toEqual({ conditions: 'wet', club_specific: 'half a sentence' });
  });

  it('keeps a half-written section when a different section is saved under it', async () => {
    // A save carries one section and leaves the other two absent, so the re-read it triggers is
    // not an answer about them. Holding the whole card as one either-or would clear the guard on
    // a field-data save and let that re-read wipe a safety section somebody was part way
    // through — and the save after that would write the emptied bag out over their work.
    const { rerender } = show(trip(), true);
    openSection('Safety');
    openSection('Field data');

    fireEvent.change(screen.getByTestId('trip-section-field-incident_summary'), {
      target: { value: 'slip on the pitch head' },
    });
    fireEvent.change(screen.getByTestId('trip-section-field-conditions'), {
      target: { value: 'wet' },
    });
    fireEvent.click(screen.getByTestId('trip-section-save-fieldData'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalled());

    // The re-read: the server's copy, which has the saved field data and knows nothing of the
    // safety text, because that text was never sent.
    rerender(
      <App>
        <TripSections trip={trip({ fieldData: { conditions: 'wet' } as never })} canEdit />
      </App>,
    );

    expect(screen.getByTestId('trip-section-field-incident_summary')).toHaveValue(
      'slip on the pitch head',
    );

    fireEvent.click(screen.getByTestId('trip-section-save-safety'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalledTimes(2));
    const body = (updateTrip.mock.calls[1][0] as { body: TripLogWrite }).body;
    expect(body.safety).toEqual({ incident_summary: 'slip on the pitch head' });
  });

  it('never carries what was typed on one trip onto another', async () => {
    // The page is not remounted when it moves to another trip — the route element is the same
    // and a trip already read arrives without a loading pass, so this card is only handed a new
    // prop. Unsent text belongs to the trip it was typed on, and holding it back from the next
    // one would show one trip's report on another and then save it there.
    const { rerender } = show(trip({ fieldData: { conditions: 'wet' } as never }), true);
    openSection('Field data');

    fireEvent.change(screen.getByTestId('trip-section-field-club_specific'), {
      target: { value: 'about trip one' },
    });

    rerender(
      <App>
        <TripSections
          trip={trip({ id: 'trip-2', fieldData: { conditions: 'dry' } as never })}
          canEdit
        />
      </App>,
    );

    expect(screen.getByTestId('trip-section-field-club_specific')).toHaveValue('');
    expect(screen.getByTestId('trip-section-field-conditions')).toHaveValue('dry');

    fireEvent.click(screen.getByTestId('trip-section-save-fieldData'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalled());
    const call = updateTrip.mock.calls[0][0] as { id: string; body: TripLogWrite };
    expect(call.id).toBe('trip-2');
    expect(call.body.fieldData).toEqual({ conditions: 'dry' });
  });

  it('takes up an edit made elsewhere while nothing is being typed here', async () => {
    // The other half of the same rule, and the reason the card re-syncs at all: with nothing
    // half-written on it, a trip re-read because somebody else changed it must show what they
    // changed, and the next save must carry theirs rather than putting the old value back.
    const { rerender } = show(trip({ fieldData: { conditions: 'wet' } as never }), true);
    openSection('Field data');

    rerender(
      <App>
        <TripSections trip={trip({ fieldData: { conditions: 'flooded' } as never })} canEdit />
      </App>,
    );

    expect(screen.getByTestId('trip-section-field-conditions')).toHaveValue('flooded');

    fireEvent.click(screen.getByTestId('trip-section-save-fieldData'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalled());
    const body = (updateTrip.mock.calls[0][0] as { body: TripLogWrite }).body;
    expect(body.fieldData).toEqual({ conditions: 'flooded' });
  });

  it('keeps a counted fact somebody typed while a save of their own was in flight', async () => {
    // The window is between a save being sent and the re-read it causes coming back. Anything
    // typed inside it is not in the copy the server answers with, and is lost the moment the
    // answer lands unless the card knows it is being written on again.
    const { rerender } = show(trip(), true);
    openSection('Measured');

    fireEvent.click(screen.getByTestId('trip-section-save-measured'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalled());

    fireEvent.change(screen.getByTestId('trip-measure-depthReachedM'), { target: { value: '218' } });
    rerender(
      <App>
        <TripSections trip={trip()} canEdit />
      </App>,
    );

    expect(screen.getByTestId('trip-measure-depthReachedM')).toHaveValue('218');
    fireEvent.click(screen.getByTestId('trip-section-save-measured'));
    await vi.waitFor(() => expect(updateTrip).toHaveBeenCalledTimes(2));
    const body = (updateTrip.mock.calls[1][0] as { body: TripLogWrite }).body;
    expect(body.depthReachedM).toBe(218);
  });

  it('words a shipped choice in the reader’s language, in the control and in the reading', () => {
    show(trip({ safety: { incident_severity: 'near_miss' } as never }), false);
    openSection('Safety');
    // Not "near_miss": the label was translated, and a value left as its stored code puts the
    // schema's own language back on the screen underneath it.
    expect(screen.getByTestId('trip-section-value-incident_severity').textContent).toBe('Near miss');
  });

  it('tells a reader who may not see the safety account that it is withheld, and shows it to one who may', () => {
    // Withheld: the server sends nothing at all for the section rather than an empty object,
    // so the page can say so instead of drawing a blank that reads as "nothing happened".
    show(trip({ safety: null } as Partial<TripLogInfo>), false);
    openSection('Safety');
    expect(screen.getByTestId('trip-safety-withheld')).toBeTruthy();
    expect(screen.queryByText('What happened')).toBeNull();
    cleanup();

    // The positive half over the same fixture: a caller the server did answer sees the fields.
    show(trip({ safety: { incident_summary: 'slip on the pitch head' } as never }), true);
    openSection('Safety');
    expect(screen.queryByTestId('trip-safety-withheld')).toBeNull();
    expect(screen.getByText('What happened')).toBeTruthy();
  });
});
