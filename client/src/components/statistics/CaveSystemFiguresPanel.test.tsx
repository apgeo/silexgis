// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveSystemFigures } from '../../api/hooks/entranceAltitudes.ts';

const { figuresSpy } = vi.hoisted(() => ({ figuresSpy: vi.fn() }));

vi.mock('../../api/hooks/entranceAltitudes.ts', () => ({
  useCaveSystemFigures: (...args: unknown[]) => figuresSpy(...args),
}));

const { default: CaveSystemFiguresPanel } = await import('./CaveSystemFiguresPanel.tsx');

const measured: CaveSystemFigures = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  surveyHighestM: 1150,
  surveyLowestM: 900,
  figures: {
    entranceCount: 2,
    entrancesWithAltitude: 2,
    highestEntranceM: 1200,
    lowestEntranceM: 1100,
    entranceSpreadM: 100,
    totalDepthM: 300,
    heightAboveLowestEntranceM: 50,
    resurgenceLevelM: 800,
    potentialDepthM: 400,
  },
  potentialDepth: { computedM: 400, declaredM: null, differenceM: null, relativeDifference: null, agreement: 'notDeclared' },
};

function show(data: CaveSystemFigures | undefined, isError = false) {
  figuresSpy.mockReturnValue({ data, isLoading: false, isError });
  return render(<CaveSystemFiguresPanel caveId="cave-1" />);
}

afterEach(cleanup);

describe('CaveSystemFiguresPanel', () => {
  it('prints the depth and the potential depth the server computed', () => {
    show(measured);
    const text = screen.getByTestId('cave-system-figures').textContent ?? '';
    expect(text).toContain('300.0 m');
    expect(text).toContain('400.0 m');
  });

  it('shows a dash and an explanation, never a nought, when the survey has no altitudes', () => {
    show({
      ...measured,
      hasAltitudes: false,
      figures: { ...measured.figures, totalDepthM: null, heightAboveLowestEntranceM: null, potentialDepthM: null },
    });
    const text = screen.getByTestId('cave-system-figures').textContent ?? '';
    expect(text).not.toMatch(/(^|[^\d.])0\.0 m/);
    expect(text).toContain('—');
  });

  it('renders nothing when the server refuses', () => {
    show(undefined, true);
    expect(screen.queryByTestId('cave-system-figures')).toBeNull();
  });
});
