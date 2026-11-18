// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import type { EChartsOption } from 'echarts';
import type {
  CavePotentialModelInfo,
  CavePotentialResultInfo,
  CavePotentialTrainingInfo,
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
  drawn: [] as unknown[],
}));

// The drawing hook keeps the option it was handed, so the curves are read as the numbers drawn.
vi.mock('../../components/statistics/useECharts.ts', () => ({
  useECharts: (option: unknown) => {
    state.drawn.push(option);
    return { current: null };
  },
}));

const evidenceLayers: EvidenceLayerInfo[] = [
  { code: 'slope', valueKind: 'continuous', source: 'terrain', usesPositions: false, unit: 'deg', transforms: ['classes', 'fuzzyLinear'], categories: [] },
  { code: 'sinkhole_distance', valueKind: 'continuous', source: 'features', usesPositions: true, unit: 'm', transforms: ['classes', 'fuzzyLinear'], categories: [] },
  { code: 'entrance_density', valueKind: 'continuous', source: 'features', usesPositions: true, unit: 'per_km2', transforms: ['classes', 'fuzzyLinear'], categories: [] },
  { code: 'lithology', valueKind: 'categorical', source: 'features', usesPositions: true, unit: null, transforms: ['categories'], categories: ['limestone', 'dolomite'] },
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
    summary: { layers: [], areaCells: 400, scoredCells: 380, minimum: 0, maximum: 1, mean: 0.42, training: null },
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

/** A model that learns from known entrances: bounds and a parting, and no weights for the server to work out. */
function learning(over: Partial<CavePotentialModelInfo> = {}): CavePotentialModelInfo {
  return model({
    id: 'm-2',
    name: 'Learnt from entrances',
    method: 'weightsOfEvidence',
    definition: {
      layers: [
        {
          layer: 'sinkhole_distance',
          transform: { kind: 'classes', classes: [{ upperBound: 200, score: null }, { upperBound: null, score: null }] },
        },
        { layer: 'lithology', transform: { kind: 'categories' } },
      ],
      training: { heldOutShare: 0.3, seed: 7 },
    },
    weighting: null,
    ...over,
  });
}

/** What a run of it learnt. Invented counts: twelve occupied cells learnt from, five kept back. */
function training(over: Partial<CavePotentialTrainingInfo> = {}): CavePotentialTrainingInfo {
  return {
    heldOutShare: 0.3,
    seed: 7,
    trainingEntrances: 14,
    heldOutEntrances: 5,
    trainingCells: 12,
    heldOutCells: 5,
    protectedEntrances: 0,
    cells: 380,
    priorProbability: 12 / 380,
    valueMinimum: 0.004,
    valueMaximum: 0.21,
    expectedOccurrences: 13.4,
    tables: [
      {
        layer: 'sinkhole_distance',
        classes: [
          { class: 0, cells: 80, occurrences: 9, frequencyRatio: 3.5625, positiveWeight: 1.3321, negativeWeight: -1.2345, contrast: 2.5666, contrastDeviation: 0.6789, studentisedContrast: 3.78, corrected: false },
          { class: 1, cells: 300, occurrences: 3, frequencyRatio: 0.3167, positiveWeight: -1.2345, negativeWeight: 1.3321, contrast: -2.5666, contrastDeviation: 0.6789, studentisedContrast: -3.78, corrected: false },
        ],
      },
      {
        layer: 'lithology',
        classes: [
          { class: 0, cells: 380, occurrences: 12, frequencyRatio: 1, positiveWeight: 0, negativeWeight: null, contrast: null, contrastDeviation: null, studentisedContrast: null, corrected: false },
          { class: 1, cells: 0, occurrences: 0, frequencyRatio: null, positiveWeight: null, negativeWeight: null, contrast: null, contrastDeviation: null, studentisedContrast: null, corrected: true },
        ],
      },
    ],
    successRate: {
      points: [{ areaShare: 0, occurrenceShare: 0 }, { areaShare: 0.2, occurrenceShare: 0.75 }, { areaShare: 1, occurrenceShare: 1 }],
      areaUnderCurve: 0.8421,
      cells: 380,
      occurrences: 12,
    },
    predictionRate: {
      points: [{ areaShare: 0, occurrenceShare: 0 }, { areaShare: 0.2, occurrenceShare: 0.6 }, { areaShare: 1, occurrenceShare: 1 }],
      areaUnderCurve: 0.7012,
      cells: 380,
      occurrences: 5,
    },
    ...over,
  };
}

/** A finished run of the learning model, as its asker reads it. */
function learnt(over: Partial<CavePotentialResultInfo> = {}, trained: CavePotentialTrainingInfo = training()): CavePotentialResultInfo {
  return result({
    id: 'r-2',
    modelId: 'm-2',
    modelName: 'Learnt from entrances',
    method: 'weightsOfEvidence',
    definition: learning().definition,
    summary: { layers: [], areaCells: 400, scoredCells: 380, minimum: 0, maximum: 1, mean: 0.18, training: trained },
    ...over,
  });
}

type Series = { name: string; data: [number, number][] };

beforeEach(async () => {
  await i18n.changeLanguage('en');
  state.mayReadTerrain = true;
  state.canCreate = true;
  state.models = [model()];
  state.results = [result()];
  state.weighting = null;
  state.resultsEnabled.length = 0;
  state.weightingRequests.length = 0;
  state.drawn.length = 0;
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

  it('lists a model that learns from entrances by its layers, says so, and offers it for a run', () => {
    state.models = [learning()];
    show();
    const row = within(screen.getByTestId('cave-potential-models')).getByText('Learnt from entrances').closest('tr')!;
    expect(row.textContent).toContain('Weights of evidence');
    expect(row.textContent).toContain('Distance to sinkholes');
    // No weight is shown: there is none before a run.
    expect(row.textContent).not.toContain('%');
    expect(within(row).getByTestId('cave-potential-learns')).toBeInTheDocument();
    expect(row.textContent).not.toContain('cannot be read');
    expect(within(row).getByTestId('cave-potential-run')).toBeInTheDocument();
  });

  it('offers no run of a learning model whose definition cannot be read', () => {
    state.models = [learning({ definition: null })];
    show();
    const row = within(screen.getByTestId('cave-potential-models')).getByText('Learnt from entrances').closest('tr')!;
    expect(row.textContent).toContain('cannot be read');
    expect(within(row).queryByTestId('cave-potential-run')).not.toBeInTheDocument();
  });

  it('shows what a run learnt: counts, the two areas under the curve, the weight tables and both curves', () => {
    state.results = [result(), learnt()];
    show();

    // Only a run that learnt anything has tables to open.
    expect(screen.getAllByTestId('cave-potential-training-toggle')).toHaveLength(1);
    expect(screen.getByTestId('cave-potential-result-training').textContent).toBe(
      'learnt from 14 entrances, tested on 5 · AUC 0.842 / 0.701',
    );
    expect(screen.queryByTestId('cave-potential-training-view')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('cave-potential-training-toggle'));
    const view = screen.getByTestId('cave-potential-training-view');
    expect(within(view).getByTestId('cave-potential-training-counts').textContent).toBe(
      'Learnt from 14 entrances in 12 cells; 5 entrances in 5 cells kept back.',
    );
    expect(within(view).getByTestId('cave-potential-training-split').textContent).toBe(
      '30 % kept back, seed 7 · 380 cells of ground weighed',
    );
    expect(within(view).getByTestId('cave-potential-auc-success').textContent).toContain('AUC 0.842');
    expect(within(view).getByTestId('cave-potential-auc-prediction').textContent).toContain('AUC 0.701');
    expect(within(view).getByTestId('cave-potential-training-expected').textContent).toContain('13.4 occupied cells against the 12');
    // No entrance used was protected, so nothing is said of protected ones.
    expect(within(view).queryByTestId('cave-potential-training-protected')).not.toBeInTheDocument();

    // A measured layer's classes are named by the bounds the model was run with.
    const distance = within(view).getByTestId('cave-potential-weights-sinkhole_distance');
    const near = within(distance).getByText('below 200').closest('tr')!;
    expect(Array.from(near.querySelectorAll('td')).map((cell) => cell.textContent)).toEqual([
      'below 200', '80', '9', '3.56', '1.332', '-1.234', '2.567', '0.679', '3.78',
    ]);
    expect(within(distance).getByText('200 and above')).toBeInTheDocument();

    // A layer that is classes already names each by its own word; a class no cell falls in has
    // no figures, and one whose counts were adjusted says so.
    const rock = within(view).getByTestId('cave-potential-weights-lithology');
    const empty = within(rock).getByText('Dolomite').closest('tr')!;
    expect(Array.from(empty.querySelectorAll('td')).slice(1).map((cell) => cell.textContent)).toEqual([
      '0', '0', '–', '–', '–', '–', '–', '–',
    ]);
    expect(within(empty).getByText('adjusted')).toBeInTheDocument();

    // Both curves as percentages, with the line a map that knew nothing would draw.
    const option = state.drawn.at(-1) as EChartsOption;
    const series = option.series as Series[];
    expect(series.map((line) => line.name)).toEqual(['Success rate', 'Prediction rate', 'Chance']);
    expect(series[0].data).toEqual([[0, 0], [20, 75], [100, 100]]);
    expect(series[1].data).toEqual([[0, 0], [20, 60], [100, 100]]);
    expect(series[2].data).toEqual([[0, 0], [100, 100]]);

    // Closed again by the same button, which says which way it stands.
    expect(screen.getByTestId('cave-potential-training-toggle')).toHaveAttribute('aria-expanded', 'true');
    fireEvent.click(screen.getByTestId('cave-potential-training-toggle'));
    expect(screen.getByTestId('cave-potential-training-toggle')).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByTestId('cave-potential-training-view')).not.toBeVisible();
  });

  it('numbers the classes for a reader who is not shown the model, and says when protected entrances were used', () => {
    state.results = [learnt({ definition: null, modelName: null, mine: false }, training({ protectedEntrances: 3, expectedOccurrences: null }))];
    show();
    fireEvent.click(screen.getByTestId('cave-potential-training-toggle'));
    const view = screen.getByTestId('cave-potential-training-view');
    const distance = within(view).getByTestId('cave-potential-weights-sinkhole_distance');
    expect(within(distance).getByText('Class 1')).toBeInTheDocument();
    expect(within(distance).getByText('Class 2')).toBeInTheDocument();
    expect(within(distance).queryByText('below 200')).not.toBeInTheDocument();
    expect(within(view).getByTestId('cave-potential-training-protected').textContent).toContain('among those used: 3');
    // A frequency-ratio run has no probabilities to add up.
    expect(within(view).queryByTestId('cave-potential-training-expected')).not.toBeInTheDocument();
  });

  it('asks a learning model for bounds and a parting only, asks the server for no weights, and saves it so', async () => {
    state.models = [learning()];
    show();
    fireEvent.click(screen.getByTestId('cave-potential-edit'));
    const editor = await screen.findByTestId('cave-potential-editor');

    expect(within(editor).getByTestId('cave-potential-training')).toBeInTheDocument();
    expect(within(editor).getByRole('spinbutton', { name: 'Entrances kept back for testing (%)' })).toHaveValue('30');
    expect(within(editor).getByRole('spinbutton', { name: 'Seed of the draw' })).toHaveValue('7');
    // Bounds are asked, scores and weights are not.
    expect(within(editor).getByRole('spinbutton', { name: 'Upper bound of class 1 of Distance to sinkholes (m)' })).toHaveValue('200');
    expect(within(editor).queryByRole('spinbutton', { name: /^Score of/ })).not.toBeInTheDocument();
    expect(within(editor).queryByTestId('cave-potential-layer-weight')).not.toBeInTheDocument();
    expect(within(editor).queryByTestId('cave-potential-layer-kind')).not.toBeInTheDocument();
    // What follows from learning from entrances is said, in place of the notice about layers.
    expect(within(editor).getByTestId('cave-potential-learning-notice')).toBeInTheDocument();
    expect(within(editor).queryByTestId('cave-potential-positions-notice')).not.toBeInTheDocument();
    expect(within(editor).getByTestId('cave-potential-weights-counted')).toBeInTheDocument();
    expect(state.weightingRequests.length).toBeGreaterThan(0);
    expect(state.weightingRequests.every((request) => request === null)).toBe(true);

    fireEvent.click(screen.getByTestId('cave-potential-save'));
    await waitFor(() => expect(state.updateSpy).toHaveBeenCalledTimes(1));
    expect(state.updateSpy.mock.calls[0][0]).toEqual({
      id: 'm-2',
      write: {
        name: 'Learnt from entrances',
        description: null,
        method: 'weightsOfEvidence',
        definition: learning().definition,
        visibility: 'private',
        cavingGroupId: null,
      },
    });
  });

  it('refuses to save a learning model that weighs the density of the entrances it learns from', async () => {
    state.models = [
      model({
        method: 'directWeights',
        definition: {
          layers: [{ layer: 'entrance_density', transform: { kind: 'fuzzyLinear', low: 0, high: 5 }, weight: 1 }],
        },
      }),
    ];
    state.weighting = { weights: [{ layer: 'entrance_density', weight: 1 }], consistency: null };
    show();
    fireEvent.click(screen.getByTestId('cave-potential-edit'));
    const editor = await screen.findByTestId('cave-potential-editor');
    expect(screen.getByTestId('cave-potential-save')).toBeEnabled();
    expect(within(editor).queryByTestId('cave-potential-layer-circular')).not.toBeInTheDocument();

    fireEvent.click(within(within(editor).getByTestId('cave-potential-method')).getByText('Frequency ratio'));
    expect(await within(editor).findByTestId('cave-potential-layer-circular')).toBeInTheDocument();
    expect(screen.getByTestId('cave-potential-save')).toBeDisabled();
  });
});
