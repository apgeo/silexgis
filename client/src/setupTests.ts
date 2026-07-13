// SPDX-License-Identifier: AGPL-3.0-or-later
import '@testing-library/jest-dom/vitest';

// antd relies on browser APIs that jsdom does not implement.
if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
}

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
if (typeof HTMLCanvasElement !== 'undefined') {
  HTMLCanvasElement.prototype.getContext = function stubGetContext(this: HTMLCanvasElement) {
    const gradient = { addColorStop() {} };
    return {
      canvas: this,
      createLinearGradient: () => gradient,
      fillRect() {},
      clearRect() {},
      drawImage() {},
      putImageData() {},
      getImageData: (_x: number, _y: number, w: number, h: number) => ({
        data: new Uint8ClampedArray(Math.max(1, w * h * 4)),
      }),
      set fillStyle(_v: unknown) {},
      get fillStyle() {
        return '';
      },
    } as unknown as CanvasRenderingContext2D;
  } as unknown as typeof HTMLCanvasElement.prototype.getContext;
}
