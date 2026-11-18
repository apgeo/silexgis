// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, tryAsPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

// The terrain builder page, driven the way an operator drives it: a rectangle drawn on a map
// becomes an area, and a build started against it walks its phases and says where it got to.
//
// What this file can and cannot cover on a development machine is worth stating, because the
// difference is not a gap in the page:
//
//   - Drawing the rectangle and starting the build are ordinary browser work, and are covered here.
//   - A plain installation runs nothing that can turn rasters into tiles, so a build started here
//     obtains its data, prepares it, and stops at the bake. That is the common case rather than a
//     fault, and the second test covers exactly it: the build stops, and the page then says what to
//     type to have the service.
//   - A build reaching completion, and the 3D scene drawing what it produced, needs the tile-making
//     service running against the same directories the application writes to. That service is a
//     container sharing a volume with the containerised application, while this suite drives an
//     application run straight from a build output on the developer's own disk — the two never meet.
//     So that leg is not covered here, and it is not covered by the server-side tests either:
//     those run every phase, but the tile maker in them is a stand-in that writes a made-up pyramid
//     into the handover directory. The supervisor's flags are tested against such a stand-in and
//     the pyramid check against pyramids the tests fabricate, so neither is ever tested against the
//     real tool's output. A real bake is proved only by running the worker image by hand.

/** The map the rectangle is drawn on. It opens over Braşov, so anything near its middle is land. */
const AREA_MAP = '[data-testid="terrain-area-map"]';

/**
 * Where the area map is, once it has stopped moving.
 *
 * The page above the map grows after its first paint: the list of builds arrives, and with it the
 * notice about the missing tile-making service, which is inserted above the form. A gesture aimed
 * at coordinates measured before that arrives lands somewhere else entirely by the time it is made,
 * and the symptom is a rectangle that was never drawn rather than an error — so the position is
 * taken only once two readings agree.
 *
 * It is also scrolled to first. Pointer events are made at coordinates in the window, so a map that
 * has been pushed below the fold by that same notice is aimed at correctly and still missed.
 */
async function settledBox(page: Page) {
  const map = page.locator(AREA_MAP);
  await expect(map).toBeVisible();
  await map.scrollIntoViewIfNeeded();
  let previous = '';
  let box!: NonNullable<Awaited<ReturnType<typeof map.boundingBox>>>;
  await expect
    .poll(
      async () => {
        box = (await map.boundingBox())!;
        const now = JSON.stringify(box);
        const unchanged = now === previous;
        previous = now;
        return unchanged;
      },
      { message: 'the area map never stopped moving on the page', timeout: 30_000 },
    )
    .toBe(true);
  return box;
}

/**
 * Where a build obtains its elevation data: a public bucket on the internet, which is the only
 * source the application has. Probed from here because the application server runs on this same
 * machine and reaches the internet the same way.
 */
const ELEVATION_SOURCE = 'https://copernicus-dem-30m.s3.amazonaws.com';

/** Whether the elevation source answers at all; any HTTP answer, a refusal included, means it is reachable. */
async function elevationSourceReachable(): Promise<boolean> {
  try {
    await fetch(ELEVATION_SOURCE, { method: 'HEAD', signal: AbortSignal.timeout(15_000) });
    return true;
  } catch {
    return false;
  }
}

interface BuildRow {
  id: string;
  status: string;
  errorCode?: string | null;
}

/** Whether a build the page lists first has already met the missing tile-making service. */
async function anyBuildStoppedAtBake(page: Page): Promise<boolean> {
  const first = await asPerson<{ items: BuildRow[] }>(page, 'GET', '/api/v1/terrain/builds?page=1&pageSize=10');
  return first.items.some(
    (build) => build.status === 'failed' && build.errorCode === 'terrain_build.bake_unavailable',
  );
}

/**
 * Draws a rectangle on the area map, roughly in the middle of it.
 *
 * Two clicks, not a drag. The box comes from the map library's two-corner interaction, which
 * settles a corner on a click and abandons the gesture if the pointer travelled while it was down —
 * a press-move-release is how that library is told to pan, so drawing that way silently moves the
 * map and draws nothing at all.
 *
 * The size is deliberately small. What is drawn decides how much elevation data the build has to
 * obtain and warp before it reaches the step this installation cannot do, and none of that work is
 * what is under test: a rectangle a few kilometres across proves the same things as a county does,
 * and costs seconds instead of minutes.
 */
async function drawArea(page: Page) {
  await page.getByRole('button', { name: 'Draw rectangle' }).click();
  const box = await settledBox(page);
  const x = box.x + box.width / 2;
  const y = box.y + box.height / 2;
  await page.mouse.move(x - 20, y - 20);
  await page.mouse.down();
  await page.mouse.up();
  // The sketch follows the pointer, so the second corner has to be arrived at rather than jumped
  // to: without a move between the two clicks the shape has no extent to finish with.
  await page.mouse.move(x + 20, y + 20, { steps: 10 });
  await page.mouse.down();
  await page.mouse.up();
}

test('a rectangle drawn on the map becomes the area a build may be started for', async ({
  page,
}) => {
  await login(page);
  await gotoRoute(page, '/admin/terrain');

  // Reached by address here, but the menu entry is what an operator actually finds. The rail
  // starts collapsed and a collapsed rail shows no group's entries; expanding it opens the group
  // of the page being shown, which is where the entry then is.
  await page.locator('.ant-layout-sider-trigger').click();
  await expect(page.getByRole('menuitem', { name: 'Terrain' })).toBeVisible({ timeout: 15_000 });

  const start = page.getByRole('button', { name: 'Start build' });
  await expect(page.getByTestId('terrain-builds')).toBeVisible();

  // Said before anything is drawn, not after a build has already spent an hour obtaining data:
  // what this installation may not be able to finish is a fact about the installation, and the
  // moment worth reading it is the moment before committing to a rectangle.
  await expect(page.getByTestId('terrain-bake-service')).toContainText(
    'needs a service of its own',
  );

  // Nothing drawn yet: the field says what to do, and the button refuses.
  await expect(page.getByTestId('terrain-area-extent')).toContainText('Drag a rectangle');
  await expect(start).toBeDisabled();

  await drawArea(page);

  // The field now reports a real extent, and the button will take it.
  await expect(page.getByTestId('terrain-area-extent')).toContainText('West, south, east, north', {
    timeout: 15_000,
  });
  await expect(page.getByTestId('terrain-area-extent')).toContainText('square degrees');
  await expect(page.getByTestId('terrain-area-too-large')).toHaveCount(0);
  await expect(start).toBeEnabled();

  // Clearing takes it back out again, rather than leaving a rectangle nothing can remove.
  await page.getByRole('button', { name: 'Clear rectangle' }).click();
  await expect(page.getByTestId('terrain-area-extent')).toContainText('Drag a rectangle');
  await expect(start).toBeDisabled();
});

test('a build on an installation with no tile-making service stops there, and the page says what to type', async ({
  page,
  consoleErrors,
}) => {
  // Longer than the file's own bound, and for a reason no other test here has: this one waits on a
  // public elevation-data service somewhere on the internet answering, and then on that data being
  // warped. Neither of those is this application, and neither is quick on a machine already busy.
  // The ordinary bound stays right for everything driven purely by the application itself.
  test.setTimeout(300_000);

  // The application has no local source of elevation data to point a build at, so without the
  // internet the build would stop at obtaining its data and never reach the step this test is
  // about. Said as a skip with its reason rather than as a red run that names a timeout.
  test.skip(
    !(await elevationSourceReachable()),
    `the public elevation-data source (${ELEVATION_SOURCE}) cannot be reached, and a build cannot obtain data without it`,
  );

  await login(page);
  await gotoRoute(page, '/admin/terrain');
  await expect(page.getByTestId('terrain-builds')).toBeVisible();

  // The installation is shared with every other run and test, so this test removes only the build
  // it starts and leaves everything else as it found it. That means the page may already know of a
  // build that met the missing service; only when it does not can the notice's absence be asserted,
  // and the notice's arrival below is then the same page changing its answer because of this build.
  const alreadyStopped = await anyBuildStoppedAtBake(page);
  if (!alreadyStopped) {
    await expect(page.getByTestId('terrain-worker-missing')).toHaveCount(0);
  } else {
    test.info().annotations.push({
      type: 'precondition-not-asserted',
      description: 'an earlier build had already met the missing tile-making service, so the notice was there before this one',
    });
  }

  await drawArea(page);
  await expect(page.getByTestId('terrain-area-extent')).toContainText('West, south, east, north', {
    timeout: 15_000,
  });

  // The depth is left at what the form offers. It decides how much the tile maker would be asked
  // for, and this build never reaches the tile maker — so the realistic case is the default one,
  // and driving that control here would test the control rather than the state under test.
  const submitted = page.waitForResponse(
    (response) => response.request().method() === 'POST' && response.url().endsWith('/api/v1/terrain/builds'),
  );
  await page.getByRole('button', { name: 'Start build' }).click();
  const response = await submitted;
  expect(response.ok(), `starting the build answered ${response.status()}`).toBeTruthy();
  const made = ((await response.json()) as BuildRow).id;

  try {
    await stopsAtTheBake(page, made);
  } finally {
    // Only what this test made. A build stopped at the bake is deletable; one that never got there
    // (the test failed earlier) may still be running and is then refused, and is left behind with
    // the rest of a failed run's state rather than waited out here.
    await tryAsPerson(page, 'DELETE', `/api/v1/terrain/builds/${made}`);
    // The open panel may poll the build once more before the page closes, and that answers 404 now
    // that the build is gone. Declared by its address, so another refusal in this test still counts.
    consoleErrors.allow(
      new RegExp(`/api/v1/terrain/builds/${made}`),
      'this test deleted the build whose panel was open, and its poll was already in flight',
    );
  }
});

/** What the page says once the build this test started has stopped at the step this installation cannot do. */
async function stopsAtTheBake(page: Page, made: string) {
  // The build appears in the list, obtains its coverage, prepares it, and stops at the bake. How
  // long that takes is how long a public elevation-data service takes to answer, so the wait is
  // generous: a bound tight enough to fail on a slow morning would say the page is broken when
  // what is slow is the internet. Read on this build's own row: another build in the list that
  // failed earlier says nothing about this one.
  const ownRow = page.locator('tr').filter({ has: page.getByTestId(`terrain-delete-${made}`) });
  await expect(ownRow.getByText('Failed')).toBeVisible({ timeout: 180_000 });

  // What the operator is told, and where. The notice is at the top of the page rather than beside
  // the build that met the state: what is missing is a property of the installation, and somebody
  // about to draw another rectangle should read it before drawing rather than after.
  const notice = page.getByTestId('terrain-worker-missing');
  await expect(notice).toBeVisible();
  await expect(notice).toContainText('nothing that can turn rasters into tiles');

  // Both lines are shown in full, because a paraphrase cannot be pasted.
  await expect(notice).toContainText('SILEXGIS__Terrain__BakeEnabled=true');
  await expect(notice).toContainText(
    'docker compose -f docker-compose.yml -f docker-compose.terrain-worker.yml up -d',
  );

  // And it is honest about what is left: the work before the stopped step stands, and the stopped
  // build is carried on from there once the service runs — so the control it names is on the build.
  await expect(notice).toContainText('press Run again on the stopped build');
  await expect(page.getByTestId(`terrain-retry-${made}`)).toBeVisible();

  // The build's own panel says the same thing in short, and does not also shout it as a failure —
  // one screen carrying the same alert twice makes neither of them read as the answer.
  await expect(page.getByTestId('terrain-build-bake-missing')).toBeVisible();
  await expect(page.getByTestId('terrain-build-failure')).toHaveCount(0);

  // The phases it did reach are shown as the fixed pipeline they are.
  await expect(page.getByTestId('terrain-pipeline')).toBeVisible();
  await expect(page.getByTestId('terrain-phase-bake')).toBeVisible();
}
