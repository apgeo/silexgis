// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SpeleolocImportPreview, SpeleolocImportSession } from '../../api/hooks.ts';

const commitMutate = vi.fn();
const saveSessionMutate = vi.fn();
const navigate = vi.fn();
const uploadMutate = vi.fn();

let params: { fileId?: string } = { fileId: 'file-1' };
let document_: { id: string; title: string; currentFileId: string } | undefined;

/** Everything below is invented: a made-up cave, a made-up party and made-up identifiers. */
const RECORDING = '33333333-3333-4333-8333-333333333333';
const OTHER_RECORDING = '33333333-3333-4333-8333-333333333334';
const TRIP = '55555555-5555-4555-8555-555555555555';
const MODEL = '66666666-6666-4666-8666-666666666666';
const OTHER_MODEL = '66666666-6666-4666-8666-666666666667';
const CAVE = '77777777-7777-4777-8777-777777777777';
const ION = '11111111-1111-4111-8111-111111111111';
const MARIA = '22222222-2222-4222-8222-222222222222';
const DEVICE_MAPPED = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const DEVICE_UNMAPPED = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

const PROPOSED = '44444444-4444-4444-8444-444444444441';
const NO_MARKER = '44444444-4444-4444-8444-444444444442';
const UNMAPPED_SCAN = '44444444-4444-4444-8444-444444444443';
const TIED = '44444444-4444-4444-8444-444444444444';

const cavers = [
  { id: ION, name: 'Ion Inventat', userId: null, email: null, phone: null, notes: null, cavingGroups: [] },
  { id: MARIA, name: 'Maria Inventata', userId: null, email: null, phone: null, notes: null, cavingGroups: [] },
];

/**
 * A recording holding one scan of each kind that matters: one whose marker resolved to a single
 * station, one whose marker this installation does not hold at all, one made by a device account
 * nobody has named, and one whose depth reaches two stations equally well.
 */
const BASE_PREVIEW: SpeleolocImportPreview = {
  points: [
    {
      pointId: PROPOSED,
      scannedAt: '2024-05-04T09:12:00Z',
      notes: null,
      placeId: '88888888-8888-4888-8888-888888888881',
      placeTitle: 'reper 1',
      placeDepthM: 41.2,
      deviceUserId: DEVICE_MAPPED,
      caverId: ION,
      state: 'proposed',
      candidates: [
        { stationName: 'A12', surveyName: 'galeria mare', depthM: 41, deltaM: -0.2 },
        { stationName: 'A13', surveyName: 'galeria mare', depthM: 43, deltaM: 1.8 },
      ],
      decision: null,
    },
    {
      pointId: NO_MARKER,
      scannedAt: '2024-05-04T09:40:00Z',
      notes: null,
      placeId: '88888888-8888-4888-8888-888888888882',
      placeTitle: null,
      placeDepthM: null,
      deviceUserId: DEVICE_MAPPED,
      caverId: ION,
      state: 'placeUnknown',
      candidates: [],
      decision: null,
    },
    {
      pointId: UNMAPPED_SCAN,
      scannedAt: '2024-05-04T10:05:00Z',
      notes: null,
      placeId: '88888888-8888-4888-8888-888888888881',
      placeTitle: 'reper 1',
      placeDepthM: 41.2,
      deviceUserId: DEVICE_UNMAPPED,
      caverId: null,
      state: 'proposed',
      candidates: [{ stationName: 'A12', surveyName: 'galeria mare', depthM: 41, deltaM: -0.2 }],
      decision: null,
    },
    {
      pointId: TIED,
      scannedAt: '2024-05-04T10:30:00Z',
      notes: null,
      placeId: '88888888-8888-4888-8888-888888888883',
      placeTitle: 'reper 2',
      placeDepthM: 60,
      deviceUserId: DEVICE_MAPPED,
      caverId: ION,
      state: 'proposed',
      // Two branches of the survey at the same horizon. The nearest is a coin toss, not an answer.
      candidates: [
        { stationName: 'B4', surveyName: 'galeria mare', depthM: 60.5, deltaM: 0.5 },
        { stationName: 'C7', surveyName: 'ramura din dreapta', depthM: 59.5, deltaM: -0.5 },
      ],
      decision: null,
    },
  ],
  page: 1,
  pageSize: 25,
  totalItems: 4,
  // The server's own answer over the whole recording: the scan with no marker cannot be taken, and
  // neither can the one whose device account names nobody.
  selectablePointIds: [PROPOSED, TIED],
  deviceUsers: [DEVICE_MAPPED, DEVICE_UNMAPPED],
  unmappedDeviceUsers: [DEVICE_UNMAPPED],
  caversNotOnRoster: [],
  modelUsable: true,
  proposedCount: 3,
  unresolvedCount: 1,
  recordingTitle: 'tura inventata',
  recordingStartedAt: '2024-05-04T08:00:00Z',
  recordingEndedAt: '2024-05-04T14:00:00Z',
  documentCount: 2,
};

let preview: SpeleolocImportPreview = BASE_PREVIEW;

/** What the archive is read as holding. Emptied by the test for an archive with nothing in it. */
const ONE_RECORDING = [
  {
    id: RECORDING,
    title: 'tura inventata',
    caveTitle: 'pestera 1',
    startedAt: '2024-05-04T08:00:00Z',
    endedAt: '2024-05-04T14:00:00Z',
    deviceUserId: DEVICE_MAPPED,
    pointCount: 4,
    documentCount: 2,
  },
];
let archiveRecordings: typeof ONE_RECORDING = ONE_RECORDING;

const READY_OPTIONS: SpeleolocImportSession['options'] = {
  tripUuid: RECORDING,
  tripLogId: TRIP,
  createTrip: false,
  surveyModelId: MODEL,
  cavers: { [DEVICE_MAPPED]: ION },
  cavingGroupId: null,
  visibility: 'private',
  candidateCount: 5,
};

let session: SpeleolocImportSession = {
  fileId: 'file-1',
  fileName: 'export.zip',
  options: READY_OPTIONS,
  decisions: {},
  positionsWithheld: false,
  updatedAt: null,
};

vi.mock('../../api/hooks.ts', () => ({
  useSpeleolocImportSession: () => ({ data: session, isError: false, error: null }),
  useSaveSpeleolocImportSession: () => ({ mutate: saveSessionMutate }),
  useSpeleolocImportPreview: () => ({ data: preview, isFetching: false, error: null }),
  useCommitSpeleolocImport: () => ({ mutateAsync: commitMutate, isPending: false }),
  useSpeleolocRecordings: () => ({
    data: { fileId: 'file-1', fileName: 'export.zip', recordings: archiveRecordings },
    isFetching: false,
    isError: false,
    error: null,
  }),
  useUploadFile: () => ({ mutateAsync: uploadMutate, isPending: false }),
  useDocument: () => ({ data: document_ }),
  useCavers: () => ({ data: cavers, isFetching: false }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useCaves: () => ({ data: { items: [{ id: CAVE, name: 'pestera 1' }] }, isFetching: false }),
  useSurveyModel: () => ({ data: { id: MODEL, caveId: CAVE, name: 'ridicare inventata' } }),
  useSurveyModels: () => ({
    data: [
      { id: MODEL, name: 'ridicare inventata' },
      { id: OTHER_MODEL, name: 'alta ridicare' },
    ],
    isFetching: false,
  }),
  useTripLog: () => ({ data: { id: TRIP, title: 'tura inventata', tripDate: '2024-05-04' } }),
  useTripLogs: () => ({ data: { items: [] }, isFetching: false }),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => params, useNavigate: () => navigate };
});

const { default: SpeleolocImportPage } = await import('./SpeleolocImportPage.tsx');

/**
 * A fresh element every time: rerendering the same one lets React skip the subtree, and the test
 * that opens a second archive depends on the page being rendered again under a new route parameter.
 */
function tree() {
  return (
    <MemoryRouter>
      <App>
        <SpeleolocImportPage />
      </App>
    </MemoryRouter>
  );
}

function show() {
  return render(tree());
}

/** Long enough to outlast the autosave's 800 ms debounce with room to spare. */
const afterTheDebounce = () => new Promise((resolve) => setTimeout(resolve, 1600));

afterEach(cleanup);

beforeEach(() => {
  params = { fileId: 'file-1' };
  document_ = undefined;
  preview = BASE_PREVIEW;
  archiveRecordings = ONE_RECORDING;
  session = {
    fileId: 'file-1',
    fileName: 'export.zip',
    options: READY_OPTIONS,
    decisions: {},
    positionsWithheld: false,
    updatedAt: null,
  };
  commitMutate.mockReset();
  saveSessionMutate.mockReset();
  navigate.mockReset();
  uploadMutate.mockReset();
});

describe('SpeleolocImportPage', () => {
  it('says a scan nothing could be proposed for is unresolved, rather than leaving it looking untouched', async () => {
    show();

    // The reason is on the row, in the place a proposal would have been. An empty cell here reads
    // as "nobody has got to this one yet", which is a different thing entirely.
    const state = await screen.findByTestId(`speleoloc-import-state-${NO_MARKER}`);
    expect(state.textContent).toContain('This installation does not hold that marker');

    // And there is no station control at all — nothing to accept, nothing pre-filled.
    expect(screen.queryByTestId(`speleoloc-import-station-${NO_MARKER}`)).toBeNull();
    // The switch cannot be turned on either: the confirmation would refuse the row, and a live
    // switch would be an offer this screen cannot keep.
    expect(screen.getByTestId(`speleoloc-import-take-${NO_MARKER}`)).toHaveProperty(
      'disabled',
      true,
    );

    // The count above the table says how many are in that position, over the whole recording
    // rather than over the page on screen.
    const notice = screen.getByTestId('speleoloc-import-unresolved-notice');
    expect(notice.textContent).toContain('1 scans have no station');
  });

  it('shows the station a scan with no proposal would be recorded at, instead of calling it left out', async () => {
    // A row whose marker this installation does not hold, carrying a station the reviewer typed in
    // themselves. The confirmation records it there — so the screen may not draw it as one that was
    // left alone, which is what it did while the station control was hidden for every such state.
    const named = { action: 'record' as const, stationName: 'A12', caverId: null };
    preview = {
      ...BASE_PREVIEW,
      points: BASE_PREVIEW.points.map((point) =>
        point.pointId === NO_MARKER ? { ...point, decision: named } : point,
      ),
      selectablePointIds: [PROPOSED, NO_MARKER, TIED],
    };
    session = { ...session, decisions: { [NO_MARKER]: named } };
    show();

    // The reason still stands, because it is true of the marker...
    const state = await screen.findByTestId(`speleoloc-import-state-${NO_MARKER}`);
    expect(state.textContent).toContain('This installation does not hold that marker');
    // ...and the station that will actually be written is beside it rather than nowhere.
    expect(screen.getByTestId(`speleoloc-import-named-${NO_MARKER}`).textContent).toContain('A12');

    // The switch, the count and the confirmation say the same thing about that row.
    const take = screen.getByTestId(`speleoloc-import-take-${NO_MARKER}`);
    expect(take).toHaveProperty('disabled', false);
    expect(take.getAttribute('aria-checked')).toBe('true');
    await waitFor(() =>
      expect(screen.getByTestId('speleoloc-import-commit').textContent).toContain('3'),
    );

    // And taking the name back returns the row to being left out, in one act.
    fireEvent.click(screen.getByTestId(`speleoloc-import-clear-station-${NO_MARKER}`));
    await waitFor(() =>
      expect(screen.getByTestId('speleoloc-import-commit').textContent).toContain('2'),
    );
    expect(screen.queryByTestId(`speleoloc-import-named-${NO_MARKER}`)).toBeNull();
    expect(screen.getByTestId(`speleoloc-import-take-${NO_MARKER}`)).toHaveProperty(
      'disabled',
      true,
    );
  });

  it('forgets the stations chosen under a survey model when the model is changed', async () => {
    // A station name is a name in one model's vocabulary. Left standing across a change of model it
    // would go on making the scan takeable in the dry run while the confirmation refused it as a
    // station the model does not hold — the one thing a dry run may not do.
    session = {
      ...session,
      decisions: { [PROPOSED]: { action: 'record', stationName: 'A13', caverId: null } },
    };
    show();

    const model = await screen.findByTestId('speleoloc-import-survey-model');
    fireEvent.mouseDown(within(model).getByRole('combobox'));
    fireEvent.click(await screen.findByTitle('alta ridicare'));

    await waitFor(
      () =>
        expect(saveSessionMutate).toHaveBeenCalledWith(
          expect.objectContaining({
            fileId: 'file-1',
            body: expect.objectContaining({
              options: expect.objectContaining({ surveyModelId: OTHER_MODEL }),
              decisions: { [PROPOSED]: { action: 'record', stationName: null, caverId: null } },
            }),
          }),
        ),
      { timeout: 5000 },
    );
  });

  it('marks a depth that reaches two stations equally well as the unsettled thing it is', async () => {
    show();

    const tied = await screen.findByTestId(`speleoloc-import-state-${TIED}`);
    expect(tied.textContent).toContain('Several stations at that depth');

    // The one whose depth reached a single station is not dressed up the same way.
    expect(screen.getByTestId(`speleoloc-import-state-${PROPOSED}`).textContent).toContain(
      'Proposed',
    );
  });

  it('records the scans the server calls selectable, less the ones just set aside', async () => {
    show();

    // The scan made by a device account nobody has named is not among them, and neither is the
    // one with no marker — both would be refused by the confirmation.
    await waitFor(() =>
      expect(screen.getByTestId('speleoloc-import-commit').textContent).toContain('2'),
    );

    fireEvent.click(screen.getByTestId(`speleoloc-import-take-${TIED}`));
    await waitFor(() =>
      expect(screen.getByTestId('speleoloc-import-commit').textContent).toContain('1'),
    );

    commitMutate.mockResolvedValue({
      batchId: 'b1',
      tripLogId: TRIP,
      createdTrip: false,
      createdEventCount: 1,
      skippedCount: 1,
      failures: [],
    });
    fireEvent.click(screen.getByTestId('speleoloc-import-commit'));

    await waitFor(() => expect(commitMutate).toHaveBeenCalled());
    const body = commitMutate.mock.calls[0][0].body;
    expect(body.pointIds).toEqual([PROPOSED]);
    expect(body.decisions[TIED]).toEqual({ action: 'skip' });
  });

  it('leaves the review standing when the confirmation records nothing', async () => {
    show();

    // Every chosen scan failed, so the server wrote nothing and refused the whole act. What must
    // survive that is the review: the stations chosen, the people mapped, the selection.
    commitMutate.mockRejectedValue(
      new ApiError(400, 'speleoloc_import.nothing_created', 'None of the chosen scans could be recorded.', {
        code: 'speleoloc_import.nothing_created',
      }),
    );

    const button = await screen.findByTestId('speleoloc-import-commit');
    expect(button.textContent).toContain('2');
    fireEvent.click(button);
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));

    // No result modal, because nothing was done.
    expect(screen.queryByTestId('speleoloc-import-result')).toBeNull();
    // The selection is exactly what it was, so the same confirmation can be tried again once
    // whatever the refusal named has been dealt with.
    expect(screen.getByTestId('speleoloc-import-commit').textContent).toContain('2');
    expect(screen.getByTestId('speleoloc-import-commit')).toHaveProperty('disabled', false);
    fireEvent.click(screen.getByTestId('speleoloc-import-commit'));
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(2));
    expect(commitMutate.mock.calls[1][0].body.pointIds).toEqual([PROPOSED, TIED]);
  });

  it('never says who a device account is until somebody does', async () => {
    show();

    // The standing warning names the cost before anybody works through two hundred rows.
    const warning = await screen.findByTestId('speleoloc-import-people-unmapped');
    expect(warning.textContent).toContain('1 device accounts name nobody');

    // The chooser for the unnamed account opens empty. Not "the most likely caver", not the one
    // who appears most often in the recording — a device account is a login on a phone, and a
    // filled-in box would be read as an answer by everybody who did not stop to check.
    const chooser = await screen.findByTestId(
      `speleoloc-import-map-${DEVICE_UNMAPPED.slice(0, 8)}`,
    );
    expect(chooser.querySelector('.ant-select-selection-item')).toBeNull();
    expect(chooser.textContent).toContain('Say who this is');

    // And the scan that account made says so on its own row rather than being attributed.
    expect(screen.getByTestId(`speleoloc-import-unmapped-${UNMAPPED_SCAN}`).textContent).toContain(
      'Nobody says who this account is',
    );
    // Its switch is dead while that is true: the confirmation refuses a scan nobody has named, so
    // offering to take it would be a promise this screen cannot keep.
    expect(screen.getByTestId(`speleoloc-import-take-${UNMAPPED_SCAN}`)).toHaveProperty(
      'disabled',
      true,
    );

    fireEvent.mouseDown(within(chooser).getByRole('combobox'));
    fireEvent.click(await screen.findByTitle('Maria Inventata'));

    await waitFor(
      () =>
        expect(saveSessionMutate).toHaveBeenCalledWith(
          expect.objectContaining({
            body: expect.objectContaining({
              options: expect.objectContaining({
                cavers: { [DEVICE_MAPPED]: ION, [DEVICE_UNMAPPED]: MARIA },
              }),
            }),
          }),
        ),
      // The autosave is debounced by 800ms on purpose, so this wait has to outlast it by a clear
      // margin — the default one-second budget is close enough to the debounce that the test
      // passes alone and fails under a loaded suite.
      { timeout: 5000 },
    );
  });

  it('opens a second archive on its own review, and does not save the first one over it', async () => {
    const { rerender } = show();

    // A decision on the first archive, saved under the first archive.
    fireEvent.click(await screen.findByTestId(`speleoloc-import-take-${TIED}`));
    await waitFor(() => expect(saveSessionMutate).toHaveBeenCalled(), { timeout: 5000 });
    expect(saveSessionMutate.mock.calls.every((call) => call[0].fileId === 'file-1')).toBe(true);

    // The reviewer finishes with it and drops a second archive. The two routes are the same screen,
    // so nothing here is built again: what must not happen is the first archive's review staying on
    // it and then being written over the second archive's stored one.
    params = { fileId: 'file-2' };
    session = {
      fileId: 'file-2',
      fileName: 'alta-arhiva.zip',
      options: { ...READY_OPTIONS, tripUuid: OTHER_RECORDING, tripLogId: null, createTrip: true },
      decisions: {},
      positionsWithheld: false,
      updatedAt: null,
    };
    rerender(tree());

    const forSecond = () =>
      saveSessionMutate.mock.calls.filter((call) => call[0].fileId === 'file-2');
    await waitFor(() => expect(forSecond().length).toBeGreaterThan(0), { timeout: 5000 });

    for (const call of forSecond()) {
      // Nothing of the first review reaches the second archive's row, which a save replaces whole.
      expect(call[0].body.decisions).toEqual({});
      expect(call[0].body.options.tripUuid).toBe(OTHER_RECORDING);
      expect(call[0].body.options.createTrip).toBe(true);
    }
  });

  it('saves nothing while the stored review is being withheld, so opening it cannot destroy it', async () => {
    // The decisions are still stored and the server says so while handing back none of them. What
    // is on screen is an empty set standing in for a review that exists — and a save replaces
    // rather than merges, so saving it would be the page destroying the review by being opened.
    session = { ...session, decisions: {}, positionsWithheld: true };
    show();

    const banner = await screen.findByTestId('speleoloc-import-withheld');
    expect(banner.textContent).toContain('The review is still stored');
    // Said on the screen too, because a page that silently stops saving is its own defect.
    expect(banner.textContent).toContain('Nothing changed on this page is saved');

    await afterTheDebounce();
    expect(saveSessionMutate).not.toHaveBeenCalled();
  });

  it('will not narrow the stations offered to one, which would hide every tie', async () => {
    // The offers are cut to this number by the server. Cut to one, a depth that reaches two
    // branches equally well comes back as a single station and the row reads as an ordinary
    // proposal — the tie removed by a setting, with nothing on the screen saying so.
    session = { ...session, options: { ...READY_OPTIONS, candidateCount: 1 } };
    show();

    const offered = await screen.findByRole('spinbutton');
    expect(offered.getAttribute('aria-valuemin')).toBe('2');
    expect((offered as HTMLInputElement).value).toBe('2');

    await waitFor(
      () =>
        expect(saveSessionMutate).toHaveBeenCalledWith(
          expect.objectContaining({
            body: expect.objectContaining({
              options: expect.objectContaining({ candidateCount: 2 }),
            }),
          }),
        ),
      { timeout: 5000 },
    );
  });

  it('says an archive holds no recordings rather than asking for one that is not there', async () => {
    archiveRecordings = [];
    session = { ...session, options: { ...READY_OPTIONS, tripUuid: null } };
    show();

    expect(await screen.findByTestId('speleoloc-import-archive-empty')).toBeTruthy();

    // And the list of what is missing says that, rather than instructing an impossible act.
    const blockers = screen.getByTestId('speleoloc-import-blockers');
    expect(within(blockers).getByTestId('speleoloc-import-blocker-archiveEmpty')).toBeTruthy();
    expect(within(blockers).queryByTestId('speleoloc-import-blocker-recording')).toBeNull();
  });

  it('offers the review that already exists when the same archive is uploaded twice', async () => {
    params = {};
    document_ = { id: 'doc-7', title: 'export.zip', currentFileId: 'file-9' };
    // The identifier is read from the refusal's own member, never out of its sentence — which is
    // deliberately worded here so that nothing could be recovered from the prose.
    uploadMutate.mockRejectedValue(
      new ApiError(409, 'file.duplicate', 'Fișierul este deja stocat.', {
        code: 'file.duplicate',
        duplicateOfDocumentId: 'doc-7',
      }),
    );
    show();

    fireEvent.change(document.querySelector('input[type="file"]')!, {
      target: { files: [new File(['not a real archive'], 'export.zip', { type: 'application/zip' })] },
    });

    const notice = await screen.findByTestId('speleoloc-import-duplicate');
    expect(notice).toBeTruthy();
    fireEvent.click(screen.getByTestId('speleoloc-import-duplicate-resume'));
    // Not a dead end: the reader lands on the review of the file the store already holds.
    expect(navigate).toHaveBeenCalledWith('/trip-logs/speleoloc-import/file-9');
  });

  it('names the people whose scans the chosen trip has no place for', async () => {
    // A mapping can be complete and still cost every scan it covers: putting somebody on a trip is
    // a statement about who went, and this import will not make it. The server states who, so the
    // remedy is one act rather than a hunt through the rows.
    preview = { ...BASE_PREVIEW, caversNotOnRoster: [ION], selectablePointIds: [] };
    show();

    const alert = await screen.findByTestId('speleoloc-import-people-off-roster');
    expect(alert.textContent).toContain('1 people you named are not on this trip');
    expect(alert.textContent).toContain('Ion Inventat');

    // And their scans are dead rather than takeable, for the same reason: the confirmation would
    // refuse every one of them.
    expect(screen.getByTestId(`speleoloc-import-take-${PROPOSED}`)).toHaveProperty('disabled', true);
    expect(screen.getByTestId('speleoloc-import-commit').textContent).toContain('0');
    expect(screen.getByTestId('speleoloc-import-commit')).toHaveProperty('disabled', true);
  });

  it('will not offer to confirm what the server would refuse at the button', async () => {
    // A review with a recording chosen and nothing else: no destination, no survey model. Both are
    // refusals the confirmation answers with — and both are knowable before it is pressed.
    session = {
      ...session,
      options: { ...READY_OPTIONS, tripLogId: null, createTrip: false, surveyModelId: null },
    };
    show();

    const blockers = await screen.findByTestId('speleoloc-import-blockers');
    expect(within(blockers).getByTestId('speleoloc-import-blocker-destination')).toBeTruthy();
    expect(within(blockers).getByTestId('speleoloc-import-blocker-model')).toBeTruthy();
    expect(screen.getByTestId('speleoloc-import-commit')).toHaveProperty('disabled', true);
    expect(commitMutate).not.toHaveBeenCalled();
  });

  it('will not record the same scans twice after a confirmation has gone through', async () => {
    show();

    commitMutate.mockResolvedValue({
      batchId: 'b1',
      tripLogId: TRIP,
      createdTrip: true,
      createdEventCount: 2,
      skippedCount: 0,
      failures: [],
    });
    fireEvent.click(await screen.findByTestId('speleoloc-import-commit'));
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));

    // The scans that became positions are set aside, so the selection recomputed from the preview
    // still in hand cannot offer to record them a second time — which would be reversible only as
    // a separate act of its own.
    await waitFor(() =>
      expect(screen.getByTestId('speleoloc-import-commit')).toHaveProperty('disabled', true),
    );
    expect(screen.getByTestId('speleoloc-import-commit').textContent).toContain('0');
    fireEvent.click(screen.getByTestId('speleoloc-import-commit'));
    expect(commitMutate).toHaveBeenCalledTimes(1);
  });

  it('keeps a scan that failed taken, so the mapping behind it can be fixed and tried again', async () => {
    show();

    commitMutate.mockResolvedValue({
      batchId: 'b1',
      tripLogId: TRIP,
      createdTrip: false,
      createdEventCount: 1,
      skippedCount: 0,
      failures: [
        {
          pointId: TIED,
          scannedAt: '2024-05-04T10:30:00Z',
          code: 'speleoloc_import.station_unknown',
          reason: 'That station is not one of the chosen model’s stations.',
        },
      ],
    });
    fireEvent.click(await screen.findByTestId('speleoloc-import-commit'));
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));

    // The result says why, in the screen's own words rather than in the server's sentence.
    const failures = await screen.findByTestId('speleoloc-import-result-failures');
    expect(failures.textContent).toContain('1 scans could not be recorded');
    expect(screen.getByTestId('speleoloc-import-result').textContent).toContain(
      'The station named is not one of the chosen model',
    );

    // Closing the result leaves that one scan taken and the recorded one set aside.
    fireEvent.click(within(screen.getByTestId('speleoloc-import-result')).getByText('Close'));
    await waitFor(() =>
      expect(screen.getByTestId('speleoloc-import-commit').textContent).toContain('1'),
    );
  });
});
