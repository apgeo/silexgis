// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../../i18n';
import type { SurveyUploadAnalyses } from '../../../api/hooks.ts';

// The charts are drawn by a canvas library with no canvas in the test environment; what matters
// here is which figures reach which panel, not how they are painted.
vi.mock('../../statistics/DistributionCharts.tsx', () => ({
  EnvelopeChart: ({ x, testId }: { x: number[]; testId?: string }) => <div data-testid={testId}>{x.length} readings</div>,
}));
vi.mock('../../statistics/RoseDiagram.tsx', () => ({ default: () => <div data-testid="rose" /> }));
vi.mock('../../statistics/DipHistogram.tsx', () => ({ default: () => <div data-testid="dip" /> }));

const { default: SurveyUploadAnalysesView } = await import('./SurveyUploadAnalysesView.tsx');

const measure = { meanAxisDegrees: 10, resultantLength: 0.5 } as never;
const analyses: SurveyUploadAnalyses = {
  surveyModelId: 'model-of-this-upload',
  hasAltitudes: true,
  statistics: {
    basis: 'surveyFlags',
    hasAltitudes: true,
    indices: { segmentCount: 2, pathCount: 1, totalLengthM: 71.3, planLengthM: 66.7, verticalExtentM: 20, hasAltitudes: true } as never,
    paths: [],
  },
  orientation: {
    basis: 'surveyFlags',
    hasAltitudes: true,
    segmentCount: 2,
    totalLengthM: 71.3,
    byCount: measure,
    byLength: measure,
    bins: [{ fromDegrees: 0, toDegrees: 10, count: 2, lengthM: 71.3, countFraction: 1, lengthFraction: 1 }],
    dip: { bins: [], meanAbsoluteDipDegrees: 16 } as never,
  },
  depthProfile: {
    basis: 'surveyFlags',
    hasAltitudes: true,
    passageLengthM: 71.3,
    highestM: 1000,
    lowestM: 980,
    deepestM: 20,
    samples: [
      { distanceAlongM: 0, altitudeM: 1000, depthM: 0, pathIndex: null, segmentIndex: 0 },
      { distanceAlongM: 71.3, altitudeM: 980, depthM: 20, pathIndex: null, segmentIndex: 1 },
    ],
  },
  topology: null,
};

afterEach(cleanup);

describe("an upload's own survey analysed", () => {
  it('draws the statistics, orientation and depth profile of the upload, naming no other survey', () => {
    render(<SurveyUploadAnalysesView analyses={analyses} />);
    expect(screen.getByTestId('cave-survey-statistics')).toHaveTextContent('71.3');
    expect(screen.getByTestId('rose')).toBeInTheDocument();
    expect(screen.getByTestId('chart-cave-depth-profile')).toHaveTextContent('2 readings');
    // No record to set the figures against and no model to name: there is only this upload.
    expect(screen.queryByText(i18n.t('statistics.cave.lengthAgainstRecord'))).toBeNull();
    expect(screen.queryByText(/model-of-this-upload/)).toBeNull();
    // A network that was never measured is left out rather than drawn as blanks.
    expect(screen.queryByTestId('cave-topology-completeness')).toBeNull();
  });

  it('draws the network when the reading measured one', () => {
    const topology = {
      droppedShotCount: 0,
      mergedStationCount: 0,
      nodeCount: 3,
      edgeCount: 2,
      componentCount: 1,
      reducedNodeCount: 2,
      reducedEdgeCount: 1,
      reducedComponentCount: 1,
      cyclomaticNumber: 0,
      extremityCount: 2,
      junctionCount: 0,
      branchCount: 1,
      loopingBranchCount: 0,
      computedAt: '2026-11-05T10:00:00Z',
    } as SurveyUploadAnalyses['topology'];
    render(<SurveyUploadAnalysesView analyses={{ ...analyses, topology }} />);
    expect(screen.getByTestId('topology-nodes')).toHaveTextContent('3');
  });

  it('says so in one sentence when the survey was never read into shots', () => {
    render(<SurveyUploadAnalysesView analyses={null} />);
    expect(screen.getByTestId('survey-viewer-no-analyses')).toBeInTheDocument();
    expect(screen.queryByTestId('cave-survey-statistics')).toBeNull();
  });
});
