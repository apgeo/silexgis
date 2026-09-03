// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import type { OrientationBin } from '../../api/hooks.ts';
import StructureComparisonView from './StructureComparisonView.tsx';

/** The eighteen folded sectors the server always sends, with everything in one of them. */
function bins(dominant: number): OrientationBin[] {
  return Array.from({ length: 18 }, (_, i) => ({
    fromDegrees: i * 10,
    toDegrees: i * 10 + 10,
    count: i === dominant ? 30 : 0,
    lengthM: i === dominant ? 900 : 0,
    countFraction: i === dominant ? 1 : 0,
    lengthFraction: i === dominant ? 1 : 0,
  }));
}

afterEach(cleanup);

describe('StructureComparisonView', () => {
  it('shows the divergence when both sides were measured', () => {
    render(
      <StructureComparisonView
        testId="c"
        leftTitle="Passage"
        rightTitle="Traces"
        left={{ bins: bins(4), meanAxisDegrees: 45 }}
        right={{ bins: bins(4), meanAxisDegrees: 45 }}
        divergence={{ transportDegrees: 0, normalized: 0, meanAxisSeparationDegrees: 0 }}
        leftEmpty="none"
        rightEmpty="none"
      />,
    );

    expect(screen.getByTestId('c-divergence')).toBeTruthy();
    // Stated as a distance on a nought-to-one scale, and the scale is spelled out beside it.
    expect(screen.getByText(/Nought means the two lie along each other/)).toBeTruthy();
  });

  it('refuses a divergence rather than reporting nought when one side is empty', () => {
    render(
      <StructureComparisonView
        testId="c"
        leftTitle="Passage"
        rightTitle="Traces"
        left={{ bins: bins(4), meanAxisDegrees: 45 }}
        right={null}
        divergence={null}
        leftEmpty="none"
        rightEmpty="nothing was mapped"
      />,
    );

    // Nought here would claim agreement with a structure nobody has mapped.
    expect(screen.queryByTestId('c-divergence')).toBeNull();
    expect(screen.getByTestId('c-no-divergence')).toBeTruthy();
    expect(screen.getByText('nothing was mapped')).toBeTruthy();
  });

  it('says which comparison this installation cannot make', () => {
    render(
      <StructureComparisonView
        testId="c"
        leftTitle="Passage"
        rightTitle="Traces"
        left={null}
        right={null}
        divergence={null}
        leftEmpty="none"
        rightEmpty="none"
      />,
    );

    // Named rather than left as an affordance that always comes back empty.
    expect(screen.getByText(/Bedding and joint readings/)).toBeTruthy();
  });
});
