// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveDepthProfile } from '../../api/hooks/karstProfiles.ts';

const { profileSpy } = vi.hoisted(() => ({ profileSpy: vi.fn() }));

vi.mock('../../api/hooks/karstProfiles.ts', () => ({
  useCaveDepthProfile: (...args: unknown[]) => profileSpy(...args),
}));

// The chart is drawn by a canvas library with no canvas in the test environment; what matters here
// is that it is given the readings, not how they are painted.
vi.mock('./DistributionCharts.tsx', () => ({
  EnvelopeChart: ({ x, testId }: { x: number[]; testId?: string }) => (
    <div data-testid={testId}>{x.length} readings</div>
  ),
}));

const { default: CaveDepthProfilePanel } = await import('./CaveDepthProfilePanel.tsx');

const profile: CaveDepthProfile = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  passageLengthM: 120,
  highestM: 1150,
  lowestM: 900,
  deepestM: 250,
  samples: [
    { distanceAlongM: 0, altitudeM: 1150, depthM: 0, pathIndex: 0, segmentIndex: 0 },
    { distanceAlongM: 60, altitudeM: 1020, depthM: 130, pathIndex: 0, segmentIndex: 1 },
    { distanceAlongM: 120, altitudeM: 900, depthM: 250, pathIndex: 0, segmentIndex: 2 },
  ],
};

function show(data: CaveDepthProfile | undefined, isError = false) {
  profileSpy.mockReturnValue({ data, isLoading: false, isError });
  return render(<CaveDepthProfilePanel caveId="cave-1" />);
}

afterEach(cleanup);

describe('CaveDepthProfilePanel', () => {
  it('prints the extremes the server computed and draws every reading', () => {
    show(profile);
    const text = screen.getByTestId('cave-depth-profile').textContent ?? '';
    expect(text).toContain('1150.0 m');
    expect(text).toContain('900.0 m');
    expect(text).toContain('250.0 m');
    expect(screen.getByTestId('chart-cave-depth-profile').textContent).toBe('3 readings');
  });

  it('draws no curve and says why when the line work carries no altitudes', () => {
    show({ ...profile, hasAltitudes: false, samples: [], highestM: null, lowestM: null, deepestM: null });
    expect(screen.queryByTestId('chart-cave-depth-profile')).toBeNull();
    expect(screen.getByTestId('cave-depth-profile').textContent).toMatch(/altitude/i);
  });

  it('renders nothing when the server refuses', () => {
    show(undefined, true);
    expect(screen.queryByTestId('cave-depth-profile')).toBeNull();
  });
});
