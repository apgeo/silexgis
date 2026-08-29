// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';

// Testing Library unmounts what a test rendered only when Vitest exposes its hooks as
// globals, and this project keeps them explicit imports instead. Without that unmount a
// component stays mounted after its test ends, so React's scheduler can still hold a
// pending callback when Vitest disposes the jsdom environment for the file — the callback
// then runs against a torn-down `window` and is reported as an unhandled error that is
// blamed on whichever file happened to finish last. Registering the unmount here restores
// the per-test teardown the tests are written to assume.
afterEach(cleanup);

// antd relies on browser APIs that jsdom does not implement.
//
// jsdom's own matchMedia never evaluates the query — it answers `matches: false` for
// everything. antd's responsive observer resolves its breakpoints through it, so an
// always-false answer means no `min-width` breakpoint ever matches and components pick
// their phone layout in every test, silently. Evaluate width queries against
// window.innerWidth (jsdom defaults to 1024, a desktop) so tests see the layout their
// assertions assume. A test that wants the mobile layout mocks `useIsMobile`; the width
// here only has to put the default on the desktop side of the breakpoint.
window.matchMedia = ((query: string) => {
  const min = /\(min-width:\s*([\d.]+)px\)/.exec(query);
  const max = /\(max-width:\s*([\d.]+)px\)/.exec(query);
  // Non-width queries — `(hover: none)`, `(pointer: coarse)` — stay false: the test
  // environment is not a touch device.
  const matches =
    (min !== null || max !== null) &&
    (min === null || window.innerWidth >= Number.parseFloat(min[1])) &&
    (max === null || window.innerWidth <= Number.parseFloat(max[1]));
  return {
    matches,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  };
}) as unknown as typeof window.matchMedia;

if (!window.ResizeObserver) {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  window.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver;
}

// jsdom ships no 2D canvas, so getContext('2d') returns null. OpenLayers' Heatmap
// builds its colour gradient through a canvas context the moment it is constructed,
// which would throw under jsdom. A minimal stub covering the handful of calls that
// gradient construction makes lets such layers be unit-tested without a real canvas.
//
// The stub also answers text measurement, which the charting library needs even when it
// draws vectors rather than pixels: it lays an axis out by asking how wide each label
// will be, and an unanswered question throws before anything is drawn. The width returned
// is proportional, not real — there are no fonts here to measure against. That is the
// right trade because nothing asserts on pixel positions; what matters is that layout
// completes, so the labels, boxes and paths become elements a test can look at. Anything
// that did depend on a true width would be depending on which fonts happen to be
// installed on the machine running the tests.
if (typeof HTMLCanvasElement !== 'undefined') {
  HTMLCanvasElement.prototype.getContext = function stubGetContext(this: HTMLCanvasElement) {
    const gradient = { addColorStop() {} };
    let font = '12px sans-serif';
    return {
      canvas: this,
      createLinearGradient: () => gradient,
      fillRect() {},
      clearRect() {},
      drawImage() {},
      putImageData() {},
      save() {},
      restore() {},
      beginPath() {},
      closePath() {},
      moveTo() {},
      lineTo() {},
      stroke() {},
      fill() {},
      translate() {},
      scale() {},
      rotate() {},
      setTransform() {},
      measureText: (text: string) => {
        const size = Number.parseFloat(/(\d+(?:\.\d+)?)px/.exec(font)?.[1] ?? '12');
        const width = text.length * size * 0.5;
        return {
          width,
          actualBoundingBoxLeft: 0,
          actualBoundingBoxRight: width,
          actualBoundingBoxAscent: size * 0.8,
          actualBoundingBoxDescent: size * 0.2,
          fontBoundingBoxAscent: size * 0.8,
          fontBoundingBoxDescent: size * 0.2,
        } as unknown as TextMetrics;
      },
      getImageData: (_x: number, _y: number, w: number, h: number) => ({
        data: new Uint8ClampedArray(Math.max(1, w * h * 4)),
      }),
      set font(v: string) {
        font = v;
      },
      get font() {
        return font;
      },
      set fillStyle(_v: unknown) {},
      get fillStyle() {
        return '';
      },
    } as unknown as CanvasRenderingContext2D;
  } as unknown as typeof HTMLCanvasElement.prototype.getContext;
}
