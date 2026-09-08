// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveTopology } from '../../api/hooks.ts';

const { topologySpy, modelsSpy } = vi.hoisted(() => ({
  topologySpy: vi.fn(),
  modelsSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', async () => {
  // The rule deciding whether a cave has anything to measure is the real one, not a stand-in: it
  // is the thing under test in the case below, and a mock of it would agree with itself.
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    caveHasMeasurableSurvey: actual.caveHasMeasurableSurvey,
    surveyModelReadableByViewer: actual.surveyModelReadableByViewer,
    useCaveTopology: (...args: unknown[]) => topologySpy(...args),
    useSurveyModels: (...args: unknown[]) => modelsSpy(...args),
  };
});

const { default: CaveTopologyPanel } = await import('./CaveTopologyPanel.tsx');

/** A branching cave: no loops, most stations strung along passages, plenty of dead ends. */
const branchwork: CaveTopology = {
  caveId: 'cave-1',
  surveyModelId: 'model-1',
  droppedShotCount: 0,
  mergedStationCount: 0,
  nodeCount: 41,
  edgeCount: 40,
  componentCount: 1,
  reducedNodeCount: 12,
  reducedEdgeCount: 11,
  reducedComponentCount: 1,
  cyclomaticNumber: 0,
  extremityCount: 7,
  junctionCount: 5,
  alpha: 0,
  beta: 0.92,
  gamma: 0.37,
  meanDegree: 1.83,
  degreeStandardDeviation: 0.94,
  degreeCoefficientOfVariation: 0.51,
  correlationOfVertexDegree: -0.33,
  branchCount: 11,
  loopingBranchCount: 0,
  meanBranchLengthM: 15.87,
  branchLengthCoefficientOfVariation: 1.04,
  minBranchLengthM: 2.5,
  maxBranchLengthM: 61.4,
  lengthEntropy: 0.618,
  orientationEntropy: 0.926,
  meanTortuosity: 1.2066,
  averageShortestPathLength: 3.0151,
  centralPointDominance: 0.4744,
  averageClusteringCoefficient: 0,
  computedAt: '2026-09-03T09:00:00Z',
};

const readModel = {
  id: 'model-1',
  name: 'Corrected re-export, March',
  status: 'ready',
  format: 'survex3d',
};

function show(
  data: CaveTopology | undefined,
  { isError = false, isLoading = false, models = [readModel] }: {
    isError?: boolean;
    isLoading?: boolean;
    models?: unknown[];
  } = {},
) {
  topologySpy.mockReturnValue({ data, isLoading, isError });
  modelsSpy.mockReturnValue({ data: models });
  return render(
    <App>
      <CaveTopologyPanel caveId="cave-1" />
    </App>,
  );
}

/** The text of the cell a named figure sits in, so a value and its gloss are read together. */
function cell(label: string) {
  return screen.getByText(label).closest('tr')?.textContent ?? '';
}

afterEach(cleanup);

describe('CaveTopologyPanel', () => {
  it('shows the network figures with the counts and ratios they were measured as', () => {
    show(branchwork);

    expect(cell('Independent loops')).toContain('0');
    expect(cell('Junctions')).toContain('5');
    expect(cell('Dead ends')).toContain('7');
    expect(cell('Junctions and dead ends')).toContain('12');
    expect(cell('Connections per junction (beta)')).toContain('0.92');
    expect(cell('Wandering')).toContain('1.21');
    expect(cell('Average branch length')).toContain('15.9 m');
  });

  it('says in words what each figure means, because the terms are out of a literature the reader has not read', () => {
    // The whole point of the panel. A caver told "cyclomatic number: 0" learns nothing; a caver
    // told what it counts learns what kind of cave they are looking at. A regression that dropped
    // the glosses would leave a grid of plausible numbers and pass every assertion above.
    show(branchwork);

    expect(cell('Independent loops')).toContain('genuinely different ways round the network');
    expect(cell('Passages at an average junction or dead end')).toContain('Below two is a branching cave');
    expect(cell('Evenness of passage bearings')).toContain('joints in the rock');
  });

  it('reports a figure the network was too small to define as a dash rather than as nought', () => {
    // Null is not zero here: a network of two junctions has no connectivity ratio and a single
    // branch has no spread of lengths. Printing 0.00 would state something about the cave that
    // was never measured, and it would look exactly like a measurement.
    show({
      ...branchwork,
      alpha: null,
      gamma: null,
      branchLengthCoefficientOfVariation: null,
      meanTortuosity: null,
    });

    expect(cell('Loopiness (alpha)')).toContain('—');
    expect(cell('Connectedness (gamma)')).toContain('—');
    expect(cell('Spread of branch lengths')).toContain('—');
    expect(cell('Wandering')).toContain('—');
  });

  it('says the figures cover the whole file when the reading lost nothing', () => {
    show(branchwork);

    expect(screen.getByTestId('cave-topology-completeness').textContent).toContain(
      'The figures above cover the whole of it',
    );
  });

  it('says what the reading dropped and merged when it lost something', () => {
    // A network that silently lost legs still produces entirely reasonable-looking numbers, and
    // this sentence is the only thing on the page that says they were computed over less than the
    // file contained. Nothing lost and not known are different claims and are worded differently.
    show({ ...branchwork, droppedShotCount: 12, mergedStationCount: 3 });

    const note = screen.getByTestId('cave-topology-completeness').textContent ?? '';
    expect(note).toContain('12');
    expect(note).toContain('3');
    expect(note).toContain('computed over what remained');
  });

  it('admits when it is not recorded what the reading left out', () => {
    show({ ...branchwork, droppedShotCount: null, mergedStationCount: null });

    expect(screen.getByTestId('cave-topology-completeness').textContent).toContain(
      'not recorded what the reading of this survey may have left out',
    );
  });

  it("names which of the cave's uploaded surveys was measured", () => {
    show(branchwork);

    expect(screen.getByText(/Corrected re-export, March/)).toBeTruthy();
  });

  it('draws nothing at all when the figures are refused or were never measured', () => {
    // Three answers arrive here as one refusal: a cave the caller may not read, one they may read
    // but not place exactly, and one whose network has never been measured. A panel of dashes
    // would be a claim about the cave rather than about what is known.
    const { container } = show(undefined, { isError: true });
    expect(container.textContent).toBe('');

    cleanup();
    const notMeasured = show(undefined);
    expect(notMeasured.container.textContent).toBe('');
  });

  it('asks nothing of the server for a cave with no survey that could have been measured', () => {
    // Most caves have never had a survey file uploaded, and the route answers those the same way
    // it answers a caller who may not place the cave: not found. Asking anyway would put a failed
    // request — and so an error on the browser console — on the page of nearly every cave in the
    // application, which is exactly what buries the errors that mean something.
    show(undefined, { models: [] });
    expect(topologySpy).toHaveBeenLastCalledWith('cave-1', false);

    cleanup();
    // A wall mesh has no passage network behind it, and a reading still queued has stored nothing.
    show(undefined, {
      models: [
        { id: 'm-2', name: 'Walls', status: 'ready', format: 'stl' },
        { id: 'm-3', name: 'Queued re-export', status: 'pending', format: 'survex3d' },
      ],
    });
    expect(topologySpy).toHaveBeenLastCalledWith('cave-1', false);

    cleanup();
    show(branchwork);
    expect(topologySpy).toHaveBeenLastCalledWith('cave-1', true);
  });

  it('separates a maze from a branchwork on the figures a reader can see', () => {
    // The claim the whole feature rests on: these numbers mean something. A maze and a branching
    // cave must not read alike on the page, and the three figures that separate them are the ones
    // a reader is being asked to interpret.
    show(branchwork);
    const branchingLoops = cell('Independent loops');
    const branchingDegree = cell('Passages at an average junction or dead end');
    const branchingAlpha = cell('Loopiness (alpha)');
    cleanup();

    show({
      ...branchwork,
      cyclomaticNumber: 34,
      meanDegree: 3.52,
      alpha: 0.68,
      extremityCount: 2,
      junctionCount: 30,
    });

    expect(cell('Independent loops')).not.toBe(branchingLoops);
    expect(cell('Passages at an average junction or dead end')).not.toBe(branchingDegree);
    expect(cell('Loopiness (alpha)')).not.toBe(branchingAlpha);
    expect(cell('Independent loops')).toContain('34');
    expect(cell('Passages at an average junction or dead end')).toContain('3.52');
  });
});
