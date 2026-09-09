// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

import { ApiError } from '../../api/client.ts';
import type { RegistryDistribution } from '../../api/hooks.ts';

/**
 * The distribution page, checked on the ways it can say more or less than the answer it was given.
 *
 * The previous answer is deliberately kept on screen while a new one is worked out, which is what
 * a reader re-binning or re-filtering wants — and it is also what makes every label on this page a
 * chance to name the wrong thing. The labels come from the request and the figures from the
 * answer, so until the two agree, a title, an axis and a column heading can carry one
 * measurement's name over another measurement's numbers, and the file offered beside them can be
 * a third thing again.
 *
 * The other two are refusals and keystrokes. The registry is what says which interval counts it
 * will publish, and its refusal names the control that is wrong — so the refusal is shown in its
 * own words rather than as the exception's internal one. And a control that is typed rather than
 * picked passes through values nobody meant on the way to the one they did.
 */

const { distributionSpy } = vi.hoisted(() => ({ distributionSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useRegistryDistribution: (params: unknown) => distributionSpy(params),
  useCaveTypes: () => ({ data: [{ id: 1, name: 'Cave' }] }),
  useRockTypes: () => ({ data: [{ id: 2, name: 'Limestone' }] }),
}));

vi.mock('../../api/download.ts', () => ({
  downloadFile: vi.fn(() => Promise.resolve()),
  registryDistributionExportUrl: (params: Record<string, unknown>) =>
    `/api/v1/stats/registry/distribution/export?measure=${String(params.measure)}`,
}));

const { default: RegistryDistributionPage } = await import('./RegistryDistributionPage.tsx');

function answer(overrides: Partial<RegistryDistribution> = {}): RegistryDistribution {
  return {
    measure: 'surveyedLength',
    caveCount: 220,
    measuredCount: 180,
    minimum: 4,
    maximum: 4000,
    bins: [
      { lowerBound: 0, upperBound: 1000, count: 140, merged: false },
      { lowerBound: 1000, upperBound: 3000, count: 34, merged: true },
      { lowerBound: 3000, upperBound: 4000, count: 6, merged: false },
    ],
    percentiles: [{ fraction: 0.5, value: 320 }],
    lognormal: null,
    paretoTail: null,
    basis: 'Computed over the caves you may read.',
    ...overrides,
  };
}

function renderPage(search = '') {
  return render(
    <ConfigProvider>
      <App>
        <MemoryRouter initialEntries={[`/statistics/distribution${search}`]}>
          <RegistryDistributionPage />
        </MemoryRouter>
      </App>
    </ConfigProvider>,
  );
}

/** What the hook was last asked, which is also what the file would be asked for. */
const lastAsked = () => distributionSpy.mock.calls.at(-1)?.[0] as Record<string, unknown>;

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('the registry distribution page', () => {
  it('names the measurement the figures were computed over, not the one being asked for', async () => {
    // The address asks for the default measurement; the answer on screen is still the previous
    // one. Every label on the page has to be that answer's, or the reader is shown a volume
    // heading over metres of passage.
    distributionSpy.mockReturnValue({
      data: answer({ measure: 'volume' }),
      isError: false,
      error: null,
      isPlaceholderData: true,
    });

    renderPage();

    const card = await screen.findByTestId('registry-distribution-chart-card');
    expect(card.textContent).toContain('Volume (m³)');
    expect(card.textContent).not.toContain('Surveyed length');
    expect(screen.getByTestId('registry-percentiles').textContent).toContain('Volume (m³)');
  });

  it('offers no file while the figures on screen are the previous answer', async () => {
    distributionSpy.mockReturnValue({
      data: answer(),
      isError: false,
      error: null,
      isPlaceholderData: true,
    });

    renderPage();

    // The file is the same answer as the screen or it is a different one: while the two disagree
    // there is no question both would answer, so none is offered.
    expect(await screen.findByTestId('registry-distribution-export')).toHaveProperty(
      'disabled',
      true,
    );
    expect(screen.getByTestId('registry-distribution-stale')).toBeTruthy();
  });

  it('shows the refusal in the registry own words, which name the control that is wrong', async () => {
    distributionSpy.mockReturnValue({
      data: undefined,
      isError: true,
      error: new ApiError(
        400,
        'validation.failed',
        'minimumBinCaveCount may be raised above 3 and not lowered below it.',
      ),
    });

    renderPage('?minimumBinCaveCount=2');

    const said = await screen.findByTestId('registry-distribution-error');
    expect(said.textContent).toContain('may be raised above 3');
    // Never the exception's own text, which is an internal string in one language.
    expect(said.textContent).not.toContain('API error 400');
  });

  /**
   * The interval count is typed, and a value on the way to another one is still a whole question.
   * Typing "25" passes through 2, which the registry accepts — so the answer is re-binned to two
   * intervals, a request nobody asked for is sent, and a history entry is pushed for a number
   * nobody meant, all between two keystrokes. (A value the registry would refuse never leaves the
   * control: antd withholds anything below the minimum until the field is left. The cost is the
   * legal intermediates, not a refusal.) The question is asked when the reader is done with the
   * field, and it is asked exactly as typed — nothing here corrects it on the way out.
   */
  it('asks nothing for a value typed on the way to another, and asks once the field is left', async () => {
    distributionSpy.mockReturnValue({ data: answer(), isError: false, error: null });

    renderPage();
    const found = await screen.findByTestId('registry-bins');
    const bins = found instanceof HTMLInputElement ? found : found.querySelector('input')!;

    fireEvent.change(bins, { target: { value: '2' } });
    fireEvent.change(bins, { target: { value: '25' } });
    expect(
      distributionSpy.mock.calls.map((call) => (call[0] as { bins?: number }).bins),
    ).not.toContain(2);

    fireEvent.blur(bins);
    expect(lastAsked().bins).toBe(25);
  });

  it('says where on the axis a fitted tail begins, and says nothing when none was fitted', async () => {
    distributionSpy.mockReturnValue({
      data: answer({
        paretoTail: {
          alpha: 1.9,
          alphaStandardError: 0.2,
          lowerBound: 1200,
          tailCount: 40,
          kolmogorovSmirnov: 0.031,
        },
      }),
      isError: false,
      error: null,
    });

    renderPage();

    // The exponent is tabulated beside the chart; without this the reader has a number for where
    // the tail starts and no way to see which of the drawn intervals it is a claim about.
    const said = await screen.findByTestId('registry-distribution-tail-span');
    expect(said.textContent).toContain('1,200');
    expect(said.textContent).toContain('2');

    cleanup();
    distributionSpy.mockReturnValue({ data: answer(), isError: false, error: null });
    renderPage();
    await screen.findByTestId('registry-distribution-counts');
    expect(screen.queryByTestId('registry-distribution-tail-span')).toBeNull();
  });
});
