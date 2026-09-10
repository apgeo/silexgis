// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

import type { RegistryCorrelation } from '../../api/hooks.ts';

/**
 * The relationship page, checked on the two things it can most easily be wrong about.
 *
 * A slope is the figure a reader takes away, and on its own it is a claim with its evidence
 * removed: the goodness of the fit says whether the line describes the caves at all, and the
 * number of pairs says whether either figure is worth quoting. So both are asserted to be on the
 * screen beside it rather than left to review.
 *
 * The other is the unfittable answer. The registry returns a real count and four nulls when too
 * few caves recorded both measurements, and the failure mode is drawing that as a flat line, or
 * as an empty pair of axes with nothing said — either of which is a statement about the caves
 * that nobody made.
 */

const { correlationSpy, distributionSpy } = vi.hoisted(() => ({
  correlationSpy: vi.fn(),
  distributionSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useRegistryCorrelation: (params: unknown) => correlationSpy(params),
  useRegistryDistribution: (params: unknown) => distributionSpy(params),
  useCaveTypes: () => ({ data: [{ id: 1, name: 'Cave' }] }),
  useRockTypes: () => ({ data: [{ id: 2, name: 'Limestone' }] }),
}));

vi.mock('../../api/download.ts', () => ({
  downloadFile: vi.fn(() => Promise.resolve()),
  registryCorrelationExportUrl: (params: Record<string, unknown>) =>
    `/api/v1/stats/registry/correlation/export?x=${String(params.x)}`,
}));

const { default: RegistryCorrelationPage } = await import('./RegistryCorrelationPage.tsx');

function fit(overrides: Partial<RegistryCorrelation> = {}): RegistryCorrelation {
  return {
    x: 'surveyedLength',
    y: 'depth',
    count: 137,
    slope: 0.482,
    intercept: 1.2,
    rSquared: 0.613,
    correlation: 0.783,
    logarithmic: true,
    basis: 'Counted over the caves you may read.',
    ...overrides,
  };
}

/**
 * The ends of the horizontal measurement, as the distribution route answers them — the measure
 * included, because that is what says which fit these ends belong to.
 */
function range(overrides: Record<string, unknown> = {}) {
  return { measure: 'surveyedLength', minimum: 10, maximum: 4000, ...overrides };
}

function renderPage() {
  return render(
    <ConfigProvider>
      <App>
        <MemoryRouter initialEntries={['/statistics/correlation']}>
          <RegistryCorrelationPage />
        </MemoryRouter>
      </App>
    </ConfigProvider>,
  );
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('the registry correlation page', () => {
  it('never shows a slope without the goodness and the count it rests on', async () => {
    correlationSpy.mockReturnValue({ data: fit(), isError: false, error: null });
    distributionSpy.mockReturnValue({ data: range(), isError: false });

    renderPage();

    expect((await screen.findByTestId('registry-correlation-slope')).textContent).toBe('0.482');
    expect(screen.getByTestId('registry-correlation-r2').textContent).toBe('0.613');
    expect(screen.getByTestId('registry-correlation-count').textContent).toContain('137');
    // The server's own sentence about what it counted over, in its own words rather than ours.
    expect(screen.getByTestId('registry-correlation-basis').textContent).toBe(
      'Counted over the caves you may read.',
    );
    expect(screen.getByTestId('chart-registry-correlation')).toBeTruthy();
  });

  it('draws no line and says why when too few caves recorded both measurements', async () => {
    correlationSpy.mockReturnValue({
      data: fit({ count: 1, slope: null, intercept: null, rSquared: null, correlation: null }),
      isError: false,
      error: null,
    });
    distributionSpy.mockReturnValue({ data: range(), isError: false });

    renderPage();

    const said = await screen.findByTestId('registry-correlation-absent');
    expect(said.textContent).toMatch(/line through one point/i);
    // Not a flat line, and not an empty pair of axes either.
    expect(screen.queryByTestId('chart-registry-correlation')).toBeNull();
    // The count is still true and still shown; only the fit is absent.
    expect(screen.getByTestId('registry-correlation-count').textContent).toContain('1');
    expect(screen.getByTestId('registry-correlation-slope').textContent).toBe('—');
  });

  /**
   * The range is a second question, asked of a slower route, and it can fail on its own. Neither
   * of those is a fact about the caves — but read as a missing pair of ends they used to be
   * reported as "the horizontal measurement covers no range in this set", which is a claim about
   * the registry, shown on the ordinary first paint of every load and permanently if that request
   * failed.
   */
  it('does not say the measurement covers no range while the range is still on its way', async () => {
    correlationSpy.mockReturnValue({ data: fit(), isError: false, error: null });
    distributionSpy.mockReturnValue({ data: undefined, isError: false });

    renderPage();

    const said = await screen.findByTestId('registry-correlation-absent');
    expect(said.textContent).toMatch(/has not arrived yet/i);
    expect(said.textContent).not.toMatch(/covers no range/i);
    // The fit is in hand and every figure it carries is shown: only the drawing waits.
    expect(screen.getByTestId('registry-correlation-slope').textContent).toBe('0.482');
  });

  it('says the range could not be read when the question asking for it failed', async () => {
    correlationSpy.mockReturnValue({ data: fit(), isError: false, error: null });
    distributionSpy.mockReturnValue({ data: undefined, isError: true });

    renderPage();

    const said = await screen.findByTestId('registry-correlation-absent');
    expect(said.textContent).toMatch(/could not be read/i);
    expect(screen.queryByTestId('chart-registry-correlation')).toBeNull();
  });

  /**
   * Both answers are kept while a new one loads, and they do not arrive together. A range left
   * over from the measurement that was on screen a moment ago, drawn under the new pair's axes,
   * is a line for one relationship stretched across another one's spread.
   */
  it('draws no line across a range belonging to a different measurement', async () => {
    correlationSpy.mockReturnValue({ data: fit(), isError: false, error: null });
    distributionSpy.mockReturnValue({ data: range({ measure: 'volume' }), isError: false });

    renderPage();

    await screen.findByTestId('registry-correlation-absent');
    expect(screen.queryByTestId('chart-registry-correlation')).toBeNull();
  });

  // The labels name what was computed, not what is being asked for: while a new pair loads the
  // figures on screen are the previous pair's, and the answer carries the pair it was taken over.
  it('names the pair the answer was taken over rather than the pair now being asked for', async () => {
    correlationSpy.mockReturnValue({
      data: fit({ x: 'volume', y: 'area' }),
      isError: false,
      error: null,
    });
    distributionSpy.mockReturnValue({ data: range({ measure: 'volume' }), isError: false });

    renderPage();

    const card = await screen.findByTestId('registry-correlation-chart-card');
    expect(card.textContent).toContain('Area (m²) against Volume (m³)');
    expect(card.textContent).not.toContain('Surveyed length');
  });

  // The file is the answer on screen. While the screen is showing the previous one there is no
  // question both of them answer, so none is offered until they are the same again.
  it('offers no file while the figures on screen are the previous answer', async () => {
    correlationSpy.mockReturnValue({
      data: fit(),
      isError: false,
      error: null,
      isPlaceholderData: true,
    });
    distributionSpy.mockReturnValue({ data: range(), isError: false });

    renderPage();

    expect(await screen.findByTestId('registry-correlation-export')).toHaveProperty(
      'disabled',
      true,
    );
    expect(screen.getByTestId('registry-correlation-stale')).toBeTruthy();
  });

  it('asks for the file with the same question the screen is showing', async () => {
    correlationSpy.mockReturnValue({ data: fit(), isError: false, error: null });
    distributionSpy.mockReturnValue({ data: range(), isError: false });

    renderPage();
    await screen.findByTestId('registry-correlation-export');

    // The screen's own query object, unchanged, is what the export builder is handed: the two are
    // one answer rendered twice, and there is no second place here to narrow it differently.
    const asked = correlationSpy.mock.calls.at(-1)?.[0] as Record<string, unknown>;
    expect(asked.x).toBe('surveyedLength');
    expect(asked.y).toBe('depth');
  });
});
