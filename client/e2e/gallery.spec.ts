// SPDX-License-Identifier: AGPL-3.0-or-later
import { randomUUID } from 'node:crypto';
import { crc32, deflateSync } from 'node:zlib';
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The gallery, the viewer, crediting a photograph and arranging an album — in a real browser.
 *
 * <p>
 * What only a browser can answer here is whether the surfaces join up. Each of these steps has
 * an API test behind it already; what none of those can see is a viewer that opens on the wrong
 * picture, a credit form that saves and leaves the grid showing the old caption, or an album
 * whose order changes on the server and not on screen. Those are the defects this file exists
 * for, and every one of them is invisible to a request-level test.
 * </p>
 * <p>
 * Every photograph is generated with pixels unique to the run. The store warns about content it
 * already holds, so fixtures with fixed bytes would be refused on the second run of the suite
 * and these flows would pass or fail by how recently somebody had run them.
 * </p>
 */
test.describe.configure({ timeout: 120_000 });

/** A PNG chunk: length, type, payload, CRC over type+payload. */
function chunk(type: string, body: Buffer) {
  const head = Buffer.alloc(4);
  head.writeUInt32BE(body.length);
  const typed = Buffer.concat([Buffer.from(type, 'ascii'), body]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(typed));
  return Buffer.concat([head, typed, crc]);
}

/**
 * A real PNG with random pixels.
 *
 * Generated rather than kept as a fixture because the bytes have to differ every run: two
 * uploads of identical content are a duplicate, and the second is refused until somebody
 * answers a dialog this flow is not about.
 */
function png(size = 24) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // bit depth
  header[9] = 2; // truecolour RGB

  // One filter byte per scanline, then the pixels — the whole point being that they are noise.
  const raw = Buffer.concat(
    Array.from({ length: size }, () =>
      Buffer.concat([Buffer.from([0]), Buffer.from(randomBytes(size * 3))])),
  );

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

function randomBytes(count: number) {
  return Uint8Array.from({ length: count }, () => Math.floor(Math.random() * 256));
}

/** Photographs unique to this run, in their names as well as their pixels. */
function photographs(count: number) {
  const run = randomUUID().slice(0, 8);
  const names = Array.from({ length: count }, (_, i) => `gal-${run}-${i + 1}.png`);
  return {
    run,
    names,
    files: names.map((name) => ({ name, mimeType: 'image/png', buffer: png() })),
  };
}

/**
 * Uploads photographs through the drawer and waits for the archive to have taken them.
 *
 * They are dropped with no shelf chosen, so they wait in the inbox — the gallery lists every
 * photograph whatever it is filed under, and filing is a different flow with its own cover.
 */
async function upload(page: Page, files: { name: string; mimeType: string; buffer: Buffer }[]) {
  await gotoRoute(page, '/cabinets');
  // Reaching a route by address is a full sign-in round trip, and the address matches before
  // the round trip has finished. Waiting for something only this page draws is what makes the
  // clicks below act on the page rather than on a redirect.
  const inbox = page.locator('.ant-tree-title').filter({ hasText: 'Not filed' }).first();
  await expect(inbox).toBeVisible({ timeout: 45_000 });

  // Dropping is always into somewhere, so the control appears once somewhere is chosen. The
  // inbox is the choice here: these photographs are not what the filing flow is about.
  await inbox.click();
  const uploadButton = page.getByRole('button', { name: /Upload$/ });
  await expect(uploadButton).toBeVisible({ timeout: 30_000 });
  await uploadButton.click();
  const drawer = page.getByRole('dialog').last();
  await drawer.getByTestId('upload-files').locator('input[type=file]').setInputFiles(files);
  await expect(drawer.getByText(`${files.length} of ${files.length} stored`, { exact: false }))
    .toBeVisible({ timeout: 90_000 });
  await drawer.getByTestId('upload-done').click();
}

/** The gallery, filtered to one run's photographs so other runs' pictures cannot drift in. */
async function openGallery(page: Page, run: string) {
  await page.goto(`/gallery?search=${run}`);
  await page.waitForURL((url) => url.pathname === '/gallery', { timeout: 60_000 });
  // The heading before the grid: the address matches while the sign-in round trip is still in
  // flight, so waiting on the grid alone would look for it on a redirect page.
  await expect(page.getByRole('heading', { name: 'Photographs' })).toBeVisible({ timeout: 45_000 });
  await expect(page.getByTestId('photo-grid')).toBeVisible({ timeout: 30_000 });
}

/** The album list, settled past the sign-in round trip. */
async function openAlbums(page: Page) {
  await gotoRoute(page, '/albums');
  await expect(page.getByRole('heading', { name: 'Albums' })).toBeVisible({ timeout: 45_000 });
}

/** The document ids of the tiles, in the order they are drawn. */
async function tileOrder(page: Page) {
  return page.getByTestId('photo-tile').evaluateAll((tiles) =>
    tiles.map((tile) => tile.getAttribute('data-document-id')));
}

test('the gallery shows what was uploaded and the viewer walks through it', async ({ page }) => {
  await login(page);
  const { run, names, files } = photographs(3);
  await upload(page, files);

  await openGallery(page, run);
  const tiles = page.getByTestId('photo-tile');
  await expect(tiles).toHaveCount(3, { timeout: 30_000 });

  // Every tile is a rendering. The gallery must never load an upload: a page of sixty
  // photographs at full size is hundreds of megabytes, and for a picture whose subject the
  // reader may not place the upload is not theirs to have at all.
  const sources = await tiles.locator('img').evaluateAll((images) =>
    images.map((image) => image.getAttribute('src') ?? ''));
  expect(sources.every((src) => src.includes('/thumbnail?'))).toBe(true);

  // Read from the grid rather than assumed: the gallery is newest first, so which upload is
  // leftmost is a fact about the listing's order and not about this test.
  const shown = await tiles.locator('img').evaluateAll((images) =>
    images.map((image) => image.getAttribute('alt') ?? ''));
  expect([...shown].sort()).toEqual([...names].sort());

  await tiles.first().getByRole('button', { name: shown[0] }).click();
  const viewer = page.getByTestId('lightbox');
  await expect(viewer).toBeVisible();
  await expect(viewer.getByAltText(shown[0])).toBeVisible();

  // Arrows move and wrap, because a gallery is a loop to somebody flicking through it.
  await page.keyboard.press('ArrowRight');
  await expect(viewer.getByAltText(shown[1])).toBeVisible();
  await page.keyboard.press('ArrowLeft');
  await expect(viewer.getByAltText(shown[0])).toBeVisible();
  await page.keyboard.press('ArrowLeft');
  await expect(viewer.getByAltText(shown[2])).toBeVisible();

  await page.keyboard.press('Escape');
  await expect(viewer).toBeHidden();
});

test('a photograph is credited from the viewer and the gallery says so afterwards', async ({
  page,
}) => {
  await login(page);
  const { run, names, files } = photographs(1);
  await upload(page, files);

  await openGallery(page, run);
  await page.getByTestId('photo-tile').first().getByRole('button', { name: names[0] }).click();
  await page.getByTestId('lightbox').getByRole('button', { name: 'Credit and licence' }).click();

  const drawer = page.getByRole('dialog').filter({ hasText: 'Credit and licence' });
  await expect(drawer).toBeVisible({ timeout: 15_000 });

  const caption = `Sala Mare ${run}`;
  await drawer.getByLabel('Caption').fill(caption);
  await drawer.getByLabel('Place').fill(`Padiș ${run}`);

  // A closed vocabulary rather than free text, because the field exists to be acted on: "may
  // this go in the bulletin" has to be answerable by looking.
  await drawer.getByLabel('Licence').click();
  // By the option's own title rather than by its text: the list also holds "CC BY-NC-SA", and a
  // text match would be ambiguous between the option and the node inside it.
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option[title="CC BY-SA"]')
    .click();

  await drawer.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The grid re-reads rather than keeping what it drew: a caption saved on the server and
  // absent from the tile beside it is the defect this whole flow is here for.
  await expect(page.getByTestId('photo-tile').first()).toContainText(caption, { timeout: 15_000 });

  // The viewer is still open behind the form — closing the form does not close what it was
  // opened from — so the facts panel is reached from there rather than by opening it again.
  await expect(drawer).toBeHidden();
  await page.getByTestId('lightbox').getByRole('button', { name: 'About this photograph' }).click();
  const facts = page.getByRole('dialog').filter({ hasText: 'About this photograph' });

  // The whole reason this flow exists: the listing nests these under a credit and the viewer
  // reads them flat, so the panel showed nothing at all until the two shapes were joined up.
  await expect(facts).toContainText('CC BY-SA');
  await expect(facts).toContainText(`Padiș ${run}`);
});

test('photographs are gathered into an album and arranged in it', async ({ page }) => {
  await login(page);
  const { run, files } = photographs(3);
  await upload(page, files);

  // ---- an album to put them in
  const albumTitle = `E2E album ${run}`;
  await openAlbums(page);
  await page.getByRole('button', { name: /New album$/ }).click();

  const editor = page.getByRole('dialog').filter({ hasText: 'New album' });
  await editor.getByLabel('Title').fill(albumTitle);
  await editor.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // ---- the pictures, chosen in the gallery and added in one go
  await openGallery(page, run);
  await expect(page.getByTestId('photo-tile')).toHaveCount(3, { timeout: 30_000 });
  for (const mark of await page.getByTestId('photo-select').all()) {
    await mark.click();
  }

  const bulk = page.getByTestId('photo-bulk-bar');
  await expect(bulk).toContainText('3 selected');
  await bulk.locator('.ant-select').click();
  await page.locator(`.ant-select-dropdown:visible .ant-select-item-option[title="${albumTitle}"]`)
    .click();
  await expect(page.getByText('3 added.')).toBeVisible({ timeout: 20_000 });

  // ---- the album, in its own order
  await openAlbums(page);
  await page.getByRole('link', { name: albumTitle }).click();
  await page.waitForURL(/\/albums\/[0-9a-f-]{36}$/, { timeout: 30_000 });
  await expect(page.getByTestId('photo-tile')).toHaveCount(3, { timeout: 30_000 });

  const before = await tileOrder(page);

  // The keyboard path, and not merely for completeness: dragging is a mouse gesture and an
  // album of two hundred has to be arrangeable by somebody who is not using one.
  await page.getByTestId('photo-tile').first().getByRole('button', { name: 'Move later' }).click();
  await expect
    .poll(() => tileOrder(page), { timeout: 20_000 })
    .toEqual([before[1], before[0], before[2]]);

  // And the drag itself. Playwright's mouse does not raise the browser's own drag events, so
  // they are dispatched with one data transfer shared between them — which is what a browser
  // does, and what the grid's handlers actually read.
  await dragTile(page.getByTestId('photo-tile').nth(2), page.getByTestId('photo-tile').first());
  await expect
    .poll(() => tileOrder(page), { timeout: 20_000 })
    .toEqual([before[2], before[1], before[0]]);

  // ---- a cover, which is how an album is recognised in a list of them
  await page.getByTestId('photo-tile').first().getByRole('button', { name: 'Use as the cover' })
    .click();

  await openAlbums(page);
  const card = page.locator('.ant-card').filter({ hasText: albumTitle });
  await expect(card.locator('img')).toBeVisible({ timeout: 20_000 });

  // Ordering survives the round trip rather than living in the page that did it.
  await page.getByRole('link', { name: albumTitle }).click();
  await page.waitForURL(/\/albums\/[0-9a-f-]{36}$/, { timeout: 30_000 });
  await expect.poll(() => tileOrder(page), { timeout: 20_000 })
    .toEqual([before[2], before[1], before[0]]);

  // ---- and taking one back out of the album leaves the photograph itself alone
  const removed = before[2];
  await page.getByTestId('photo-tile').first().getByRole('button', { name: 'Take out of the album' })
    .click();
  await expect(page.getByText('Taken out of the album.')).toBeVisible({ timeout: 20_000 });
  await expect(page.getByTestId('photo-tile')).toHaveCount(2, { timeout: 20_000 });

  // Membership is not ownership: an album is an arrangement of photographs, so leaving one is
  // not a way to delete it. Named by its own id rather than counted, because a count of three
  // would also be satisfied by a different picture having come back.
  await openGallery(page, run);
  await expect(page.getByTestId('photo-tile')).toHaveCount(3, { timeout: 30_000 });
  await expect(page.locator(`[data-testid="photo-tile"][data-document-id="${removed}"]`))
    .toHaveCount(1);
});

/**
 * Drags one tile onto another.
 *
 * The browser's drag protocol is a sequence of events carrying one data transfer between them,
 * and Playwright's mouse does not raise it — so it is raised here. This drives the grid's real
 * handlers rather than a stand-in for them; where the picture lands is decided by rules with
 * their own tests.
 */
async function dragTile(source: Locator, target: Locator) {
  const to = await target.elementHandle();
  if (!to) {
    throw new Error('nothing to drop onto');
  }

  await source.evaluate((from, onto) => {
    const transfer = new DataTransfer();
    from.dispatchEvent(new DragEvent('dragstart', { dataTransfer: transfer, bubbles: true }));
    onto.dispatchEvent(new DragEvent('dragover', { dataTransfer: transfer, bubbles: true }));
    onto.dispatchEvent(new DragEvent('drop', { dataTransfer: transfer, bubbles: true }));
    from.dispatchEvent(new DragEvent('dragend', { dataTransfer: transfer, bubbles: true }));
  }, to);
}
