// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFile } from 'node:fs/promises';
import { inflateRawSync } from 'node:zlib';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { entranceToPick, login, openScene3dAt } from './helpers.ts';

// A flyby downloaded as a bundle for Blender, through the panel a person uses.
//
// The panel's own suite drives a scene that is a plain object and a server that is a function; the
// server's suite sends requests it wrote itself. Neither can say whether the request the real page
// makes of the real scene — its camera at every frame, the caves it draws, where it draws them — is
// one the real server makes a bundle of. So the bundle is asked for here over the demonstration
// data, and the zip that comes back is opened and read.

interface Camera {
  longitude: number;
  latitude: number;
  height: number;
  pitch: number;
}

function camera(page: Page): Promise<Camera> {
  return page.evaluate(() => {
    const scene = (window as unknown as { __silexgisPresenter?: { scene3d?: { getCamera(): Camera } } }).__silexgisPresenter?.scene3d;
    if (!scene) {
      throw new Error('the 3D scene offers no handle to read its camera through');
    }
    const { longitude, latitude, height, pitch } = scene.getCamera();
    return { longitude, latitude, height, pitch };
  });
}

function sameCamera(a: Camera, b: Camera): boolean {
  return (
    Math.abs(a.longitude - b.longitude) < 2e-5 &&
    Math.abs(a.latitude - b.latitude) < 2e-5 &&
    Math.abs(a.height - b.height) < 2 &&
    Math.abs(a.pitch - b.pitch) < 0.5
  );
}

/** The files of a zip by their names, read from its directory at the end of the file. */
function unzip(bytes: Buffer): Map<string, Buffer> {
  const files = new Map<string, Buffer>();
  const end = bytes.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  const count = bytes.readUInt16LE(end + 10);
  let at = bytes.readUInt32LE(end + 16);
  for (let entry = 0; entry < count; entry += 1) {
    const method = bytes.readUInt16LE(at + 10);
    const packed = bytes.readUInt32LE(at + 20);
    const nameLength = bytes.readUInt16LE(at + 28);
    const extraLength = bytes.readUInt16LE(at + 30);
    const commentLength = bytes.readUInt16LE(at + 32);
    const local = bytes.readUInt32LE(at + 42);
    const name = bytes.subarray(at + 46, at + 46 + nameLength).toString('utf8');
    const data = local + 30 + bytes.readUInt16LE(local + 26) + bytes.readUInt16LE(local + 28);
    const body = bytes.subarray(data, data + packed);
    files.set(name, method === 0 ? body : inflateRawSync(body));
    at += 46 + nameLength + extraLength + commentLength;
  }
  return files;
}

test('a flyby round a cave is downloaded as a Blender bundle, and the zip holds its scene', async ({ page }) => {
  test.setTimeout(600_000);
  await login(page);
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);
  await page.getByTestId('scene3d-flyby').click();
  await expect(page.getByTestId('flyby-panel')).toBeVisible();
  // About the caves drawn in the view: a bundle is made of caves.
  await page.getByTestId('flyby-source-inView').check();
  await expect(page.getByTestId('flyby-panel').getByRole('checkbox').first()).toBeChecked();
  await page.getByTestId('flyby-turns').fill('0.25');
  await page.getByTestId('flyby-turns').blur();
  await page.getByTestId('flyby-seconds-per-turn').fill('6');
  await page.getByTestId('flyby-seconds-per-turn').blur();
  await page.getByTestId('flyby-load-route').uncheck();
  await page.getByTestId('flyby-name').fill('Demo round');
  const home = await camera(page);

  await page.getByTestId('flyby-movie').click();
  const panel = page.getByTestId('flyby-movie-dialog');
  await expect(panel).toBeVisible({ timeout: 30_000 });

  // Beside the movie formats, and said in so many words to be rendered somewhere else.
  await panel.locator('label').filter({ has: page.getByTestId('flyby-movie-format-blender') }).click();
  await expect(panel.getByTestId('flyby-movie-format-blender')).toBeChecked();
  await expect(panel.getByTestId('flyby-movie-format-help')).toContainText(
    'render it on your own machine with Blender 4.2 or newer',
  );
  await expect(panel.getByTestId('flyby-bundle-fps')).toHaveText('A bundle is made at 25 frames a second.');
  // Nothing is filmed, so nothing is asked about how a frame is taken.
  await expect(panel.getByTestId('flyby-movie-level')).toHaveCount(0);
  await expect(panel.getByTestId('flyby-movie-draft')).toHaveCount(0);
  // What it leaves out is said before it is made: the ground always, and here the entrance markers
  // the demonstration view draws.
  await expect(panel.getByTestId('flyby-bundle-left-out-ground')).toBeVisible();
  await expect(panel.getByTestId('flyby-bundle-left-out')).toContainText('The bundle is made anyway');
  await expect(panel.getByTestId('flyby-movie-privacy')).toContainText('It holds no coordinates, no terrain and no map.');

  // The first two seconds alone: fifty frames.
  await panel.getByText('A stretch of it').click();
  const to = panel.getByTestId('flyby-movie-to');
  await to.focus();
  await page.keyboard.press('ControlOrMeta+A');
  await page.keyboard.type('2');
  await page.keyboard.press('Tab');
  await expect(panel.getByTestId('flyby-movie-summary')).toContainText('50 frames of');

  const download = page.waitForEvent('download', { timeout: 420_000 });
  const asked = page.waitForRequest((request) => request.url().includes('/api/v1/flybys/blender-bundle'), { timeout: 420_000 });
  await panel.getByTestId('flyby-movie-export').click();
  const request = (await asked).postDataJSON() as { frames: number[][]; caves: { caveId: string; walls: boolean }[]; leftOut: string[] };
  const file = await download;
  expect(file.suggestedFilename()).toBe('demo-round-blender.zip');
  await expect(panel.getByTestId('flyby-bundle-report')).toBeVisible({ timeout: 60_000 });
  await expect(panel.getByTestId('flyby-movie-failed')).toHaveCount(0);

  // ---- what the page asked ----
  expect(request.frames).toHaveLength(50);
  expect(request.caves.length).toBeGreaterThan(0);
  expect(request.leftOut).toContain('ground');

  // ---- the zip, read back ----
  const files = unzip(await readFile(await file.path()));
  const names = [...files.keys()];
  expect(names.every((name) => name.startsWith('demo-round-blender/'))).toBe(true);
  for (const name of ['README.txt', 'LICENSE', 'NOTICE', 'render.py', 'render.sh', 'render.cmd', 'scene-plan.json', 'scene-plan.draft.json']) {
    expect(names).toContain(`demo-round-blender/${name}`);
  }
  // At least one cave is in it, as a mesh every Blender reads.
  const meshes = names.filter((name) => name.endsWith('.ply'));
  expect(meshes.length).toBeGreaterThan(0);
  expect(files.get(meshes[0])!.subarray(0, 3).toString('ascii')).toBe('ply');

  const plan = JSON.parse(files.get('demo-round-blender/scene-plan.json')!.toString('utf8')) as {
    frame: { crs?: string };
    timeline: { fps: number; frameCount: number };
    models: { path: string }[];
    camera: { track: { orientation?: number[] }[] };
  };
  expect(plan.timeline).toEqual({ fps: 25, frameCount: 50 });
  expect(plan.camera.track).toHaveLength(50);
  expect(plan.camera.track.every((pose) => pose.orientation?.length === 4)).toBe(true);
  expect(plan.models.map((model) => `demo-round-blender/${model.path}`).sort()).toEqual([...meshes].sort());
  // Laid out about a point it does not name.
  expect(plan.frame.crs).toBeUndefined();

  const readme = files.get('demo-round-blender/README.txt')!.toString('utf8');
  expect(readme).toContain('NOT IN THIS SCENE');
  expect(readme).toContain('the ground and the map drawn on it');

  // Getting the flyby ready moved the camera; the view is handed back as it was.
  await expect.poll(async () => sameCamera(await camera(page), home), { timeout: 15_000 }).toBe(true);
});
