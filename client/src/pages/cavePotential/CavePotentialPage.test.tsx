// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import type {
  CavePotentialModelInfo,
  CavePotentialResultInfo,
  CavePotentialWeighting,
  EvidenceLayerInfo,
} from '../../api/hooks/cavePotential.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

const state = vi.hoisted(() => ({
  mayReadTerrain: true,
  canCreate: true,
  models: [] as unknown[],
  results: [] as unknown[],
  weighting: null as unknown,
  resultsEnabled: [] as boolean[],
  weightingRequests: [] as unknown[],
  createSpy: vi.fn(),
  updateSpy: vi.fn(),
  cloneSpy: vi.fn(),
  deleteModelSpy: vi.fn(),
  deleteResultSpy: vi.fn(),
  runSpy: vi.fn(),
}));

const evidenceLayers: EvidenceLayerInfo[] = [
  { code: 'slope', valueKind: 'continuous', source: 'terrain', usesPositions: false, unit: 'deg', transforms: ['classes', 'fuzzyLinear'], categories: [] },
  { code: 'sinkhole_distance', valueKind: 'continuous', source: 'features', usesPositions: true, unit: 'm', transforms: ['classes', 'fuzzyLinear'], categories: [] },
];

vi.mock('../../api/hooks/access.ts', () => ({
  useCan: (domain: string) => domain === 'terrain' && state.mayReadTerrain,
  useCreateDoor: () => ({ canCreate: state.canCreate, unbound: state.canCreate, cavingGroups: [] }),
}));

vi.mock('../../api/hooks/people.ts', () => ({
  useCavingGroups: () => ({ data: [{ id: 'g-1', name: 'Speo Club' }] }),
}));

vi.mock('../../api/hooks/features.ts', () => ({
  useFeatures: () => ({ data: { items: [] }, isFetching: false }),
}));

vi.mock('../../api/hooks/cavePotential.ts', () => ({
  useEvidenceLayers: () => ({ data: evidenceLayers }),
  useCavePotentialModels: () => ({ data: state.models, isLoading: false }),
  useCavePotentialResults: (enabled: boolean) => {
    state.resultsEnabled.push(enabled);
    return { data: enabled ? state.results : undefined, isLoading: false };
  },
  useCavePotentialWeighting: (request: unknown) => {
    state.weightingRequests.push(request);
    return { data: request === null ? undefined : state.weighting, isError: false, error: null };
  },
  useCreateCavePotentialModel: () => ({ mutateAsync: state.createSpy, isPending: false }),
  useUpdateCavePotentialModel: () => ({ mutateAsync: state.updateSpy, isPending: false }),
  useCloneCavePotentialModel: () => ({ mutateAsync: state.cloneSpy, isPending: false }),
  useDeleteCavePotentialModel: () => ({ mutateAsync: state.deleteModelSpy, isPending: false }),
  useDeleteCavePotentialResult: () => ({ mutateAsync: state.deleteResultSpy, isPending: false }),
  useRunCavePotentialModel: () => ({ mutateAsync: state.runSpy, isPending: false }),
}));

const { default: CavePotentialPage } = await import('./CavePotentialPage.tsx');

function Address() {
  const location = useLocation();
  return <span data-testid="address">{`${location.pathname}${location.hash}`}</span>;
}

function show() {
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/cave-potential']}>
        <CavePotentialPage />
        <Address />
      </MemoryRouter>
    </AntApp>,
  );
}

function model(over: Partial<CavePotentialModelInfo> = {}): CavePotentialModelInfo {
  return {
    id: 'm-1',
    name: 'Near the sinkholes',
    description: null,
    method: 'pairwiseComparison',
    definition: {
      layers: [
        { layer: 'slope', transform: { kind: 'fuzzyLinear', low: 0, high: 30 } },
        { layer: 'sinkhole_distance', transform: { kind: 'fuzzyLinear', low: 500, high: 0 } },
      ],
      pairwise: [[3]],
    },
    weighting: {
      weights: [{ layer: 'slope', weight: 0.75 }, { layer: 'sinkhole_distance', weight: 0.25 }],
      consistency: { principalEigenvalue: 2, index: 0, randomIndex: 0, ratio: 0, acceptable: true },
    },
    usesPositions: true,
    cavingGroupId: null,
    visibility: 'private',
    mine: true,
    authorName: 'Ana Pop',
    mayEdit: true,
    mayDelete: true,
    createdAt: '2026-11-01T10:00:00Z',
    updatedAt: '2026-11-01T10:00:00Z',
    ...over,
  };
}

function result(over: Partial<CavePotentialResultInfo> = {}): CavePotentialResultInfo {
  return {
    id: 'r-1',
    modelId: 'm-1',
    modelName: 'Near the sinkholes',
    method: 'pairwiseComparison',
    definition: null,
    weighting: null,
    areaFeatureId: 'a-1',
    areaName: 'Invented plateau',
    cellMeters: 50,
    status: 'ready',
    errorCode: null,
    message: null,
    usesPositions: true,
    evidenceFeatures: 2,
    protectedFeatures: 0,
    summary: { layers: [], areaCells: 400, scoredCells: 380, minimum: 0, maximum: 1, mean: 0.42 },
    west: 22.5,
    south: 46.5,
    east: 22.51,
    north: 46.51,
    columns: 20,
    rows: 20,
    sizeBytes: 4096,
    noData: -9999,
    mine: true,
    requestedByName: 'Ana Pop',
    ranAsFullAdministrator: false,
    terrainBuildId: null,
    terrainBuildActive: null,
    terrainInputs: [],
    mayDelete: true,
    url: '/api/v1/cave-potential/results/r-1/content?token=t',
    computedAt: '2026-11-02T10:00:00Z',
    createdAt: '2026-11-02T09:59:00Z',
    ...over,
  };
}

beforeEach(async () => {
  await i18n.changeLanguage('en');
  state.mayReadTerrain = true;
  state.canCreate = true;
  state.models = [model()];
  state.results = [result()];
  state.weighting = null;
  state.resultsEnabled.length = 0;
  state.weightingRequests.length = 0;
  for (const spy of [state.createSpy, state.updateSpy, state.cloneSpy, state.deleteModelSpy, state.deleteResultSpy, state.runSpy]) {
    spy.mockReset().mockResolvedValue({});
  }
  useWorkspaceStore.setState({ visibleCavePotentialIds: [] });
});

afterEach(() => cleanup());

describe('CavePotentialPage', () => {
  it('asks for no results and offers no run to a reader without the terrain right, and says why', () => {
    state.mayReadTerrain = false;
    show();

    // The models are still theirs to read: a model holds no place.
    expect(screen.getByText('Near the sinkholes')).toBeInTheDocument();
    expect(screen.getByTestId('cave-potential-needs-terrain')).toBeInTheDocument();
    expect(screen.queryByTestId('cave-potential-run')).not.toBeInTheDocument();
    expect(screen.queryByTestId('cave-potential-results')).not.toBeInTheDocument();
    // Every reading of the list was switched off, so no request the server would refuse is made.
    expect(state.resultsEnabled.length).toBeGreaterThan(0);
    expect(state.resultsEnabled.every((enabled) => !enabled)).toBe(true);
  });

  it('lists a model with the weights it comes to and marks one that uses recorded positions', () => {
    show();
    const models = screen.getByTestId('cave-potential-models');
    expect(within(models).getByText('Slope 75 %')).toBeInTheDocument();
    expect(within(models).getByText('Distance to sinkholes 25 %')).toBeInTheDocument();
    expect(within(models).getByTestId('cave-potential-positions')).toBeInTheDocument();
    expect(within(models).getByTestId('cave-potential-run')).toBeInTheDocument();
  });

  it('offers change and delete only where the server says the reader may make them', () => {
    state.models = [model({ mine: false, mayEdit: false, mayDelete: false })];
    show();
    expect(screen.queryByTestId('cave-potential-edit')).not.toBeInTheDocument();
    expect(screen.queryByTestId('cave-potential-delete')).not.toBeInTheDocument();
    // A copy is offered: anybody who may read a model may take one of their own.
    expect(screen.getByTestId('cave-potential-copy')).toBeInTheDocument();
    expect(screen.getByText('Ana Pop')).toBeInTheDocument();
  });

  it('copies a model under a name that says it is a copy', async () => {
    show();
    fireEvent.click(screen.getByTestId('cave-potential-copy'));
    await waitFor(() =>
      expect(state.cloneSpy).toHaveBeenCalledWith({ id: 'm-1', name: 'Copy of Near the sinkholes' }),
    );
  });

  it('turns a ready map on and goes to the map looking at it', () => {
    show();
    fireEvent.click(screen.getByTestId('cave-potential-show'));
    expect(useWorkspaceStore.getState().visibleCavePotentialIds).toEqual(['r-1']);
    expect(screen.getByTestId('address').textContent).toMatch(/^\/map#\d+\.\d\d\/46\.50500\/22\.50500$/);
  });

  it('says what went wrong with a failed run and offers no map of it', () => {
    state.results = [
      result({ status: 'failed', errorCode: 'cave_potential.evidence_unavailable', message: 'slope: no_terrain', url: null, summary: null }),
    ];
    show();
    expect(screen.getByTestId('cave-potential-result-failure').textContent).toBe(
      "Some of the model's evidence is not available here (Slope: no elevation built)",
    );
    expect(screen.queryByTestId('cave-potential-show')).not.toBeInTheDocument();
  });

  it('shows the consistency of a model’s comparisons as a warning that does not stop it being saved', async () => {
    const inconsistent: CavePotentialWeighting = {
      weights: [{ layer: 'slope', weight: 0.75 }, { layer: 'sinkhole_distance', weight: 0.25 }],
      consistency: { principalEigenvalue: 3.4, index: 0.2, randomIndex: 0.58, ratio: 0.345, acceptable: false },
    };
    state.weighting = inconsistent;
    show();
    fireEvent.click(screen.getByTestId('cave-potential-edit'));

    const editor = await screen.findByTestId('cave-potential-editor');
    const consistency = await within(editor).findByTestId('cave-potential-consistency');
    expect(consistency.textContent).toContain('Consistency ratio 0.345');
    expect(consistency.textContent).toContain('The model can still be saved and run');
    expect(within(editor).getByTestId('cave-potential-weight-slope').textContent).toBe('Slope: 75.0 %');
    // The model weighs a layer made from recorded positions, and the form says what follows.
    expect(within(editor).getByTestId('cave-potential-positions-notice')).toBeInTheDocument();
    // What was asked of the server is the definition as the form holds it.
    expect(state.weightingRequests.at(-1)).toEqual({
      method: 'pairwiseComparison',
      definition: model().definition,
    });

    fireEvent.click(screen.getByTestId('cave-potential-save'));
    await waitFor(() => expect(state.updateSpy).toHaveBeenCalledTimes(1));
    expect(state.updateSpy.mock.calls[0][0]).toEqual({
      id: 'm-1',
      write: {
        name: 'Near the sinkholes',
        description: null,
        method: 'pairwiseComparison',
        definition: model().definition,
        visibility: 'private',
        cavingGroupId: null,
      },
    });
  });

  it('asks for no weights and offers no save while a new model has a blank left', async () => {
    show();
    fireEvent.click(screen.getByTestId('cave-potential-new-model'));
    const editor = await screen.findByTestId('cave-potential-editor');
    expect(within(editor).getByTestId('cave-potential-unfinished')).toBeInTheDocument();
    expect(screen.getByTestId('cave-potential-save')).toBeDisabled();
    expect(state.weightingRequests.every((request) => request === null)).toBe(true);
  });

  it('offers a new model only to a reader who may save one', () => {
    state.canCreate = false;
    show();
    expect(screen.queryByTestId('cave-potential-new-model')).not.toBeInTheDocument();
    expect(screen.queryByTestId('cave-potential-copy')).not.toBeInTheDocument();
  });
});
