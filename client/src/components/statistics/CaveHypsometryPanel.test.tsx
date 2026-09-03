// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveHypsometry, CaveLevelBands } from '../../api/hooks.ts';

const { hypsometrySpy, bandsSpy, saveSpy, clearSpy } = vi.hoisted(() => ({
  hypsometrySpy: vi.fn(),
  bandsSpy: vi.fn(),
  saveSpy: vi.fn(),
  clearSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useCaveHypsometry: (...args: unknown[]) => hypsometrySpy(...args),
  useCaveLevelBands: (...args: unknown[]) => bandsSpy(...args),
  useSaveCaveLevelBands: (...args: unknown[]) => saveSpy(...args),
  useClearCaveLevelBands: (...args: unknown[]) => clearSpy(...args),
}));

const { default: CaveHypsometryPanel } = await import('./CaveHypsometryPanel.tsx');

const twoStoreys: CaveHypsometry = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  springAltitudesM: [820],
  proposal: {
    sampleCount: 20,
    totalWeightM: 400,
    lowestM: 1000,
    highestM: 1209,
    binWidthM: 25,
    bins: [
      { fromM: 1000, toM: 1025, count: 10, weightM: 200, countFraction: 0.5, weightFraction: 0.5 },
      { fromM: 1200, toM: 1225, count: 10, weightM: 200, countFraction: 0.5, weightFraction: 0.5 },
    ],
    bands: [
      { fromM: 1000, toM: 1009, count: 10, weightM: 200, weightFraction: 0.5 },
      { fromM: 1200, toM: 1209, count: 10, weightM: 200, weightFraction: 0.5 },
    ],
    goodnessOfVarianceFit: 0.99,
  },
};

const nothingSaved: CaveLevelBands = {
  caveId: 'cave-1',
  confirmed: false,
  bands: [],
  note: null,
  confirmedBy: null,
  confirmedAt: null,
};

function show(
  data: CaveHypsometry | undefined,
  saved: CaveLevelBands = nothingSaved,
  { isError = false, isLoading = false, canEdit = true } = {},
) {
  hypsometrySpy.mockReturnValue({ data, isLoading, isError });
  bandsSpy.mockReturnValue({ data: saved });
  saveSpy.mockReturnValue({ mutateAsync: vi.fn(), isPending: false });
  clearSpy.mockReturnValue({ mutate: vi.fn(), isPending: false });
  return render(
    <App>
      <CaveHypsometryPanel caveId="cave-1" canEdit={canEdit} />
    </App>,
  );
}

afterEach(cleanup);

describe('CaveHypsometryPanel', () => {
  it('draws the histogram and lists the proposed levels as a proposal', () => {
    show(twoStoreys);

    expect(screen.getByTestId('chart-cave-hypsometry')).toBeTruthy();
    expect(screen.getByText(/Levels proposed from the measurements: 2/)).toBeTruthy();
    // The sentence is the point: without it the bands read as a statement about the cave.
    expect(screen.getByText(/not a statement about the cave/)).toBeTruthy();
  });

  it('keeps the proposal and the recorded reading as two separate things', () => {
    expect(
      show(twoStoreys).container.querySelector('[data-testid="hypsometry-saved"]')?.textContent,
    ).toMatch(/No reading has been recorded yet/);
    cleanup();

    show(twoStoreys, {
      ...nothingSaved,
      confirmed: true,
      bands: [{ fromM: 1000, toM: 1009, label: 'Lower' }],
    });
    expect(screen.getByTestId('hypsometry-saved').textContent).toMatch(
      /Levels in the recorded reading: 1/,
    );
    // A recorded reading can be withdrawn; one that was never recorded has nothing to withdraw.
    expect(screen.getByTestId('hypsometry-clear')).toBeTruthy();
  });

  it('refuses the distribution with its reason when the line work carries no heights', () => {
    show({ ...twoStoreys, hasAltitudes: false, proposal: null });

    expect(screen.getByTestId('hypsometry-refused')).toBeTruthy();
    // Never a histogram piled at zero: that would say the cave lies on one level at sea level.
    expect(screen.queryByTestId('chart-cave-hypsometry')).toBeNull();
    expect(screen.getByText(/no third coordinate/)).toBeTruthy();
  });

  it('offers no way to record a reading to somebody who may not write the cave', () => {
    show(twoStoreys, nothingSaved, { canEdit: false });

    expect(screen.queryByTestId('hypsometry-confirm')).toBeNull();
    // The reading itself is still shown — reading it is not writing it.
    expect(screen.getByTestId('hypsometry-saved')).toBeTruthy();
  });

  it('is absent rather than blank when the server refuses the question', () => {
    const { container } = show(undefined, nothingSaved, { isError: true });

    // A card of dashes would announce that a guarded cave is here.
    expect(container.textContent).toBe('');
  });
});
