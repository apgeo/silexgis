// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripLogInfo } from '../../api/hooks.ts';

const ANA = '11111111-2222-3333-4444-555555555555';
const BOGDAN = '22222222-3333-4444-5555-666666666666';
const CAVE = '33333333-4444-5555-6666-777777777777';

const safetySchema = JSON.stringify({
  type: 'object',
  properties: {
    incident_summary: { type: 'string', title: 'What happened' },
  },
});
const fieldDataSchema = JSON.stringify({
  type: 'object',
  properties: {
    conditions: { type: 'string', title: 'Conditions underground' },
  },
});

const { tripSpy, photosSpy } = vi.hoisted(() => ({ tripSpy: vi.fn(), photosSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useTripLog: () => tripSpy(),
  usePhotos: () => photosSpy(),
  useTripTypes: () => ({
    data: [
      {
        id: 1,
        code: 'survey',
        name: 'Survey',
        isSeeded: true,
        fieldDataSchema,
        fieldDataSchemaVersion: 1,
        logisticsSchema: null,
        logisticsSchemaVersion: 1,
        safetySchema,
        safetySchemaVersion: 1,
      },
    ],
  }),
  useCavingGroups: () => ({ data: [{ id: 'club-1', name: 'Clubul Speo' }] }),
  useTripParticipantRoles: () => ({
    data: [
      { id: 10, code: 'participant', name: 'Participant' },
      { id: 11, code: 'surveyor', name: 'Surveyor' },
      { id: 12, code: 'proposer', name: 'Proposer' },
    ],
  }),
  useCavers: () => ({ data: [{ id: ANA, name: 'Ana Pop' }] }),
  useCapabilities: () => ({ data: { domains: { documents: 'read' } } }),
  useCave: () => ({ data: { id: CAVE, name: 'Peștera Mare' } }),
  useTripReportTemplates: () => ({ data: [] }),
  useEffectiveAccess: () => ({ data: undefined }),
  useCan: () => false,
  useKeepTripReport: () => ({ mutate: vi.fn(), isPending: false }),
  parseAccessActions: () => new Set<string>(),
  hasAccessAction: () => true,
}));

// The document is fetched with the caller's token and handed to the browser as a file, neither of
// which a jsdom test can do or is about.
vi.mock('../../api/download.ts', () => ({
  downloadFile: vi.fn(async () => {}),
  tripReportUrl: (id: string) => `/api/v1/trip-logs/${id}/report`,
}));

// The sketch is drawn by a real map, which wants a browser; the report's own contribution is what
// it prints in the map's place, and that is what these tests are about.
vi.mock('./TripGeometryField.tsx', () => ({
  default: () => <div data-testid="stub-trip-map" />,
}));
vi.mock('./TripRoleFields.tsx', () => ({ default: () => <div data-testid="stub-role-fields" /> }));
vi.mock('../../components/trips/TripCover.tsx', () => ({ default: () => null }));

const { default: TripReportPage } = await import('./TripReportPage.tsx');

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return {
    id: 'trip-1',
    title: 'Digging weekend',
    tripTypeId: 1,
    tripDate: '2026-03-14',
    tripDateEnd: null,
    entryTime: '09:00:00',
    exitTime: '15:30:00',
    description: 'Two days at the far end of the meander.',
    results: 'Four metres of new passage.',
    weatherConditions: 'Cold and dry',
    locationText: 'Padiș',
    organizingCavingGroupId: 'club-1',
    geom: null,
    caveIds: [CAVE],
    participants: [
      {
        caverId: ANA,
        name: 'Ana Pop',
        userId: null,
        roleId: 10,
        entryTime: null,
        exitTime: null,
        note: null,
      },
      {
        caverId: ANA,
        name: 'Ana Pop',
        userId: null,
        roleId: 11,
        entryTime: '09:00:00',
        exitTime: '12:00:00',
        note: 'Turned back at the pitch head',
      },
      {
        caverId: BOGDAN,
        name: 'Bogdan Ilie',
        userId: null,
        roleId: 10,
        entryTime: null,
        exitTime: null,
        note: null,
      },
    ],
    proposers: [],
    ownerUserId: 'owner-1',
    cavingGroupId: null,
    visibility: 'private',
    createdAt: '2026-03-15T10:00:00Z',
    updatedAt: '2026-03-15T10:00:00Z',
    state: 'published',
    publishedAt: null,
    depthReachedM: 42,
    lengthSurveyedM: null,
    surveyStations: 7,
    ropeMetres: null,
    hadIncident: true,
    fieldData: { conditions: 'wet' },
    fieldDataSchemaVersion: 1,
    logistics: {},
    logisticsSchemaVersion: 1,
    safety: { incident_summary: 'Rope jammed at the third pitch' },
    safetySchemaVersion: 1,
    ...overrides,
  } as unknown as TripLogInfo;
}

function show(subject: TripLogInfo, photos: unknown[] = []) {
  tripSpy.mockReturnValue({ data: subject, isPending: false });
  photosSpy.mockReturnValue({ data: { items: photos }, isPending: false });
  return render(
    <MemoryRouter initialEntries={['/trip-logs/trip-1/report']}>
      <Routes>
        <Route path="/trip-logs/:id/report" element={<TripReportPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

afterEach(cleanup);

describe('TripReportPage', () => {
  it('reads as the write-up of one trip: what it was for, who was there, and what it did', () => {
    const { container } = show(trip());

    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe('Digging weekend');
    expect(container.textContent).toContain('Clubul Speo');
    expect(container.textContent).toContain('Padiș');
    // The cave the trip names, by name — resolved from the list the trip's own read handed over.
    expect(screen.getByText('Peștera Mare')).toBeTruthy();
    expect(container.textContent).toContain('Four metres of new passage.');
    expect(container.textContent).toContain('Two days at the far end of the meander.');
    // What it measured, in the one unit everything is stored in.
    expect(container.textContent).toContain('42');
    expect(container.textContent).toContain('7');
  });

  /**
   * The roster is one row per person and job, so somebody who was there and surveyed is two rows
   * and one caver. A report that counted rows would say a club sent more people underground than
   * it did.
   */
  it('counts the people on the roster and not the rows', () => {
    const { container } = show(trip());
    expect(container.textContent).toContain('People: 2');
    // The note is written out rather than clipped behind a hover: on paper there is no hover.
    expect(screen.getByTestId('trip-report-roster').textContent).toContain(
      'Turned back at the pitch head',
    );
    expect(screen.getByTestId('trip-report-roster').textContent).toContain('09:00 – 12:00');
  });

  /**
   * The account of what went wrong is decided in one place, on the server, and arrives as nothing
   * at all rather than as an empty object for a reader who may not change the trip. Both sides are
   * asserted here: given the account the report prints it, and withheld it, the report is one
   * without that part — not one with an empty heading where it would have been, which on a
   * circulated document would read as a record that nothing happened.
   */
  it('prints the safety account when it is given and omits the part entirely when it is withheld', () => {
    const withAccount = show(trip());
    expect(withAccount.container.textContent).toContain('Rope jammed at the third pitch');
    expect(screen.getByTestId('trip-report-safety')).toBeTruthy();
    cleanup();

    const withheld = show(trip({ safety: null }));
    expect(withheld.container.textContent).not.toContain('Rope jammed');
    expect(screen.queryByTestId('trip-report-safety')).toBeNull();
    // Whether something went wrong is on the trip itself and stays for every reader; only the
    // account of it is withheld.
    expect(screen.getByTestId('trip-report-incident')).toBeTruthy();
    // And the rest of the report is still there.
    expect(withheld.container.textContent).toContain('Four metres of new passage.');
  });

  /**
   * A map is painted on a canvas while somebody looks at it, so a print of the page gets whatever
   * the canvas happened to hold — often nothing. The written position is what the printed report
   * carries instead, and it is the trip's own sketch, which every reader of the trip sees exactly.
   */
  it('writes the sketch out in words and coordinates for the printed copy', () => {
    show(trip({ geom: { type: 'Point', coordinates: [22.76314, 46.81953] } as never }));

    const sketch = screen.getByTestId('trip-report-sketch');
    expect(sketch.textContent).toContain('46.81953° N');
    expect(sketch.textContent).toContain('22.76314° E');
  });

  it('draws no sketch at all for a trip that has none', () => {
    show(trip());
    expect(screen.queryByTestId('trip-report-sketch')).toBeNull();
    expect(screen.queryByTestId('stub-trip-map')).toBeNull();
  });

  /**
   * A photograph goes into the report as the rendering the gallery shows, never as the stored
   * file: the original carries where it was taken, and a caller who may see the trip is not
   * thereby entitled to that.
   */
  it('lays the pictures in as renderings rather than as the files behind them', () => {
    show(trip(), [
      {
        documentId: 'doc-1',
        title: 'The dig face',
        thumbnailUrl: '/api/v1/files/f1/thumbnail?size=480&token=x',
        previewUrl: '/api/v1/files/f1/thumbnail?size=2400&token=x',
        contentUrl: '/api/v1/files/f1/content?token=x',
        width: 100,
        height: 80,
        originalName: 'IMG_0001.jpg',
        mayDownloadOriginal: false,
        credit: { caption: 'The dig face', photographerName: 'Ana Pop', licenceCode: null, placeName: null },
        photo: null,
      },
    ]);

    const image = screen.getByAltText('The dig face');
    expect(image.getAttribute('src')).toBe('/api/v1/files/f1/thumbnail?size=480&token=x');
    expect(screen.getByTestId('trip-report-plates').textContent).toContain('Ana Pop');
  });
});
