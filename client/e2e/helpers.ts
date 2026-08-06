// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type CDPSession, type Page } from '@playwright/test';

// Demo credentials/data: `dotnet run -- seed-demo` with the dev admin bootstrap.
// The full OIDC code+PKCE flow runs in the real browser.
export const adminEmail = 'admin@dev.local';
export const adminPassword = 'dev-admin-pass-1';

/**
 * Signs in, as the demo administrator unless another account is named. A flow that has to
 * show one person's content to a different person needs a second account, and the sign-in
 * itself is identical for both — only the credentials differ.
 */
export async function login(page: Page, email = adminEmail, password = adminPassword) {
  await page.goto('/');
  // Unauthenticated → OIDC authorize → SPA login page with returnUrl.
  await page.waitForURL(/\/login\?returnUrl=/);
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  // Authorize completes, callback exchanges the code, workspace renders. The wait is generous
  // because signing in is a redirect chain through the server and then a first paint of the
  // map, and every worker in the run starts with one: on a machine running the whole suite in
  // parallel this is the slowest moment of any test, and a tighter bound fails tests that have
  // nothing wrong with them.
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
}

/**
 * Goes to a route by address and waits until the application is really on it.
 *
 * Reaching a route this way is a full sign-in round trip: the session is held in memory, so a
 * hard navigation drops it and the application fetches a new one through the authorization
 * server before it renders anything. Looking for a row before that round trip has finished
 * looks for it on the redirect pages, where it will never be — and because a click waits for
 * its target, the test does not fail at the mistake. It hangs until its whole time is gone and
 * then blames the row.
 */
export async function gotoRoute(page: Page, path: string) {
  await page.goto(path);
  await page.waitForURL((url) => url.pathname === path, { timeout: 60_000 });
}

/** A named overlay row in the layer composer tree (left dock). */
export function overlayTreeNode(page: Page, name: string) {
  return page.locator('.layer-composer .ant-tree-treenode').filter({ hasText: name });
}

/**
 * Removes a surface feature through the registry table — cleanup for flows that save one.
 *
 * The registry shows a page at a time, so the row is narrowed to by name first: whether it
 * happens to be on the first page is a fact about how many other features sort ahead of it,
 * not about the one being removed.
 */
export async function deleteFeature(page: Page, featureName: string) {
  await page.goto('/features');
  // The pointer does not move when a page does. Whatever was last clicked leaves the virtual
  // mouse at those coordinates, and if this page happens to put its export button there, the
  // menu opens on hover and covers the table underneath — so the row's own delete button is
  // clicked at, retried against the menu, and the test's clock runs out on a cleanup step that
  // has nothing to do with what it is cleaning up. Parking the pointer in the corner first is
  // the whole fix; nothing about the application is involved.
  await page.mouse.move(0, 0);
  await page.getByPlaceholder('Search by name').fill(featureName);
  const row = page.getByRole('row', { name: new RegExp(featureName) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
}

/**
 * Puts the workspace map over the demo cave. Search cannot do this: its results carry no
 * coordinates by design, so picking one opens the record instead of moving the map. The
 * feature list's "show on map" fits the map to the row's geometry and goes there.
 */
export async function centreOnDemoCave(page: Page) {
  await page.goto('/features');
  await page.getByPlaceholder('Search by name').fill('Peștera Demo Mare');
  const row = page.getByRole('row', { name: /Peștera Demo Mare/ });
  await expect(row).toBeVisible({ timeout: 15_000 });
  // Icon-only antd button: its accessible name is the icon's aria-label.
  await row.getByRole('button', { name: 'aim' }).click();
  await expect(page.locator('.map-canvas')).toBeVisible({ timeout: 15_000 });
}

/** Taps the map at a viewport-relative point, the way a finger places a vertex. */
export async function tapMap(page: Page, x: number, y: number) {
  const box = (await page.locator('.map-canvas').boundingBox())!;
  await page.touchscreen.tap(box.x + x, box.y + y);
}

/**
 * Presses and holds the map, the gesture that opens the context menu on a phone.
 *
 * Dispatched rather than performed with a real touch: a genuine hold makes Android
 * Chromium raise its own `contextmenu`, which would prove nothing about the timer the app
 * has to run for iOS Safari — where no such event ever arrives. This drives that timer.
 * The threshold and cancel rules themselves are unit-tested with a fake clock.
 */
export async function longPressMap(page: Page, x: number, y: number) {
  // The listener sits on OL's viewport; the canvas element below it would not bubble up.
  const viewport = page.locator('.ol-viewport');
  const box = (await viewport.boundingBox())!;
  const at = { pointerType: 'touch', isPrimary: true, clientX: box.x + x, clientY: box.y + y };
  await viewport.dispatchEvent('pointerdown', at);
  await page.waitForTimeout(700); // comfortably past the 550ms the app waits
  await viewport.dispatchEvent('pointerup', at);
}

/** Waits until the scene is drawing and its current load has answered. */
export async function waitForScene3dReady(page: Page) {
  // The engine owns the canvas inside our element; its presence is the scene having started.
  await expect(page.getByTestId('scene3d-container').locator('canvas').first()).toBeAttached({
    timeout: 30_000,
  });
  await expect(page.getByText('The 3D view could not be started')).toHaveCount(0);
  await expect(page.getByTestId('scene3d-data')).toHaveAttribute('data-loading', 'false', {
    timeout: 30_000,
  });
}

/** Opens the 3D route on its opening view and waits for it. */
export async function openScene3d(page: Page) {
  await page.goto('/map3d');
  await waitForScene3dReady(page);
}

/**
 * Opens the scene looking straight down at a place, through the address bar the view honours.
 *
 * The blank page in the middle is not ceremony. The position is read out of the address bar when
 * the scene is built, and changing only the hash of the page already open is a navigation within
 * the same document: nothing reloads, the camera stays where it was, and the view goes on showing
 * — and asking the server about — somewhere else entirely.
 */
export async function openScene3dAt(page: Page, lat: number, lon: number) {
  await page.goto('about:blank');
  await page.goto(`/map3d#3d/${lat.toFixed(5)}/${lon.toFixed(5)}/900/0.0/-90.0`);
  await waitForScene3dReady(page);
}

/** The scene's drawing surface, which is the element every 3D gesture is measured against. */
export function scene3dSurface(page: Page) {
  return page.getByTestId('scene3d-surface');
}

/** Taps the scene at a point measured from the top left of its drawing surface. */
export async function tapScene3d(page: Page, x: number, y: number) {
  const box = (await scene3dSurface(page).boundingBox())!;
  await page.touchscreen.tap(box.x + x, box.y + y);
}

// ---- gestures a finger makes, and why they are not made with the mouse ------
//
// Playwright's public touch API is a tap and nothing else. Reaching for `page.mouse` to make up
// the difference does NOT work here and passes while doing nothing: a touch-enabled browser
// context does not turn injected mouse input into touch, so `page.mouse.down/move/up` produce
// pointer events of type `mouse` even on a phone profile — verified against the browser this
// suite runs. The engine branches on exactly that field before it does anything else, so a drag
// made that way is driven through the desktop path, which the desktop project already covers.
// What would go untested is everything that only a real touch sequence exercises: the engine's
// own bookkeeping of which finger is which, the gestures that need two of them, and — the reason
// this matters most — whether the browser hands the gesture to the page at all rather than taking
// it as a scroll, which is decided by a `touch-action` rule that mouse input is not subject to.
//
// So these speak to the browser's input pipeline directly, which is the same route the tap above
// already takes and the only one that produces genuine touch pointers. Two consequences: this is
// a chromium-only facility and must not be used from a WebKit project, and the coordinates are
// viewport pixels exactly as the tap's are.

/** One touch point, in the shape the browser's input pipeline expects. */
function finger(x: number, y: number) {
  return { x, y, radiusX: 12, radiusY: 12, force: 1 };
}

/**
 * How long the fingers rest where they finished before they are lifted, and how many times that
 * resting position is repeated.
 *
 * A gesture that ENDS AT SPEED is not the same input as one that comes to rest first, and the
 * difference is not cosmetic. Moves injected one after another arrive microseconds apart, so a
 * release straight after the last of them reads as an enormous velocity, the browser starts a fling
 * from it, and the NEXT TAP — anywhere on the page, on any element, however long afterwards — is
 * eaten to stop that fling instead of being delivered as a press. What that looks like from here is
 * a button that reports every pointer and touch event of a tap and no `click` at all, so nothing
 * bound to a click runs and the panel it opens never appears. Measured, sideways, dragging the
 * scene and then tapping a button: released at speed, six taps in ten were lost; rested first,
 * none of ten were. Waiting after the release does not help — a fling is not a timer, it is
 * cancelled by the tap. Resting first is also simply what a finger does.
 */
const GESTURE_REST_MILLISECONDS = 60;
const GESTURE_REST_SAMPLES = 2;

/**
 * Drags one finger across the scene, stepped, from one point to another.
 *
 * Both points are measured from the top left of the drawing surface. Stepped because a gesture is
 * read out of a sequence of moves: one jump from corner to corner is not a drag to anything.
 */
export async function dragScene3dWithFinger(
  page: Page,
  from: { x: number; y: number },
  to: { x: number; y: number },
  steps = 10,
) {
  const box = (await scene3dSurface(page).boundingBox())!;
  const cdp = await page.context().newCDPSession(page);
  try {
    await cdp.send('Input.dispatchTouchEvent', {
      type: 'touchStart',
      touchPoints: [finger(box.x + from.x, box.y + from.y)],
    });
    for (let step = 1; step <= steps; step += 1) {
      const fraction = step / steps;
      await cdp.send('Input.dispatchTouchEvent', {
        type: 'touchMove',
        touchPoints: [
          finger(
            box.x + from.x + (to.x - from.x) * fraction,
            box.y + from.y + (to.y - from.y) * fraction,
          ),
        ],
      });
    }
    await restAndLift(page, cdp, () => [finger(box.x + to.x, box.y + to.y)]);
  } finally {
    await cdp.detach();
  }
}

/**
 * Holds the fingers still where the gesture left them, then lifts them.
 *
 * The extra samples are what make the browser read the release as still rather than fast; see
 * `GESTURE_REST_MILLISECONDS`.
 */
async function restAndLift(
  page: Page,
  cdp: CDPSession,
  at: () => ReturnType<typeof finger>[],
): Promise<void> {
  for (let sample = 0; sample < GESTURE_REST_SAMPLES; sample += 1) {
    await page.waitForTimeout(GESTURE_REST_MILLISECONDS);
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: at() });
  }
  await page.waitForTimeout(GESTURE_REST_MILLISECONDS);
  await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
}

/**
 * Pinches two fingers together or apart about the middle of the scene, which is how a phone zooms.
 *
 * `from` and `to` are how far apart the two fingers are, in pixels; apart is a zoom in. This is the
 * one gesture with no mouse equivalent at all, so nothing else in the suite can stand in for it.
 */
export async function pinchScene3d(page: Page, from: number, to: number, steps = 10) {
  const box = (await scene3dSurface(page).boundingBox())!;
  const middle = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  const pair = (gap: number) => [
    finger(middle.x - gap / 2, middle.y),
    finger(middle.x + gap / 2, middle.y),
  ];
  const cdp = await page.context().newCDPSession(page);
  try {
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: pair(from) });
    for (let step = 1; step <= steps; step += 1) {
      await cdp.send('Input.dispatchTouchEvent', {
        type: 'touchMove',
        touchPoints: pair(from + ((to - from) * step) / steps),
      });
    }
    await restAndLift(page, cdp, () => pair(to));
  } finally {
    await cdp.detach();
  }
}

// ---- watching the scene draw ------------------------------------------------
//
// Counting what the 3D view draws from a test is harder than it looks, and the obvious way is
// wrong. Importing the scene module inside `page.evaluate` and taking a hold on "the" scene does
// NOT join the one the application is running: the development server hands the test a second
// instance of the module, whose singleton is its own, so the hold builds a SECOND drawing context
// inside the same element. Measured: one canvas in the surface before, two after acquiring, one
// again after releasing. Every number taken that way describes a scene that was built for the
// measurement and torn down after it — including, memorably, a steady five frames a second of
// "idle" cost that was entirely the second scene starting up over and over.
//
// So nothing here reaches for the module. The instrument is the graphics context itself: every
// draw the engine issues goes through one of four calls, and counting them needs no cooperation
// from the application, no module identity and no second scene. Zero draw calls is zero frames.

/**
 * Starts counting draw calls, before any application code runs.
 *
 * Must be called before the page is navigated — it patches the prototype the engine's context will
 * inherit from, and a context created earlier keeps the original methods.
 */
export async function watchScene3dDrawing(page: Page) {
  await page.addInitScript(() => {
    const counter = { calls: 0 };
    (window as unknown as { __drawCalls: { calls: number } }).__drawCalls = counter;
    const prototype = WebGL2RenderingContext.prototype as unknown as Record<string, unknown>;
    for (const name of [
      'drawElements',
      'drawArrays',
      'drawElementsInstanced',
      'drawArraysInstanced',
    ]) {
      const original = prototype[name];
      if (typeof original === 'function') {
        prototype[name] = function patched(this: unknown, ...args: unknown[]) {
          counter.calls += 1;
          return (original as (...a: unknown[]) => unknown).apply(this, args);
        };
      }
    }
  });
}

/** Draw calls issued over a span of time. Requires `watchScene3dDrawing` before the navigation. */
export async function countScene3dDrawCalls(page: Page, milliseconds: number): Promise<number> {
  return page.evaluate(async (duration) => {
    const counter = (window as unknown as { __drawCalls?: { calls: number } }).__drawCalls;
    if (!counter) {
      throw new Error('draw calls are not being watched; call watchScene3dDrawing first');
    }
    const before = counter.calls;
    await new Promise((resolve) => setTimeout(resolve, duration));
    return counter.calls - before;
  }, milliseconds);
}

/**
 * Waits until the scene has stopped drawing.
 *
 * Opening the view is not idle and is not meant to be: the loader answers, the batches go to the
 * renderer and each of them asks for the frame that shows it, and a second load follows the first
 * because the opening flight settles the camera. Waiting a fixed number of seconds races that and
 * measures the burst instead of the rest.
 */
export async function waitForScene3dQuiet(page: Page, timeoutMilliseconds = 60_000) {
  const deadline = Date.now() + timeoutMilliseconds;
  for (;;) {
    if ((await countScene3dDrawCalls(page, 3_000)) === 0) {
      return;
    }
    if (Date.now() > deadline) {
      throw new Error('the 3D scene never stopped drawing');
    }
  }
}

/**
 * A named, individually served entrance out of the seeded data, with where it is.
 *
 * Read out of the response the application itself asked for rather than written down here, so this
 * does not need editing every time the demo data changes — and read from the application's own
 * request rather than a request of this test's, because the endpoint wants the session's bearer
 * token and only the running application is holding one.
 *
 * The list is watched at a close zoom, where the server serves entrances one by one. At anything
 * wider it aggregates them into counted cells, and a cell has no name for a label to be checked
 * against.
 */
export async function entranceToPick(page: Page) {
  interface Entrance {
    geometry: { type: string; coordinates: number[] };
    properties: Record<string, unknown>;
  }
  const named: Entrance[] = [];
  page.on('response', (response) => {
    if (!response.url().includes('/api/v1/map/cave-entrances') || !response.ok()) {
      return;
    }
    void response
      .json()
      .then((collection: { features?: Entrance[] }) => {
        for (const feature of collection.features ?? []) {
          if (
            feature.properties.cluster !== true &&
            feature.geometry.type === 'Point' &&
            typeof feature.properties.name === 'string' &&
            feature.properties.name !== ''
          ) {
            named.push(feature);
          }
        }
      })
      .catch(() => {
        // A response that cannot be read is simply not a source of entrances.
      });
  });

  // Read off the FLAT map rather than the 3D view, and framed by the demo cave's own geometry
  // rather than by a position written down here. Both matter: the 3D view's box is sized from its
  // viewport, so a phone asks about a patch of ground small enough to miss an entrance a desktop
  // would have found, and the wider views the server answers with counted cells instead of names.
  // Fitting the map to the cave puts the camera where the entrances certainly are, at whatever
  // size the screen happens to be.
  await centreOnDemoCave(page);
  await expect
    .poll(() => named.length, {
      timeout: 30_000,
      message: 'the seeded demo data served no individually named entrance to pick',
    })
    .toBeGreaterThan(0);

  // The most isolated entrance, not whichever one arrived first.
  //
  // Which entrance arrives first depends on the order the responses land, so taking the first
  // returns a different place from run to run and makes every assertion about the name downstream
  // of a race. Farthest from any other is a property of the data rather than of the network, so it
  // names the same place every run — and it is the one worth choosing on its own merits, because
  // entrances are grouped server-side by how much ground is on screen and an entrance with a close
  // neighbour is served by name at one size and as a count at another.
  const unique = [...new Map(named.map((f) => [f.geometry.coordinates.join(), f])).values()];
  const distanceToNearestOther = (feature: Entrance) =>
    Math.min(
      ...unique
        .filter((other) => other !== feature)
        // Raw degrees are enough to rank: every candidate sits in one demo dataset, so the
        // latitude scaling that would matter across a continent is the same for all of them.
        .map((other) =>
          Math.hypot(
            other.geometry.coordinates[0] - feature.geometry.coordinates[0],
            other.geometry.coordinates[1] - feature.geometry.coordinates[1],
          ),
        ),
      Number.POSITIVE_INFINITY,
    );
  const feature = unique.reduce((best, candidate) =>
    distanceToNearestOther(candidate) > distanceToNearestOther(best) ? candidate : best,
  );
  return {
    lon: feature.geometry.coordinates[0],
    lat: feature.geometry.coordinates[1],
    name: feature.properties.name as string,
  };
}
