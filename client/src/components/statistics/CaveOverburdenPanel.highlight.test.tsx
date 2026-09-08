// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CaveOverburden, CaveOverburdenSample } from '../../api/hooks.ts';
import {
  getOverburdenHighlight,
  setOverburdenHighlight,
} from '../../workspace/overburdenHighlight.ts';

const { overburdenSpy } = vi.hoisted(() => ({ overburdenSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCaveOverburden: (...args: unknown[]) => overburdenSpy(...args),
}));

// The chart is stood in for, because what is under test is what the panel does when a reading is
// pressed — not whether a drawing library can be clicked in a headless DOM. The stub offers one
// button per reading and one for pressing the chart away from the curve, which is exactly the two
// things the real chart reports.
vi.mock('./DistributionCharts.tsx', () => ({
  EnvelopeChart: ({
    x,
    tooltipFormatter,
    onPointClick,
    onEmptyClick,
  }: {
    x: number[];
    tooltipFormatter?: (index: number) => string;
    onPointClick?: (index: number) => void;
    onEmptyClick?: () => void;
  }) => (
    <div data-testid="stub-envelope">
      {x.map((_, index) => (
        <button key={index} type="button" onClick={() => onPointClick?.(index)}>
          {`press ${index}`}
        </button>
      ))}
      <button type="button" onClick={() => onEmptyClick?.()}>
        press nothing
      </button>
      <span data-testid="stub-hover">{tooltipFormatter?.(1) ?? ''}</span>
    </div>
  ),
}));

const { default: CaveOverburdenPanel } = await import('./CaveOverburdenPanel.tsx');

const sample = (
  distanceAlongM: number,
  overburdenM: number | null,
  outcome: CaveOverburdenSample['outcome'] = 'sampled',
): CaveOverburdenSample => ({
  distanceAlongM,
  longitude: 25.1 + distanceAlongM / 10000,
  latitude: 46.2,
  passageAltitudeM: 900 - distanceAlongM / 10,
  outcome,
  groundAltitudeM: overburdenM === null ? null : 900 - distanceAlongM / 10 + overburdenM,
  overburdenM,
  pathIndex: 0,
  segmentIndex: 2,
});

const measured: CaveOverburden = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  hasTerrain: true,
  passageLengthM: 300,
  coveredSampleCount: 2,
  minOverburdenM: 40,
  maxOverburdenM: 55,
  meanOverburdenM: 47.5,
  samples: [sample(0, 40), sample(150, 55), sample(300, null, 'outsideCoverage')],
};

function show(data: CaveOverburden | undefined) {
  overburdenSpy.mockReturnValue({ data, isLoading: false, isError: false });
  return render(
    <App>
      <CaveOverburdenPanel caveId="cave-1" />
    </App>,
  );
}

afterEach(() => {
  cleanup();
  setOverburdenHighlight(null);
});

describe('pressing a reading on the overburden curve', () => {
  it('announces where that reading was taken, at the passage altitude and not at the surface', () => {
    // The chart's horizontal axis is distance along the passage, which names no place. What the
    // views need is the position the reading was taken at, and the altitude of the passage there
    // — the surface height is what the reading measured down from, not where the mark belongs.
    show(measured);
    fireEvent.click(screen.getByText('press 1'));

    const highlight = getOverburdenHighlight();
    expect(highlight?.caveId).toBe('cave-1');
    expect(highlight?.longitude).toBe(measured.samples[1].longitude);
    expect(highlight?.latitude).toBe(46.2);
    expect(highlight?.altitudeM).toBe(measured.samples[1].passageAltitudeM);
    expect(highlight?.label).toContain('55.0 m');
  });

  it('says which reading is marked, in words rather than as a bare number', () => {
    show(measured);
    fireEvent.click(screen.getByText('press 1'));
    expect(screen.getByTestId('overburden-readout').textContent).toContain('55.0 m');
  });

  it('marks a reading with no ground height too, and says the thickness is unknown', () => {
    // The passage was surveyed there; only the surface over it is unknown. Refusing to mark it
    // would hide the very place a reader is trying to find, and a mark labelled with a number
    // would invent one.
    show(measured);
    fireEvent.click(screen.getByText('press 2'));

    const highlight = getOverburdenHighlight();
    expect(highlight?.longitude).toBe(measured.samples[2].longitude);
    expect(highlight?.label).not.toContain(' m');
    // A thickness of nought would draw the passage arriving at the surface. The readout says what
    // is actually the case instead.
    expect(screen.getByTestId('overburden-readout').textContent).toContain(
      'No elevation data reaches here',
    );
  });

  it('takes the mark down when the chart is pressed away from the curve', () => {
    show(measured);
    fireEvent.click(screen.getByText('press 1'));
    fireEvent.click(screen.getByText('press nothing'));
    expect(getOverburdenHighlight()).toBeNull();
  });

  it('keeps the mark up for a map opened after the page is left', () => {
    // A cave's page has no map on it, and the flat map and the 3D scene are reached by leaving it.
    // So the press has to outlive the page: a mark taken down on the way out is a mark no view can
    // ever draw, and the reader is left pressing a curve that answers nowhere.
    const { unmount } = show(measured);
    fireEvent.click(screen.getByText('press 1'));
    unmount();
    expect(getOverburdenHighlight()?.longitude).toBe(measured.samples[1].longitude);
  });

  it('takes down a mark left by another cave when a page is opened', () => {
    // The other half of the same rule: what outlives the page is one reading, not a pile of them,
    // and a reading belongs to the profile it was taken from.
    setOverburdenHighlight({
      caveId: 'cave-0',
      longitude: 1,
      latitude: 2,
      altitudeM: 3,
      label: 'stale',
    });
    show(measured);
    expect(getOverburdenHighlight()).toBeNull();
  });

  it('names the reading and its units when the pointer rests on it', () => {
    // The library's own rendering of the raw values names no units and no reading; what a hover
    // has to say is which piece of passage answered and what the number means.
    show(measured);
    const hover = screen.getByTestId('stub-hover').textContent ?? '';
    expect(hover).toContain('55.0 m');
    expect(hover).toContain('150.0 m');
    // Pieces are numbered from one for a reader, not from zero.
    expect(hover).toContain('3');
  });
});
