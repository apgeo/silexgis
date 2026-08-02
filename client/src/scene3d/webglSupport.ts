// SPDX-License-Identifier: AGPL-3.0-or-later

// The 3D scene needs WebGL 2. Around one browser in twenty cannot provide it — old hardware,
// a blocked or blacklisted graphics driver, a locked-down enterprise build — and there is nothing
// the user can install to change that from inside the page. The engine library does not help
// here: asked for WebGL 2 on a machine that has only WebGL 1 it silently builds a degraded scene
// instead of failing, and its own capability check needs a scene to already exist. So the answer
// has to be worked out before anything is downloaded, which is also why this module imports
// nothing: it runs ahead of the engine chunk, so a browser that cannot run the scene never pays
// for it and gets a plain explanation instead of a black canvas.

/** True when this browser can actually create a WebGL 2 drawing context. */
export function supportsWebGl2(): boolean {
  // Checked before touching a canvas, because on a browser without WebGL 2 the constructor name
  // is not defined at all and the `instanceof` below would throw rather than answer.
  if (typeof WebGL2RenderingContext === 'undefined') {
    return false;
  }

  let context: unknown;
  try {
    context = document.createElement('canvas').getContext('webgl2');
  } catch {
    // Some drivers throw out of context creation rather than returning null.
    return false;
  }

  // Deliberately an identity check rather than a truthiness check: a canvas that hands back some
  // other kind of context — a 2D one, or a test double — must read as "no WebGL 2", not as a
  // scene that will fail later in a much less explainable way.
  if (!(context instanceof WebGL2RenderingContext)) {
    return false;
  }

  // A browser keeps only a handful of live drawing contexts and drops the oldest to make room.
  // A probe that kept its own would be one fewer for the scene it is deciding whether to build.
  context.getExtension('WEBGL_lose_context')?.loseContext();
  return true;
}
