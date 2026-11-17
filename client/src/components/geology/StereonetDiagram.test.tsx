// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import type { Stereonet } from '../../api/hooks/structural.ts';
import StereonetDiagram from './StereonetDiagram.tsx';

const data: Stereonet = {
  readingCount: 2,
  measurementType: null,
  radiusMetres: 1000,
  poles: [
    {
      featureId: 'a',
      measurementType: 'joint',
      dipDirectionDegrees: 245,
      dipDegrees: 60,
      trendDegrees: 65,
      plungeDegrees: 30,
      x: 0.641,
      y: 0.299,
    },
    {
      featureId: 'b',
      measurementType: 'bedding',
      dipDirectionDegrees: 10,
      dipDegrees: 15,
      trendDegrees: 190,
      plungeDegrees: 75,
      x: -0.032,
      y: -0.183,
    },
  ],
  density: {
    size: 3,
    values: [null, 1, null, 1, 6, 1, null, 1, null],
    countingAreaFraction: 0.8,
    expectedSigma: 3,
    contourIntervalSigma: 2,
    maxSigma: 6,
  },
};

afterEach(cleanup);

describe('StereonetDiagram', () => {
  it('plots one dot per pole, a band per dense node and says what the shading means', () => {
    const { container } = render(<StereonetDiagram data={data} />);

    expect(screen.getAllByTestId('stereonet-pole')).toHaveLength(2);
    expect(container.querySelectorAll('[data-testid="stereonet-density"] rect')).toHaveLength(1);
    expect(screen.getByTestId('stereonet-density-legend').textContent).toMatch(/every 2 standard deviations/);
    expect(screen.getByTestId('stereonet-density-legend').textContent).toMatch(/densest spot here reads 6/);
    // Each pole names its plane in words for a reader who cannot see the dot.
    expect(screen.getByText('Joint: dip 60° towards 245°')).toBeTruthy();
    // A legend entry per kind present.
    expect(screen.getByText('Bedding')).toBeTruthy();
  });
});
