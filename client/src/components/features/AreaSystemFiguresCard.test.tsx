// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AreaSystemFigures } from '../../api/hooks/systemFigures.ts';

const { figuresSpy } = vi.hoisted(() => ({ figuresSpy: vi.fn() }));

vi.mock('../../api/hooks/systemFigures.ts', () => ({
  useAreaSystemFigures: (...args: unknown[]) => figuresSpy(...args),
}));

const { default: AreaSystemFiguresCard } = await import('./AreaSystemFiguresCard.tsx');

function figures(overrides: Partial<AreaSystemFigures> = {}): AreaSystemFigures {
  return {
    areaId: 'a1',
    basis: 'declared',
    caveCount: 3,
    surveyedCaveCount: 2,
    truncated: false,
    isApproximation: false,
    hasAltitudes: true,
    figures: {
      entranceCount: 4,
      entrancesWithAltitude: 4,
      highestEntranceM: 1300,
      lowestEntranceM: 800,
      entranceSpreadM: 500,
      totalDepthM: 600,
      heightAboveLowestEntranceM: 450,
      resurgenceLevelM: 800,
      potentialDepthM: 500,
    },
    surveyHighestM: 1250,
    surveyLowestM: 700,
    ...overrides,
  } as AreaSystemFigures;
}

function renderCard(typeCode = 'cave_system') {
  return render(
    <App>
      <AreaSystemFiguresCard featureId="a1" featureTypeCode={typeCode} />
    </App>,
  );
}

afterEach(() => {
  cleanup();
  figuresSpy.mockReset();
});

describe('AreaSystemFiguresCard', () => {
  it('shows the system figures and says the membership is the declared, placeable one', async () => {
    figuresSpy.mockReturnValue({ data: figures(), isLoading: false, isError: false });

    renderCard();

    expect(await screen.findByText(/closed to you adds nothing here/i)).toBeTruthy();
    expect(screen.getByText('1300.0 m')).toBeTruthy();
    expect(screen.getByText('600.0 m')).toBeTruthy();
    // The spread between entrances and the potential depth both come to 500 m here.
    expect(screen.getAllByText('500.0 m')).toHaveLength(2);
    expect(figuresSpy).toHaveBeenCalledWith('a1', true);
  });

  it('shows a dash, never a nought, for a figure that could not be measured', async () => {
    figuresSpy.mockReturnValue({
      data: figures({
        hasAltitudes: false,
        figures: {
          entranceCount: 1,
          entrancesWithAltitude: 1,
          highestEntranceM: 1200,
          lowestEntranceM: 1200,
          entranceSpreadM: 0,
          totalDepthM: null,
          heightAboveLowestEntranceM: null,
          resurgenceLevelM: null,
          potentialDepthM: null,
        },
      }),
      isLoading: false,
      isError: false,
    });

    renderCard('karst_area');

    expect(await screen.findByText(/cannot be measured from them/i)).toBeTruthy();
    expect(screen.getAllByText('—').length).toBeGreaterThanOrEqual(4);
  });

  it('says so when the system holds no cave the reader may place', async () => {
    figuresSpy.mockReturnValue({
      data: figures({ caveCount: 0 }),
      isLoading: false,
      isError: false,
    });

    renderCard();

    expect(await screen.findByText(/nothing to measure/i)).toBeTruthy();
  });

  it('is absent for a refused or unreadable area and for a kind it is not offered on', () => {
    figuresSpy.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const { container } = renderCard();
    expect(container.querySelector('[data-testid="area-system-figures"]')).toBeNull();
    cleanup();

    figuresSpy.mockReturnValue({ data: figures(), isLoading: false, isError: false });
    const other = renderCard('building');
    expect(other.container.querySelector('[data-testid="area-system-figures"]')).toBeNull();
    expect(figuresSpy).toHaveBeenLastCalledWith('a1', false);
  });
});
