// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test, type Page } from '@playwright/test';
import { gotoRoute, login } from './helpers.ts';

/**
 * The document surfaces, driven end to end in a real browser: an upload becoming a document,
 * the document's own page, the page strip a portable document is read through, the discussion
 * under it, filing it in a cabinet, and finding it again by a word written inside it.
 *
 * Everything here rests on one fixture, `e2e-report.pdf` — two pages written by an office
 * suite, whose second page carries a word that appears nowhere else in the archive. That word
 * is what makes the content search assertion mean something: a hit on it can only have come
 * from text the server read out of the file itself, and the page it opens at can only have
 * come from knowing which page the word was on.
 *
 * Reading the text is asynchronous, so every assertion that depends on it is written as a
 * poll with a generous timeout rather than a wait for a fixed time.
 */

// One demo cave holds the fixture for every test here, so they take turns rather than
// uploading three copies of the same report over each other.
//
// These are the longest flows in the suite: each signs in, uploads a file, and then waits for
// work the server does after the upload — reading the words out of it and indexing them — none
// of which the browser can hurry. Under the default bound they pass alone and fail when the
// rest of the suite is competing for the same server, which is the worst of both, so the bound
// is raised here rather than left to decide the result by how busy the machine is.
test.describe.configure({ mode: 'serial', timeout: 150_000 });

const fixture = 'e2e/fixtures/e2e-report.pdf';
/** On the fixture's second page and nowhere else in the demo archive. */
const rareWord = 'hidrogeologiedemo';

/**
 * Uploads a fixture to the demo cave and opens the document it became, returning that
 * document's id.
 *
 * Which viewer a document is read through is decided by the server from the file itself, so
 * the only way to drive a viewer in a browser is to put a file of that kind in and follow it:
 * the paged strip comes from the report, the words from a set of notes, the player from a
 * recording. Hence the parameter rather than three copies of this.
 */
async function uploadFixture(page: Page, path: string, fileName: string): Promise<string> {
  // An earlier run that stopped half way would otherwise leave a copy behind, and a second
  // one would make every assertion below ambiguous.
  await removeFixture(page, fileName);

  const gallery = page.locator('.ant-card', { hasText: 'Photos & documents' });
  await gallery.locator('input[type=file]').setInputFiles(path);
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 30_000 });

  // The upload became a document, and the row that carries it offers a way in that is not a
  // download — which was the whole point of the document page existing.
  const row = page.locator('.ant-list-item', { hasText: fileName }).first();
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByRole('button', { name: 'Open the document' }).click();

  await page.waitForURL(/\/documents\/[0-9a-f-]{36}/);
  await expect(page.getByRole('heading', { level: 3, name: fileName })).toBeVisible({
    timeout: 15_000,
  });
  return page.url().split('/documents/')[1].split('?')[0];
}

/** Removes an uploaded fixture from the cave, so a re-run starts where this one did. */
async function removeFixture(page: Page, fileName: string) {
  await gotoRoute(page, '/caves');
  await page.getByText('Peștera Demo Mare').click();
  await expect(page.getByText('Photos & documents')).toBeVisible({ timeout: 15_000 });
  const rows = page.locator('.ant-list-item').filter({ hasText: fileName });
  for (let remaining = await rows.count(); remaining > 0; remaining--) {
    await rows.first().getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
}

const uploadReport = (page: Page) => uploadFixture(page, fixture, 'e2e-report.pdf');
const removeReport = (page: Page) => removeFixture(page, 'e2e-report.pdf');

test('an upload becomes a readable document, and its text is read and discussed', async ({
  page,
}) => {
  await login(page);
  await uploadReport(page);

  // What the document says about itself, including the one line that is not metadata but a
  // sentence to a reader: what came of trying to read the words in it.
  await expect(page.getByText('application/pdf')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('The text has been read.')).toBeVisible({ timeout: 60_000 });

  // The document is rendered here rather than handed over: the page strip knows how many
  // pages there are, which it can only have got from the file.
  const viewer = page.locator('.ant-card', { hasText: 'The document' });
  await expect(viewer.getByText('Page 1 of 2')).toBeVisible({ timeout: 30_000 });
  await viewer.getByRole('button', { name: 'Next page' }).click();
  await expect(viewer.getByText('Page 2 of 2')).toBeVisible({ timeout: 15_000 });

  // A remark posts and reads back, under the document it is about.
  const remark = `E2E remark ${Date.now()}`;
  const discussion = page.locator('.ant-card', { hasText: 'Discussion' });
  const draft = discussion.getByPlaceholder('Write a remark about this document');
  await draft.fill(remark);
  await discussion.getByRole('button', { name: 'Post' }).click();

  // The box empties only when the server has taken the remark, so this is waited for before
  // the remark itself: a draft still sitting in the box carries the same words, and asserting
  // on those would pass whether anything was posted or not.
  await expect(draft).toHaveValue('', { timeout: 15_000 });
  await expect(discussion.getByText(remark)).toBeVisible({ timeout: 15_000 });

  // And it survives a reload, so what is on screen is what the server kept. A reload here is
  // a full sign-in round trip — the session is held in memory — so the page is waited for
  // before the remark on it.
  await page.reload();
  await expect(page.getByRole('heading', { level: 3, name: 'e2e-report.pdf' })).toBeVisible({
    timeout: 60_000,
  });
  await expect(page.getByText(remark)).toBeVisible({ timeout: 30_000 });

  await removeReport(page);
});

/**
 * The other two viewers, driven for the reason the paged one is: under jsdom a viewer is a
 * tree of elements, and everything that decides whether it actually shows anything — the
 * bundle splitting the chunk out, the delivery address the browser has to fetch, the element
 * the browser has to know what to do with — only exists in a browser.
 */
test('a text document is read on the page, and a recording is playable there', async ({
  page,
}) => {
  await login(page);
  await uploadFixture(page, 'e2e/fixtures/e2e-notes.txt', 'e2e-notes.txt');

  // The words of the file are on the page, fetched from the server rather than laid out from
  // anything the upload response happened to carry.
  const viewer = page.locator('.ant-card', { hasText: 'The document' });
  await expect(viewer.getByText('textviewerdemo')).toBeVisible({ timeout: 30_000 });
  await expect(viewer.getByText('Galeria principală')).toBeVisible({ timeout: 15_000 });

  await removeFixture(page, 'e2e-notes.txt');

  // A recording is not laid out at all: it is handed to the browser's own player, pointed at
  // the delivery route. What is asserted is that the player is there and sourced — playing it
  // is the browser's job and asserting on audio output would test the machine, not this.
  await uploadFixture(page, 'e2e/fixtures/e2e-clip.wav', 'e2e-clip.wav');
  const player = page.locator('.ant-card', { hasText: 'The document' }).locator('audio');
  await expect(player).toBeAttached({ timeout: 30_000 });
  await expect(player).toHaveAttribute('src', /.+/);
  // The address is a real one: the player reaches metadata for it, which it can only do by
  // fetching bytes the server served.
  await expect
    .poll(() => player.evaluate((el: HTMLAudioElement) => el.readyState), { timeout: 30_000 })
    .toBeGreaterThan(0);

  await removeFixture(page, 'e2e-clip.wav');
});

test('a content search finds the document by a word inside it and opens it there', async ({
  page,
}) => {
  await login(page);
  await uploadReport(page);

  // Nothing can be found until the words have been read, and the page says when that is.
  await expect(page.getByText('The text has been read.')).toBeVisible({ timeout: 60_000 });

  // The search box lives on the workspace; the hit is a document, not a place or a cave.
  await page.goto('/');
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 20_000 });
  await page.getByPlaceholder(/Search/i).first().fill(rareWord);

  const hit = page.locator('.ant-select-item-option').filter({ hasText: 'e2e-report' }).first();
  await expect(hit).toBeVisible({ timeout: 30_000 });
  await hit.click();

  // The hit carries the page it matched on, and the document opens there rather than at the
  // beginning — the one thing a download could never do with a search result.
  // Polled rather than waited for as a navigation: this is the router pushing a URL, and no
  // page load happens for Playwright to wait on.
  // The document is identified by what it is rather than by the id this run happened to
  // create: taking a report out of a cave leaves the document in the archive, so an earlier
  // run's copy is still findable and either is a correct answer to the search. What must hold
  // either way is that the hit knew the page and said so.
  await expect
    .poll(() => new URL(page.url()).search, { timeout: 30_000 })
    .toBe('?page=2');
  expect(new URL(page.url()).pathname).toMatch(/^\/documents\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { level: 3, name: 'e2e-report.pdf' })).toBeVisible({
    timeout: 30_000,
  });
  await expect(page.locator('.ant-card', { hasText: 'The document' }).getByText('Page 2 of 2'))
    .toBeVisible({ timeout: 30_000 });

  await removeReport(page);
});

/**
 * A portable document is laid out in the browser, so its words are words rather than part of
 * a picture of the page.
 *
 * This is the one property nothing below a browser can check. Under jsdom there is no canvas,
 * no worker and no layout, so a test there proves only that some elements were created; what
 * decides whether a reader can drag across a sentence is where the runs land on the screen,
 * which only a real engine produces. So the assertions are: the runs exist, they carry the
 * words the file carries, they cover the page rather than collapsing into its corner, and a
 * selection over them yields the sentence and not the whole document.
 */
test('the words of a portable document can be selected on the page', async ({ page }) => {
  await login(page);
  await uploadReport(page);

  const viewer = page.locator('.ant-card', { hasText: 'The document' });
  await expect(viewer.getByText('Page 1 of 2')).toBeVisible({ timeout: 60_000 });

  const layer = page.getByTestId('pdf-text-layer');
  await expect(layer).toBeVisible({ timeout: 30_000 });

  // The runs are laid out over the drawing, at the same size, and each is sized by the scale
  // the page was drawn at. A layer that never received that scale still contains every word:
  // the runs collapse to zero-height nothings in the corner, the drawing underneath is
  // unaffected, and the page looks perfectly correct while nothing on it can be selected.
  const geometry = await layer.evaluate((el) => {
    const canvas = el.parentElement?.querySelector('canvas');
    const runs = Array.from(el.querySelectorAll('span'));
    const box = runs.map((run) => run.getBoundingClientRect());
    return {
      runs: runs.length,
      words: runs.map((run) => run.textContent ?? '').join(' ').trim().length,
      layer: [el.clientWidth, el.clientHeight],
      canvas: canvas ? [canvas.clientWidth, canvas.clientHeight] : null,
      widestRun: Math.max(0, ...box.map((r) => r.width)),
      tallestRun: Math.max(0, ...box.map((r) => r.height)),
    };
  });
  expect(geometry.runs).toBeGreaterThan(0);
  expect(geometry.words).toBeGreaterThan(40);
  expect(geometry.canvas).toEqual(geometry.layer);
  // Sized against the page, not collapsed: a run has to be tall enough to put a caret in and
  // wide enough to cover a good part of the line it describes.
  expect(geometry.tallestRun).toBeGreaterThan(5);
  expect(geometry.widestRun).toBeGreaterThan(geometry.layer[0] * 0.2);

  // And the runs carry the file's own words: selecting one gives back text, not an empty
  // string, which is what a layer of positioned but wordless elements would give.
  const selected = await layer.evaluate((el) => {
    const run = Array.from(el.querySelectorAll('span')).find((s) => (s.textContent ?? '').trim().length > 8);
    if (!run) {
      return null;
    }
    const range = document.createRange();
    range.selectNodeContents(run);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
    return selection.toString();
  });
  expect(selected?.trim().length ?? 0).toBeGreaterThan(8);

  await removeReport(page);
});

test('filing a document in a cabinet says so on both sides', async ({ page }) => {
  const cabinetName = `E2E Cabinet ${Date.now()}`;
  await login(page);
  const documentId = await uploadReport(page);

  // A cabinet to file it in.
  await gotoRoute(page, '/cabinets');
  await page.getByRole('button', { name: 'New' }).click();
  await page.getByLabel('Name').fill(cabinetName);
  await page.getByRole('button', { name: 'OK' }).click();
  // The name lands in the tree, the breadcrumb and the heading at once, so the tree row is
  // named rather than the text.
  const cabinetRow = page.getByRole('treeitem').filter({ hasText: cabinetName });
  await expect(cabinetRow).toBeVisible({ timeout: 15_000 });

  // Filing happens on the document, through its properties.
  await page.goto(`/documents/${documentId}`);
  await expect(page.getByRole('heading', { level: 3, name: 'e2e-report.pdf' })).toBeVisible({
    timeout: 60_000,
  });
  await page.getByRole('button', { name: 'Document properties' }).first().click();
  // The filing control is the select under the "Filed in" label in the properties popover;
  // named that way rather than by its placeholder, which is only there while the document is
  // filed nowhere.
  const popover = page.locator('.ant-popover:visible');
  const filing = popover
    .locator('div')
    .filter({ has: page.getByText('Filed in', { exact: true }) })
    .locator('.ant-select')
    .last();
  await filing.click();
  await page.locator('.ant-select-item-option').filter({ hasText: cabinetName }).first().click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The document now names where it is filed…
  await page.reload();
  await expect(page.getByRole('heading', { level: 3, name: 'e2e-report.pdf' })).toBeVisible({
    timeout: 60_000,
  });
  await expect(page.getByText(cabinetName)).toBeVisible({ timeout: 30_000 });

  // …and the cabinet names what is filed in it, which is the half an access rule scoped to a
  // cabinet actually reaches.
  await gotoRoute(page, '/cabinets');
  await page.getByRole('treeitem').filter({ hasText: cabinetName }).click();
  await expect(page.getByText('e2e-report.pdf')).toBeVisible({ timeout: 15_000 });

  // Cleanup. Taking the report out of the cave leaves the document filed, and a cabinet that
  // still holds something refuses to go — which is itself the rule, so the unfiling is done
  // here rather than worked around.
  await removeReport(page);
  await gotoRoute(page, '/cabinets');
  await page.getByRole('treeitem').filter({ hasText: cabinetName }).click();
  const filed = page.getByRole('row').filter({ hasText: 'e2e-report.pdf' });
  await expect(filed).toHaveCount(1, { timeout: 15_000 });
  await filed.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(filed).toHaveCount(0, { timeout: 15_000 });

  await page.getByRole('button', { name: 'delete' }).first().click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByRole('treeitem').filter({ hasText: cabinetName }))
    .toHaveCount(0, { timeout: 15_000 });
});
