// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Gives a disposed viewer's WebGL context back at once, rather than whenever its canvas is
 * collected. Disposing the renderer frees what was drawn with the context but not the context; a
 * page may hold only so many, and past that the browser takes the oldest one still alive — which
 * is the viewer somebody opened first and is still looking at, not the one just closed.
 *
 * For a viewer that comes and goes while another stays: a movie's preview over a live trip, the
 * second model of a comparison beside the first. The canvas is found before the viewer is
 * disposed, which may take it out of its container.
 */
export function releaseWebGlContext(canvas: HTMLCanvasElement | null): void {
  if (canvas === null) {
    return;
  }
  try {
    // Asking for the kind of context a canvas already has answers that one; a canvas answers no
    // other kind, so whichever of the two is not null is the viewer's.
    const context = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
    context?.getExtension('WEBGL_lose_context')?.loseContext();
  } catch {
    // A canvas that cannot be asked has nothing to give back.
  }
}
