// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { AreaKarstStatistics } from '../../api/hooks.ts';

const { statisticsSpy } = vi.hoisted(() => ({ statisticsSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useAreaKarstStatistics: (...args: unknown[]) => statisticsSpy(...args),
}));

const { default: AreaKarstStatisticsCard } = await import('./AreaKarstStatisticsCard.tsx');

function statistics(overrides: Partial<AreaKarstStatistics> = {}): AreaKarstStatistics {
  return {
    areaId: 'a1',
    basis: 'declared',
    areaKm2: 20,
    caveCount: 4,
    entranceCount: 6,
    cavesPerKm2: 0.2,
    entrancesPerKm2: 0.3,
    surveyedLengthM: 5300,
    surveyedCaveCount: 2,
    surveyedMetresPerKm2: 265,
    depressionCount: 0,
    depressionAreaKm2: null,
    depressionAreaRatio: null,
    deepestCaves: [{ featureId: 'c1', name: 'Deep One', value: 310 }],
    longestCaves: [{ featureId: 'c2', name: 'Long One', value: 4200 }],
    rockTypes: [{ rockTypeId: 1, code: 'limestone', name: 'Limestone', caveCount: 3 }],
    karstification: {
      score: 0.04,
      class: 'veryLow',
      components: [
        { name: 'cave_density', value: 0.2, normalised: 0.04, reference: 5 },
        { name: 'depression_area_ratio', value: null, normalised: null, reference: 0.1 },
      ],
    },
    unparentedInsideCount: 1,
    placeableCaveCount: 4,
    ...overrides,
  } as AreaKarstStatistics;
}

function renderCard(typeCode = 'karst_area') {
  return render(
    <App>
      <AreaKarstStatisticsCard featureId="a1" featureTypeCode={typeCode} />
    </App>,
  );
}

afterEach(() => {
  cleanup();
  statisticsSpy.mockReset();
});

describe('AreaKarstStatisticsCard', () => {
  it('says the membership basis before it shows a single number', async () => {
    statisticsSpy.mockReturnValue({ data: statistics(), isLoading: false, isError: false });

    renderCard();

    // The basis is the load-bearing sentence: the same outline answers differently if it is asked
    // spatially, and a reader who takes these totals for a spatial answer is reading them wrong.
    expect(
      await screen.findByText(/declared to be in this area/i),
    ).toBeTruthy();

    // The drift is stated beside the totals and is visibly not one of them.
    expect(screen.getByText(/without being declared here/i)).toBeTruthy();
  });

  it('shows an unmeasured component as absent rather than as a nought', async () => {
    statisticsSpy.mockReturnValue({ data: statistics(), isLoading: false, isError: false });

    renderCard();

    expect(
      await screen.findByText(/not recorded, so it is left out of the index/i),
    ).toBeTruthy();
    expect(screen.getByText(/not mapped/i)).toBeTruthy();
  });

  it('reads an area with no measurable component as unknown, not as the least karstified', async () => {
    statisticsSpy.mockReturnValue({
      data: statistics({
        areaKm2: null,
        cavesPerKm2: null,
        surveyedMetresPerKm2: null,
        karstification: {
          score: null,
          class: 'unknown',
          components: [
            { name: 'cave_density', value: null, normalised: null, reference: 5 },
            { name: 'depression_area_ratio', value: null, normalised: null, reference: 0.1 },
          ],
        },
      }),
      isLoading: false,
      isError: false,
    });

    renderCard();

    expect(await screen.findByText(/not measurable/i)).toBeTruthy();
    expect(screen.getByText('Unknown')).toBeTruthy();
  });

  it('does not offer the reading for a kind that is not a tract of karst', () => {
    statisticsSpy.mockReturnValue({ data: undefined, isLoading: false, isError: false });

    const { container } = renderCard('building');

    expect(container.textContent).toBe('');
    // And the request is not made either: the hook is called with the feature disabled.
    expect(statisticsSpy).toHaveBeenCalledWith('a1', false);
  });

  it('shows nothing at all when the area is refused, because a refusal and an absence read alike', () => {
    statisticsSpy.mockReturnValue({ data: undefined, isLoading: false, isError: true });

    const { container } = renderCard();

    expect(container.textContent).toBe('');
  });
});
