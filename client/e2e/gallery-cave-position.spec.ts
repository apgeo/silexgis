// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { uniquePng } from './png.ts';

/**
 * The gallery's editor asks two questions of pictures that it could not ask before: which cave a
 * picture hangs on, picked with the object selector, and whether its position is within a distance
 * of a typed point.
 *
 * What only a browser can show is the road: that both fields are offered for photographs, that the
 * cave is picked through the selector and written as an identity, that the point is typed and sent
 * as a point, that the server accepts what the editor writes and the grid narrows on it. Which
 * caves a reader may ask about, and which positions they are shown, is the server's to prove, and
 * its tests prove it — a cave the reader may not place exactly is refused like a missing one, and a
 * picture whose position they are not shown answers no position condition.
 *
 * One picture is generated and hung on the demonstration cave "Peștera Demo Mică"; the other is
 * the suite's invented geotagged JPEG, made unique to the run by bytes after its end, so the
 * archive never refuses it as a repeat.
 */
test.describe.configure({ timeout: 180_000 });

const DEMO_CAVE = 'Peștera Demo Mică';

/** Signs in and hands back the bearer header the application itself sends. */
async function signedIn(page: Page): Promise<string> {
  let authorization: string | undefined;
  page.on('request', (request) => {
    const header = request.headers()['authorization'];
    if (header && request.url().includes('/api/v1/')) {
      authorization = header;
    }
  });
  await login(page);
  await expect.poll(() => authorization, { timeout: 30_000 }).toBeTruthy();
  return authorization!;
}

async function upload(page: Page, files: { name: string; mimeType: string; buffer: Buffer }[]) {
  await gotoRoute(page, '/cabinets');
  const inbox = page.locator('.ant-tree-title').filter({ hasText: 'Not filed' }).first();
  await expect(inbox).toBeVisible({ timeout: 45_000 });
  await inbox.click();
  const uploadButton = page.getByRole('button', { name: 'Upload', exact: true });
  await expect(uploadButton).toBeVisible({ timeout: 30_000 });
  await uploadButton.click();
  const drawer = page.getByRole('dialog').last();
  await drawer.getByTestId('upload-files').locator('input[type=file]').setInputFiles(files);
  await expect(drawer.getByText(`${files.length} of ${files.length} stored`, { exact: false }))
    .toBeVisible({ timeout: 90_000 });
  await drawer.getByTestId('upload-done').click();
}

interface Listed {
  fileId: string;
  originalName: string;
  position: { geom: { coordinates: number[] } | null } | null;
}

async function listed(page: Page, authorization: string, run: string): Promise<Listed[]> {
  const response = await page.request.get(`/api/v1/photos?search=${run}&pageSize=50`, { headers: { authorization } });
  expect(response.ok(), await response.text()).toBe(true);
  return ((await response.json()) as { items: Listed[] }).items;
}

async function demoCaveId(page: Page, authorization: string): Promise<string> {
  const response = await page.request.post('/api/v1/filters/query', {
    headers: { authorization },
    data: {
      document: {
        version: 1,
        sort: 'updated',
        descending: true,
        scope: [{ world: 'feature', where: { node: 'allOf', of: [
          { node: 'condition', field: 'kind', op: 'equals', values: [{ type: 'id', value: 'cave' }] },
          { node: 'condition', field: 'name', op: 'equals', values: [{ type: 'text', value: DEMO_CAVE }] },
        ] } }],
      },
      pageSize: 5,
    },
  });
  expect(response.ok(), await response.text()).toBe(true);
  const body = (await response.json()) as { worlds: { hits: { id: string }[] }[] };
  const hit = body.worlds[0].hits[0];
  expect(hit, 'the demonstration data holds no cave to hang a picture on').toBeTruthy();
  return hit.id;
}

async function choose(page: Page, testId: string, option: RegExp, search?: string) {
  await page.getByTestId(testId).click();
  if (search !== undefined) {
    await page.keyboard.type(search);
  }
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: option }).first().click();
}

async function openGallery(page: Page, run: string) {
  await gotoRoute(page, '/gallery');
  await expect(page.getByRole('heading', { name: 'Photographs' })).toBeVisible({ timeout: 45_000 });
  const box = page.getByPlaceholder('Search captions and places');
  await box.fill(run);
  await box.press('Enter');
  await expect(page).toHaveURL(/[?&]filter=/);
  await expect(page.getByTestId('photo-tile')).toHaveCount(2, { timeout: 30_000 });
}

/** Opens the editor over the gallery's search and starts a row of this field beside it. */
async function startRow(page: Page, field: RegExp, search: string, op: RegExp) {
  await page.getByTestId('gallery-advanced').click();
  await expect(page.getByTestId('filter-editor')).toBeVisible();
  await page.getByTestId('filter-editor-add-row-root').click();
  await choose(page, 'filter-editor-field-1', field, search);
  await choose(page, 'filter-editor-op-1', op);
}

async function applied(page: Page, name: string) {
  await expect(page.getByTestId('filter-editor-count')).toHaveText(/Matches: 1\b/, { timeout: 30_000 });
  await page.getByTestId('filter-editor-apply').click();
  await expect(page.getByTestId('filter-editor')).toHaveCount(0);
  const tiles = page.getByTestId('photo-tile');
  await expect(tiles).toHaveCount(1, { timeout: 30_000 });
  await expect(tiles.locator('img')).toHaveAttribute('alt', name);
  const token = new URL(page.url()).searchParams.get('filter');
  return Buffer.from(token!, 'base64url').toString('utf8');
}

test('the gallery narrows by the cave a picture hangs on and by a distance from its position', async ({ page }) => {
  test.slow();
  const run = randomUUID().slice(0, 8);
  const ofCave = `kqf-${run}-cave.png`;
  const fixed = `kqf-${run}-fixed.jpg`;

  const authorization = await signedIn(page);
  const jpeg = Buffer.concat([readFileSync('e2e/fixtures/e2e-spring.jpg'), Buffer.from(`run ${run}`)]);
  await upload(page, [
    { name: ofCave, mimeType: 'image/png', buffer: uniquePng() },
    { name: fixed, mimeType: 'image/jpeg', buffer: jpeg },
  ]);

  // The geotagged picture's position, as the gallery would print it beside the picture.
  let pictures: Listed[] = [];
  await expect.poll(async () => {
    pictures = await listed(page, authorization, run);
    return pictures.find((p) => p.originalName === fixed)?.position?.geom?.coordinates.length ?? 0;
  }, { timeout: 60_000 }).toBe(2);
  const [lon, lat] = pictures.find((p) => p.originalName === fixed)!.position!.geom!.coordinates;

  // Hung on the demonstration cave.
  const cave = await demoCaveId(page, authorization);
  const attached = await page.request.post('/api/v1/attachments', {
    headers: { authorization },
    data: { fileId: pictures.find((p) => p.originalName === ofCave)!.fileId, entityType: 'feature', entityId: cave, role: 'photoInterior', caption: null, sortOrder: 0 },
  });
  expect(attached.ok(), await attached.text()).toBe(true);

  // ---- the cave, picked with the object selector ----
  await openGallery(page, run);
  await startRow(page, /^Cave$/, 'Cave', /^is$/);
  await page.getByTestId('filter-editor-value-1').click();
  await page.getByTestId('filter-editor-value-1-input').fill('Demo Mică');
  const row = page.getByTestId('filter-editor-value-1-row').filter({ hasText: DEMO_CAVE });
  await expect(row.first()).toBeVisible({ timeout: 15_000 });
  await row.first().click();
  await expect(page.getByTestId('filter-editor-value-1')).toContainText(DEMO_CAVE);
  const byCave = await applied(page, ofCave);
  expect(byCave).toContain('"field":"cave"');
  expect(byCave).toContain(`"value":"${cave}"`);

  // ---- within 200 m of a typed point, on a fresh visit ----
  await openGallery(page, run);
  await startRow(page, /^Position$/, 'Position', /^within$/);
  await page.getByTestId('filter-editor-value-1-radius').fill('200');
  await choose(page, 'filter-editor-value-1-anchor-kind', /^a point$/);
  await page.getByTestId('filter-editor-value-1-lon').fill(String(lon));
  await page.getByTestId('filter-editor-value-1-lat').fill(String(lat));
  const byPosition = await applied(page, fixed);
  expect(byPosition).toContain('"field":"position"');
  expect(byPosition).toContain('"type":"point"');
});
