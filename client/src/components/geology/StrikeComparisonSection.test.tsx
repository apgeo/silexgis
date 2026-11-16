// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import type { MeasuredStrikeComparison } from '../../api/hooks/structural.ts';
import StrikeComparisonSection from './StrikeComparisonSection.tsx';

function comparison(overrides: Partial<MeasuredStrikeComparison>): MeasuredStrikeComparison {
  return {
    measurementType: null,
    readingCount: 6,
    passageLegCount: 11,
    strikes: null,
    watson: null,
    minimumSampleSize: 5,
    ...overrides,
  };
}

afterEach(cleanup);

describe('StrikeComparisonSection', () => {
  it('states the statistic, the probability and what they say', () => {
    const { container } = render(
      <StrikeComparisonSection
        data={comparison({ watson: { statistic: 0.4127, pValue: 0.00058, firstCount: 11, secondCount: 6 } })}
      />,
    );

    // The statistic is drawn in two spans, its whole and its decimals.
    expect(container.textContent).toMatch(/U²0\.413/);
    expect(screen.getAllByText(/< 0.001/).length).toBeGreaterThan(0);
    expect(screen.getByTestId('strike-comparison-verdict').textContent).toMatch(/unlikely to come from one distribution/);
    expect(screen.getByText(/11 survey legs against 6 strikes/)).toBeTruthy();
    // The caveat is always there: legs are not independent observations.
    expect(screen.getByText(/not independent/)).toBeTruthy();
  });

  it('says nothing differs when the probability is large', () => {
    render(
      <StrikeComparisonSection
        data={comparison({ watson: { statistic: 0.05, pValue: 0.62, firstCount: 11, secondCount: 6 } })}
      />,
    );

    expect(screen.getByTestId('strike-comparison-verdict').textContent).toMatch(/Nothing here shows/);
  });

  it('says how many of each it had when there are too few to test', () => {
    render(<StrikeComparisonSection data={comparison({ readingCount: 2 })} />);

    expect(screen.getByTestId('strike-comparison-too-few').textContent).toMatch(/at least 5 survey legs/);
    expect(screen.getByTestId('strike-comparison-too-few').textContent).toMatch(/there are 11 and 2/);
    expect(screen.queryByTestId('strike-comparison-verdict')).toBeNull();
  });
});
