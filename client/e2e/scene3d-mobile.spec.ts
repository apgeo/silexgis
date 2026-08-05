// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test } from '@playwright/test';
import {
  countScene3dDrawCalls,
  dragScene3dWithFinger,
  entranceToPick,
  login,
  openScene3d,
  openScene3dAt,
  pinchScene3d,
  scene3dSurface,
  tapScene3d,
  waitForScene3dQuiet,
  watchScene3dDrawing,
} from './helpers.ts';

// The 3D scene on emulated touch, in both orientations.
//
// READ THIS BEFORE TRUSTING ANY OF IT. An emulated phone is a desktop browser with a small
// viewport, a coarse pointer and a touch event source. It is a good instrument for exactly three
// classes of defect and a bad one for everything else:
//
//   it CATCHES chrome that covers the scene at this width, controls a finger cannot hit or read,
//   gestures wired to the wrong events, and work done per frame that should not be;
//
//   it CANNOT SEE a phone's GPU memory ceiling, thermal throttling over minutes rather than
//   seconds, iOS Safari's WebGL limits, a context lost when the tab is backgrounded, or how a real
//   drag feels under a real finger's latency.
//
// Nothing here should be read as "the 3D view works on phones". It says the code path is reachable
// and the layout holds at this size. The rest needs a device.
//
// It runs in portrait and in landscape, and the second is not a formality: turned sideways a phone
// is wider than the breakpoint that decides a phone layout, while still being a device with no
// hovering pointer. Everything asserted below about a finger being able to reach and read a
// control is exactly the thing that a layout chosen on width alone gets wrong there, and nothing
// in a portrait run can see it.

test('the scene fills a phone screen without its own controls standing over it', async ({
  page,
}) => {
  await login(page);
  await openScene3d(page);

  const surface = (await scene3dSurface(page).boundingBox())!;
  expect(surface.width).toBeGreaterThan(300);

  // Two buttons in the top-right corner, not a column of eight down the edge. Measured against the
  // scene rather than asserted as a class name, because what matters is how much of the view they
  // stand on: the caves are in the middle and the chrome must stay out of it.
  const chrome = [
    page.getByTestId('scene3d-layers-trigger'),
    page.getByTestId('scene3d-camera-trigger'),
  ];
  let covered = 0;
  for (const control of chrome) {
    await expect(control).toBeVisible();
    const box = (await control.boundingBox())!;
    covered += box.width * box.height;
    // A finger-sized target, which is the size the rest of this application uses.
    expect(Math.min(box.width, box.height)).toBeGreaterThanOrEqual(36);
    // Out of the middle of the view, where the cave is.
    expect(box.y + box.height).toBeLessThan(surface.y + surface.height / 3);
  }
  expect(covered / (surface.width * surface.height)).toBeLessThan(0.05);

  // The seven-button preset strip is not out here at this width; it is behind the button above.
  await expect(page.getByTestId('scene3d-preset-north')).toHaveCount(0);
});

test('every camera control is reachable and captioned by a finger', async ({ page }) => {
  await login(page);
  await openScene3d(page);

  await page.getByTestId('scene3d-camera-trigger').tap();

  // On a touch device the tooltips that explain the desktop strip's glyphs never appear, because a
  // finger cannot hover. Out here that would leave seven unlabelled buttons; in the panel every
  // one of them is captioned in words, and every one can be brought into the viewport rather than
  // sitting off an edge with nothing to say so. Scrolled to rather than asserted to be visible
  // already: seven finger-sized rows are taller than a phone held sideways, so the panel gives way
  // and scrolls — what must never happen is a control that cannot be reached at all.
  for (const testId of [
    'scene3d-preset-top',
    'scene3d-preset-north',
    'scene3d-preset-south',
    'scene3d-preset-east',
    'scene3d-preset-west',
    'scene3d-fit-cave',
    'scene3d-projection-toggle',
  ]) {
    const control = page.getByTestId(testId);
    await control.scrollIntoViewIfNeeded();
    await expect(control).toBeInViewport();
    await expect(control).not.toHaveText('');
    const box = (await control.boundingBox())!;
    expect(box.height).toBeGreaterThanOrEqual(36);
  }

  await page.getByTestId('scene3d-preset-north').tap();
  await expect.poll(() => page.url(), { timeout: 30_000 }).toMatch(/#3d\//);
});

test('the layer panel fits the screen, with every control on it', async ({ page }) => {
  await login(page);
  await openScene3d(page);
  const viewport = page.viewportSize()!;

  await page.getByTestId('scene3d-layers-trigger').tap();
  const panel = page.getByTestId('scene3d-layer-panel');
  await expect(panel).toBeVisible();

  // A fixed-width panel plus the popover's own padding overflows a phone, and the sliders on the
  // far side then cannot be reached at all.
  const box = (await panel.boundingBox())!;
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
  await expect(panel).toBeInViewport();

  // And the page itself never scrolls sideways because of it.
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
  ).toBe(true);
});

test('a finger turns the camera, and the scene goes quiet again afterwards', async ({ page }) => {
  await watchScene3dDrawing(page);
  await login(page);
  await openScene3d(page);
  await waitForScene3dQuiet(page);

  // Where the camera is, read from the address bar the view writes it to when it comes to rest.
  // That is the application's own account of its camera, which is the point — reaching into the
  // scene module from here would answer for a different scene entirely.
  const positionInUrl = () => new URL(page.url()).hash;
  const before = positionInUrl();

  const box = (await scene3dSurface(page).boundingBox())!;
  // A genuine one-finger drag across the middle of the scene. It has to be a genuine one: the
  // browser does not turn injected mouse input into touch even on a phone profile, the engine
  // branches on which it was, and the desktop project already drives the mouse path — so a drag
  // made with the mouse here would move the camera, pass, and prove nothing about a finger.
  await dragScene3dWithFinger(
    page,
    { x: box.width / 2, y: box.height / 2 },
    { x: box.width / 2 - 80, y: box.height / 2 - 40 },
  );

  await expect.poll(positionInUrl, { timeout: 30_000 }).not.toBe(before);

  // The one measurement here that is genuinely about a phone rather than about a layout: once the
  // finger is lifted the scene must stop drawing entirely. A view that kept drawing after every
  // gesture would look identical and cost the battery for as long as the tab was open. The gesture
  // settles the camera, which reloads the cave data for where it now points, so this waits for
  // that to answer rather than for a fixed number of seconds.
  await waitForScene3dQuiet(page);
  expect(await countScene3dDrawCalls(page, 5_000)).toBe(0);
});

test('two fingers move the camera in and out', async ({ page }) => {
  await login(page);
  // Opened at a stated height, so how far the pinch moved the camera can be read rather than
  // inferred: the address bar is where this view writes where its camera is.
  await openScene3dAt(page, 45.5, 25.5);

  // The one gesture with no mouse equivalent at all — nothing else in this suite could stand in
  // for it — and the one most easily lost, because a browser takes a two-finger gesture the page
  // has not claimed as a zoom of the page instead.
  const heightInUrl = () => Number(new URL(page.url()).hash.split('/')[3]);
  await expect.poll(heightInUrl, { timeout: 30_000 }).toBeGreaterThan(0);
  const before = heightInUrl();

  await pinchScene3d(page, 80, 320);

  await expect.poll(heightInUrl, { timeout: 30_000 }).toBeLessThan(before);

  // And the page itself did not zoom, which is what a gesture the scene failed to claim looks
  // like: every piece of chrome grows and the camera stays where it was.
  expect(await page.evaluate(() => window.visualViewport?.scale ?? 1)).toBe(1);
});

test('a callout near the controls does not take their taps', async ({ page }) => {
  await login(page);
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);

  const box = (await scene3dSurface(page).boundingBox())!;
  await expect(async () => {
    await tapScene3d(page, box.width / 2, box.height / 2);
    await expect(page.getByTestId('scene3d-callout')).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });

  // The callout is pinned to a place in the world, not to a corner, so it goes where that place
  // goes — and dragging never dismisses it, because only a press and release in the same spot is a
  // click. Drag the entrance up into the corner the two triggers stand in, which is where a viewer
  // turning the camera to look at what they just picked ordinarily puts it.
  await dragScene3dWithFinger(
    page,
    { x: box.width / 2, y: box.height / 2 },
    { x: box.width - 40, y: 30 },
  );
  const callout = page.getByTestId('scene3d-callout');
  await expect(callout).toBeVisible();
  // It really is up in that corner rather than still mid-view, or this would pass without ever
  // staging the thing it is about.
  expect((await callout.boundingBox())!.y).toBeLessThan(box.y + 160);

  // Both triggers still take their own taps. Without this the callout is drawn over them and takes
  // the tap itself, which on a phone leaves no route at all to the camera positions or to any
  // layer control — and it is the state a viewer reaches by doing the most natural thing there is,
  // which is picking something and then turning the camera to look at it.
  await page.getByTestId('scene3d-camera-trigger').tap();
  await expect(page.getByTestId('scene3d-camera-controls')).toBeVisible();

  // The second tap closes the first panel and opens its own, which is what tapping past an open
  // panel does anywhere in this application.
  await page.getByTestId('scene3d-layers-trigger').tap();
  await expect(page.getByTestId('scene3d-layer-panel')).toBeVisible();
});

test('a tap names what it found, with a target a finger can close', async ({ page }) => {
  await login(page);
  // Over a real entrance, looking straight down, so it is in the middle of the view by
  // construction. "Frame the cave" cannot be used: the demo caves have entrances but no surveys,
  // so there is nothing drawn to frame and the control is correctly disabled.
  const entrance = await entranceToPick(page);
  await openScene3dAt(page, entrance.lat, entrance.lon);

  const box = (await scene3dSurface(page).boundingBox())!;
  await expect(async () => {
    await tapScene3d(page, box.width / 2, box.height / 2);
    await expect(page.getByTestId('scene3d-callout')).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });
  await expect(page.getByTestId('scene3d-callout')).toContainText(entrance.name);

  // There is no hover on a phone, so the callout is the only thing that names a pick here — which
  // is why its close button has to be a finger-sized target rather than a small icon.
  await expect(page.getByTestId('scene3d-hover-tooltip')).toHaveCount(0);
  const close = page.getByTestId('scene3d-callout-close');
  const closeBox = (await close.boundingBox())!;
  expect(Math.min(closeBox.width, closeBox.height)).toBeGreaterThanOrEqual(36);
  await close.tap();
  await expect(page.getByTestId('scene3d-callout')).toHaveCount(0);
});
