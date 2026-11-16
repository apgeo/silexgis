// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import { releaseWebGlContext } from './releaseWebGlContext.ts';

/** A canvas whose `getContext` answers as the given table says, kind by kind. */
function canvasAnswering(contexts: Partial<Record<'webgl2' | 'webgl', unknown>>) {
  const canvas = document.createElement('canvas');
  const getContext = vi.fn((kind: string) => contexts[kind as 'webgl2' | 'webgl'] ?? null);
  canvas.getContext = getContext as never;
  return { canvas, getContext };
}

const contextWith = (loseContext: () => void) => ({
  getExtension: vi.fn((name: string) => (name === 'WEBGL_lose_context' ? { loseContext } : null)),
});

describe('giving a viewer’s drawing context back', () => {
  it('does nothing for a viewer that never put a canvas on the page', () => {
    expect(() => releaseWebGlContext(null)).not.toThrow();
  });

  it('loses a WebGL 2 context, which is what the viewer asks for first', () => {
    const loseContext = vi.fn();
    const { canvas, getContext } = canvasAnswering({ webgl2: contextWith(loseContext) });

    releaseWebGlContext(canvas);

    expect(loseContext).toHaveBeenCalledTimes(1);
    // A canvas that answered for WebGL 2 is never asked for the other kind.
    expect(getContext).not.toHaveBeenCalledWith('webgl');
  });

  it('loses a WebGL 1 context on a canvas that has no WebGL 2 one', () => {
    const loseContext = vi.fn();
    const { canvas } = canvasAnswering({ webgl: contextWith(loseContext) });

    releaseWebGlContext(canvas);

    expect(loseContext).toHaveBeenCalledTimes(1);
  });

  it('says nothing for a canvas with no WebGL context, or one that cannot give it back', () => {
    expect(() => releaseWebGlContext(canvasAnswering({}).canvas)).not.toThrow();
    // A context without the extension has nothing to call.
    const bare = { getExtension: () => null };
    expect(() => releaseWebGlContext(canvasAnswering({ webgl2: bare }).canvas)).not.toThrow();
  });

  it('says nothing for a canvas that throws when asked', () => {
    const canvas = document.createElement('canvas');
    canvas.getContext = (() => {
      throw new Error('the canvas has another kind of context');
    }) as never;
    expect(() => releaseWebGlContext(canvas)).not.toThrow();
  });
});
