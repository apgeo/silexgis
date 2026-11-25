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
//
// The flyby grows the relief while it circles. A bundle carries that — the caves stretched by keys,
// the camera lifted by the same keys — so it is not among what the panel says is left out, and
// the plan in the zip has the keys. The panel also says what a bundle does that the screen does
// not — its sun and its sky go out while the camera is under the ground — and the zip's own notes
// say how the scene is laid out and lit.
//
// And it points out what the view does: the entrances the demonstration view draws about its caves
// are named to the server by id and come back as markers pinned to the caves' models, the name of
// an entrance of the flyby's own cave beside it. Ticked in the panel, the caves appear part by
// part: every mesh in the zip then carries a number on each vertex and its model a threshold.

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

/** Picks an option of a drop-down list by its title, then closes it. */
async function pickFrom(page: Page, select: string, title: string) {
  await page.getByTestId(select).click();
  const list = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)');
  await list.getByTitle(title, { exact: true }).click();
  await expect(list).toHaveCount(0);
}

test('a flyby round a cave is downloaded as a Blender bundle, and the zip holds its scene and what changes in it', async ({ page }) => {
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
  // The relief rises to three times true during the circle, and falls back.
  await page.getByText('Ready-made changes').click();
  await page.getByTestId('flyby-recipe-relief-factor').fill('3');
  await page.getByTestId('flyby-recipe-relief-factor').blur();
  await pickFrom(page, 'flyby-recipe-relief-section', 'In every circle');
  await page.getByTestId('flyby-recipe-relief-add').click();
  // A rise and a fall for each circle the flyby makes.
  await expect(page.getByText(/^\d+ changes$/)).toBeVisible();
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
  // What it leaves out is said before it is made: the ground always. The relief that changes is
  // not on the list, and neither are the entrances the demonstration view draws: both are carried.
  await expect(panel.getByTestId('flyby-bundle-left-out-ground')).toBeVisible();
  await expect(panel.getByTestId('flyby-bundle-left-out')).not.toContainText(/entrance markers/i);
  await expect(panel.getByTestId('flyby-bundle-pointed')).toContainText('the entrances and the features that are points go in as markers');
  // The caves are to appear part by part.
  await panel.getByTestId('flyby-bundle-reveal').check();
  await expect(panel.getByTestId('flyby-bundle-reveal')).toBeChecked();
  await expect(panel.getByTestId('flyby-bundle-left-out')).toContainText('The bundle is made anyway');
  await expect(panel.getByTestId('flyby-bundle-left-out')).not.toContainText(/relief/i);
  await expect(panel.getByTestId('flyby-bundle-left-out')).not.toContainText('keeps the look of its first frame');
  await expect(panel.getByTestId('flyby-bundle-daylight')).toContainText(
    'the sun and the sky go out while the camera is under the ground',
  );
  await expect(panel.getByTestId('flyby-movie-privacy')).toContainText('It holds no coordinates, no terrain and no map.');

  // The whole of it — the approaches and the quarter circles the relief rises in.
  const summary = (await panel.getByTestId('flyby-movie-summary').textContent()) ?? '';
  const frames = Number(/^([\d,.\s]+) frames of/.exec(summary)?.[1].replace(/\D/g, ''));
  expect(frames, summary).toBeGreaterThan(25);
  expect(frames, summary).toBeLessThan(5000);

  const download = page.waitForEvent('download', { timeout: 420_000 });
  const asked = page.waitForRequest((request) => request.url().includes('/api/v1/flybys/blender-bundle'), { timeout: 420_000 });
  await panel.getByTestId('flyby-movie-export').click();
  const request = (await asked).postDataJSON() as {
    frames: number[][];
    caves: { caveId: string; walls: boolean }[];
    leftOut: string[];
    relief: number;
    language?: string;
    changes?: {
      relief: { frame: number; value: number; easing: string | null }[] | null;
      daylight?: { frame: number; value: number }[] | null;
    };
    markers?: { kind: string; colour: string; markers: { featureId: string; groundM: number; named: boolean }[] }[];
    reveal?: { by: string; startS: number; endS: number };
  };
  const file = await download;
  expect(file.suggestedFilename()).toBe('demo-round-blender.zip');
  await expect(panel.getByTestId('flyby-bundle-report')).toBeVisible({ timeout: 60_000 });
  await expect(panel.getByTestId('flyby-movie-failed')).toHaveCount(0);

  // ---- what the page asked ----
  expect(request.frames).toHaveLength(frames);
  expect(request.caves.length).toBeGreaterThan(0);
  expect(request.leftOut).toContain('ground');
  // The relief as keys on the clip's frames: from the viewer's own up to three times true and back.
  const relief = request.changes?.relief ?? [];
  expect(Math.max(...relief.map((key) => key.value))).toBe(3);
  expect(relief[0]).toMatchObject({ frame: 0, value: request.relief });
  // The last fall ends as the flyby does: on the frame after the clip's last, or before it.
  expect(relief.at(-1)!.frame).toBeLessThanOrEqual(frames);
  // The notes are asked for in the language the page is read in.
  expect(request.language).toMatch(/^en/);
  // The entrances about the caves, by id and the ground under each — and no place.
  const entrances = request.markers?.find((layer) => layer.kind === 'entrances');
  expect(entrances, JSON.stringify(request.markers ?? null)).toBeDefined();
  expect(entrances!.markers.length).toBeGreaterThan(0);
  expect(Object.keys(entrances!.markers[0]).sort()).toEqual(['featureId', 'groundM', 'named']);
  // The reveal: by distance, ending before the clip does.
  expect(request.reveal?.by).toBe('distance');
  expect(request.reveal!.endS).toBeGreaterThan(request.reveal!.startS);
  expect(request.reveal!.endS).toBeLessThan(frames / 25);
  await expect(panel.getByTestId('flyby-bundle-report-markers')).toBeVisible();

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
    look: { lights: { id: string; kind: string; energy: number }[] };
    features: string[];
    annotations?: { id: string; kind: string; role: string; model: string; at: number[]; radius?: number; text?: string }[];
    changes?: {
      target?: { id?: string; role?: string };
      scale?: { t: number; value: number[]; easing?: string }[];
      energy?: { t: number; value: number }[];
      reveal?: { t: number; value: number }[];
      revealFront?: { edge: number; glow?: { width: number } };
    }[];
  };
  expect(plan.timeline).toEqual({ fps: 25, frameCount: frames });
  expect(plan.camera.track).toHaveLength(frames);
  expect(plan.camera.track.every((pose) => pose.orientation?.length === 4)).toBe(true);
  expect(plan.models.map((model) => `demo-round-blender/${model.path}`).sort()).toEqual([...meshes].sort());
  // Laid out about a point it does not name.
  expect(plan.frame.crs).toBeUndefined();
  // Every model is stretched by the relief's keys, played by the player's module for changes.
  expect(plan.features).toContain('changes');
  const stretched = (plan.changes ?? []).filter((change) => change.scale);
  expect(stretched.map((change) => `demo-round-blender/${change.target?.id}.ply`).sort()).toEqual([...meshes].sort());
  expect(Math.max(...stretched[0].scale!.map((key) => key.value[2]))).toBe(3);
  expect(stretched[0].scale!.some((key) => key.easing === 'smooth')).toBe(true);

  // A lamp on the camera and a sun. Where the camera of this flyby went under the demonstration
  // ground the page said so, and the sun's strength is keyed by exactly what it said; where it
  // did not, nothing changes of the sun.
  expect(plan.look.lights.map((light) => light.id).sort()).toEqual(['Headlamp', 'Sun']);
  const sun = (plan.changes ?? []).filter((change) => change.target?.id === 'Sun');
  const daylight = request.changes?.daylight ?? null;
  expect(sun).toHaveLength(daylight ? 1 : 0);
  if (daylight) {
    const whole = plan.look.lights.find((light) => light.id === 'Sun')!.energy;
    expect(sun[0].energy!.map((key) => key.value)).toEqual(daylight.map((key) => key.value * whole));
  }

  // What the scene points out: every entrance the server could place as a ball pinned to a point
  // of one of the plan's own models, played by the player's module for annotations; a name stands
  // beside an entrance of the flyby's own caves.
  expect(plan.features).toContain('annotations');
  const markers = (plan.annotations ?? []).filter((annotation) => annotation.kind === 'marker');
  const named = (request.markers ?? []).reduce((count, layer) => count + layer.markers.length, 0);
  expect(markers.length).toBeGreaterThan(0);
  expect(markers.length).toBeLessThanOrEqual(named);
  const mouths = markers.filter((marker) => marker.role === 'entrance');
  expect(mouths.length).toBeGreaterThan(0);
  expect(mouths.length).toBeLessThanOrEqual(entrances!.markers.length);
  const ids = plan.models.map((model) => model.path.replace(/\.ply$/, ''));
  for (const marker of markers) {
    // The entrances, and whatever other layer of features the demonstration view draws.
    expect(marker.role).toMatch(/^(entrance|surface-feature|feature-layer-\d+)$/);
    expect(ids).toContain(marker.model);
    expect(marker.at).toHaveLength(3);
    expect(marker.radius).toBeGreaterThan(0);
  }
  const written = (plan.annotations ?? []).filter((annotation) => annotation.kind === 'callout');
  expect(written.every((name) => name.role === 'entrance-name' && (name.text ?? '').length > 0)).toBe(true);

  // The reveal: a threshold on every model whose cave has a survey to measure along, rising over
  // the seconds the page asked for, with a soft edge and a glow; and the number it rises over on
  // every vertex of that model's mesh.
  const revealed = (plan.changes ?? []).filter((change) => change.reveal);
  expect(revealed.length).toBeGreaterThan(0);
  for (const change of revealed) {
    expect(change.reveal).toHaveLength(2);
    expect(change.reveal![0].value).toBe(0);
    expect(change.reveal![1].value).toBeGreaterThan(0);
    expect(Math.abs(change.reveal![0].t - request.reveal!.startS)).toBeLessThanOrEqual(0.02);
    expect(Math.abs(change.reveal![1].t - request.reveal!.endS)).toBeLessThanOrEqual(0.02);
    expect(change.revealFront!.edge).toBeGreaterThan(0);
    const header = files.get(`demo-round-blender/${change.target!.id}.ply`)!.subarray(0, 400).toString('latin1');
    expect(header).toContain('property float reveal');
  }

  const readme = files.get('demo-round-blender/README.txt')!.toString('utf8');
  expect(readme).toContain('WHAT IS POINTED OUT');
  expect(readme).toContain('THE REVEAL');
  expect(readme).toContain('A flyby of caves made by SilexGIS for Blender');
  expect(readme).toContain('HOW THE SCENE IS LAID OUT');
  expect(readme).toContain('THE LIGHT');
  expect(readme).toContain('NOT IN THIS SCENE');
  expect(readme).toContain('the ground and the map drawn on it');

  // Getting the flyby ready moved the camera; the view is handed back as it was.
  await expect.poll(async () => sameCamera(await camera(page), home), { timeout: 15_000 }).toBe(true);
});
