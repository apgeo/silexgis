// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  clampFraction,
  hitTest,
  pixelBounds,
  polygonFrom,
  readRegion,
  regionAnchor,
  regionFromDrag,
  toPixels,
  type ImageRegion,
} from './regions.ts';

const SIZE = { width: 800, height: 400 };

describe('readRegion', () => {
  it('reads each shape the server accepts', () => {
    expect(readRegion({ shape: 'point', x: 0.5, y: 0.25 })).toEqual({
      shape: 'point',
      x: 0.5,
      y: 0.25,
    });
    expect(readRegion({ shape: 'rect', x: 0.1, y: 0.2, w: 0.3, h: 0.4 })).toEqual({
      shape: 'rect',
      x: 0.1,
      y: 0.2,
      w: 0.3,
      h: 0.4,
    });
    expect(readRegion({ shape: 'circle', cx: 0.5, cy: 0.5, r: 0.2 })).toEqual({
      shape: 'circle',
      cx: 0.5,
      cy: 0.5,
      r: 0.2,
    });
    expect(
      readRegion({
        shape: 'polygon',
        points: [
          [0, 0],
          [1, 0],
          [0.5, 1],
        ],
      }),
    ).toEqual({
      shape: 'polygon',
      points: [
        [0, 0],
        [1, 0],
        [0.5, 1],
      ],
    });
  });

  it('refuses a payload written in pixels rather than fractions', () => {
    // The failure this exists for. A payload in pixels is well formed in every other respect,
    // so without the bound it would be drawn — somewhere far outside the picture — instead of
    // being reported as no region at all.
    expect(readRegion({ shape: 'point', x: 640, y: 480 })).toBeNull();
    expect(readRegion({ shape: 'rect', x: 10, y: 10, w: 200, h: 100 })).toBeNull();
    expect(
      readRegion({
        shape: 'polygon',
        points: [
          [0, 0],
          [640, 0],
          [320, 480],
        ],
      }),
    ).toBeNull();
  });

  it('refuses shapes that select nothing, and anything it does not recognise', () => {
    expect(readRegion({ shape: 'rect', x: 0.1, y: 0.1, w: 0, h: 0.4 })).toBeNull();
    expect(readRegion({ shape: 'circle', cx: 0.5, cy: 0.5, r: 0 })).toBeNull();
    expect(
      readRegion({
        shape: 'polygon',
        points: [
          [0, 0],
          [1, 1],
        ],
      }),
    ).toBeNull();
    expect(readRegion({ shape: 'oval', x: 0.1, y: 0.1 })).toBeNull();
    expect(readRegion({ shape: 'point', x: 0.5 })).toBeNull();
    expect(readRegion(null)).toBeNull();
    expect(readRegion('a region')).toBeNull();
  });

  it('round-trips what it stores', () => {
    const region: ImageRegion = { shape: 'rect', x: 0.1, y: 0.2, w: 0.3, h: 0.4 };
    expect(readRegion(regionAnchor(region))).toEqual(region);
  });
});

describe('toPixels', () => {
  it('scales each axis by its own side', () => {
    expect(toPixels({ shape: 'rect', x: 0.5, y: 0.5, w: 0.25, h: 0.5 }, SIZE)).toEqual({
      shape: 'rect',
      x: 400,
      y: 200,
      w: 200,
      h: 200,
    });
  });

  it('takes a circle radius from the width so it stays a circle', () => {
    // The picture is twice as wide as it is tall. Scaling the radius by each axis in turn — the
    // obvious reading of a normalised frame — would draw an ellipse, and somebody who dragged
    // out a circle would get back a shape they did not draw.
    const drawn = toPixels({ shape: 'circle', cx: 0.5, cy: 0.5, r: 0.1 }, SIZE);
    expect(drawn).toEqual({ shape: 'circle', cx: 400, cy: 200, r: 80 });
  });
});

describe('hitTest', () => {
  it('accepts a click inside a rectangle and refuses one outside it', () => {
    const rect = toPixels({ shape: 'rect', x: 0.25, y: 0.25, w: 0.5, h: 0.5 }, SIZE);
    expect(hitTest(rect, 400, 200)).toBe(true);
    expect(hitTest(rect, 100, 200)).toBe(false);
  });

  it('accepts a click inside a circle and refuses one in its corner', () => {
    const circle = toPixels({ shape: 'circle', cx: 0.5, cy: 0.5, r: 0.1 }, SIZE);
    expect(hitTest(circle, 400, 200)).toBe(true);
    expect(hitTest(circle, 470, 260)).toBe(false); // inside the bounding box, outside the circle
  });

  it('accepts a click inside a polygon and refuses one in its concavity', () => {
    // An arrowhead: the notch between its barbs is inside the bounding box and outside the
    // shape, which is what distinguishes a real containment test from a box test.
    const arrow = toPixels(
      {
        shape: 'polygon',
        points: [
          [0.5, 0],
          [1, 1],
          [0.5, 0.6],
          [0, 1],
        ],
      },
      SIZE,
    );
    expect(hitTest(arrow, 400, 100)).toBe(true);
    expect(hitTest(arrow, 400, 380)).toBe(false);
  });

  it('gives a point enough slack to be clickable', () => {
    const point = toPixels({ shape: 'point', x: 0.5, y: 0.5 }, SIZE);
    expect(hitTest(point, 400, 200)).toBe(true);
    expect(hitTest(point, 404, 202)).toBe(true);
    expect(hitTest(point, 430, 200)).toBe(false);
  });
});

describe('pixelBounds', () => {
  it('boxes a polygon by its extremes', () => {
    const bounds = pixelBounds(
      toPixels(
        {
          shape: 'polygon',
          points: [
            [0.25, 0.5],
            [0.75, 0.25],
            [0.5, 1],
          ],
        },
        SIZE,
      ),
    );
    expect(bounds).toEqual({ x: 200, y: 100, w: 400, h: 300 });
  });
});

describe('regionFromDrag', () => {
  it('normalises a rectangle dragged backwards', () => {
    const forward = regionFromDrag('rect', { x: 0.2, y: 0.2 }, { x: 0.6, y: 0.7 }, SIZE);
    const backward = regionFromDrag('rect', { x: 0.6, y: 0.7 }, { x: 0.2, y: 0.2 }, SIZE);
    expect(forward).toMatchObject({ shape: 'rect', x: 0.2, y: 0.2 });
    expect((forward as { w: number }).w).toBeCloseTo(0.4, 10);
    expect((forward as { h: number }).h).toBeCloseTo(0.5, 10);
    // Which corner was pressed first is not part of what was drawn.
    expect(backward).toEqual(forward);
  });

  it('gives nothing for a drag that did not move', () => {
    // A click is a click. Returning a rectangle of no width would store something invisible
    // that can never be selected again to be removed.
    expect(regionFromDrag('rect', { x: 0.3, y: 0.3 }, { x: 0.3, y: 0.3 }, SIZE)).toBeNull();
    expect(regionFromDrag('circle', { x: 0.3, y: 0.3 }, { x: 0.3, y: 0.3 }, SIZE)).toBeNull();
  });

  it('measures a circle radius in widths, so the same drag gives the same circle either way', () => {
    // The picture is twice as wide as tall, so 0.1 down is the same distance on screen as 0.05
    // across. Both drags must therefore produce the same radius.
    const across = regionFromDrag('circle', { x: 0.5, y: 0.5 }, { x: 0.55, y: 0.5 }, SIZE);
    const down = regionFromDrag('circle', { x: 0.5, y: 0.5 }, { x: 0.5, y: 0.6 }, SIZE);
    expect(across).not.toBeNull();
    expect(down).not.toBeNull();
    expect((down as { r: number }).r).toBeCloseTo((across as { r: number }).r, 10);
  });

  it('keeps a drag that leaves the picture inside it', () => {
    // Dragging off the right edge and above the top: the far corner lands on (1, 0), so what is
    // stored runs from the press to the picture's own corner rather than past it.
    const region = regionFromDrag('rect', { x: 0.5, y: 0.5 }, { x: 1.4, y: -0.3 }, SIZE);
    expect(region).toEqual({ shape: 'rect', x: 0.5, y: 0, w: 0.5, h: 0.5 });
  });
});

describe('polygonFrom', () => {
  it('needs three points, because two are a line and a line cannot be stored', () => {
    expect(polygonFrom([{ x: 0.1, y: 0.1 }])).toBeNull();
    expect(polygonFrom([{ x: 0.1, y: 0.1 }, { x: 0.5, y: 0.5 }])).toBeNull();
    expect(
      polygonFrom([
        { x: 0.1, y: 0.1 },
        { x: 0.5, y: 0.5 },
        { x: 0.2, y: 0.8 },
      ]),
    ).toEqual({
      shape: 'polygon',
      points: [
        [0.1, 0.1],
        [0.5, 0.5],
        [0.2, 0.8],
      ],
    });
  });
});

describe('clampFraction', () => {
  it('brings a value back into the picture', () => {
    expect(clampFraction(-0.2)).toBe(0);
    expect(clampFraction(1.4)).toBe(1);
    expect(clampFraction(0.42)).toBe(0.42);
  });
});
