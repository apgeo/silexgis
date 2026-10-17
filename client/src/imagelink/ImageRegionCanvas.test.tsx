// SPDX-License-Identifier: AGPL-3.0-or-later
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../i18n';
import ImageRegionCanvas, { type DrawnRegion } from './ImageRegionCanvas.tsx';
import type { ImageRegion } from './regions.ts';

// The surface is drawn only once the picture has a size, and jsdom lays nothing out: the picture
// is given a 200 x 100 box and told it has loaded.
beforeEach(() => {
  vi.spyOn(HTMLImageElement.prototype, 'getBoundingClientRect').mockReturnValue({
    x: 0, y: 0, left: 0, top: 0, right: 200, bottom: 100, width: 200, height: 100, toJSON: () => ({}),
  } as DOMRect);
});

afterEach(() => {
  vi.restoreAllMocks();
});

function loaded() {
  fireEvent.load(screen.getByRole('img', { name: 'cave.jpg' }));
}

function press(target: Element, key: string, times = 1, shiftKey = false) {
  for (let i = 0; i < times; i++) {
    fireEvent.keyDown(target, { key, shiftKey });
  }
}

describe('drawing a region from the keyboard', () => {
  function drawWith(shape: 'rect' | 'circle' | 'polygon' | 'point') {
    const onDrawn = vi.fn<(region: ImageRegion) => void>();
    render(<ImageRegionCanvas src="/x.jpg" alt="cave.jpg" regions={[]} drawing={shape} onDrawn={onDrawn} />);
    loaded();
    const surface = screen.getByRole('application', { name: /cave\.jpg/ });
    fireEvent.focus(surface);
    return { surface, onDrawn };
  }

  it('places a point where the cursor was moved to', () => {
    const { surface, onDrawn } = drawWith('point');
    expect(screen.getByTestId('region-keyboard-cursor')).toBeInTheDocument();
    press(surface, 'ArrowRight', 1, true); // 0.5 -> 0.6
    press(surface, 'ArrowUp', 5); // 0.5 -> 0.4
    press(surface, 'Enter');
    expect(onDrawn).toHaveBeenCalledWith({ shape: 'point', x: 0.6, y: 0.4 });
  });

  it('boxes a rectangle between two corners placed with Enter', () => {
    const { surface, onDrawn } = drawWith('rect');
    press(surface, 'ArrowLeft', 2, true); // x 0.3
    press(surface, 'ArrowUp', 2, true); // y 0.3
    press(surface, 'Enter');
    expect(onDrawn).not.toHaveBeenCalled();
    press(surface, 'ArrowRight', 4, true); // x 0.7
    press(surface, 'ArrowDown', 3, true); // y 0.6
    press(surface, 'Enter');
    expect(onDrawn).toHaveBeenCalledTimes(1);
    const region = onDrawn.mock.calls[0][0];
    expect(region.shape).toBe('rect');
    if (region.shape === 'rect') {
      expect(region.x).toBeCloseTo(0.3);
      expect(region.y).toBeCloseTo(0.3);
      expect(region.w).toBeCloseTo(0.4);
      expect(region.h).toBeCloseTo(0.3);
    }
  });

  it('closes an outline with Enter pressed again on its last corner, and Backspace takes a corner back', () => {
    const { surface, onDrawn } = drawWith('polygon');
    press(surface, 'Enter'); // (0.5, 0.5)
    press(surface, 'ArrowRight', 2, true);
    press(surface, 'Enter'); // (0.7, 0.5)
    press(surface, 'ArrowDown', 1, true);
    press(surface, 'Enter'); // (0.7, 0.6) — taken back
    press(surface, 'Backspace');
    press(surface, 'ArrowDown', 2, true);
    press(surface, 'Enter'); // (0.7, 0.8)
    press(surface, 'Enter'); // again on the last corner: closes
    expect(onDrawn).toHaveBeenCalledTimes(1);
    const region = onDrawn.mock.calls[0][0];
    expect(region.shape).toBe('polygon');
    if (region.shape === 'polygon') {
      expect(region.points).toHaveLength(3);
      expect(region.points[2][0]).toBeCloseTo(0.7);
      expect(region.points[2][1]).toBeCloseTo(0.8);
    }
  });

  it('abandons the shape in progress with Escape and keeps Escape otherwise', () => {
    const outer = vi.fn();
    const onDrawn = vi.fn();
    render(
      // oxlint-disable-next-line jsx-a11y/no-static-element-interactions -- a listener standing in for the dialog around the picture
      <div onKeyDown={(event) => event.key === 'Escape' && outer()}>
        <ImageRegionCanvas src="/x.jpg" alt="cave.jpg" regions={[]} drawing="rect" onDrawn={onDrawn} />
      </div>,
    );
    loaded();
    const surface = screen.getByRole('application');
    press(surface, 'Enter');
    press(surface, 'Escape');
    expect(outer).not.toHaveBeenCalled();
    press(surface, 'ArrowRight', 2, true);
    press(surface, 'Enter'); // a fresh first corner, not the end of the abandoned drag
    expect(onDrawn).not.toHaveBeenCalled();
    press(surface, 'Escape');
    press(surface, 'Escape');
    expect(outer).toHaveBeenCalledTimes(1);
  });
});

describe('picking a region', () => {
  const regions: DrawnRegion[] = [
    { id: 'small', region: { shape: 'rect', x: 0.4, y: 0.4, w: 0.1, h: 0.1 }, stroke: 'red', fill: 'red', label: 'Shown in' },
    { id: 'large', region: { shape: 'rect', x: 0.1, y: 0.1, w: 0.8, h: 0.8 }, stroke: 'blue', fill: 'blue' },
  ];

  it('reaches every region with Tab, largest first, and opens one with Enter', () => {
    const onPick = vi.fn();
    render(<ImageRegionCanvas src="/x.jpg" alt="cave.jpg" regions={regions} onPick={onPick} />);
    loaded();
    const buttons = screen.getAllByRole('button');
    expect(buttons.map((button) => button.getAttribute('aria-label'))).toEqual(['Region 1', 'Shown in']);
    buttons.forEach((button) => expect(button).toHaveAttribute('tabindex', '0'));
    press(screen.getByRole('button', { name: 'Shown in' }), 'Enter');
    expect(onPick).toHaveBeenCalledWith('small');
    fireEvent.click(screen.getByRole('button', { name: 'Region 1' }));
    expect(onPick).toHaveBeenLastCalledWith('large');
  });

  it('offers nothing to focus on a picture that only shows its regions', () => {
    render(<ImageRegionCanvas src="/x.jpg" alt="cave.jpg" regions={regions} />);
    loaded();
    expect(screen.queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryByRole('application')).toBeNull();
  });
});
