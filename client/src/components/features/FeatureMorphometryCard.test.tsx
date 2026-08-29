// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { FeatureMorphometry } from '../../api/hooks.ts';

const { morphometrySpy } = vi.hoisted(() => ({ morphometrySpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useFeatureMorphometry: (...args: unknown[]) => morphometrySpy(...args),
}));

const { default: FeatureMorphometryCard } = await import('./FeatureMorphometryCard.tsx');

const measured: FeatureMorphometry = {
  featureId: 'feature-1',
  name: 'Dolina',
  geometryValid: true,
  areaM2: 10_000,
  perimeterM: 500,
  circularity: 0.502655,
  longAxisM: 200,
  shortAxisM: 50,
  elongation: 4,
  longAxisAzimuthDegrees: 60,
  centroidLongitude: 26.3536,
  centroidLatitude: 46.0517,
};

function show(
  geometryType: string | null,
  data: FeatureMorphometry | undefined,
  { isError = false, isLoading = false } = {},
) {
  morphometrySpy.mockReturnValue({ data, isLoading, isError });
  return render(
    <App>
      <FeatureMorphometryCard featureId="feature-1" geometryType={geometryType} />
    </App>,
  );
}

afterEach(cleanup);

describe('FeatureMorphometryCard', () => {
  it('shows the measured parameters of a drawn outline', () => {
    const { container } = show('Polygon', measured);
    expect(screen.getByTestId('feature-morphometry')).toBeTruthy();

    // Read off the figure elements rather than by text: antd renders the whole and the
    // fractional part of a number in separate spans, and groups the thousands.
    const figures = Array.from(container.querySelectorAll('.ant-statistic-content-value')).map(
      (node) => node.textContent?.replace(/[\s,\u00a0]/g, '') ?? '',
    );
    expect(figures).toContain('10000');
    expect(figures).toContain('0.503');
    expect(figures).toContain('4.00');
    expect(figures).toContain('60.0');

    // The sentence that keeps a reader from taking the alignment for a bearing.
    expect(screen.getByText(/alignment, not a direction/)).toBeTruthy();
  });

  it('is absent for a feature that has no outline to measure', () => {
    // A marker has no area, and asking for one would put a card of dashes on every point on
    // the map.
    const { container } = show('Point', undefined);
    expect(container.textContent).toBe('');
    expect(morphometrySpy).toHaveBeenCalledWith('feature-1', false);
  });

  it('is absent when the server gives no answer, rather than showing blanks', () => {
    // The server withholds the whole measurement from a reader who may see this feature but may
    // not be told where it is. Blanks in that case would say a guarded doline is here and would
    // read as "this one has no size".
    const { container } = show('Polygon', undefined, { isError: true });
    expect(container.textContent).toBe('');
  });

  it('says an outline that crosses itself cannot be measured', () => {
    // Its area function answers, and it answers zero. Printing the zero would describe the
    // drawing as a doline of no size rather than as a drawing that needs fixing.
    show('Polygon', { ...measured, geometryValid: false, areaM2: null, circularity: null });
    expect(screen.getByText(/crosses itself/)).toBeTruthy();
    expect(screen.queryByTestId('feature-morphometry')).toBeNull();
  });
});
