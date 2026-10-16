// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, within } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

import type { RegistryCategoryComparison } from '../../api/hooks.ts';

const { categoriesSpy } = vi.hoisted(() => ({ categoriesSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useRegistryCategories: (params: unknown) => categoriesSpy(params),
  useCaveTypes: () => ({ data: [{ id: 1, name: 'Vertical' }] }),
  useRockTypes: () => ({ data: [{ id: 2, name: 'Limestone' }, { id: 3, name: 'Dolomite' }] }),
}));

// The chart draws on a canvas the test document does not have; what it was handed is the claim.
const { chartSpy } = vi.hoisted(() => ({ chartSpy: vi.fn() }));
vi.mock('../../components/statistics/DistributionCharts.tsx', () => ({
  SummaryBoxChart: (props: unknown) => {
    chartSpy(props);
    return <div data-testid="registry-categories-chart" />;
  },
}));

const { default: RegistryCategoriesPage } = await import('./RegistryCategoriesPage.tsx');

function comparison(): RegistryCategoryComparison {
  return {
    measure: 'surveyedLength',
    category: 'rockType',
    minimumGroupCaveCount: 6,
    groups: [
      {
        categoryId: 2,
        caveCount: 12,
        measuredCount: 10,
        lowerDecile: 40,
        lowerQuartile: 120,
        median: 300,
        upperQuartile: 900,
        upperDecile: 2400,
      },
      {
        categoryId: 3,
        caveCount: 3,
        measuredCount: 3,
        lowerDecile: null,
        lowerQuartile: null,
        median: null,
        upperQuartile: null,
        upperDecile: null,
      },
      {
        categoryId: null,
        caveCount: 5,
        measuredCount: 1,
        lowerDecile: null,
        lowerQuartile: null,
        median: null,
        upperQuartile: null,
        upperDecile: null,
      },
    ],
    basis: 'Counted over the caves you may read.',
  };
}

function renderPage(search = '') {
  return render(
    <ConfigProvider>
      <App>
        <MemoryRouter initialEntries={[`/statistics/categories${search}`]}>
          <RegistryCategoriesPage />
        </MemoryRouter>
      </App>
    </ConfigProvider>,
  );
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('RegistryCategoriesPage', () => {
  it('asks for the rock comparison of surveyed length when the address names nothing', () => {
    categoriesSpy.mockReturnValue({ data: undefined, isError: false });

    renderPage();

    expect(categoriesSpy).toHaveBeenCalledWith(
      expect.objectContaining({ measure: 'surveyedLength', category: 'rockType' }),
    );
  });

  it('draws a box for a group with enough measured caves and keeps a thin group as its counts', () => {
    categoriesSpy.mockReturnValue({ data: comparison(), isError: false });

    renderPage('?category=rockType');

    const chart = chartSpy.mock.calls.at(-1)?.[0] as {
      categories: string[];
      series: { summaries: ({ min: number; max: number } | null)[] }[];
    };
    expect(chart.categories).toEqual(['Limestone', 'Dolomite', 'None recorded']);
    // The whiskers end where the answer says, at the tenth and ninetieth percentiles.
    expect(chart.series[0].summaries[0]).toMatchObject({ min: 40, max: 2400 });
    expect(chart.series[0].summaries[1]).toBeNull();
    expect(chart.series[0].summaries[2]).toBeNull();

    // Every group keeps its row and its counts; the thin ones say why they have no box.
    const table = screen.getByTestId('registry-categories-table');
    expect(within(table).getByText('Dolomite')).toBeInTheDocument();
    expect(within(table).getAllByText('Too few measured caves to draw')).toHaveLength(2);
    expect(screen.getByText(/fewer than 6 caves/)).toBeInTheDocument();
  });
});
