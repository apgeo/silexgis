// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { supportsWebGl2 } from './webglSupport.ts';

// The test environment stubs canvas contexts with a plain 2D-ish object for every context id, so
// "did getContext return something truthy?" would answer yes here and in any browser with a 2D
// canvas and no 3D at all. These tests pin the detector to the stricter question it has to ask.

class FakeWebGl2Context {
  lostContext = false;
  getExtension(name: string) {
    return name === 'WEBGL_lose_context' ? { loseContext: () => (this.lostContext = true) } : null;
  }
}

function withWebGl2Global(context: unknown) {
  vi.stubGlobal('WebGL2RenderingContext', FakeWebGl2Context);
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(
    context as CanvasRenderingContext2D,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('supportsWebGl2', () => {
  it('says no when the browser has no WebGL 2 constructor at all', () => {
    // jsdom is exactly this browser, so nothing has to be stubbed away.
    expect(supportsWebGl2()).toBe(false);
  });

  it('says no when the canvas hands back a context that is not WebGL 2', () => {
    withWebGl2Global({ fillRect() {} });
    expect(supportsWebGl2()).toBe(false);
  });

  it('says no when the canvas returns nothing', () => {
    withWebGl2Global(null);
    expect(supportsWebGl2()).toBe(false);
  });

  it('says no when context creation throws', () => {
    vi.stubGlobal('WebGL2RenderingContext', FakeWebGl2Context);
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(() => {
      throw new Error('driver blocked');
    });
    expect(supportsWebGl2()).toBe(false);
  });

  it('says yes for a real WebGL 2 context, and releases the one it probed with', () => {
    const context = new FakeWebGl2Context();
    withWebGl2Global(context);
    expect(supportsWebGl2()).toBe(true);
    // A browser allows only a handful of live contexts; the probe must not keep one.
    expect(context.lostContext).toBe(true);
  });
});
