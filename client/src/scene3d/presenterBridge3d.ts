// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DCameraState, Scene3DPosition, Scene3DScreenPosition } from './scene3dEngine.ts';

// A handle on the 3D scene for a scripted recording of the application.
//
// Demonstration movies are made by a script that drives the real application in a browser. The
// parts a person would do through the interface it does through the interface; the camera it has
// to place exactly — an even orbit around a cave, a stop underground beside its walls — and no
// pointer gesture does that. On a machine without a graphics card the scene also draws only a few
// frames a second, so the movie is made one frame at a time: put the camera where the frame should
// see from, wait until everything in view has finished loading, take the picture. That wait needs
// to know when the scene is finished, which nothing outside the page can tell.
//
// Present only when the browser is driven by automation (`navigator.webdriver`, which every driver
// sets and no ordinary browser does). It hands out nothing a signed-in person could not already do
// from the browser's console, and no data: a camera position, the ground's height, where the
// loaded models are, where a place is drawn on the screen, and a tint and a relief factor that last
// only as long as the page.

/** The parts of the scene the handle needs; the real scene provides them, a test a plain object. */
export interface Scene3DPresenterTarget {
  isDestroyed(): boolean;
  getCamera(): Scene3DCameraState;
  setCamera(state: Scene3DCameraState, options?: { animate?: boolean }): void;
  getCameraTarget(): Scene3DPosition | undefined;
  requestRender(): void;
  /** True when no tile, model or point cloud in the scene is still loading. */
  isSettled(): boolean;
  /** Height of the ground under a point, in metres; 0 where nothing is loaded there. */
  groundHeight(longitude: number, latitude: number): number;
  /** The sphere around every loaded, visible model, or undefined when none is drawn. */
  modelSphere(): Scene3DPresenterSphere | undefined;
  /** Tints every loaded model; undefined takes the tint off. */
  setModelTint(tint: Scene3DPresenterTint | undefined): void;
  /** The pixel a position, in true metres, is drawn at; undefined behind the camera. */
  positionToScreen(position: Scene3DPosition): Scene3DScreenPosition | undefined;
  /** Draws the scene at a vertical exaggeration; heights handed over and read back stay true. */
  setVerticalExaggeration(factor: number): void;
  getVerticalExaggeration(): number;
}

/** A colour (each channel 0–1) mixed into a model's own by `amount` (0–1). */
export interface Scene3DPresenterTint {
  red: number;
  green: number;
  blue: number;
  amount: number;
}

export interface Scene3DPresenterSphere extends Scene3DPosition {
  /** Metres. */
  radius: number;
}

export interface Scene3DPresenterBridge {
  getCamera(): Scene3DCameraState;
  /** Jumps, never flies: a recording moves the camera itself, frame by frame. */
  setCamera(state: Scene3DCameraState): void;
  getCameraTarget(): Scene3DPosition | undefined;
  groundHeight(longitude: number, latitude: number): number;
  modelSphere(): Scene3DPresenterSphere | undefined;
  /** Recolours the cave's walls for the movie: undefined gives them back their own colours. */
  setModelTint(tint: Scene3DPresenterTint | undefined): void;
  /**
   * Where a place is drawn on the screen, its height in true metres at every factor — so a script
   * can check that something placed by the scene, a marker on the ground, is where it belongs.
   */
  positionToScreen(position: Scene3DPosition): Scene3DScreenPosition | undefined;
  /**
   * Draws the scene taller or flatter than true, as the 3D view's relief setting does, for a
   * movie that stretches the relief over time. Camera and ground heights stay true metres.
   */
  setVerticalExaggeration(factor: number): void;
  getVerticalExaggeration(): number;
  /**
   * Resolves true once the scene has drawn and stayed settled for `settledFrames` frames in a row,
   * or false when `timeoutMs` passes first. A single settled frame is not enough: a tile that
   * finishes loading asks for a redraw, and the redraw is what may discover the next tiles.
   */
  whenSettled(timeoutMs?: number, settledFrames?: number): Promise<boolean>;
}

declare global {
  interface Window {
    __silexgisPresenter?: { scene3d?: Scene3DPresenterBridge };
  }
}

function nextFrame(): Promise<void> {
  return new Promise((resolve) => requestAnimationFrame(() => resolve()));
}

export function createPresenterBridge(target: Scene3DPresenterTarget): Scene3DPresenterBridge {
  return {
    getCamera: () => target.getCamera(),
    setCamera: (state) => {
      target.setCamera(state, { animate: false });
      target.requestRender();
    },
    getCameraTarget: () => target.getCameraTarget(),
    groundHeight: (longitude, latitude) => target.groundHeight(longitude, latitude),
    modelSphere: () => target.modelSphere(),
    setModelTint: (tint) => target.setModelTint(tint),
    positionToScreen: (position) => target.positionToScreen(position),
    setVerticalExaggeration: (factor) => target.setVerticalExaggeration(factor),
    getVerticalExaggeration: () => target.getVerticalExaggeration(),
    async whenSettled(timeoutMs = 60_000, settledFrames = 3) {
      const deadline = performance.now() + timeoutMs;
      let run = 0;
      while (performance.now() < deadline) {
        if (target.isDestroyed()) {
          return false;
        }
        // Asked for on every pass: the scene draws only on request, and a settled check over a
        // frame that was never drawn would be a check of the previous picture.
        target.requestRender();
        await nextFrame();
        run = target.isSettled() ? run + 1 : 0;
        if (run >= settledFrames) {
          return true;
        }
      }
      return false;
    },
  };
}

/** Whether this browser is driven by automation, and so whether the handle is offered at all. */
export function presenterWanted(): boolean {
  return typeof navigator !== 'undefined' && navigator.webdriver === true;
}

/**
 * Puts the handle on the window and returns a function that takes it away again — only the handle
 * it put there, so a scene being replaced cannot remove its successor's.
 */
export function exposeScene3dToPresenter(target: Scene3DPresenterTarget): () => void {
  const bridge = createPresenterBridge(target);
  window.__silexgisPresenter = { ...window.__silexgisPresenter, scene3d: bridge };
  return () => {
    if (window.__silexgisPresenter?.scene3d === bridge) {
      delete window.__silexgisPresenter.scene3d;
    }
  };
}
