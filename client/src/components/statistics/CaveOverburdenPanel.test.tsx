// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveOverburden, CaveOverburdenSample } from '../../api/hooks.ts';

const { overburdenSpy } = vi.hoisted(() => ({ overburdenSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCaveOverburden: (...args: unknown[]) => overburdenSpy(...args),
}));

const { default: CaveOverburdenPanel } = await import('./CaveOverburdenPanel.tsx');

const sample = (
  distanceAlongM: number,
  overburdenM: number | null,
  outcome: CaveOverburdenSample['outcome'] = 'sampled',
): CaveOverburdenSample => ({
  distanceAlongM,
  longitude: 25.1,
  latitude: 46.2,
  passageAltitudeM: 900,
  outcome,
  groundAltitudeM: overburdenM === null ? null : 900 + overburdenM,
  overburdenM,
  pathIndex: 0,
  segmentIndex: 0,
});

const measured: CaveOverburden = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  hasTerrain: true,
  passageLengthM: 300,
  coveredSampleCount: 3,
  minOverburdenM: 40,
  maxOverburdenM: 61,
  meanOverburdenM: 52,
  samples: [sample(0, 40), sample(150, 55), sample(300, 61)],
};

function show(data: CaveOverburden | undefined, { isError = false, isLoading = false } = {}) {
  overburdenSpy.mockReturnValue({ data, isLoading, isError });
  return render(
    <App>
      <CaveOverburdenPanel caveId="cave-1" />
    </App>,
  );
}

afterEach(cleanup);

describe('CaveOverburdenPanel', () => {
  it('draws the profile and prints the thicknesses the server measured', () => {
    const { container } = show(measured);

    expect(screen.getByTestId('chart-cave-overburden')).toBeTruthy();
    // Carried across rather than recomputed here: a figure derived a second way eventually
    // disagrees with the same figure printed beside the curve it came from.
    expect(container.textContent).toContain('40.0 m');
    expect(container.textContent).toContain('61.0 m');
    expect(container.textContent).toContain('52.0 m');
    expect(container.textContent).toContain('3 of 3');
    // A fully covered profile makes no claim about gaps.
    expect(screen.queryByTestId('overburden-partial')).toBeNull();
  });

  it('renders nothing at all when the server refuses the profile', () => {
    // The refusal a reader gets for a cave they may see but may not place is the same "no such
    // cave" a never-created cave gets. A card of blanks would announce that a guarded cave is
    // here, which is precisely the fact being protected.
    const { container } = show(undefined, { isError: true });

    // The assertion is on the whole rendering rather than on one figure being blank: no card, no
    // heading, no sentence about there being nothing to show.
    expect(container.querySelector('.ant-card')).toBeNull();
    expect(container.textContent).toBe('');
    expect(screen.queryByTestId('chart-cave-overburden')).toBeNull();
  });

  it('says which kind of gap broke the curve, and does not draw it as no rock', () => {
    const partly: CaveOverburden = {
      ...measured,
      coveredSampleCount: 2,
      samples: [sample(0, 40), sample(150, null, 'outsideCoverage'), sample(300, 61)],
    };
    const { container } = show(partly);

    expect(screen.getByTestId('chart-cave-overburden')).toBeTruthy();
    expect(screen.getByTestId('overburden-partial')).toBeTruthy();
    expect(container.textContent).toContain('2 of 3');
    expect(container.textContent).toMatch(/no prepared elevation data reaches the passage/);
    // The mean is over the readings that got a ground height, and the panel says so rather than
    // letting a reader take it for the whole cave.
    expect(container.textContent).toMatch(/over the readings that got a ground height/);
  });

  it('separates a hole in the model from ground nobody has modelled', () => {
    const holed: CaveOverburden = {
      ...measured,
      coveredSampleCount: 2,
      samples: [sample(0, 40), sample(150, null, 'noData'), sample(300, 61)],
    };
    show(holed);

    expect(screen.getByTestId('overburden-partial').textContent).toMatch(/a hole in the model/);
  });

  it('refuses to draw a plan-only survey rather than laying it on the surface', () => {
    show({ ...measured, hasAltitudes: false, coveredSampleCount: 0, samples: [] });

    expect(screen.getByTestId('overburden-no-altitudes')).toBeTruthy();
    expect(screen.queryByTestId('chart-cave-overburden')).toBeNull();
  });

  it('tells an installation with no elevation data apart from one whose data misses this cave', () => {
    show({ ...measured, hasTerrain: false, coveredSampleCount: 0, samples: [] });
    expect(screen.getByTestId('overburden-no-terrain')).toBeTruthy();
    cleanup();

    show({
      ...measured,
      coveredSampleCount: 0,
      samples: [sample(0, null, 'outsideCoverage'), sample(300, null, 'outsideCoverage')],
    });
    expect(screen.getByTestId('overburden-no-coverage')).toBeTruthy();
    expect(screen.queryByTestId('chart-cave-overburden')).toBeNull();
  });

  it('says which body of line work answered, so two caves are not compared as one figure', () => {
    const { container } = show({ ...measured, basis: 'skeletonHeuristic', isApproximation: true });

    expect(container.textContent).toMatch(/centerline/i);
  });
});
