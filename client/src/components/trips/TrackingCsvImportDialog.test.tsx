// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

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

// How much room there is across, which is what decides whether the preview is a table or a list
// of cards. False by default — the desk this suite is read on.
let narrow = false;
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => narrow }));

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
  timeZone: null,
  day: null,
};

/**
 * Choosing a file, and waiting for the dialog to have read it.
 *
 * The wait is the point: the dialog reads the file itself rather than uploading it, so the text
 * arrives a tick later and Read stays disabled until it does. A test that clicked straight through
 * would be clicking a disabled button and asserting against a screen nothing had happened on.
 */
async function drop(text: string | Uint8Array) {
  const input = document.querySelector('input[type="file"]')!;
  const file = new File([text as BlobPart], 'sheet.csv', { type: 'text/csv' });
  fireEvent.change(input, { target: { files: [file] } });
  await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-preview')).toBeEnabled());
}

beforeEach(() => {
  narrow = false;
  save.mockReset().mockResolvedValue(undefined);
  look.mockReset().mockResolvedValue(PREVIEW);
  send.mockReset().mockResolvedValue({ created: 1, updated: 1, skipped: 0, refused: [] });
});
afterEach(cleanup);

/**
 * antd names a deprecated prop through `console.error`, once per render — and in development the
 * application's own error sweep files every one of those as a defect. So every case in this file
 * also checks that nothing it drew spoke that way; a notice written with the old spelling of a
 * prop fails here rather than in somebody's diagnostics.
 */
let consoleError: MockInstance<typeof console.error>;
beforeEach(() => {
  consoleError = vi.spyOn(console, 'error');
});
afterEach(() => {
  const deprecations = consoleError.mock.calls
    .map(([first]) => String(first))
    .filter((line) => /\[antd: [^\]]+\].*deprecated/.test(line));
  consoleError.mockRestore();
  expect(deprecations).toEqual([]);
});

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

  it('disarms Import when a setting changes after the read, until the sheet is read again', async () => {
    // A preview is a page of exactly what committing would do under the settings it was read with.
    // The commit reads the sheet again under the current ones — so a word added for "went in" after
    // the read would turn rows the table showed as depth reports into entries, and Import must not
    // stay armed over a table that no longer describes what it would do.
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-commit')).toBeEnabled());
    expect(look.mock.calls[0][0].options.wentInWords).toBeNull();

    fireEvent.click(screen.getByText('Column settings'));
    fireEvent.change(await screen.findByRole('textbox', { name: 'Words for going in' }), {
      target: { value: 'coborat' },
    });

    expect(screen.queryByTestId('trip-tracking-csv-rows')).not.toBeInTheDocument();
    expect(screen.getByTestId('trip-tracking-csv-commit')).toBeDisabled();

    // Read again under the new word, and Import is armed over the reading it will commit.
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(look).toHaveBeenCalledTimes(2));
    expect(look.mock.calls[1][0].options.wentInWords).toEqual(['coborat']);
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-commit')).toBeEnabled());
    fireEvent.click(screen.getByTestId('trip-tracking-csv-commit'));
    await waitFor(() => expect(send).toHaveBeenCalledOnce());
    expect(send.mock.calls[0][0].options.wentInWords).toEqual(['coborat']);
  });

  it('keeps offering the sheet\'s columns to map after a choice has disarmed the preview', async () => {
    // The choosers offer the header the sheet was last read with. If the header went with the
    // preview, the first remap would empty every chooser and a second column could never be pointed
    // anywhere without reading in between.
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

    fireEvent.click(screen.getByText('Column settings'));
    fireEvent.mouseDown(
      within(await screen.findByTestId('trip-tracking-csv-map-RecordedAt')).getByRole('combobox'),
    );
    await waitFor(() =>
      expect(document.querySelector('.ant-select-item-option[title="Adancime"]')).not.toBeNull(),
    );
    fireEvent.click(document.querySelector('.ant-select-item-option[title="Adancime"]')!);
    expect(screen.queryByTestId('trip-tracking-csv-rows')).not.toBeInTheDocument();

    // The second chooser still offers the header after the first choice.
    fireEvent.mouseDown(
      within(screen.getByTestId('trip-tracking-csv-map-Cavers')).getByRole('combobox'),
    );
    await waitFor(() =>
      expect(document.querySelector('.ant-select-item-option[title="Speologi"]')).not.toBeNull(),
    );
  });

  it('forgets a tick to overwrite when another file is chosen', async () => {
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-replace')).toBeInTheDocument());
    fireEvent.click(screen.getByTestId('trip-tracking-csv-replace'));

    await drop('y');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-replace')).toBeInTheDocument());
    expect(screen.getByTestId('trip-tracking-csv-replace')).not.toBeChecked();
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
    fireEvent.click(screen.getByText(/Findings worth reading/));
    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-csv-problems')).toBeInTheDocument(),
    );
    expect(screen.getByText(/share that name/)).toBeInTheDocument();
    expect(screen.queryByText('PlaceLabelAmbiguous')).not.toBeInTheDocument();
  });

  it('words a row\'s own findings beside the row, and counts them among the things to read', async () => {
    // The finding that changes what a row means: a "went out" word the reader did not know
    // leaves the row filed as still underground. It reaches the reviewer beside the row it is
    // about, worded, and in the fold's count — not only in the response.
    look.mockResolvedValue({
      ...PREVIEW,
      rows: [
        {
          ...PREVIEW.rows[0],
          diagnostics: [
            { severity: 'Warning', problem: 'StateWordUnknown', line: 2, column: 'State', detail: 'plecat' },
          ],
        },
        PREVIEW.rows[1],
      ],
    });
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

    const beside = screen.getByTestId('trip-tracking-csv-row-findings-2');
    expect(beside).toHaveTextContent('Neither list of words knows this one');
    expect(beside).toHaveTextContent('plecat');
    expect(screen.queryByText('StateWordUnknown')).not.toBeInTheDocument();
    expect(screen.queryByTestId('trip-tracking-csv-row-findings-3')).toBeNull();

    fireEvent.click(screen.getByText('Findings worth reading: 1'));
    await waitFor(() =>
      expect(screen.getByTestId('trip-tracking-csv-problems')).toHaveTextContent('Line 2'),
    );
  });

  it('says how the sheet\'s times and dates were read, where the numbers are', async () => {
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

    const rule = screen.getByTestId('trip-tracking-csv-moments-rule');
    expect(rule).toHaveTextContent('read as UTC');
    expect(rule).toHaveTextContent('day first');
  });

  /**
   * Which of the two numbers of a date is the day, and what decided it.
   *
   * <b>The defect these pin.</b> The reading was shown but not what settled it, so a sheet whose
   * every day is twelve or under — which no date in it can settle — was read in the usual order
   * with nothing on screen to tell it apart from a reading the file had proved, and nothing to say
   * where the control that changes it is.
   */
  describe('what settled the day and the month', () => {
    it('says so when the file\'s own dates settled it', async () => {
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));

      expect(await screen.findByTestId('trip-tracking-csv-moments-rule')).toHaveTextContent(
        "The file's own dates settle it.",
      );
    });

    it('says a reading nothing settled is the usual one, and where to change it', async () => {
      look.mockResolvedValue({ ...PREVIEW, dateOrderSource: 'Stated' });
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));

      const rule = await screen.findByTestId('trip-tracking-csv-moments-rule');
      expect(rule).toHaveTextContent('day first');
      expect(rule).toHaveTextContent('the usual reading was used');
      expect(rule).toHaveTextContent('File settings');
    });

    it('says the reviewer\'s own choice decided, once there was one', async () => {
      look.mockResolvedValue({ ...PREVIEW, dateOrder: 'monthFirst', dateOrderSource: 'Stated' });
      open();
      await drop('x');
      fireEvent.click(screen.getByText('File settings'));
      fireEvent.mouseDown(
        within(await screen.findByTestId('trip-tracking-csv-date-order')).getByRole('combobox'),
      );
      fireEvent.click(await screen.findByText('Month first (09/12/2026)'));
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));

      const rule = await screen.findByTestId('trip-tracking-csv-moments-rule');
      expect(rule).toHaveTextContent('month first');
      expect(rule).toHaveTextContent('the choice under File settings was used');
    });

    it('says a file that argues both ways was read one way throughout', async () => {
      look.mockResolvedValue({ ...PREVIEW, dateOrderSource: 'Conflict' });
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));

      expect(await screen.findByTestId('trip-tracking-csv-moments-rule')).toHaveTextContent(
        'both ways round',
      );
    });
  });

  /**
   * Importing some of what was read.
   *
   * <b>The defect these pin.</b> The import always took every line the reading accepted, so a
   * reviewer who saw one wrong row could only fix the sheet or import the mistake. The server takes
   * a list of lines — and takes an empty list to mean every line, which is why Import must be out
   * of reach once nothing is ticked rather than send one.
   */
  describe('choosing the rows to import', () => {
    /** A sheet whose line 4 names two people: two rows of the preview, one line of the sheet. */
    const SHARED: Preview = {
      ...PREVIEW,
      creates: 3,
      rows: [
        ...PREVIEW.rows,
        { ...PREVIEW.rows[0], line: 4, caverId: 'caver-1', recordedAt: '2026-09-12T11:00:00Z' },
        { ...PREVIEW.rows[0], line: 4, caverId: 'caver-2', caverWritten: 'Maria Pop', caverMatched: 'Maria Pop', recordedAt: '2026-09-12T11:00:00Z' },
      ],
    };
    const tick = (line: number) => screen.getAllByRole('checkbox', { name: `Import line ${line}` });

    it('ticks every row after a read, and sends the lines it ticked', async () => {
      look.mockResolvedValue(SHARED);
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

      for (const line of [2, 3, 4]) {
        for (const box of tick(line)) expect(box).toBeChecked();
      }
      fireEvent.click(screen.getByTestId('trip-tracking-csv-commit'));
      await waitFor(() => expect(send).toHaveBeenCalledOnce());
      expect(send.mock.calls[0][0].lines).toEqual([2, 3, 4]);
    });

    it('leaves an unticked line out, and counts what importing would do from the ticks', async () => {
      look.mockResolvedValue(SHARED);
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());
      expect(screen.getByTestId('trip-tracking-csv-replaces')).toHaveTextContent('1');

      // Line 3 is the one that would overwrite: unticked, nothing is overwritten and the offer to
      // overwrite goes with it.
      fireEvent.click(tick(3)[0]);
      expect(screen.getByTestId('trip-tracking-csv-replaces')).toHaveTextContent('0');
      expect(screen.getByTestId('trip-tracking-csv-creates')).toHaveTextContent('3');
      expect(screen.queryByTestId('trip-tracking-csv-replace')).not.toBeInTheDocument();

      fireEvent.click(screen.getByTestId('trip-tracking-csv-commit'));
      await waitFor(() => expect(send).toHaveBeenCalledOnce());
      expect(send.mock.calls[0][0].lines).toEqual([2, 4]);
    });

    it('takes a line naming two people out whole when one of its rows is unticked', async () => {
      look.mockResolvedValue(SHARED);
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

      fireEvent.click(tick(4)[0]);
      for (const box of tick(4)) expect(box).not.toBeChecked();
      expect(screen.getByTestId('trip-tracking-csv-creates')).toHaveTextContent('1');

      // And ticked back whole from its other row.
      fireEvent.click(tick(4)[1]);
      for (const box of tick(4)) expect(box).toBeChecked();
    });

    it('puts Import out of reach once nothing is ticked, rather than sending an empty list', async () => {
      look.mockResolvedValue(SHARED);
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());

      for (const line of [2, 3, 4]) fireEvent.click(tick(line)[0]);

      expect(screen.getByTestId('trip-tracking-csv-commit')).toBeDisabled();
      fireEvent.click(screen.getByTestId('trip-tracking-csv-commit'));
      expect(send).not.toHaveBeenCalled();
    });

    it('ticks everything again on the next read', async () => {
      look.mockResolvedValue(SHARED);
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());
      fireEvent.click(tick(2)[0]);

      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(look).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(tick(2)[0]).toBeChecked());
    });
  });

  /**
   * Every setting answers to its own label.
   *
   * None of them is a field the form holds, and a form item with no field name gives its label no
   * `for` — so each was announced as a bare "combobox" or an unnamed text box.
   */
  it('names each setting by its label, for somebody who cannot see which box is which', async () => {
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());
    fireEvent.click(screen.getByText('File settings'));
    fireEvent.click(screen.getByText('Column settings'));

    for (const name of ['Characters', 'Column separator', 'Day and month', 'When', 'People']) {
      expect(await screen.findByRole('combobox', { name })).toBeInTheDocument();
    }
    for (const name of ['Words for going in', 'Words for coming out']) {
      expect(screen.getByRole('textbox', { name })).toBeInTheDocument();
    }
  });

  /**
   * The two word fields hint in the page's language, and say what typing in them does.
   *
   * The hints were written into the component in Romanian and without diacritics, so an English
   * reader was shown Romanian and a Romanian one was shown "iesire" where every other string on
   * the page writes "ieșire". And what the fields do is not visible from them: a word typed here
   * replaces the usual list for its side rather than adding to it.
   */
  it('hints the state words in the page\'s language, and says what a typed word replaces', async () => {
    open();
    fireEvent.click(screen.getByText('Column settings'));
    const wentIn = await screen.findByRole('textbox', { name: 'Words for going in' });
    const cameOut = screen.getByRole('textbox', { name: 'Words for coming out' });

    expect(wentIn).toHaveAttribute('placeholder', 'went in, entered');
    expect(cameOut).toHaveAttribute('placeholder', 'went out, exited');
    expect(wentIn).toHaveAccessibleDescription(/replace the usual ones/);
    expect(cameOut).toHaveAccessibleDescription(/ieșire and iesire are the same word/);

    await i18n.changeLanguage('ro');
    try {
      await waitFor(() => expect(wentIn).toHaveAttribute('placeholder', 'intrare, intrat'));
      expect(cameOut).toHaveAttribute('placeholder', 'ieșire, ieșit');
      expect(cameOut).toHaveAccessibleDescription(/le înlocuiesc pe cele obișnuite/);
    } finally {
      await i18n.changeLanguage('en');
    }
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

  /**
   * A refusal reaches the screen in the server's own words.
   *
   * <b>The defect these pin.</b> The sentence for a refusal was looked up with its two arguments the
   * wrong way round, which the type check cannot see because a caught error is `any`. At runtime
   * that threw inside the catch, so a coordinator whose sheet was refused saw the button return to
   * rest and nothing else — on the one screen whose job is saying what to go and fix — while the
   * browser recorded an unhandled rejection nobody was looking at. So each of the three writes is
   * refused here and its sentence asserted, and the refusal has to be a worded one rather than the
   * general fallback: that is what proves the error, and not the translator, was what got looked up.
   */
  it('words a refused read rather than showing nothing', async () => {
    look.mockRejectedValue(new ApiError(409, 'tracking.not_writable'));
    open();
    await drop('x');

    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));

    expect(await screen.findByText(/Tracking has never been started/)).toBeInTheDocument();
    expect(screen.queryByTestId('trip-tracking-csv-rows')).not.toBeInTheDocument();
  });

  it('words a refused import and keeps the dialog open to fix it', async () => {
    send.mockRejectedValue(new ApiError(409, 'tracking.not_writable'));
    const onClose = vi.fn();
    render(
      <App>
        <TrackingCsvImportDialog tripLogId="trip-1" open onClose={onClose} />
      </App>,
    );
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-commit')).toBeEnabled());

    fireEvent.click(screen.getByTestId('trip-tracking-csv-commit'));

    expect(await screen.findByText(/Tracking has never been started/)).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('words a sheet offered to a trip that has no watch at all', async () => {
    // The refusal the import answers most often on a new trip, and the one whose way out is
    // neither starting nor reopening the watch but setting it up. Worded on its own so that the
    // reviewer is sent to the right act, not to the general "try again".
    look.mockRejectedValue(new ApiError(409, 'tracking.not_configured'));
    open();
    await drop('x');

    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));

    expect(await screen.findByText(/no watch to import reports onto/)).toBeInTheDocument();
  });

  it('says so when the sample sheet could not be fetched', async () => {
    save.mockRejectedValue(new Error('Download failed (503)'));
    open();

    fireEvent.click(screen.getByTestId('trip-tracking-csv-template'));

    expect(await screen.findByText('The operation failed. Please try again.')).toBeInTheDocument();
  });

  /**
   * The file as a spreadsheet saves it, not as the browser would like it.
   *
   * <b>The defect these pin.</b> The dialog read the chosen file as UTF-8 and sent a comma to the
   * server whatever the header said. A plain "CSV" save from a Romanian Excel is Windows-1250 and
   * semicolon-separated — so the one sheet a Romanian coordinator is most likely to hand over
   * arrived with a replacement character in every diacritic ("ieșire" was no longer a state word,
   * and the row imported as a place at 0 m instead of an exit) and previewed as a file with no
   * moment and no caver column, with no control anywhere to say otherwise.
   */
  describe('the shape of the file', () => {
    it('reads a Windows-1250 sheet as Windows-1250, and says so', async () => {
      open();
      // "Stare\r\nieşire": 0xBA is ş in the Central European code page and not UTF-8 at all.
      await drop(Uint8Array.from([0x53, 0x74, 0x61, 0x72, 0x65, 0x0d, 0x0a, 0x69, 0x65, 0xba, 0x69, 0x72, 0x65]));

      expect(screen.getByTestId('trip-tracking-csv-read-as')).toHaveTextContent(
        /read as Windows-1250/,
      );
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(look).toHaveBeenCalledOnce());
      expect(look.mock.calls[0][0].text).toBe('Stare\r\nieşire');
      expect(look.mock.calls[0][0].text).not.toContain('\uFFFD');
    });

    it('reads a semicolon sheet with a semicolon, and a comma sheet with a comma', async () => {
      open();
      await drop('Data si ora;Adancime;Speologi;Stare\r\n12.09.2026 08:15;0;Maria Pop;intrare\r\n');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(look).toHaveBeenCalledOnce());
      expect(look.mock.calls[0][0].options.delimiter).toBe(';');
      expect(screen.getByTestId('trip-tracking-csv-read-as')).toHaveTextContent(/Semicolon/);

      await drop('Data si ora,Adancime,Speologi\r\n12.09.2026 09:00,96,Ion\r\n');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(look).toHaveBeenCalledTimes(2));
      expect(look.mock.calls[1][0].options.delimiter).toBe(',');
    });

    it('sends the day-and-month reading only once the reviewer has chosen one', async () => {
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(look).toHaveBeenCalledOnce());
      // Left to the file's own dates unless said otherwise: one row cannot settle what 5/11 means.
      expect(look.mock.calls[0][0].options.dateOrder).toBeNull();

      // Behind the fold with the other file settings, so the fold is opened first.
      fireEvent.click(screen.getByText('File settings'));
      fireEvent.mouseDown(
        within(await screen.findByTestId('trip-tracking-csv-date-order')).getByRole('combobox'),
      );
      fireEvent.click(await screen.findByText('Month first (09/12/2026)'));
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      await waitFor(() => expect(look).toHaveBeenCalledTimes(2));
      expect(look.mock.calls[1][0].options.dateOrder).toBe('monthFirst');
    });
  });

  /**
   * The moment column reads in the application's language, like every other clock on the
   * tracking surfaces. It was the one date on the dialog formatted in the browser's own locale,
   * so a Romanian reviewer saw American dates in a preview the rest of which was in Romanian.
   */
  it('formats each row\'s moment in the language the page is in', async () => {
    open();
    await drop('x');
    fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
    await waitFor(() => expect(screen.getByTestId('trip-tracking-csv-rows')).toBeInTheDocument());
    expect(
      screen.getByText(new Date('2026-09-12T09:00:00Z').toLocaleString('en')),
    ).toBeInTheDocument();

    await i18n.changeLanguage('ro');
    try {
      expect(
        await screen.findByText(new Date('2026-09-12T09:00:00Z').toLocaleString('ro')),
      ).toBeInTheDocument();
    } finally {
      await i18n.changeLanguage('en');
    }
  });

  /**
   * The same dialog at the width of a phone.
   *
   * Five columns of a preview do not fit across one, and a table told to keep its own overflow
   * keeps it by scrolling sideways — with the outcome column, the one that says whether a row
   * overwrites, the one off the edge. So each row becomes a card with its fields labelled, and the
   * settings each take the whole width rather than standing in a row too narrow to read.
   */
  describe('at the width of a phone', () => {
    it('stacks each previewed row into labelled fields, with the outcome beside the line', async () => {
      narrow = true;
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      const rows = await screen.findByTestId('trip-tracking-csv-rows');

      // No row of headings: with every field labelled in its row, one would be the same words a
      // second time on the axis there is least of.
      expect(rows.querySelector('thead')).toBeNull();
      const cards = rows.querySelectorAll('.tracking-csv-stacked');
      expect(cards).toHaveLength(2);
      const first = within(cards[0] as HTMLElement);
      expect(first.getByText('Line 2')).toBeInTheDocument();
      expect(first.getByText('New')).toBeInTheDocument();
      expect(first.getByText('When')).toBeInTheDocument();
      expect(first.getByText('Person')).toBeInTheDocument();
      expect(first.getByText('Ion Popescu')).toBeInTheDocument();
      expect(first.getByText('Place')).toBeInTheDocument();
      expect(first.getByText('upper.2')).toBeInTheDocument();
      // A row with nothing noticed about it carries no empty "Findings" line.
      expect(first.queryByText('Findings')).toBeNull();
      expect(within(cards[1] as HTMLElement).getByText('Replaces')).toBeInTheDocument();
    });

    it('marks the dialog and its settings so the stylesheet can give them the whole width', async () => {
      narrow = true;
      open();
      expect(document.querySelector('.ant-modal.tracking-csv-dialog-narrow')).not.toBeNull();
      // The settings sit behind a fold; opened, each is marked as a field the stylesheet sizes.
      fireEvent.click(screen.getByText('File settings'));
      const encoding = await screen.findByTestId('trip-tracking-csv-encoding');
      expect(encoding.closest('.ant-form-item')).toHaveClass('tracking-csv-field');
    });

    it('keeps the table, its headings and its width on a desk', async () => {
      open();
      await drop('x');
      fireEvent.click(screen.getByTestId('trip-tracking-csv-preview'));
      const rows = await screen.findByTestId('trip-tracking-csv-rows');

      expect(rows.querySelector('thead')).not.toBeNull();
      expect(rows.querySelectorAll('.tracking-csv-stacked')).toHaveLength(0);
      expect(document.querySelector('.ant-modal.tracking-csv-dialog-narrow')).toBeNull();
    });
  });
});
