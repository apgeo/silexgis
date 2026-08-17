// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
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
//     So that leg is not covered here; what covers it is the server-side tests that run the phases
//     themselves, against the real tool.

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
 * Removes every build this installation is holding.
 *
 * The tests below describe an installation that has not built terrain yet, and the dev database
 * outlives the run — so without this the second run of the suite would be describing the leftovers
 * of the first, and the assertion that nothing has yet met the missing service would fail against a
 * page that is telling the truth.
 */
async function removeExistingBuilds(page: Page) {
  const remove = page.locator('[data-testid^="terrain-delete-"]');
  await expect(page.getByTestId('terrain-builds')).toBeVisible();

  // The delete button is offered on every row on purpose, including rows the server will refuse
  // to delete, because the state can change in another browser between the list being read and
  // the button being pressed. That makes pressing it blindly wrong here: two ordinary states are
  // refused, and each of them would hang this loop on a confirmation that never comes *and* put
  // an undeclared 409 in the browser's log, failing every test in this file on the console guard
  // rather than on the thing that actually went wrong.
  for (let guard = 0; guard < 50 && (await remove.count()) > 0; guard += 1) {
    const id = (await remove.first().getAttribute('data-testid'))!.slice('terrain-delete-'.length);
    const row = page.locator('tr').filter({ has: page.getByTestId(`terrain-delete-${id}`) });

    // Refused state one: the build the 3D scene is drawing. Somebody activated it, which is the
    // feature's happy path and what the activate button is for — so this is the ordinary state of
    // a development database, not a damaged one. Stop drawing it first, which is the same thing
    // an operator would do and needs no more right than the delete does.
    const stop = page.getByTestId(`terrain-stop-${id}`);
    if ((await stop.count()) > 0) {
      await stop.click();
      await expect(stop).toHaveCount(0, { timeout: 30_000 });
    }

    // Refused state two: a build still queued or running, whose files are being written while the
    // request is being made. It is waited out rather than skipped, because the tests below assert
    // against an installation that has built nothing at all and a row left behind would make them
    // describe something else. Terrain runs one build at a time and this one is already on its
    // way; the bound is the same generous one the build test itself uses, and for the same reason
    // — what is slow here is a public elevation service, not this application.
    await expect(row).not.toContainText(/Queued|Running/, { timeout: 240_000 });

    await page.getByTestId(`terrain-delete-${id}`).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText('Deleted')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('Deleted')).toBeHidden({ timeout: 30_000 });
  }
  await expect(remove).toHaveCount(0);
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

  // Reached by address here, but the menu entry is what an operator actually finds.
  await expect(page.getByRole('menuitem', { name: 'Terrain' })).toBeVisible();

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

  // Emptying the list below removes the build whose detail panel is open, and the request already
  // on its way for that build answers 404 before the list has been read again. The refusal is
  // correct — the build really is gone — and nothing on screen misbehaves; it is declared here
  // rather than left in the log because this test is what causes it, and an undeclared one would
  // make every future run look as though it had found something.
  // Matched on the wording rather than on the address: what the browser writes carries the status
  // in its message and the route only in the location, and a declaration is read against the
  // message. That makes this one flow's expectation of one refusal, which is why it lives here and
  // not in the guard's global list.
  consoleErrors.allow(
    /status of 404/,
    'this test deletes the build whose panel is open, and its poll was already in flight',
  );

  await login(page);
  await gotoRoute(page, '/admin/terrain');
  await removeExistingBuilds(page);

  // Nothing has met the missing service yet, so nothing is claimed about it. Asserted against a
  // list this test emptied itself, so the silence means the page has nothing to say rather than
  // that the answer has not arrived.
  await expect(page.getByTestId('terrain-worker-missing')).toHaveCount(0);

  await drawArea(page);
  await expect(page.getByTestId('terrain-area-extent')).toContainText('West, south, east, north', {
    timeout: 15_000,
  });

  // The depth is left at what the form offers. It decides how much the tile maker would be asked
  // for, and this build never reaches the tile maker — so the realistic case is the default one,
  // and driving that control here would test the control rather than the state under test.
  await page.getByRole('button', { name: 'Start build' }).click();

  // The build appears in the list, obtains its coverage, prepares it, and stops at the bake. How
  // long that takes is how long a public elevation-data service takes to answer, so the wait is
  // generous: a bound tight enough to fail on a slow morning would say the page is broken when
  // what is slow is the internet.
  const failed = page.getByTestId('terrain-builds').getByText('Failed');
  await expect(failed.first()).toBeVisible({ timeout: 180_000 });

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

  // And it is honest about what is left: the work before the stopped step stands, and there is no
  // way to carry that build further from here.
  await expect(notice).toContainText('There is no way to take that build further');

  // The build's own panel says the same thing in short, and does not also shout it as a failure —
  // one screen carrying the same alert twice makes neither of them read as the answer.
  await expect(page.getByTestId('terrain-build-bake-missing')).toBeVisible();
  await expect(page.getByTestId('terrain-build-failure')).toHaveCount(0);

  // The phases it did reach are shown as the fixed pipeline they are.
  await expect(page.getByTestId('terrain-pipeline')).toBeVisible();
  await expect(page.getByTestId('terrain-phase-bake')).toBeVisible();
});
