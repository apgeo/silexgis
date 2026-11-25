// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

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
    basisKind: 'readable',
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

describe('RegistryCategoriesPage answers', () => {
  it('names a refused comparison with the server\'s own reason, and draws no chart', () => {
    categoriesSpy.mockReturnValue({
      data: undefined,
      isError: true,
      error: new ApiError(400, 'validation.failed', 'minimumGroupCaveCount may not be lowered below 6.'),
    });

    renderPage('?minimumGroupCaveCount=2');

    expect(screen.getByText(i18n.t('registryStats.categoriesFailed'))).toBeInTheDocument();
    expect(screen.getByText(/may not be lowered below 6/)).toBeInTheDocument();
    expect(screen.queryByTestId('registry-categories-chart')).toBeNull();
    expect(screen.queryByText(i18n.t('registryStats.nothingInScope'))).toBeNull();
  });

  it('names a failed read without repeating an internal message', () => {
    categoriesSpy.mockReturnValue({ data: undefined, isError: true, error: new TypeError('Failed to fetch') });

    renderPage();

    expect(screen.getByText(i18n.t('registryStats.categoriesFailed'))).toBeInTheDocument();
    expect(screen.queryByText(/Failed to fetch/)).toBeNull();
  });

  it('says nothing is in scope when no group came back, rather than drawing an empty chart', () => {
    categoriesSpy.mockReturnValue({ data: { ...comparison(), groups: [] }, isError: false });

    renderPage();

    expect(screen.getByText(i18n.t('registryStats.nothingInScope'))).toBeInTheDocument();
    expect(screen.queryByTestId('registry-categories-chart')).toBeNull();
  });

  it('labels the groups from the cave types when the comparison is by cave type', () => {
    categoriesSpy.mockReturnValue({
      data: {
        ...comparison(),
        category: 'caveType',
        groups: [{ ...comparison().groups[0], categoryId: 1 }],
      },
      isError: false,
    });

    renderPage('?category=caveType');

    const chart = chartSpy.mock.calls.at(-1)?.[0] as { categories: string[] };
    expect(chart.categories).toEqual(['Vertical']);
  });
});

describe('a failed read of the registry figures', () => {
  it('offers to read again after a dropped connection, and not after a refusal that would only repeat', async () => {
    const refetch = vi.fn();
    categoriesSpy.mockReturnValue({ data: undefined, isError: true, error: new TypeError('Failed to fetch'), refetch });
    const first = renderPage();

    expect(await screen.findByText(i18n.t('registryStats.categoriesFailed'))).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(refetch).toHaveBeenCalledTimes(1);
    first.unmount();

    categoriesSpy.mockReturnValue({ data: undefined, isError: true, error: new ApiError(400, 'validation.failed', 'Invented reason.'), refetch });
    renderPage();

    expect(await screen.findByText('Invented reason.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: i18n.t('common.retry') })).toBeNull();
  });
});
