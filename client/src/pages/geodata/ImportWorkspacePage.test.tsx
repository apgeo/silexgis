// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { ImportCandidate, ImportPreview, ImportPreviewRequest, ImportSession } from '../../api/hooks.ts';

const commitMutate = vi.fn();
const saveSessionMutate = vi.fn();
// What the save mutation reports about its last call; a test sets it to a failed save.
const saveSessionState: { isError: boolean; isPending: boolean; variables?: unknown } = {
  isError: false,
  isPending: false,
};
const previewRefetch = vi.fn();
const sessionRefetch = vi.fn();
const previewCalls: { body: ImportPreviewRequest; enabled: boolean }[] = [];
/** What the preview hook answers besides its data: a failure, when a test wants one. */
let previewFailure: ApiError | null = null;
let sessionFailure: ApiError | null = null;

let session: ImportSession = {
  geofileId: 'geo-1',
  options: {
    termRuleSetId: 'set-1',
    languages: [],
    mapping: {},
    elevation: 'keep',
    tracks: 'ignore',
    visibility: 'private',
    locationProtected: false,
    tagIds: [],
    duplicateRadiusMeters: 50,
  },
  decisions: {},
  allowCreateWithoutReview: false,
  updatedAt: null,
};

const cave: ImportCandidate = {
  sourceId: 1,
  geometry: 'point',
  sourceName: 'P. Ursilor',
  sourceDescription: null,
  sourceCode: null,
  sourceElevation: 812,
  ruleId: 'cave-abbrev',
  ruleName: 'Cave abbreviation',
  conflictingRuleNames: [],
  proposedKind: 'cave',
  proposedCaveTypeCode: 'cave',
  proposedEntranceTypeCode: null,
  proposedFeatureTypeCode: null,
  proposedName: 'Ursilor',
  geom: { type: 'Point', coordinates: [25.5, 45.6] },
  duplicate: null,
  decision: null,
};

const unclaimed: ImportCandidate = {
  ...cave,
  sourceId: 2,
  sourceName: 'Parcare',
  sourceElevation: null,
  ruleId: null,
  ruleName: null,
  proposedKind: null,
  proposedCaveTypeCode: null,
  proposedName: null,
  geom: { type: 'Point', coordinates: [25.54, 45.64] },
};

const conflicted: ImportCandidate = {
  ...cave,
  sourceId: 3,
  sourceName: 'Peștera de la Izbuc',
  proposedName: 'de la Izbuc',
  conflictingRuleNames: ['Spring'],
  geom: { type: 'Point', coordinates: [25.52, 45.62] },
  duplicate: {
    featureId: 'feat-9',
    name: 'Izbucul Mare',
    kind: 'generic',
    distanceMeters: 12.4,
    nameSimilarity: 0.4,
    caveFeatureId: null,
    caveName: null,
  },
};

const fullPreview: ImportPreview = {
  items: [cave, unclaimed, conflicted],
  page: 1,
  pageSize: 25,
  totalItems: 3,
  filteredSourceIds: [1, 2, 3],
  // The unclaimed row is filtered in but not selectable: nothing has said what it should
  // become, so "select all" must not take it.
  selectableSourceIds: [1, 3],
  candidateCount: 3,
  matchedCount: 2,
  unmatchedCount: 1,
  trackCount: 0,
  areaCount: 0,
  truncated: false,
  fileRowCount: 3,
  scannedRowCount: 3,
  ruleHits: [
    { ruleId: 'cave-abbrev', ruleName: 'Cave abbreviation', count: 2 },
    { ruleId: 'spring', ruleName: 'Spring', count: 0 },
  ],
  ruleSet: null,
};

let preview: ImportPreview = fullPreview;

vi.mock('../../api/hooks.ts', () => ({
  useImportSession: () =>
    sessionFailure
      ? { data: undefined, isError: true, error: sessionFailure, refetch: sessionRefetch }
      : { data: session, isError: false },
  useEffectiveTermRuleSet: () => ({ data: null }),
  useSaveImportSession: () => ({ mutate: saveSessionMutate, ...saveSessionState }),
  useImportPreview: (_geofileId: string, body: ImportPreviewRequest, enabled: boolean) => {
    previewCalls.push({ body, enabled });
    // A failed dry run still holds the previous answer as data when the query kept it, which is
    // exactly what must not be shown as current.
    return previewFailure
      ? { data: preview, isFetching: false, isError: true, error: previewFailure, refetch: previewRefetch }
      : { data: preview, isFetching: false, isError: false, refetch: previewRefetch };
  },
  useCommitImport: () => ({ mutateAsync: commitMutate, isPending: false }),
  useGeofile: () => ({ data: { id: 'geo-1', name: 'trip.gpx', format: 'gpx' } }),
  useTermRuleSets: () => ({ data: [], isLoading: false }),
  useCaveTypes: () => ({ data: [{ id: 1, code: 'cave', name: 'Cave' }] }),
  useEntranceTypes: () => ({ data: [{ id: 1, code: 'natural', name: 'Natural' }] }),
  useFeatureTypes: () => ({ data: [{ id: 1, code: 'sinkhole', name: 'Sinkhole', acceptedGeometryClasses: ['point'] }] }),
  useGenericFeatureTypes: () => ({ data: [{ id: 1, code: 'sinkhole', name: 'Sinkhole', acceptedGeometryClasses: ['point'] }] }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useGeofileColumns: () => ({ data: undefined }),
  // A confirmation is queued and its objects are created by a job, so the page watches that job
  // and then reads the batch it named. Both are idle here: these tests are about the review
  // table and what it sends, not about what the queue does with it afterwards.
  useProcessingJob: () => ({ data: undefined }),
  useImportBatch: () => ({ data: undefined }),
}));

// The map builds a real OpenLayers instance against a canvas jsdom does not have; the
// workspace is a table beside a map, and this file is about the table.
vi.mock('../../components/import/CandidateMap.tsx', () => ({
  default: () => <div data-testid="import-candidate-map" />,
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => ({ geofileId: 'geo-1' }), useNavigate: () => vi.fn() };
});

const { default: ImportWorkspacePage } = await import('./ImportWorkspacePage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <App>
        <ImportWorkspacePage />
      </App>
    </MemoryRouter>,
  );
}

afterEach(cleanup);

describe('ImportWorkspacePage', () => {
  beforeEach(() => {
    commitMutate.mockReset();
    commitMutate.mockResolvedValue({
      batch: { id: 'batch-1', createdCount: 1, attachedCount: 0, skippedCount: 2 },
      failures: [],
    });
    saveSessionMutate.mockReset();
  saveSessionState.isError = false;
  saveSessionState.isPending = false;
  saveSessionState.variables = undefined;
    session = { ...session, allowCreateWithoutReview: false, decisions: {} };
    preview = fullPreview;
    previewFailure = null;
    sessionFailure = null;
    previewCalls.length = 0;
    previewRefetch.mockReset();
    sessionRefetch.mockReset();
  });

  it('says when the file holds more rows than one review reads, and how many it read', async () => {
    preview = { ...fullPreview, truncated: true, fileRowCount: 9000, scannedRowCount: 5000 };
    show();
    await screen.findByText('Ursilor');

    expect(screen.getByText(i18n.t('vectorImport.truncatedTitle'))).toBeInTheDocument();
    expect(screen.getByText(i18n.t('vectorImport.truncated', { scanned: 5000, total: 9000 })))
      .toBeInTheDocument();
  });

  it('says nothing about a ceiling when the whole file was read', async () => {
    show();
    await screen.findByText('Ursilor');

    expect(screen.queryByText(i18n.t('vectorImport.truncatedTitle'))).toBeNull();
  });

  it('creates without review by asking for exactly that, with no selection of its own', async () => {
    session = { ...session, allowCreateWithoutReview: true };
    show();
    await screen.findByText('Ursilor');

    fireEvent.click(screen.getByTestId('import-commit-all'));

    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));
    const body = commitMutate.mock.calls[0][0].body;
    expect(body.withoutReview).toBe(true);
    expect(body.selection).toEqual([]);
  });

  it('shows what the rules made of the file before anything is created', async () => {
    show();

    // The proposed name, with the identifying term already out of it, next to what the file
    // actually said — the reviewer checks both.
    expect(await screen.findByText('Ursilor')).toBeInTheDocument();
    expect(screen.getByText('P. Ursilor')).toBeInTheDocument();

    // A rule that claimed nothing is listed with its zero: that is the rule not doing what
    // its author thought, and hiding it is how it stays broken.
    const hits = screen.getByTestId('import-rule-hits');
    expect(within(hits).getByText('Spring')).toBeInTheDocument();
    expect(within(hits).getByText('0')).toBeInTheDocument();
  });

  it('says when two rules claimed one candidate rather than arbitrating silently', async () => {
    show();
    expect(await screen.findByText('Two rules claimed it')).toBeInTheDocument();
  });

  // The review is saved as it is worked on; a save that failed leaves the stored review behind
  // the screen, so the page says so, offers to send it again and holds the confirmation meanwhile.
  it('says the last save of the review failed, holds the confirmation and sends it again on Retry', async () => {
    const lastSave = { geofileId: 'g1', body: { options: {}, decisions: {} } };
    saveSessionState.isError = true;
    saveSessionState.variables = lastSave;
    show();
    await screen.findByText('Ursilor');
    fireEvent.click(screen.getByTestId('import-select-all'));

    expect(await screen.findByText(/latest changes to this review could not be saved/)).toBeInTheDocument();
    expect(screen.getByTestId('import-commit')).toBeDisabled();
    fireEvent.click(screen.getByTestId('import-commit'));
    expect(commitMutate).not.toHaveBeenCalled();

    saveSessionMutate.mockClear();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(saveSessionMutate).toHaveBeenCalledWith(lastSave);
  });

  it('creates only what was selected, and nothing until the button is pressed', async () => {
    show();
    await screen.findByText('Ursilor');

    // Nothing has been sent yet.
    expect(commitMutate).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('import-select-all'));
    fireEvent.click(screen.getByTestId('import-commit'));

    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));
    const body = commitMutate.mock.calls[0][0].body;
    expect(body.withoutReview).toBe(false);
    // The unclaimed row is not selectable — nothing said what it should become — so "all"
    // means the two the rules claimed.
    expect(body.selection.sort()).toEqual([1, 3]);
  });

  it('offers creating without review only where the installation allows it', async () => {
    show();
    await screen.findByText('Ursilor');
    expect(screen.queryByTestId('import-commit-all')).not.toBeInTheDocument();

    cleanup();
    session = { ...session, allowCreateWithoutReview: true };
    show();
    await screen.findByText('Ursilor');
    expect(screen.getByTestId('import-commit-all')).toBeInTheDocument();
  });

  it('turns "it is this one" into a decision that creates nothing', async () => {
    show();
    await screen.findByText('Ursilor');

    fireEvent.click(screen.getByText('It is this one'));
    fireEvent.click(screen.getByTestId('import-select-all'));
    fireEvent.click(screen.getByTestId('import-commit'));

    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));
    expect(commitMutate.mock.calls[0][0].body.decisions['3']).toMatchObject({
      action: 'attach',
      attachToFeatureId: 'feat-9',
    });
  });

  it('says a refused dry run failed, offers a retry and shows no rows or counts as current', async () => {
    previewFailure = new ApiError(400, 'import.file_not_ready');
    show();

    expect(await screen.findByTestId('import-preview-error')).toBeInTheDocument();
    expect(screen.getByText(i18n.t('geodata.refusals.fileNotReady'))).toBeInTheDocument();
    // The previous answer is not presented as the answer for these settings.
    expect(screen.queryByText('Ursilor')).toBeNull();
    expect(screen.getByTestId('import-commit')).toBeDisabled();

    fireEvent.click(within(screen.getByTestId('import-preview-error')).getByRole('button'));
    expect(previewRefetch).toHaveBeenCalledTimes(1);
  });

  it('says why a confirmation was refused', async () => {
    commitMutate.mockReset();
    commitMutate.mockRejectedValue(new ApiError(400, 'import.nothing_created'));
    show();
    await screen.findByText('Ursilor');

    fireEvent.click(screen.getByTestId('import-select-all'));
    fireEvent.click(screen.getByTestId('import-commit'));

    expect(await screen.findByText(i18n.t('geodata.refusals.nothingCreated'))).toBeInTheDocument();
  });

  it('says a saved review could not be read without calling the file missing, and offers a retry', async () => {
    sessionFailure = new ApiError(503);
    show();

    const alert = await screen.findByTestId('import-session-error');
    expect(alert.textContent).toContain(i18n.t('geodata.refusals.server', { status: 503 }));
    expect(alert.textContent).not.toContain(i18n.t('vectorImport.fileNotFound'));
    fireEvent.click(within(alert).getByRole('button'));
    expect(sessionRefetch).toHaveBeenCalledTimes(1);
  });

  it('says the file is not available when it is gone', async () => {
    sessionFailure = new ApiError(404, 'geofile.not_found');
    show();

    expect((await screen.findByTestId('import-session-error')).textContent).toContain(
      i18n.t('vectorImport.fileNotFound'),
    );
  });

  it('drops from the selection a row the new settings no longer let anything be made of', async () => {
    const view = show();
    await screen.findByText('Ursilor');
    fireEvent.click(screen.getByTestId('import-select-all'));
    expect(screen.getByTestId('import-commit').textContent).toContain('2');

    // The settings changed and the answer for them no longer lets the conflicted row be made.
    preview = { ...fullPreview, selectableSourceIds: [1] };
    view.rerender(
      <MemoryRouter>
        <App>
          <ImportWorkspacePage />
        </App>
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByTestId('import-commit').textContent).toContain('1'));
    fireEvent.click(screen.getByTestId('import-commit'));
    await waitFor(() => expect(commitMutate).toHaveBeenCalledTimes(1));
    expect(commitMutate.mock.calls[0][0].body.selection).toEqual([1]);
  });

  it('asks for a dry run once the reviewer pauses, not on every letter typed into an option', async () => {
    show();
    await screen.findByText('Ursilor');
    // Past the first settled request, so what follows is only the typing.
    await new Promise((resolve) => setTimeout(resolve, 400));

    const prefix = screen
      .getByText(i18n.t('vectorImport.namePrefix'))
      .closest('.ant-form-item')!
      .querySelector('input')!;
    previewCalls.length = 0;
    for (const typed of ['P', 'Pe', 'Pes', 'Pest']) {
      fireEvent.change(prefix, { target: { value: typed } });
    }
    await new Promise((resolve) => setTimeout(resolve, 500));

    const asked = new Set(
      previewCalls.filter((call) => call.enabled).map((call) => call.body.options.namePrefix ?? ''),
    );
    expect(asked.has('Pest')).toBe(true);
    for (const partial of ['P', 'Pe', 'Pes']) {
      expect(asked.has(partial)).toBe(false);
    }
  });
});
