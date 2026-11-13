// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { entranceToPick, login, waitForScene3dReady } from './helpers.ts';
import { serveSyntheticRelief, type SyntheticHill } from './syntheticRelief.ts';

// The 3D view's relief setting, in the real scene, over ground that has relief.
//
// The unit tests prove the arithmetic against a stand-in for the 3D library. What only a real
// library can answer is whether what it stretches by itself — the ground — and what the scene
// stretches at its own boundary stay together: an entrance marker the library lays on the ground
// has to be found on the stretched ground, at the pixel the scene reports for the place it marks,
// at every factor. A hill is served for the purpose (the scratch stack's globe is otherwise the
// smooth ellipsoid, on which a stretch changes nothing), with its top just east of the entrance so
// the entrance sits high on its flank.

interface Camera {
  longitude: number;
  latitude: number;
  height: number;
  heading: number;
  pitch: number;
}

/** The handle the scene offers a driven browser, as much of it as this spec reads. */
interface SceneHandle {
  getCamera(): Camera;
  groundHeight(longitude: number, latitude: number): number;
  positionToScreen(position: { longitude: number; latitude: number; height: number }): { x: number; y: number } | undefined;
  whenSettled(timeoutMs?: number, settledFrames?: number): Promise<boolean>;
}

type WithHandle = { __silexgisPresenter?: { scene3d?: SceneHandle } };

const camera = (page: Page) =>
  page.evaluate(() => {
    const scene = (window as unknown as WithHandle).__silexgisPresenter?.scene3d;
    if (!scene) {
      throw new Error('the 3D scene offers no handle to read it through');
    }
    const { longitude, latitude, height, heading, pitch } = scene.getCamera();
    return { longitude, latitude, height, heading, pitch };
  });

/** Waits until the scene has drawn everything in view, three frames running. */
const settled = (page: Page) =>
  page.evaluate(() => (window as unknown as WithHandle).__silexgisPresenter?.scene3d?.whenSettled(120_000, 3));

/** Where the ground under the entrance is, in true metres, and where on the screen it is drawn. */
function groundUnder(page: Page, entrance: { lon: number; lat: number }) {
  return page.evaluate(({ lon, lat }) => {
    const scene = (window as unknown as WithHandle).__silexgisPresenter?.scene3d;
    if (!scene) {
      throw new Error('the 3D scene offers no handle to read it through');
    }
    const height = scene.groundHeight(lon, lat);
    return { height, screen: scene.positionToScreen({ longitude: lon, latitude: lat, height }) };
  }, entrance);
}

/** Puts the pointer at a pixel of the scene; whatever is drawn there is named in the hover. */
async function hoverAt(page: Page, at: { x: number; y: number }) {
  const box = (await page.getByTestId('scene3d-surface').boundingBox())!;
  await page.mouse.move(box.x + at.x, box.y + at.y);
}

/** Moves the relief slider in the layer panel by whole steps, and closes the panel again. */
async function stepRelief(page: Page, steps: number, key: 'ArrowRight' | 'ArrowLeft', reads: string) {
  await page.getByTestId('scene3d-layers-trigger').click();
  await page.getByRole('slider', { name: 'Vertical exaggeration' }).focus();
  for (let step = 0; step < steps; step += 1) {
    await page.keyboard.press(key);
  }
  await expect(page.getByTestId('scene3d-relief-value')).toHaveText(reads);
  await page.getByTestId('scene3d-layers-trigger').click();
}

/**
 * The pixel the ground under the entrance is drawn at holds the entrance's marker: the hover names
 * it there. Asked at a pixel the scene worked out from the place, against what the library drew
 * by itself, which is the whole question.
 */
async function markerOnTheGround(page: Page, entrance: { lon: number; lat: number; name: string }) {
  const ground = await groundUnder(page, entrance);
  await hoverAt(page, ground.screen!);
  await expect(page.getByTestId('scene3d-hover-tooltip')).toContainText(entrance.name);
  return ground;
}

test('stretches the relief and everything on it together, and puts it back', async ({ page }) => {
  test.slow();
  await login(page);
  const entrance = await entranceToPick(page);
  const hill: SyntheticHill = {
    longitude: entrance.lon + 600 / (111_320 * Math.cos((entrance.lat * Math.PI) / 180)),
    latitude: entrance.lat,
    heightM: 1200,
    radiusM: 2000,
  };
  await serveSyntheticRelief(page, hill);

  // A kilometre south of the entrance and four up, looking north and steeply down at it. The
  // entrance stays near the middle of the view at half the height and at twice it — so the data
  // the view asks for around the middle of the screen always includes it — and is still seen
  // from a slant, so a stretch moves it on the screen.
  await page.goto('about:blank');
  await page.goto(`/map3d#3d/${(entrance.lat - 1000 / 110_540).toFixed(5)}/${entrance.lon.toFixed(5)}/4000/0.0/-70.0`);
  await waitForScene3dReady(page, 120_000);
  await expect(page.getByText('Invented relief for a test')).toBeAttached({ timeout: 60_000 });
  expect(await settled(page)).toBe(true);
  // The ground under the entrance is the hill's flank, not the ellipsoid, once its tiles are in.
  await expect.poll(async () => (await groundUnder(page, entrance)).height, { timeout: 60_000 }).toBeGreaterThan(900);

  // The cave's survey here is a plan laid on the ground through its entrance, and a hover over
  // the entrance may name either; with the survey hidden, only a marker can answer.
  await page.getByTestId('scene3d-layers-trigger').click();
  await page.getByRole('checkbox', { name: 'Cave centerlines' }).uncheck();
  await page.getByTestId('scene3d-layers-trigger').click();
  expect(await settled(page)).toBe(true);

  const tooltip = page.getByTestId('scene3d-hover-tooltip');
  const cameraAtTrueHeight = await camera(page);
  const address = page.url();
  // The marker the library lays on the ground is found where the scene says the ground is.
  const atTrueHeight = await markerOnTheGround(page, entrance);
  await page.screenshot({ path: test.info().outputPath('relief-x1.png') });

  // Twice as tall, from the panel.
  await stepRelief(page, 10, 'ArrowRight', '×2');
  await expect(page.getByTestId('scene3d-data').getByText('Relief ×2')).toBeVisible();
  expect(await settled(page)).toBe(true);

  // The camera is where it was over the ground, at the same true height: it now stands twice as
  // high over ground drawn twice as high, which is the same place in a taller world.
  const cameraStretched = await camera(page);
  expect(cameraStretched.longitude).toBeCloseTo(cameraAtTrueHeight.longitude, 7);
  expect(cameraStretched.latitude).toBeCloseTo(cameraAtTrueHeight.latitude, 7);
  expect(Math.abs(cameraStretched.height - cameraAtTrueHeight.height)).toBeLessThan(0.5);
  expect(cameraStretched.pitch).toBeCloseTo(cameraAtTrueHeight.pitch, 4);
  // The ground is reported at its true height still, and is drawn somewhere else on the screen.
  const stretched = await groundUnder(page, entrance);
  expect(Math.abs(stretched.height - atTrueHeight.height)).toBeLessThan(15);
  expect(Math.abs(stretched.screen!.y - atTrueHeight.screen!.y)).toBeGreaterThan(40);
  // And the marker went with it: on the stretched ground, and no longer where the ground used to be.
  await markerOnTheGround(page, entrance);
  await hoverAt(page, atTrueHeight.screen!);
  await expect(tooltip.filter({ hasText: entrance.name })).toHaveCount(0);
  // Nothing about the place has changed, so neither has its address.
  expect(page.url()).toBe(address);
  await page.screenshot({ path: test.info().outputPath('relief-x2.png') });

  // Straight on to half the height. The library keeps the ground it found under each marker from
  // one factor to the next unless told otherwise; a marker left at twice the height is above this
  // camera now, and the hover finds nothing.
  await stepRelief(page, 15, 'ArrowLeft', '×0.5');
  await expect(page.getByTestId('scene3d-data').getByText('Relief ×0.5')).toBeVisible();
  expect(await settled(page)).toBe(true);
  const flattened = await markerOnTheGround(page, entrance);
  expect(Math.abs(flattened.height - atTrueHeight.height)).toBeLessThan(15);
  expect(page.url()).toBe(address);
  await page.screenshot({ path: test.info().outputPath('relief-x0.5.png') });

  // Back to true height with the panel's reset: the picture is the one it started as.
  await page.getByTestId('scene3d-layers-trigger').click();
  await page.getByRole('button', { name: 'True height' }).click();
  await expect(page.getByTestId('scene3d-relief-value')).toHaveText('×1');
  await expect(page.getByRole('button', { name: 'True height' })).toBeDisabled();
  await page.getByTestId('scene3d-layers-trigger').click();
  await expect(page.getByTestId('scene3d-data').getByText(/^Relief ×/)).toHaveCount(0);
  expect(await settled(page)).toBe(true);
  const back = await groundUnder(page, entrance);
  expect(Math.abs(back.screen!.x - atTrueHeight.screen!.x)).toBeLessThan(1);
  expect(Math.abs(back.screen!.y - atTrueHeight.screen!.y)).toBeLessThan(1);
  const cameraBack = await camera(page);
  expect(Math.abs(cameraBack.height - cameraAtTrueHeight.height)).toBeLessThan(0.5);
  await hoverAt(page, back.screen!);
  await expect(tooltip).toContainText(entrance.name);
  expect(page.url()).toBe(address);
  await page.screenshot({ path: test.info().outputPath('relief-x1-again.png') });
});
