// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Scene3DCameraState } from './scene3dEngine.ts';
import {
  createPresenterBridge,
  exposeScene3dToPresenter,
  presenterWanted,
  type Scene3DPresenterTarget,
} from './presenterBridge3d.ts';

const camera: Scene3DCameraState = {
  longitude: 25,
  latitude: 45,
  height: 1000,
  heading: 0,
  pitch: -45,
  roll: 0,
};

function target(settledAfter: number): Scene3DPresenterTarget & { renders: number } {
  const fake = {
    renders: 0,
    isDestroyed: () => false,
    getCamera: () => camera,
    setCamera: vi.fn(),
    getCameraTarget: () => undefined,
    requestRender: () => {
      fake.renders += 1;
    },
    isSettled: () => fake.renders > settledAfter,
    groundHeight: () => 0,
    modelSphere: () => undefined,
    setModelTint: vi.fn(),
    positionToScreen: vi.fn(() => ({ x: 12, y: 34 })),
    setVerticalExaggeration: vi.fn(),
    getVerticalExaggeration: () => 2,
  };
  return fake;
}

afterEach(() => {
  delete window.__silexgisPresenter;
});

describe('the scripted-recording handle on the 3D scene', () => {
  it('jumps the camera rather than flying it, and asks for the frame', () => {
    const fake = target(0);
    createPresenterBridge(fake).setCamera(camera);
    expect(fake.setCamera).toHaveBeenCalledWith(camera, { animate: false });
    expect(fake.renders).toBe(1);
  });

  it('passes a tint for the models on, and taking it off too', () => {
    const fake = target(0);
    const bridge = createPresenterBridge(fake);
    bridge.setModelTint({ red: 1, green: 0.5, blue: 0, amount: 0.8 });
    bridge.setModelTint(undefined);
    expect(fake.setModelTint).toHaveBeenNthCalledWith(1, { red: 1, green: 0.5, blue: 0, amount: 0.8 });
    expect(fake.setModelTint).toHaveBeenNthCalledWith(2, undefined);
  });

  it('passes the relief factor and the question of where a place is drawn straight through', () => {
    const fake = target(0);
    const bridge = createPresenterBridge(fake);
    const place = { longitude: 25, latitude: 45, height: 1200 };

    bridge.setVerticalExaggeration(2);

    expect(fake.setVerticalExaggeration).toHaveBeenCalledWith(2);
    expect(bridge.getVerticalExaggeration()).toBe(2);
    expect(bridge.positionToScreen(place)).toEqual({ x: 12, y: 34 });
    expect(fake.positionToScreen).toHaveBeenCalledWith(place);
  });

  it('answers settled only after several settled frames in a row', async () => {
    const fake = target(4);
    expect(await createPresenterBridge(fake).whenSettled(5_000, 3)).toBe(true);
    // Unsettled for four requested frames, then three settled ones.
    expect(fake.renders).toBe(7);
  });

  it('answers false when the scene never settles in time', async () => {
    const fake = target(Number.POSITIVE_INFINITY);
    expect(await createPresenterBridge(fake).whenSettled(50)).toBe(false);
  });

  it('removes only the handle it put on the window', () => {
    const hideFirst = exposeScene3dToPresenter(target(0));
    const first = window.__silexgisPresenter?.scene3d;
    exposeScene3dToPresenter(target(0));
    const second = window.__silexgisPresenter?.scene3d;
    expect(second).not.toBe(first);
    hideFirst();
    expect(window.__silexgisPresenter?.scene3d).toBe(second);
  });

  it('is offered only to a browser under automation', () => {
    for (const webdriver of [false, true]) {
      Object.defineProperty(navigator, 'webdriver', { value: webdriver, configurable: true });
      expect(presenterWanted()).toBe(webdriver);
    }
    Reflect.deleteProperty(navigator, 'webdriver');
  });
});
