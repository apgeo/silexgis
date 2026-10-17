// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ClosestApproach } from '../../api/hooks.ts';
import {
  getClosestApproachLine,
  setClosestApproachLine,
} from '../../workspace/closestApproachLine.ts';

const { approachSpy, queryCalls } = vi.hoisted(() => ({
  approachSpy: vi.fn(),
  queryCalls: [] as { body: unknown; enabled: boolean }[],
}));

const hit = (id: string, title: string) =>
  ({ world: 'feature', id, title, subtitle: 'Cave', symbol: null, placeable: false });

vi.mock('../../api/hooks.ts', () => ({
  useClosestApproach: (...args: unknown[]) => approachSpy(...args),
  useFilterVocabulary: () => ({ data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] } }),
  // The search answers with the cave whose page this is and one other.
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled
      ? {
          data: {
            worlds: [{ world: 'feature', hits: [hit('cave-a', 'Peștera A'), hit('cave-b', 'Peștera B')], total: null }],
            page: 1, pageSize: 8, counted: false,
          },
          isFetching: false,
        }
      : { data: undefined, isFetching: false };
  },
  // A stored choice is named the way the route would name it.
  useFilterResolve: (_world: string, ids: readonly string[]) => ({
    data: ids.filter((id) => id === 'cave-b').map((id) => hit(id, 'Peștera B')),
  }),
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
async function showPicked(result: {
  data?: ClosestApproach;
  isError?: boolean;
  isLoading?: boolean;
}) {
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

  search();
  // The search is debounced; a loaded machine can take longer than the default second to answer.
  fireEvent.click(await screen.findByRole('button', { name: /Peștera B/ }, { timeout: 3000 }));
  return rendered;
}

/** Opens the picker and types into it, which is what makes it ask the filter route anything. */
function search() {
  fireEvent.click(screen.getByTestId('closest-approach-other'));
  fireEvent.change(screen.getByTestId('closest-approach-other-input'), { target: { value: 'pe' } });
}

afterEach(cleanup);
beforeEach(() => {
  queryCalls.length = 0;
});

describe('CaveClosestApproachSection', () => {
  it('shows the distance split into how far apart the two ends are across and down', async () => {
    const { container } = await showPicked({ data: measured });

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

  it('shows nothing measurable, and says why, when a cave recorded no depths', async () => {
    // An absence with its reason. A blank where a number belongs would present "no depths were
    // recorded here" as "these two have not been compared".
    await showPicked({ data: { ...measured, absence: 'noAltitudes', distanceM: null } });
    expect(screen.queryByTestId('closest-approach')).toBeNull();
    expect(screen.getByText(/recorded no depths/)).toBeTruthy();
  });

  it('names the ends of the line the way the bearing is measured, on either cave\u2019s page', async () => {
    // The server reports the pair lowest id first and measures the bearing from the first cave's
    // end to the second's. Putting the cave whose page this is first would, on half of all pages,
    // print a direction and a bearing that disagree by a hundred and eighty degrees — and a caver
    // following it would walk the wrong way.
    await showPicked({ data: { ...measured, caveAId: 'cave-b', caveBId: 'cave-a' } });
    expect(screen.getByText('From Peștera A to Peștera B.')).toBeTruthy();
  });

  it('hands the shortest line to the views that can draw it, and leaves it up for them', async () => {
    // The number is half the answer; where the two caves nearly touch is the other half, and this
    // page has no map on it. Both ends come off the same answer as the figures beside them.
    const { unmount } = await showPicked({ data: measured });
    expect(getClosestApproachLine()).toEqual({
      caveAId: 'cave-a',
      caveBId: 'cave-b',
      from: measured.from,
      to: measured.to,
      label: '268.2 m',
    });

    // The line has to outlive the page that published it. The flat map and the 3D scene are
    // separate routes from a cave's page, so only one of them is ever mounted: a line taken down
    // on the way out is a line no view can ever draw, which is exactly what used to happen — the
    // leaving page's cleanup ran before the arriving view's effects, and this feature had never
    // once been visible in a browser.
    unmount();
    expect(getClosestApproachLine()).not.toBeNull();
  });

  it('takes down a line left by another pair when a page is opened', async () => {
    // The other half of the same rule: what outlives the page is one line, not a pile of them,
    // and a line belongs to the pair it was measured from.
    setClosestApproachLine({
      caveAId: 'stale-a',
      caveBId: 'stale-b',
      from: measured.from!,
      to: measured.to!,
      label: 'stale',
    });
    await showPicked({ data: { ...measured, absence: 'noAltitudes', distanceM: null } });
    expect(getClosestApproachLine()).toBeNull();
  });

  it('draws no line when there is no measurement to draw', async () => {
    await showPicked({ data: { ...measured, absence: 'noAltitudes', distanceM: null } });
    expect(getClosestApproachLine()).toBeNull();
  });

  it('offers only caves, and never the cave whose page this is', async () => {
    approachSpy.mockReturnValue({ data: undefined, isError: false, isLoading: false });
    render(
      <App>
        <CaveClosestApproachSection caveId="cave-a" />
      </App>,
    );
    search();
    // The search is debounced; a loaded machine can take longer than the default second to answer.
    await screen.findByRole('button', { name: /Peștera B/ }, { timeout: 3000 });

    const document = JSON.stringify(queryCalls.filter((c) => c.enabled).at(-1)!.body);
    expect(document).toContain('"kind"');
    expect(document).toContain('"cave"');
    const offered = screen.getAllByTestId('closest-approach-other-row').map((r) => r.textContent);
    expect(offered).toEqual([expect.stringContaining('Peștera B')]);
    // Nothing is measured until a second cave is chosen.
    expect(approachSpy).toHaveBeenLastCalledWith('cave-a', undefined);
  });

  it('measures against the picked cave, names it on the box, and forgets it when cleared', async () => {
    await showPicked({ data: measured });
    expect(approachSpy).toHaveBeenLastCalledWith('cave-a', 'cave-b');
    expect(screen.getByTestId('closest-approach-other')).toHaveTextContent('Peștera B');

    fireEvent.click(screen.getByTestId('closest-approach-other'));
    fireEvent.click(screen.getByTestId('closest-approach-other-clear'));
    expect(approachSpy).toHaveBeenLastCalledWith('cave-a', undefined);
    expect(screen.queryByTestId('closest-approach')).toBeNull();
  });

  it('picks the other cave from the keyboard alone', async () => {
    approachSpy.mockReturnValue({ data: measured, isError: false, isLoading: false });
    render(
      <App>
        <CaveClosestApproachSection caveId="cave-a" />
      </App>,
    );
    search();
    // The search is debounced; a loaded machine can take longer than the default second to answer.
    await screen.findByRole('button', { name: /Peștera B/ }, { timeout: 3000 });

    fireEvent.keyDown(screen.getByTestId('closest-approach-other-input'), { key: 'ArrowDown' });
    const row = screen.getByRole('button', { name: /Peștera B/ });
    expect(row).toHaveFocus();
    fireEvent.keyDown(row, { key: 'Enter' });

    expect(approachSpy).toHaveBeenLastCalledWith('cave-a', 'cave-b');
  });

  it('offers no number at all to a reader who may not place both caves', async () => {
    // The server refuses the whole measurement rather than rounding or snapping it, and spells
    // the refusal as absence, so this reads the same for a guarded cave as for one that never
    // existed — which is the point.
    await showPicked({ isError: true });
    expect(screen.queryByTestId('closest-approach')).toBeNull();
    expect(screen.getByText(/may see exactly where both are/)).toBeTruthy();
  });
});
