// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Page, type TestInfo } from '@playwright/test';
import { asPerson, localDay, tripBody, tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { flatPng, zipEntries, zipEntryBytes } from './reportMap.ts';

/**
 * A trip's write-up, downloaded, carries a map of the trip.
 *
 * Everything that makes this work happens in a browser and nowhere else: the map is drawn on a
 * canvas by the reader's own page, its background is fetched from a tile server in the one way
 * that lets the result be read back out, and the picture travels to the server in the same
 * request that asks for the document. No unit test has a canvas, and no server test has a
 * browser, so the whole length of it is driven here and judged on the file that comes down.
 *
 * The tile server is stood in for. A flow that fetched real tiles would pass or fail by somebody
 * else's uptime, and — the reason that matters more — could not show the second half: what the
 * page does when a source will not let its tiles be copied.
 */

/** Unlike anything the application draws, so finding it in the picture means the tiles did. */
const TILE_COLOUR = [118, 168, 208] as const;

/** The ground a map is drawn on when it has no background. */
const PLAIN_GROUND = [244, 241, 234] as const;

/** The colour a cave the trip names is marked in. */
const CAVE_MARK = [90, 63, 160] as const;

const TILES = '**://tile.openstreetmap.org/**';

/** A trip with a line it walked and a place it met, near enough to share a picture. */
function tripWithShapes(title: string) {
  return tripBody(title, localDay(-3), {
    geom: {
      type: 'LineString',
      coordinates: [
        [25.4412, 45.5381],
        [25.4455, 45.5402],
        [25.4489, 45.5437],
      ],
    },
    meetingGeom: { type: 'Point', coordinates: [25.4371, 45.5352] },
  });
}

interface Downloaded {
  fileName: string;
  entries: string[];
  text: string;
  pictures: Buffer[];
}

/**
 * Presses the download button and opens up the file that arrives.
 *
 * The pictures found in it are kept with the run's other artifacts. What these flows assert of a
 * picture is a pixel or two, and whoever reads a failure — or wonders what the map looks like —
 * needs the picture itself.
 */
async function downloadWriteUp(page: Page, testInfo: TestInfo): Promise<Downloaded> {
  const arriving = page.waitForEvent('download');
  await page.getByTestId('trip-report-download').click();
  const file = await arriving;
  const zip = readFileSync(await file.path());
  const entries = zipEntries(zip);
  const part = (name: string) => {
    const entry = entries.find((candidate) => candidate.name === name);
    expect(entry, `the file holds no ${name}`).toBeTruthy();
    return zipEntryBytes(zip, entry!);
  };
  // A word-processor document keeps its pictures in a folder of their own. Which level that
  // folder sits at is the writer's choice — this one files it at the top, beside the folder the
  // text is in — so it is looked for at either.
  const pictures = entries
    .filter((entry) => /(^|\/)media\/[^/]+$/.test(entry.name))
    .map((entry) => zipEntryBytes(zip, entry));
  for (const [index, picture] of pictures.entries()) {
    await testInfo.attach(`write-up-picture-${index + 1}.jpg`, {
      body: picture,
      contentType: 'image/jpeg',
    });
  }
  return {
    fileName: file.suggestedFilename(),
    entries: entries.map((entry) => entry.name),
    // The words of the document, with the markup between them taken out.
    text: part('word/document.xml')
      .toString('utf8')
      .replace(/<[^>]+>/g, ' '),
    pictures,
  };
}

/** A box of a picture, in pixels. */
interface Box {
  left: number;
  top: number;
  width: number;
  height: number;
}

/**
 * A picture's size, the colour of one pixel of it, and whether a box of it holds anything dark —
 * all as a browser decodes it.
 */
async function lookAt(page: Page, picture: Buffer, x: number, y: number, inkIn?: Box) {
  return page.evaluate(
    async ({ base64, x: px, y: py, box }) => {
      const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes]));
      const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
      const context = canvas.getContext('2d')!;
      context.drawImage(bitmap, 0, 0);
      const [red, green, blue] = context.getImageData(px, py, 1, 1).data;

      // Dark means written: nothing else on these pictures is nearly black.
      let ink = false;
      if (box) {
        const { data } = context.getImageData(box.left, box.top, box.width, box.height);
        for (let i = 0; i < data.length && !ink; i += 4) {
          ink = data[i] < 80 && data[i + 1] < 80 && data[i + 2] < 80;
        }
      }
      return { width: bitmap.width, height: bitmap.height, pixel: [red, green, blue], ink };
    },
    { base64: picture.toString('base64'), x, y, box: inkIn ?? null },
  );
}

/**
 * Whether a colour is the one expected, give or take what re-encoding a picture costs. A wide
 * flat area comes back almost exactly; a small mark takes on a little of what is around it.
 */
function near(actual: number[], expected: readonly number[], tolerance = 12) {
  return actual.every((channel, i) => Math.abs(channel - expected[i]) <= tolerance);
}

test('a downloaded write-up carries a map of the trip, and the copy filed against the trip does not', async ({
  page,
}, testInfo) => {
  test.slow();
  const title = `E2E Mapped Trip ${Date.now()}`;

  // Every tile is one flat colour, and comes with leave to be read back out of the browser.
  const tile = flatPng(256, TILE_COLOUR);
  await page.route(TILES, (route) =>
    route.fulfill({
      status: 200,
      contentType: 'image/png',
      headers: { 'access-control-allow-origin': '*' },
      body: tile,
    }),
  );

  await login(page);
  const trip = await asPerson<{ id: string }>(page, 'POST', '/api/v1/trip-logs/', tripWithShapes(title));
  try {
    await gotoRoute(page, `/trip-logs/${trip.id}/report`);
    await expect(page.getByTestId('trip-report')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('heading', { name: title })).toBeVisible();

    const sent = page.waitForRequest(
      (request) => request.method() === 'POST' && /\/report\/download(\?|$)/.test(request.url()),
    );
    const document = await downloadWriteUp(page, testInfo);

    // The picture went up in the same request that asked for the document, as a form.
    expect((await sent).headers()['content-type']).toContain('multipart/form-data');

    // What came down is the write-up, and it holds one picture and the line that says whose
    // view the picture is.
    expect(document.fileName).toMatch(/^trip-report-[0-9a-f]{8}-\d{8}\.docx$/);
    expect(document.entries).toContain('word/document.xml');
    expect(document.pictures).toHaveLength(1);
    expect(document.text).toContain('Map as shown to Administrator on ');
    expect(document.text).toContain('positions as this reader may see them');
    // The words that were there before the picture are still there beside it.
    expect(document.text).toMatch(/LineString of 3 position\(s\), centred on/);

    // The picture is the size it is always drawn at — the map, and a strip under it — and its
    // corner, where no shape is, is the colour of the tiles: the background is in the picture.
    // That is also what shows the tiles were asked for in the way that lets a picture be read
    // back. Asked the ordinary way they would have drawn just as well, and the browser would
    // then have refused to hand the finished picture over at all.
    const seen = await lookAt(page, document.pictures[0], 10, 10);
    expect(seen.width).toBe(1200);
    expect(seen.height).toBeGreaterThan(800);
    expect(near(seen.pixel, TILE_COLOUR), `the corner is ${seen.pixel.join(',')}`).toBe(true);

    // Nothing to tell the reader: the file is what the page showed.
    await expect(page.locator('.ant-message-notice')).toHaveCount(0);

    // Filing the write-up against the trip sends nothing with the request. The copy that is
    // kept is opened by everybody who may read the trip, and a map is one reader's view.
    const filing = page.waitForRequest(
      (request) => request.method() === 'POST' && /\/report(\?|$)/.test(request.url()),
    );
    const filed = page.waitForResponse(
      (response) =>
        response.request().method() === 'POST' && /\/report(\?|$)/.test(response.url()),
      { timeout: 30_000 },
    );
    await page.getByTestId('trip-report-keep').click();
    expect((await filing).postDataBuffer()).toBeNull();
    expect((await filed).status()).toBe(200);
  } finally {
    await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
  }
});

test('a cave the trip names is marked on the map where its own page puts it, under its name', async ({
  page,
}, testInfo) => {
  test.slow();
  const stamp = Date.now();
  const caveName = `E2E Marked ${stamp}`;

  const tile = flatPng(256, TILE_COLOUR);
  await page.route(TILES, (route) =>
    route.fulfill({
      status: 200,
      contentType: 'image/png',
      headers: { 'access-control-allow-origin': '*' },
      body: tile,
    }),
  );

  await login(page);
  // A cave with a main entrance, which is where a cave's position comes from. Open to anybody
  // signed in and not protected, so that the administrator's reading of it is nobody's special
  // case: this flow is about the mark being drawn, and who may be shown a position is decided
  // on the server and proved there.
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const entranceTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/entrance-types');
  const cave = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name: caveName,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'unknown',
    isShowCave: false,
  });
  let tripId: string | undefined;
  try {
    await asPerson(page, 'POST', `/api/v1/caves/${cave.id}/entrances`, {
      name: null,
      entranceTypeId: entranceTypes[0].id,
      isMain: true,
      geom: { type: 'Point', coordinates: [25.4433, 45.5391] },
      altitude: null,
      description: null,
      positionQuality: 'gps',
      surveyedAt: null,
    });
    // A trip that drew nothing of its own and names the cave: most of a club's older trips.
    const trip = await asPerson<{ id: string; caveIds: string[] }>(
      page,
      'POST',
      '/api/v1/trip-logs/',
      tripBody(`E2E Cave Trip ${stamp}`, localDay(-3), { caveIds: [cave.id] }),
    );
    tripId = trip.id;
    expect(trip.caveIds, 'the trip names the cave for this reader').toEqual([cave.id]);

    await gotoRoute(page, `/trip-logs/${trip.id}/report`);
    await expect(page.getByTestId('trip-report')).toBeVisible({ timeout: 30_000 });
    // The page has read the cave to print its name; the map is drawn from that same answer.
    // (Its name is on the page more than once — in the list of caves and in what the trip did
    // there — and either is the page having read it.)
    await expect(page.getByRole('link', { name: caveName }).first()).toBeVisible({ timeout: 30_000 });

    const document = await downloadWriteUp(page, testInfo);
    expect(document.pictures).toHaveLength(1);

    // One mark and nothing else, so the picture is centred on it: the middle of the map is the
    // colour a named cave is drawn in, and there is writing just above it — its name.
    const seen = await lookAt(page, document.pictures[0], 600, 400, {
      left: 520,
      top: 354,
      width: 160,
      height: 30,
    });
    expect(near(seen.pixel, CAVE_MARK, 30), `the middle is ${seen.pixel.join(',')}`).toBe(true);
    expect(seen.ink, 'no name is written above the mark').toBe(true);

    // The document says where under its own heading even though the trip drew no sketch.
    expect(document.text).toContain('Where');
    expect(document.text).toContain('Map as shown to Administrator on ');
    // And it still places the cave in no words at all: the position is in the picture only.
    expect(document.text).not.toContain('45.5391');
    expect(document.text).not.toContain('25.4433');
  } finally {
    if (tripId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${tripId}`);
    }
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${cave.id}`);
  }
});

test('a map source that will not let its tiles be copied leaves the map on a plain ground, and the reader is told', async ({
  page,
  consoleErrors,
}, testInfo) => {
  test.slow();
  const title = `E2E Unmapped Trip ${Date.now()}`;

  // The same tiles, from a server that lets only a site of its own read them back — which is how
  // a real one behaves when it keeps a list of who may. The page's own maps draw them
  // regardless; the picture's map is refused them, which is the browser doing what it should.
  // (Simply leaving the header off would not do: the browser driver adds one to an answer it
  // supplies when a request from another origin would otherwise be refused.)
  consoleErrors.allow(
    /blocked by CORS policy/,
    'the stand-in tile server answers that somebody else may read its tiles back, on purpose: ' +
      'the browser refusing the picture those tiles is the condition this flow is about',
  );
  consoleErrors.allow(
    /Failed to load resource: net::ERR_FAILED/,
    'the same refusal as the browser reports it against each tile request it turned away',
  );
  const tile = flatPng(256, TILE_COLOUR);
  await page.route(TILES, (route) =>
    route.fulfill({
      status: 200,
      contentType: 'image/png',
      headers: { 'access-control-allow-origin': 'https://somewhere-else.invalid' },
      body: tile,
    }),
  );

  await login(page);
  const trip = await asPerson<{ id: string }>(page, 'POST', '/api/v1/trip-logs/', tripWithShapes(title));
  try {
    await gotoRoute(page, `/trip-logs/${trip.id}/report`);
    await expect(page.getByTestId('trip-report')).toBeVisible({ timeout: 30_000 });

    const document = await downloadWriteUp(page, testInfo);

    // The reader is told the file differs from the page, rather than left to open it. Looked
    // for first, because it is said for a few seconds and then taken down.
    await expect(
      page.getByText('The map in the document has no background map under it.', { exact: false }),
    ).toBeVisible();

    // The document still came, and still carries a map — drawn on a plain ground this time,
    // with none of the tiles' colour in it.
    expect(document.pictures).toHaveLength(1);
    const seen = await lookAt(page, document.pictures[0], 10, 10);
    expect(seen.width).toBe(1200);
    expect(near(seen.pixel, PLAIN_GROUND), `the corner is ${seen.pixel.join(',')}`).toBe(true);
    expect(document.text).toContain('Map as shown to Administrator on ');
  } finally {
    await tryAsPerson(page, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
  }
});
