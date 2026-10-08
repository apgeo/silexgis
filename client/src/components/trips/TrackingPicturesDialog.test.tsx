// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';

const attach = vi.fn();

/** What the trip's gallery answers with. Two pictures with their own instants, one without. */
let photos: {
  documentId: string;
  title: string;
  takenAt: string | null;
}[] = [];

/** One report of the trip's log, as the list route hands it over. */
interface Report {
  id: string;
  caverId: string;
  teamId: null;
  kind: string;
  surveyModelId: string | null;
  stationName: string | null;
  depthEnteredM: number | null;
  note: null;
  recordedAt: string;
}

/** The trip's whole log as this reader is sent it, or undefined while it has not been read. */
let reports: Report[] | undefined = [];
/** Whether the read of the log failed. */
let logFailed = false;
/** Whether the dialog asked for the log at all. */
const logAsked = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  usePhotos: () => ({ data: { items: photos }, isPending: false }),
  useAttachTrackingPictures: () => ({ mutate: attach, isPending: false }),
  useTripTrackingEventLog: (_tripLogId: string, enabled: boolean) => {
    logAsked(enabled);
    return enabled
      ? { data: logFailed ? undefined : reports, isError: logFailed }
      : { data: undefined, isError: false };
  },
}));

const { default: TrackingPicturesDialog } = await import('./TrackingPicturesDialog.tsx');

const CAVERS = [
  { caverId: 'caver-1', name: 'Ana' },
  { caverId: 'caver-2', name: 'Bogdan' },
];

/** The moment the dialog is opened at — a report's own hour, or the scrubber's. */
const OPENED_AT = Date.parse('2026-09-12T16:00:00Z');

/** The survey the watch is on. */
const MODEL = 'model-1';

function show(defaultCaverId: string | null = 'caver-1', surveyModelId: string | null = MODEL) {
  return render(
    <App>
      <TrackingPicturesDialog
        open
        tripLogId="trip-1"
        defaultAt={OPENED_AT}
        defaultCaverId={defaultCaverId}
        cavers={CAVERS}
        surveyModelId={surveyModelId}
        onClose={vi.fn()}
      />
    </App>,
  );
}

function report(overrides: Partial<Report> & { recordedAt: string }): Report {
  return {
    id: `report-${overrides.recordedAt}-${overrides.caverId ?? 'caver-1'}`,
    caverId: 'caver-1',
    teamId: null,
    kind: 'atStation',
    surveyModelId: MODEL,
    stationName: null,
    depthEnteredM: null,
    note: null,
    ...overrides,
  };
}

/** What the preview says about one chosen photograph, and which of its answers that is. */
function previewOf(documentId: string) {
  const node = screen.getByTestId(`trip-tracking-pictures-row-place-${documentId}`);
  return { kind: node.getAttribute('data-placement'), text: node.textContent };
}

/** Types into the camera-clock correction. */
async function correctClock(minutes: number) {
  await act(async () => {
    const node = screen.getByTestId('trip-tracking-pictures-offset');
    const box = node instanceof HTMLInputElement ? node : node.querySelector('input')!;
    fireEvent.change(box, { target: { value: String(minutes) } });
  });
}

/** Chooses somebody in an antd select found by its test id — or nobody, with `null`. */
async function choose(testId: string, name: string | null) {
  const node = screen.getByTestId(testId);
  const root = node.closest('.ant-select') ?? node;
  if (name === null) {
    await act(async () => {
      fireEvent.mouseDown(root.querySelector('.ant-select-clear')!);
    });
    return;
  }
  await act(async () => {
    fireEvent.mouseDown(root.querySelector('.ant-select-selector') ?? root);
  });
  const options = Array.from(document.querySelectorAll('.ant-select-item-option')).filter(
    (option) => option.textContent === name,
  );
  await act(async () => {
    fireEvent.click(options.at(-1)!);
  });
}

/**
 * Ticks one photograph in the chooser. antd puts the test id on whichever of the label and the
 * input it forwards rest props to, so the box is found either way rather than by assuming.
 */
function pick(documentId: string) {
  const node = screen.getByTestId(`trip-tracking-pictures-pick-${documentId}`);
  const box = node instanceof HTMLInputElement ? node : node.querySelector('input');
  fireEvent.click(box ?? node);
}

async function save() {
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: /Attach/ }));
  });
}

/** What the last write would have sent, per photograph. */
function sent() {
  return (attach.mock.calls.at(-1)![0] as { items: { documentId: string; at: string; caverId: string | null }[] })
    .items;
}

beforeEach(() => {
  attach.mockReset();
  logAsked.mockReset();
  logFailed = false;
  // Ana at the pitch head from nine and in the sump from half past ten; Bogdan at the entrance
  // series all morning. Newest first, as the server lists a log.
  reports = [
    report({ recordedAt: '2026-09-12T10:30:00Z', stationName: 'cave.sump.1' }),
    report({ recordedAt: '2026-09-12T09:00:00Z', stationName: 'cave.upper.2' }),
    report({ recordedAt: '2026-09-12T08:30:00Z', caverId: 'caver-2', stationName: 'cave.ent.0' }),
  ];
  photos = [
    { documentId: 'doc-1', title: 'Pitch head', takenAt: '2026-09-12T09:05:00Z' },
    { documentId: 'doc-2', title: 'Sump', takenAt: '2026-09-12T10:40:00Z' },
    { documentId: 'doc-3', title: 'Scanned print', takenAt: null },
  ];
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe('TrackingPicturesDialog', () => {
  it('files each photograph at the moment its own file says it was taken', () => {
    show();
    pick('doc-1');
    pick('doc-2');
    void save();

    expect(sent().map((item) => item.at)).toEqual([
      '2026-09-12T09:05:00.000Z',
      '2026-09-12T10:40:00.000Z',
    ]);
    // Who the moment is about travels with it, so the replay knows whose position to draw it at.
    expect(sent().every((item) => item.caverId === 'caver-1')).toBe(true);
  });

  it('corrects every prefilled moment by the camera-clock offset, stated once', async () => {
    show();
    pick('doc-1');
    pick('doc-2');

    // "The camera was 37 minutes fast" — taken off what the clock said, for all of them at once,
    // which is the work this dialog exists to remove.
    await act(async () => {
      const node = screen.getByTestId('trip-tracking-pictures-offset');
      const box = node instanceof HTMLInputElement ? node : node.querySelector('input')!;
      fireEvent.change(box, { target: { value: '37' } });
    });
    await save();

    expect(sent().map((item) => item.at)).toEqual([
      '2026-09-12T08:28:00.000Z',
      '2026-09-12T10:03:00.000Z',
    ]);
  });

  it('flags a photograph whose file says nothing about when it was taken, and never the ones that do', async () => {
    show();
    pick('doc-1');
    // Its twin: with only dated pictures chosen there is no warning at all, so the one below is a
    // decision about the picture rather than a box the dialog always shows.
    expect(screen.queryByTestId('trip-tracking-pictures-guessed')).toBeNull();

    pick('doc-3');
    expect(screen.getByTestId('trip-tracking-pictures-guessed')).toBeInTheDocument();

    await save();
    // The undated one falls back to the moment the dialog was opened with — offered, flagged, and
    // never applied silently. The offset is a correction to a camera's clock and is not applied to
    // a moment that never came off one.
    expect(sent().map((item) => item.at)).toEqual([
      '2026-09-12T09:05:00.000Z',
      new Date(OPENED_AT).toISOString(),
    ]);
  });

  /**
   * A camera whose battery died reports 1970 or 2000 — a clock that was never set rather than one
   * set wrongly. The replay refuses to stretch its scrubber around such a moment, so nothing is
   * destroyed by attaching one; what is left is a photograph filed where nobody meant, and this is
   * the surface that can say so before it is stored. It says it and does not refuse: the moment can
   * be right, and throwing away the record of a photograph over a number somebody can correct here
   * would be the worse trade.
   */
  it('remarks on a moment far outside the trip, and never on the ordinary wrong hour', async () => {
    photos = [
      { documentId: 'doc-1', title: 'Pitch head', takenAt: '2026-09-12T09:05:00Z' },
      { documentId: 'doc-dead', title: 'Dead battery', takenAt: '2000-01-01T00:00:00Z' },
    ];
    show();

    // The twin, and it is the important half: a clock out by hours — a time zone, a date rolled
    // over at midnight, a camp that ran a week — is the ordinary case and passes without a word.
    pick('doc-1');
    expect(screen.queryByTestId('trip-tracking-pictures-stray')).toBeNull();

    pick('doc-dead');
    expect(screen.getByTestId('trip-tracking-pictures-stray')).toBeInTheDocument();

    // Remarked on, not refused: both are still sent, at the moments their files claim.
    await save();
    expect(sent().map((item) => item.at)).toEqual([
      '2026-09-12T09:05:00.000Z',
      '2000-01-01T00:00:00.000Z',
    ]);
  });

  /**
   * The station a photograph hangs under is never stored: the replay reads it out of the log at
   * the photograph's own moment. So a camera clock that was out is a photograph under the wrong
   * station, and the place to find that out is here, before it is attached.
   */
  it('shows where each chosen photograph will be drawn, and follows the clock correction', async () => {
    show();
    pick('doc-1');
    pick('doc-2');

    // 09:05 is the pitch head and 10:40 the sump.
    expect(previewOf('doc-1')).toEqual({
      kind: 'station',
      text: 'Drawn at station cave.upper.2',
    });
    expect(previewOf('doc-2').text).toBe('Drawn at station cave.sump.1');

    // The camera was twenty minutes fast: the second photograph was taken at 10:20, when Ana was
    // still at the pitch head, and the first at 08:45, before anybody had reported her anywhere.
    await correctClock(20);
    expect(previewOf('doc-2').text).toBe('Drawn at station cave.upper.2');
    expect(previewOf('doc-1').kind).toBe('unplaced');
    expect(previewOf('doc-1').text).toMatch(/No station had been reported for them by then/);

    // And back: the answer is worked out from the correction, not remembered from before it.
    await correctClock(0);
    expect(previewOf('doc-1').text).toBe('Drawn at station cave.upper.2');
  });

  /**
   * A memory card holds pictures of several people. Each photograph is about whoever it is about,
   * the request says so per photograph, and the chooser at the top goes on meaning all of them.
   */
  it('takes a person per photograph, previews each at their own place, and applies one to all', async () => {
    show();
    pick('doc-1');
    pick('doc-2');

    await choose('trip-tracking-pictures-row-subject-doc-2', 'Bogdan');
    // Bogdan was at the entrance series at 10:40; the other photograph is still about Ana.
    expect(previewOf('doc-2').text).toBe('Drawn at station cave.ent.0');
    expect(previewOf('doc-1').text).toBe('Drawn at station cave.upper.2');

    // About nobody in particular it is on the timeline and under no station.
    await choose('trip-tracking-pictures-row-subject-doc-1', null);
    expect(previewOf('doc-1')).toEqual({
      kind: 'party',
      text: 'On the timeline only — name somebody to place it',
    });

    await save();
    expect(sent().map((item) => [item.documentId, item.caverId])).toEqual([
      ['doc-1', null],
      ['doc-2', 'caver-2'],
    ]);

    // Chosen at the top it is everybody's again, whatever was said about one alone.
    await choose('trip-tracking-pictures-subject', 'Bogdan');
    expect(previewOf('doc-1').text).toBe('Drawn at station cave.ent.0');
    await save();
    expect(sent().every((item) => item.caverId === 'caver-2')).toBe(true);
  });

  /**
   * A reader from whom the cave's place is kept is sent each report with no station and no survey
   * on it. The preview must not hand one back, and must not say that nobody had reported the
   * person either — somebody had, and this reader is the one not being told.
   */
  it('never previews a station for a position this reader is not told', () => {
    // The log as that reader is sent it: the same reports, each stripped of its place.
    const told = reports!;
    reports = told.map((entry) => ({ ...entry, stationName: null, surveyModelId: null }));
    show();
    pick('doc-1');
    pick('doc-2');

    for (const documentId of ['doc-1', 'doc-2']) {
      expect(previewOf(documentId).kind).toBe('withheld');
      expect(previewOf(documentId).text).toMatch(/is not shown to you/);
    }
    expect(screen.getByTestId('trip-tracking-pictures-preview').textContent).not.toMatch(/cave\./);

    // A reader told no survey at all asks for no log and is told there is none to draw on.
    cleanup();
    logAsked.mockReset();
    show('caver-1', null);
    pick('doc-1');
    expect(previewOf('doc-1').kind).toBe('noModel');
    expect(logAsked.mock.calls.every(([enabled]) => enabled === false)).toBe(true);

    // The positive twin: the same photographs and the same person, for a reader who is told.
    cleanup();
    reports = told;
    show();
    pick('doc-1');
    expect(previewOf('doc-1')).toEqual({ kind: 'station', text: 'Drawn at station cave.upper.2' });
  });

  /**
   * The answer comes from the whole log or it is not given. A log still being read, and one that
   * could not be read, each say so — and neither stops the photograph being attached, because
   * where it is drawn is worked out again from the log every time it is shown.
   */
  it('says nothing about a place until the whole log has been read, and attaches all the same', async () => {
    reports = undefined;
    show();
    pick('doc-1');
    expect(previewOf('doc-1')).toEqual({ kind: 'reading', text: "Reading the trip's reports…" });

    cleanup();
    logFailed = true;
    show();
    pick('doc-1');
    expect(previewOf('doc-1').kind).toBe('unread');
    await save();
    expect(sent().map((item) => item.documentId)).toEqual(['doc-1']);
  });

  it('sends no subject for a picture of the party, and nothing at all with nothing chosen', async () => {
    show(null);
    pick('doc-1');
    await save();
    expect(sent()[0].caverId).toBeNull();

    attach.mockReset();
    cleanup();
    show();
    // Nothing chosen is nothing to send: the button is refused rather than writing an empty batch.
    expect(screen.getByRole('button', { name: /Attach/ })).toBeDisabled();
  });

  /**
   * A refusal of the whole request arrives as a failed write. Each has a different remedy — start
   * the watch, or tell whoever runs the installation — so each is said in its own words, and only a
   * refusal nothing here has words for gets the dialog's general sentence.
   */
  it.each([
    ['tracking.not_tracked', /never been started for this trip/],
    ['tracking.picture_relation_missing', /installation is missing the kind of link/],
  ])('says why the whole request was refused: %s', async (code, says) => {
    attach.mockImplementation((_request, on: { onError(error: unknown): void }) =>
      on.onError(new ApiError(409, code)),
    );
    show();
    pick('doc-1');
    await save();

    // Found as the one message on the screen: a second copy of it would fail the search.
    expect(await screen.findByText(says)).toBeInTheDocument();
    expect(document.querySelectorAll('.ant-message-notice')).toHaveLength(1);
  });

  it('keeps its general sentence for a refusal nobody has worded, and for a failure that is not one', async () => {
    attach.mockImplementation((_request, on: { onError(error: unknown): void }) =>
      on.onError(new ApiError(409, 'tracking.something_nobody_has_worded')),
    );
    show();
    pick('doc-1');
    await save();
    expect(await screen.findByText('The photographs could not be attached.')).toBeInTheDocument();

    attach.mockImplementation((_request, on: { onError(error: unknown): void }) =>
      on.onError(new TypeError('Failed to fetch')),
    );
    await save();
    // Said again for the second failure, in the same words.
    await waitFor(() =>
      expect(screen.getAllByText('The photographs could not be attached.')).toHaveLength(2),
    );
  });

  /**
   * The other kind arrives inside an answer that succeeded: the write takes every photograph it
   * can and names, per photograph, why it left the rest. The count alone said that something was
   * left out and never what to do about it.
   */
  it.each([
    ['tracking.picture_in_future', /moment that has not happened yet/],
    ['tracking.picture_not_image', /chosen files is not a picture/],
    ['tracking.picture_already_attached', /already on that moment of the trip/],
    ['tracking.caver_not_participant', /on the trip's roster first/],
  ])('says why a photograph was left out: %s', async (code, says) => {
    attach.mockImplementation((_request, on: { onSuccess(result: unknown): void }) =>
      on.onSuccess({
        attached: [{ memberId: 'member-1', documentId: 'doc-1' }],
        refused: { 'doc-2': code },
      }),
    );
    show();
    pick('doc-1');
    pick('doc-2');
    await save();

    // Both halves are still counted, and the reason is a line of its own beside the count.
    expect(await screen.findByText('Attached: 1. Not attached: 1.')).toBeInTheDocument();
    expect(await screen.findByText(says)).toBeInTheDocument();
    expect(document.querySelectorAll('.ant-message-warning')).toHaveLength(1);
  });

  it('gives one reason once however many photographs share it, and no line for a reason it has no words for', async () => {
    attach.mockImplementation((_request, on: { onSuccess(result: unknown): void }) =>
      on.onSuccess({
        attached: [],
        refused: {
          'doc-1': 'tracking.picture_in_future',
          'doc-2': 'tracking.picture_in_future',
          'doc-3': 'tracking.something_nobody_has_worded',
        },
      }),
    );
    show();
    pick('doc-1');
    pick('doc-2');
    pick('doc-3');
    await save();

    expect(await screen.findByText(/moment that has not happened yet/)).toBeInTheDocument();
    expect(document.querySelectorAll('.ant-message-warning')).toHaveLength(1);

    // Its twin: an answer that left nothing out explains nothing.
    attach.mockImplementation((_request, on: { onSuccess(result: unknown): void }) =>
      on.onSuccess({ attached: [{ memberId: 'member-1', documentId: 'doc-1' }], refused: {} }),
    );
    cleanup();
    show();
    pick('doc-1');
    await save();
    expect(await screen.findByText('Attached: 1. Not attached: 0.')).toBeInTheDocument();
    expect(document.querySelectorAll('.ant-message-warning')).toHaveLength(0);
  });
});
