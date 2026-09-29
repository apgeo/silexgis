// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

const look = vi.fn();
const send = vi.fn();
const save = vi.fn();

// The one helper that knows how to carry the caller's token into a file download.
vi.mock('../../api/download.ts', () => ({ downloadFile: (url: string) => save(url) }));

// The two writes are stubbed and nothing else is: the column roles, the wording of every finding
// and the shape of a preview are the real ones, so a rename on either side is felt here.
vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useTrackingCsvPreview: () => ({ mutateAsync: look, isPending: false }),
    useTrackingCsvCommit: () => ({ mutateAsync: send, isPending: false }),
    useTrackingCsvFields: () => ({
      data: [
        { field: 'RecordedAt', candidates: ['data si ora'] },
        { field: 'Cavers', candidates: ['speologi'] },
      ],
    }),
  };
});

const { default: TrackingCsvImportDialog } = await import('./TrackingCsvImportDialog.tsx');
type Preview = import('../../api/hooks.ts').TrackingCsvPreview;

const PREVIEW: Preview = {
  header: ['Data si ora', 'Adancime', 'Speologi'],
  resolvedColumns: { RecordedAt: 'Data si ora', Depth: 'Adancime', Cavers: 'Speologi' },
  unmappedColumns: [],
  dateOrder: 'dayFirst',
  dateOrderSource: 'File',
  rowsRead: 2,
  creates: 1,
  replaces: 1,
  unmatchedCavers: [],
  rows: [
    {
      line: 2,
      recordedAt: '2026-09-12T09:00:00Z',
      caverId: 'caver-1',
      caverWritten: 'Ion',
      caverMatched: 'Ion Popescu',
      matchedBy: 'GivenName',
      teamId: null,
      kind: 'atDepth',
      stationName: 'upper.2',
      depthM: 96,
      note: null,
      replaces: false,
      diagnostics: [],
    },
    {
      line: 3,
      recordedAt: '2026-09-12T10:00:00Z',
      caverId: 'caver-2',
      caverWritten: 'Maria Pop',
      caverMatched: 'Maria Pop',
      matchedBy: 'FullName',
      teamId: null,
      kind: 'atDepth',
      stationName: 'upper.2',
      depthM: 96,
      note: null,
      replaces: true,
      diagnostics: [],
    },
  ],
  fileDiagnostics: [],
  refused: [],
};

/**
 * Choosing a file, and waiting for the dialog to have read it.
 *
 * The wait is the point: the dialog reads the file itself rather than uploading it, so the text
 * arrives a tick later and Read stays disabled until it does. A test that clicked straight through
 * would be clicking a disabled button and asserting against a screen nothing had happened on.
 */
async function drop(text: string) {
  const input = document.querySelector('input[type="file"]')!;
  const file = new File([text], 'sheet.csv', { type: 'text/csv' });
  fireEvent.change(input, { target: { files: [file] } });
  await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-preview')).toBeEnabled());
}

beforeEach(() => {
  save.mockReset().mockResolvedValue(undefined);
  look.mockReset().mockResolvedValue(PREVIEW);
  send.mockReset().mockResolvedValue({ created: 1, updated: 1, skipped: 0, refused: [] });
});
afterEach(cleanup);

function open() {
  render(
    <App>
      <TrackingCsvImportDialog tripLogId="trip-1" open onClose={() => {}} />
    </App>,
  );
}

/**
 * Reading a coordinator's sheet onto a trip's log.
 *
 * The cases here are the ones where the screen could mislead the person deciding: a commit button
 * live before anything has been read, an overwrite that was never asked for, or a name that
 * matched somebody other than who was written shown as if it were exact.
 */
describe('TrackingCsvImportDialog', () => {
  it('will not commit anything that has not been read first', () => {
    open();
    // Nothing chosen: neither button can do anything, and Import stays out of reach until a
    // preview exists — the whole design is that somebody reads before they write.
    expect(screen.getByTestId('trip-tracking-csv-preview')).toBeDisabled();
    expect(screen.getByTestId('trip-tracking-csv-commit')).toBeDisabled();
  });

  it('reads the chosen file and says what importing it would do', async () => {
    open();
    await drop('Data si ora,Adancime,Speologi\r\n12.09.2026 09:00,96,Ion\r\n');

    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

    expect(look).toHaveBeenCalledOnce();
    expect(look.mock.calls[0][0].tripLogId).toBe('trip-1');
    expect(screen.getByTestId('trip-tracking-csv-commit')).toBeEnabled();
  });

  it('shows the written name beside the matched one wherever the two differ', async () => {
    // The whole of what a reviewer is checking: "Ion" matching one person is right until the day
    // two people on a trip answer to it, and a screen showing only the match hides the guess.
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByText('Ion Popescu')).toBeInTheDocument());

    expect(screen.getByText(/written as Ion/)).toBeInTheDocument();
    // An exact match is not annotated, so the annotation means something when it appears.
    expect(screen.queryByText(/written as Maria Pop/)).not.toBeInTheDocument();
  });

  it('asks before overwriting, and imports without overwriting when it was not asked for', async () => {
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-replace')).toBeInTheDocument());

    fireEvent.click(screen.getByTestId('trip-tracking-csv-commit'));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());
    expect(send.mock.calls[0][0].replaceExisting).toBe(false);
  });

  it('overwrites only once the box has been ticked', async () => {
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-replace')).toBeInTheDocument());

    fireEvent.click(screen.getByTestId('trip-tracking-csv-replace'));
    fireEvent.click(screen.getByTestId('trip-tracking-csv-commit'));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());
    expect(send.mock.calls[0][0].replaceExisting).toBe(true);
  });

  it('offers nothing to overwrite when the sheet would change nothing', async () => {
    look.mockResolvedValue({ ...PREVIEW, replaces: 0, rows: [PREVIEW.rows[0]] });
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

    expect(screen.queryByTestId('trip-tracking-csv-replace')).not.toBeInTheDocument();
  });

  it('names the people nobody on the trip answers to, once each', async () => {
    look.mockResolvedValue({ ...PREVIEW, unmatchedCavers: ['Gheorghe Necunoscut'] });
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-csv-unmatched')).toBeInTheDocument(),
    );

    expect(screen.getByText('Gheorghe Necunoscut')).toBeInTheDocument();
  });

  it('words every finding rather than showing its name', async () => {
    look.mockResolvedValue({
      ...PREVIEW,
      refused: [
        { severity: 'Error', problem: 'PlaceLabelAmbiguous', line: 4, column: null, detail: null },
      ],
    });
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

    // Opened, because the findings sit behind a fold: a sheet with one problem should not push the
    // rows off the screen, and a sheet with fifty certainly should not.
    fireEvent.click(screen.getByText(/thing\(s\) worth reading|lucruri de citit/));
    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-csv-problems')).toBeInTheDocument(),
    );
    expect(screen.getByText(/share that name/)).toBeInTheDocument();
    expect(screen.queryByText('PlaceLabelAmbiguous')).not.toBeInTheDocument();
  });

  it('fetches the sample sheet with the caller\'s token rather than opening a link', async () => {
    // <b>The defect this pins.</b> The route is authenticated like every other, and plain anchor
    // navigation carries no Authorization header — so an href would download a 401 problem
    // document under a .csv name. That is worse than no button at all: the reviewer gets a file,
    // opens it in a spreadsheet, and finds a refusal where the sample was meant to be.
    open();
    const button = screen.getByTestId('trip-tracking-csv-template');
    expect(button).not.toHaveAttribute('href');

    fireEvent.click(button);
    await waitFor(() => expect(save).toHaveBeenCalledOnce());
    expect(save).toHaveBeenCalledWith('/api/v1/tracking-csv-import/template');
  });
});
