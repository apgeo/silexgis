// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { TripImportPreview, TripImportSession } from '../../api/hooks.ts';

const commitMutate = vi.fn();
const saveSessionMutate = vi.fn();
const navigate = vi.fn();
const uploadMutate = vi.fn();

let params: { fileId?: string } = { fileId: 'file-1' };
let document_: { id: string; title: string; currentFileId: string } | undefined;

const session: TripImportSession = {
  fileId: 'file-1',
  fileName: 'ture.csv',
  options: {
    delimiter: ',',
    multiValueSeparators: ';,',
    slashSeparatedFields: [],
    dateOrder: 'dayFirst',
    columns: {},
    visibility: 'private',
    cavingGroupId: null,
    createMissingCaves: false,
    createMissingAreas: false,
    createMissingCavers: false,
    createMissingTripTypes: false,
  },
  decisions: {},
  updatedAt: null,
};

/** The two roster entries the ambiguous name answers to, and so the only choices offered for it. */
const TWO_IOANAS = [
  { id: '11111111-1111-4111-8111-111111111111', name: 'Ioana Campioana (1998)' },
  { id: '22222222-2222-4222-8222-222222222222', name: 'Ioana Campioana (2011)' },
];

/**
 * A sheet whose people cannot all be created: one name two roster entries answer to, one bare
 * initial that must create nobody, and — the control — one ordinary full name that missed and
 * that a single switch would create. Both of the first two end with the person off the trip they
 * went on; the third does not, and a screen that lists it among them is lying about the remedy.
 */
const preview: TripImportPreview = {
  items: [
    {
      line: 2,
      sourceId: '1',
      startDate: '2024-05-04',
      endDate: '2024-05-04',
      startDateText: '04/05/2024',
      endDateText: '04/05/2024',
      title: 'Prospectare',
      country: null,
      massif: 'Muntii Inventati',
      subArea: null,
      caves: ['pestera 1'],
      proposers: ['Ioana Campioana'],
      participants: ['Ion A.'],
      details: null,
      details2: null,
      tripType: 'explorare',
      errors: null,
      unmapped: {},
      warnings: [],
      decision: null,
      resolution: {
        line: 2,
        tripType: { source: 'explorare', state: 'matched', id: 3, name: 'Explorare', willCreate: false },
        caves: [],
        massif: null,
        subArea: null,
        proposers: [
          { source: 'Ioana Campioana', state: 'ambiguous', caverId: null, name: null, candidates: TWO_IOANAS, mayCreate: true, willCreate: false },
        ],
        participants: [
          { source: 'Ion A.', state: 'unmatched', caverId: null, name: null, candidates: [], mayCreate: false, willCreate: false },
        ],
        locationNote: 'Muntii Inventati',
      },
    },
    {
      line: 3,
      sourceId: '2',
      startDate: '2024-06-01',
      endDate: null,
      startDateText: '01/06/2024',
      endDateText: null,
      title: 'Cartare',
      country: null,
      massif: null,
      subArea: null,
      caves: [],
      proposers: [],
      participants: [],
      details: null,
      details2: null,
      tripType: null,
      errors: null,
      unmapped: {},
      warnings: [],
      decision: null,
      resolution: null,
    },
  ],
  page: 1,
  pageSize: 25,
  totalItems: 2,
  filteredLines: [2, 3],
  selectableLines: [2, 3],
  truncated: false,
  rowCount: 2,
  readableRowCount: 2,
  failedRowCount: 0,
  skippedRowCount: 0,
  header: ['Data inceput', 'Titlu', 'Participanti'],
  resolvedColumns: {},
  unmappedColumns: [],
  // The file proved itself month-first while the stored choice still says day-first. The two
  // disagree on purpose: this is the case where the control must show the order the rows were
  // actually read in rather than the one that was asked for and overridden.
  dateOrder: 'monthFirst',
  dateOrderSource: 'file',
  ambiguousDateRows: 0,
  problems: [],
  proposals: {
    tripTypes: [
      { source: 'explorare', state: 'matched', id: 3, name: 'Explorare', willCreate: false },
      { source: 'expl.', state: 'unmatched', id: null, name: null, willCreate: true },
    ],
    people: [
      { source: 'Ioana Campioana', state: 'ambiguous', caverId: null, name: null, candidates: TWO_IOANAS, mayCreate: true, willCreate: false },
      { source: 'Ion A.', state: 'unmatched', caverId: null, name: null, candidates: [], mayCreate: false, willCreate: false },
      // Missed the roster, but is a name a person can be made from — so a single switch would
      // create them and they are nobody's decision. With that switch off `willCreate` is false
      // here exactly as it is for the bare initial above, which is why the list of names a
      // reviewer must act on can never be derived from it.
      { source: 'Vasile Necunoscut', state: 'unmatched', caverId: null, name: null, candidates: [], mayCreate: true, willCreate: false },
    ],
    caves: [],
    areas: [],
    newTripTypeCount: 1,
    newCaverCount: 0,
    newCaveCount: 0,
    newAreaCount: 0,
    ambiguousPersonCount: 1,
    uncreatablePersonCount: 1,
    ambiguousPlaceCount: 0,
  },
};

vi.mock('../../api/hooks.ts', () => ({
  useTripImportSession: () => ({ data: session, isError: false }),
  useSaveTripImportSession: () => ({ mutate: saveSessionMutate }),
  useTripImportPreview: () => ({ data: preview, isFetching: false, error: null }),
  useCommitTripImport: () => ({ mutateAsync: commitMutate, isPending: false }),
  useUploadFile: () => ({ mutateAsync: uploadMutate, isPending: false }),
  useDocument: () => ({ data: document_ }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useTripImportColumns: () => ({ data: undefined }),
  useTripTypes: () => ({
    data: [
      { id: 3, name: 'Explorare' },
      { id: 4, name: 'Cartare' },
    ],
    isLoading: false,
  }),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => params, useNavigate: () => navigate };
});

const { default: TripImportPage } = await import('./TripImportPage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <App>
        <TripImportPage />
      </App>
    </MemoryRouter>,
  );
}

afterEach(cleanup);

beforeEach(() => {
  params = { fileId: 'file-1' };
  document_ = undefined;
  commitMutate.mockReset();
  saveSessionMutate.mockReset();
  navigate.mockReset();
  uploadMutate.mockReset();
});

describe('TripImportPage', () => {
  it('says people cannot all be created before the confirm button is pressed', async () => {
    show();

    // The warning stands above the table on the loaded screen — not behind a tooltip, not
    // behind a disabled button, and not after the fact in the result modal.
    const warning = await screen.findByTestId('trip-import-people-warning');
    expect(warning).toBeTruthy();
    expect(warning.textContent).toContain('Ion A.');
    expect(commitMutate).not.toHaveBeenCalled();

    // And the headline figure counts both kinds: one name two cavers answer to, and one the
    // rules refuse to invent a person for.
    const summary = screen.getByTestId('trip-import-summary');
    expect(summary.textContent).toContain('People needing a decision');
    expect(within(summary).getByText('2')).toBeTruthy();
  });

  it('shows a count that is nothing rather than hiding it', () => {
    show();
    const summary = screen.getByTestId('trip-import-summary');
    // Four "would be added" figures, all zero on this sheet, all on screen.
    for (const title of ['New trip types', 'New cavers', 'New caves', 'New areas']) {
      expect(summary.textContent).toContain(title);
    }
    expect(within(summary).getAllByText('0').length).toBeGreaterThanOrEqual(4);
  });

  it('confirms the rows the server calls selectable, less the ones just set aside', async () => {
    show();

    fireEvent.click(screen.getByTestId('trip-import-take-3'));
    await waitFor(() => expect(screen.getByTestId('trip-import-commit').textContent).toContain('1'));

    commitMutate.mockResolvedValue({
      batchId: 'b1',
      createdTripCount: 1,
      createdFeatureCount: 0,
      skippedCount: 1,
      failures: [],
    });
    fireEvent.click(screen.getByTestId('trip-import-commit'));

    await waitFor(() => expect(commitMutate).toHaveBeenCalled());
    const body = commitMutate.mock.calls[0][0].body;
    expect(body.lines).toEqual([2]);
    expect(body.decisions['3']).toEqual({ action: 'skip' });
  });

  it('offers the review that already exists when the same sheet is uploaded twice', async () => {
    params = {};
    document_ = { id: 'doc-7', title: 'ture.csv', currentFileId: 'file-9' };
    // The identifier is read from the refusal's own member, never out of the sentence. The
    // detail here is deliberately worded so that no identifier could be recovered from it: a
    // client parsing the prose would fall through to "the upload failed" and send the reader
    // looking for a problem that does not exist.
    uploadMutate.mockRejectedValue(
      new ApiError(409, 'file.duplicate', 'Fișierul este deja stocat.', {
        code: 'file.duplicate',
        duplicateOfDocumentId: 'doc-7',
      }),
    );
    show();

    fireEvent.change(document.querySelector('input[type="file"]')!, {
      target: { files: [new File(['a,b\n1,2\n'], 'ture.csv', { type: 'text/csv' })] },
    });

    const notice = await screen.findByTestId('trip-import-duplicate');
    expect(notice).toBeTruthy();
    fireEvent.click(screen.getByTestId('trip-import-duplicate-resume'));
    // Not a dead end: the reader lands on the review of the file the store already holds.
    expect(navigate).toHaveBeenCalledWith('/trip-logs/import/file-9');
  });

  it('does not call a person uncreatable when a switch would create them', async () => {
    show();

    // The distinction the screen exists to make. With the create-cavers switch off — the state
    // the screen opens in — nothing is created, so every name that missed the roster reports
    // `willCreate: false`, the bare initial and the ordinary full name alike. Only one of them
    // is a decision, and the count in the title says so; a list derived from `willCreate` named
    // both and contradicted the number directly above it.
    const warning = await screen.findByTestId('trip-import-people-warning');
    expect(warning.textContent).toContain('and 1 cannot be created');

    const uncreatable = within(warning).getByTestId('trip-import-people-uncreatable');
    expect(uncreatable.textContent).toContain('Ion A.');
    expect(uncreatable.textContent).not.toContain('Vasile Necunoscut');
  });

  it('lets the reviewer say which caver an ambiguous name meant', async () => {
    show();

    // A count that says a decision is needed, next to no way of making it, is the same failure
    // as no count at all — one step further along. The choice offered is exactly the roster
    // entries the name answered to, which is also the only set the server honours.
    const chooser = await screen.findByTestId('trip-import-choose-caverChoices-Ioana Campioana');
    fireEvent.mouseDown(within(chooser).getByRole('combobox'));
    fireEvent.click(await screen.findByTitle('Ioana Campioana (2011)'));

    await waitFor(() =>
      expect(saveSessionMutate).toHaveBeenCalledWith(
        expect.objectContaining({
          body: expect.objectContaining({
            options: expect.objectContaining({
              caverChoices: { 'Ioana Campioana': '22222222-2222-4222-8222-222222222222' },
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

  it('lists each distinct type value and what confirming would do to the vocabulary', async () => {
    show();

    // The figure "New trip types: 1" cannot say which word, and a word this import appends to
    // an installation-wide vocabulary is not removed by taking the import back. So every
    // distinct value is listed with what it matched.
    const types = await screen.findByTestId('trip-import-types');
    expect(types.textContent).toContain('explorare');
    expect(types.textContent).toContain('expl.');
    expect(types.textContent).toContain('A new type word is added');

    // And the unmatched one can be pointed at a type that already exists instead of creating a
    // second spelling of it.
    const chooser = within(types).getByTestId('trip-import-type-choice-expl.');
    fireEvent.mouseDown(within(chooser).getByRole('combobox'));
    fireEvent.click(await screen.findByTitle('Cartare'));

    await waitFor(() =>
      expect(saveSessionMutate).toHaveBeenCalledWith(
        expect.objectContaining({
          body: expect.objectContaining({
            options: expect.objectContaining({ tripTypeChoices: { 'expl.': 4 } }),
          }),
        }),
      ),
      // The autosave is debounced by 800ms on purpose, so this wait has to outlast it by a clear
      // margin — the default one-second budget is close enough to the debounce that the test
      // passes alone and fails under a loaded suite.
      { timeout: 5000 },
    );
  });

  it('shows the day/month order the file was read in, not the one that was overridden', async () => {
    show();

    // The stored choice says day-first; the file settled on month-first and every row was read
    // that way. Showing the stored choice put "Day first" beside a tag reading "The file
    // itself" — two statements contradicting each other about how a club's whole history was
    // dated, next to a control that appeared to do nothing when it was moved.
    const control = await screen.findByTestId('trip-import-date-order');
    expect(control.textContent).toContain('Month first');
    expect(screen.getByTestId('trip-import-date-order-fixed')).toBeTruthy();
  });

  it('will not write a second batch of the same trips after a confirmation', async () => {
    show();

    commitMutate.mockResolvedValue({
      batchId: 'b1',
      createdTripCount: 2,
      createdFeatureCount: 0,
      skippedCount: 0,
      failures: [],
    });
    fireEvent.click(screen.getByTestId('trip-import-commit'));
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));

    // The rows that were created are set aside, so the selection cannot recreate them. Left
    // standing, it recomputed itself from the still-cached preview and the button read "Create
    // 2 trips" again over trips that already existed.
    await waitFor(() =>
      expect(screen.getByTestId('trip-import-commit')).toHaveProperty('disabled', true),
    );

    // Two independent reasons, and the second is the one that survives the modal being closed:
    // the selection itself is now empty, so dismissing the result and pressing again cannot
    // write a second batch of the same trips. Asserted on the count rather than on the modal
    // because the count is what the button acts on.
    expect(screen.getByTestId('trip-import-commit').textContent).toContain('0');
    fireEvent.click(screen.getByTestId('trip-import-commit'));
    expect(commitMutate).toHaveBeenCalledTimes(1);
  });
});
