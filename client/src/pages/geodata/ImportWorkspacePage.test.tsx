// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ImportCandidate, ImportPreview, ImportSession } from '../../api/hooks.ts';

const commitMutate = vi.fn();
const saveSessionMutate = vi.fn();

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

const preview: ImportPreview = {
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

vi.mock('../../api/hooks.ts', () => ({
  useImportSession: () => ({ data: session, isError: false }),
  useEffectiveTermRuleSet: () => ({ data: null }),
  useSaveImportSession: () => ({ mutate: saveSessionMutate }),
  useImportPreview: () => ({ data: preview, isFetching: false }),
  useCommitImport: () => ({ mutateAsync: commitMutate, isPending: false }),
  useGeofile: () => ({ data: { id: 'geo-1', name: 'trip.gpx', format: 'gpx' } }),
  useTermRuleSets: () => ({ data: [], isLoading: false }),
  useCaveTypes: () => ({ data: [{ id: 1, code: 'cave', name: 'Cave' }] }),
  useEntranceTypes: () => ({ data: [{ id: 1, code: 'natural', name: 'Natural' }] }),
  useFeatureTypes: () => ({ data: [{ id: 1, code: 'sinkhole', name: 'Sinkhole', acceptedGeometryClasses: ['point'] }] }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useGeofileColumns: () => ({ data: undefined }),
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
    session = { ...session, allowCreateWithoutReview: false, decisions: {} };
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
});
