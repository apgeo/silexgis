// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ClosestApproach } from '../../api/hooks.ts';
import { getClosestApproachLine } from '../../workspace/closestApproachLine.ts';

const { cavesSpy, approachSpy } = vi.hoisted(() => ({
  cavesSpy: vi.fn(),
  approachSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useCaves: (...args: unknown[]) => cavesSpy(...args),
  useClosestApproach: (...args: unknown[]) => approachSpy(...args),
}));

const { default: CaveClosestApproachSection } = await import('./CaveClosestApproachSection.tsx');

const measured: ClosestApproach = {
  caveAId: 'cave-a',
  caveAName: 'Peștera A',
  caveBId: 'cave-b',
  caveBName: 'Peștera B',
  absence: 'none',
  distanceM: 268.2,
  horizontalDistanceM: 222.2,
  verticalDistanceM: 150,
  bearingDegrees: 2,
  from: { longitude: 24, latitude: 46, altitudeM: 100 },
  to: { longitude: 24.01, latitude: 46, altitudeM: -50 },
};

/** Renders the section and picks the second cave, which is what makes it ask anything. */
function showPicked(result: {
  data?: ClosestApproach;
  isError?: boolean;
  isLoading?: boolean;
}) {
  cavesSpy.mockReturnValue({
    data: { items: [{ id: 'cave-b', name: 'Peștera B' }] },
    isFetching: false,
  });
  approachSpy.mockReturnValue({
    data: result.data,
    isError: result.isError ?? false,
    isLoading: result.isLoading ?? false,
  });

  const rendered = render(
    <App>
      <CaveClosestApproachSection caveId="cave-a" />
    </App>,
  );

  fireEvent.mouseDown(screen.getByRole('combobox'));
  fireEvent.click(screen.getAllByText('Peștera B').at(-1)!);
  return rendered;
}

afterEach(cleanup);

describe('CaveClosestApproachSection', () => {
  it('shows the distance split into how far apart the two ends are across and down', () => {
    const { container } = showPicked({ data: measured });

    const figures = Array.from(container.querySelectorAll('.ant-statistic-content-value')).map(
      (node) => node.textContent?.replace(/[\s, ]/g, '') ?? '',
    );
    expect(figures).toContain('268.2');
    expect(figures).toContain('222.2');
    expect(figures).toContain('150.0');

    // The horizontal and vertical figures belong to one line. A reader who took them for the
    // shortest distance across and the shortest distance down would be reading two other pairs
    // of points, and would find the three numbers do not agree with each other.
    expect(screen.getByText(/two parts of that same shortest line/)).toBeTruthy();
  });

  it('shows nothing measurable, and says why, when a cave recorded no depths', () => {
    // An absence with its reason. A blank where a number belongs would present "no depths were
    // recorded here" as "these two have not been compared".
    showPicked({ data: { ...measured, absence: 'noAltitudes', distanceM: null } });
    expect(screen.queryByTestId('closest-approach')).toBeNull();
    expect(screen.getByText(/recorded no depths/)).toBeTruthy();
  });

  it('names the ends of the line the way the bearing is measured, on either cave\u2019s page', () => {
    // The server reports the pair lowest id first and measures the bearing from the first cave's
    // end to the second's. Putting the cave whose page this is first would, on half of all pages,
    // print a direction and a bearing that disagree by a hundred and eighty degrees — and a caver
    // following it would walk the wrong way.
    showPicked({ data: { ...measured, caveAId: 'cave-b', caveBId: 'cave-a' } });
    expect(screen.getByText('From Peștera A to Peștera B.')).toBeTruthy();
  });

  it('hands the shortest line to the views that can draw it, and takes it back', () => {
    // The number is half the answer; where the two caves nearly touch is the other half, and this
    // page has no map on it. Both ends come off the same answer as the figures beside them.
    const { unmount } = showPicked({ data: measured });
    expect(getClosestApproachLine()).toEqual({
      caveAId: 'cave-a',
      caveBId: 'cave-b',
      from: measured.from,
      to: measured.to,
      label: '268.2 m',
    });

    // A line left drawn after its panel has gone is an answer with no question beside it, and
    // nothing else would ever take it down.
    unmount();
    expect(getClosestApproachLine()).toBeNull();
  });

  it('draws no line when there is no measurement to draw', () => {
    showPicked({ data: { ...measured, absence: 'noAltitudes', distanceM: null } });
    expect(getClosestApproachLine()).toBeNull();
  });

  it('offers no number at all to a reader who may not place both caves', () => {
    // The server refuses the whole measurement rather than rounding or snapping it, and spells
    // the refusal as absence, so this reads the same for a guarded cave as for one that never
    // existed — which is the point.
    showPicked({ isError: true });
    expect(screen.queryByTestId('closest-approach')).toBeNull();
    expect(screen.getByText(/may see exactly where both are/)).toBeTruthy();
  });
});
