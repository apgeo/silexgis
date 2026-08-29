// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveSurveyStatistics } from '../../api/hooks.ts';

const { statisticsSpy } = vi.hoisted(() => ({ statisticsSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCaveSurveyStatistics: (...args: unknown[]) => statisticsSpy(...args),
}));

const { default: CaveStatisticsPanel } = await import('./CaveStatisticsPanel.tsx');

const surveyed: CaveSurveyStatistics = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  indices: {
    segmentCount: 412,
    pathCount: 0,
    totalLengthM: 1840.25,
    planLengthM: 1700,
    hasAltitudes: true,
    highestZM: 940,
    lowestZM: 820,
    verticalExtentM: 120,
    maximumExtentM: 610,
    verticality: 0.065,
    horizontality: 0.924,
    linearity: 0.331,
    lengthToDepthRatio: 15.3,
    sinuosity: null,
  },
  paths: [],
  length: {
    computedM: 1840.25,
    declaredM: 1200,
    differenceM: -640.25,
    relativeDifference: 0.348,
    agreement: 'disagrees',
  },
  depth: { computedM: 120, declaredM: 118, differenceM: -2, relativeDifference: 0.017, agreement: 'agrees' },
  declaredDisagrees: true,
};

function show(data: CaveSurveyStatistics | undefined, { isError = false, isLoading = false } = {}) {
  statisticsSpy.mockReturnValue({ data, isLoading, isError });
  return render(
    <App>
      <CaveStatisticsPanel caveId="cave-1" />
    </App>,
  );
}

afterEach(cleanup);

describe('CaveStatisticsPanel', () => {
  it('shows what the line work measures', () => {
    show(surveyed);

    expect(screen.getByText('Length surveyed')).toBeTruthy();
    expect(screen.getByText('1,840.3 m')).toBeTruthy();
    // antd splits a decimal figure across two spans, so the tile is read whole.
    expect(screen.getByText('Horizontality').closest('.ant-statistic')?.textContent).toContain('0.92');
  });

  it('warns when the survey and the record disagree, without saying which is wrong', () => {
    show(surveyed);

    expect(screen.getByText('The survey and the record do not agree')).toBeTruthy();
    // The panel must not resolve the disagreement for the reader: a typed-in figure is often
    // older than the survey, and telling somebody the record is wrong invites them to overwrite
    // the more recent of the two.
    expect(screen.getByText(/Neither is automatically the wrong one/)).toBeTruthy();
    expect(screen.getByText(/1,200 m in the record/)).toBeTruthy();
  });

  it('says a figure was measured from the compiled survey', () => {
    show(surveyed);

    expect(screen.getByText(/using the surveyor's own flags/)).toBeTruthy();
    expect(screen.queryByText(/Approximated from the stored centerline/)).toBeNull();
  });

  it('says a figure was approximated when there is no compiled survey to read', () => {
    // The same panel, the same numbers, a different measurement — and it says so. Without this
    // sentence a centerline-derived rose and a survey-derived one read as one figure.
    show({
      ...surveyed,
      basis: 'skeletonHeuristic',
      isApproximation: true,
      surveyModelId: null,
      declaredDisagrees: false,
      length: { ...surveyed.length, agreement: 'agrees', declaredM: 1830, differenceM: -10.25, relativeDifference: 0.006 },
    });

    expect(screen.getByText(/Approximated from the stored centerline/)).toBeTruthy();
    expect(screen.queryByText(/using the surveyor's own flags/)).toBeNull();
    expect(screen.queryByText('The survey and the record do not agree')).toBeNull();
  });

  it('says a figure could not be computed rather than showing it as nothing', () => {
    show({
      ...surveyed,
      hasAltitudes: false,
      indices: { ...surveyed.indices, hasAltitudes: false, verticalExtentM: null, verticality: null },
      depth: { computedM: null, declaredM: 118, differenceM: null, relativeDifference: null, agreement: 'notComputed' },
      declaredDisagrees: false,
    });

    expect(screen.getByText("Not computed from this cave's line work.")).toBeTruthy();
    // A cave with no altitudes has no vertical extent; nought metres would be a different claim.
    const tile = screen.getByText('Vertical extent').closest('.ant-statistic');
    expect(tile?.textContent).toContain('—');
  });

  it('shows nothing to measure rather than a row of zeros', () => {
    show({
      ...surveyed,
      basis: 'unavailable',
      isApproximation: false,
      surveyModelId: null,
      hasAltitudes: false,
      declaredDisagrees: false,
    });

    expect(screen.getByText(/no line work to measure/)).toBeTruthy();
    expect(screen.queryByText('Length surveyed')).toBeNull();
  });

  it('shows no figure at all while it is still asking', () => {
    const { container } = show(undefined, { isLoading: true });

    // A skeleton where the number will be, and no nought anywhere: "0 m surveyed" is a statement
    // about the cave, and the panel must not make it before it has an answer.
    expect(container.querySelector('.ant-skeleton')).not.toBeNull();
    const tile = screen.getByText('Length surveyed').closest('.ant-statistic');
    expect(tile?.querySelector('.ant-statistic-content-value')).toBeNull();
  });

  it('shows nothing at all when the cave may not be read', () => {
    const { container } = show(undefined, { isError: true });
    expect(container.textContent).toBe('');
  });
});
