// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Locator, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { deleteFeature, login } from './helpers.ts';

/**
 * Links between things, driven the way a caver drives them: from the page of whatever they
 * are standing on, from the map dock, from a trip they just wrote up, and from the admin
 * page where the wording itself is authored.
 *
 * Every flow here brings its own subject. A run stamps the names of everything it creates,
 * hangs its links off those rather than off seeded demo rows, and takes them down again at
 * the end — so nothing depends on the database being empty, two runs never contend for the
 * same row, and a leftover from an aborted run cannot make a later run pass or fail on
 * something it did not do. The seeded public features are used only as the *other* end of a
 * link, which reads them and never edits them.
 */

/**
 * Waits for the confirmation an action was accepted, and for the slate to be clean again.
 *
 * Toasts stack and stay up for a few seconds, so two accepted actions close together put two
 * identical notices on screen at once — and a wait for "the" notice would then have to choose
 * between one that belongs to this action and one left over from the last. Waiting for the
 * screen to go quiet afterwards is what makes the next wait unambiguous rather than lucky:
 * every flow below reaches this point with no notice showing.
 */
async function accepted(page: Page, word: 'Saved.' | 'Deleted.') {
  await expect(page.getByText(word).first()).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.ant-message-notice')).toHaveCount(0, { timeout: 15_000 });
}

/**
 * The registry row for one feature, found by narrowing the list down to it.
 *
 * The registry shows a page at a time, so whether a row is on screen depends on how many
 * other features sort ahead of it — which is a fact about the rest of the database rather
 * than about this feature. Typing the name in first makes the row's presence mean what this
 * suite needs it to mean, however busy the installation is.
 */
async function featureRow(page: Page, name: string | RegExp): Promise<Locator> {
  await page.goto('/features');
  await page.getByPlaceholder('Search by name').fill(typeof name === 'string' ? name : name.source);
  const row = page.getByRole('row', { name: typeof name === 'string' ? new RegExp(name) : name });
  await expect(row).toBeVisible({ timeout: 15_000 });
  return row;
}

/** Opens a feature's detail page from the registry and hands back its address. */
async function openFeature(page: Page, name: string): Promise<string> {
  const row = await featureRow(page, name);
  await row.getByRole('cell', { name }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
  return page.url();
}

/**
 * Draws and saves a point feature on the workspace map, so a test has an anchor of its own
 * to link from. Deliberately not selected by clicking the map afterwards — every flow below
 * reaches it by name through the registry, which no other run's leftovers can shadow.
 *
 * The spot it draws on is its own, kept clear of the one flows that select by map click use:
 * a point left stacked under somebody else's pixel is a point somebody else has to clear away
 * before they can trust what their click selected.
 */
async function createPointFeature(page: Page, name: string) {
  await page.goto('/');
  const toolbar = page.locator('.map-edit-overlay');
  await expect(toolbar).toBeVisible();
  await toolbar.getByRole('button', { name: /Feature type/ }).click();
  await page.getByRole('button', { name: 'Sinkhole / Doline' }).click();

  await toolbar.getByRole('button', { name: 'edit' }).click();
  await page.locator('.map-canvas').click({ position: { x: 180, y: 380 } });
  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible();
  await modal.getByLabel('Name').fill(name);
  await modal.getByRole('button', { name: 'OK' }).click();

  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
  await toolbar.getByRole('button', { name: /Save/ }).click();
  await accepted(page, 'Saved.');
  await reloaded;
}

/**
 * The resource-links section on whatever page is open. Addressed by its own title rather
 * than by position: a feature page carries the older feature-to-feature card as well, and
 * "which card is last" is not a fact worth depending on.
 */
function linksCard(page: Page): Locator {
  return page.locator('.ant-card').filter({ has: page.getByText(/^Linked items \(\d+\)$/) });
}

/**
 * Opens an antd select and picks the option with this label (options render in a portal).
 *
 * The wait at the end is the load-bearing part. An option list closes over an animation, and
 * while it is closing it still covers whatever sits under it — so the next control this flow
 * reaches for is invisible to a click that arrives too early. Waiting for the list to be gone
 * is what makes one pick and the next independent of each other.
 *
 * The wait is on the list rather than on the select that opened it: picking a language
 * renames every control on the page, this one included, so a locator that found the select a
 * moment ago finds nothing at all once the pick has landed.
 */
async function pickOption(page: Page, select: Locator, label: string | RegExp) {
  await select.click();
  const dropdown = page.locator('.ant-select-dropdown:visible');
  await expect(dropdown.locator('.ant-select-item-option').first()).toBeVisible();
  await dropdown.locator('.ant-select-item-option').filter({ hasText: label }).first().click();
  await expect(page.locator('.ant-select-dropdown:visible')).toHaveCount(0);
}

/** Picks an existing item in the record-a-link dialog by typing part of its name. */
async function pickExistingItem(page: Page, dialog: Locator, name: string, query: string) {
  await dialog.getByRole('combobox', { name: 'Item', exact: true }).click();
  await page.keyboard.type(query);
  const suggestion = page.locator('.ant-select-item-option').filter({ hasText: name });
  await expect(suggestion).toBeVisible({ timeout: 15_000 });
  await suggestion.click();
}

/**
 * One link's row, addressed by what it says rather than by position.
 *
 * A row is a flex inside the flex that stacks every row, so the selector goes one level in:
 * the stack itself matches the same text and carries every row's actions button at once,
 * which would make a click on "the" button ambiguous the moment a second link exists.
 */
function linkRow(scope: Locator, text: string | RegExp): Locator {
  return scope.locator('.ant-flex > .ant-flex').filter({ hasText: text }).first();
}

/**
 * Makes the dock's links section show its rows and its record-a-link button.
 *
 * There is no room for a section in a dock, so once an entity has any links at all both the
 * rows and the add button hide behind a counted button. The only entity this suite selects on
 * the map is a shared demo entrance, which another run — or an aborted one — may well have
 * left a link on, so which of the two shapes the dock is in is not something to assume.
 * Expanding only when there is something to expand leaves the flow reading the same either way.
 */
async function expandDockLinks(dock: Locator) {
  const counted = dock.getByRole('button', { name: /Linked items \(\d+\)/ });
  const add = dock.getByRole('button', { name: 'Link…' });
  if ((await counted.count()) > 0 && (await add.count()) === 0) {
    await counted.click();
  }
  await expect(add).toBeVisible({ timeout: 15_000 });
}

/** Removes a link through the row menu the reader would use, addressing the row by its content. */
async function deleteLinkRow(page: Page, rowText: string | RegExp) {
  await linkRow(linksCard(page), rowText).getByRole('button', { name: 'Link actions' }).click();
  await page.getByRole('menuitem', { name: 'Delete link' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete link' }).click();
  await accepted(page, 'Saved.');
}

test('a link is recorded from a feature page and reached again by its own address', async ({
  page,
  context,
}) => {
  const stamp = Date.now();
  const anchorName = `E2E Link Anchor ${stamp}`;
  const note = `E2E link ${stamp}`;
  // The address is offered for copying in two places, and both put it on the real clipboard.
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await login(page);

  // The subject of this run, made by this run: it starts with no links at all, which is
  // what makes the "nothing left behind" check at the end mean something.
  await createPointFeature(page, anchorName);
  const featureUrl = await openFeature(page, anchorName);

  // A feature page carries two link lists, and they are about different things: the older
  // feature-to-feature card, and the section that links this feature to anything at all.
  // Only the first exists before a link is recorded, and their titles say which is which.
  await expect(page.getByText('Related features')).toBeVisible();
  await expect(page.getByText(/^Linked items \(\d+\)$/)).toHaveCount(0);

  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();

  // Relation: an undirected one, so neither end has to be marked as the one it reads from.
  await pickOption(page, dialog.getByRole('combobox', { name: 'Relation' }), 'Related to');

  // The other member, found the way a reader finds it: by typing part of its name. The kind
  // of item is already "Feature", which is what this one is.
  await pickExistingItem(page, dialog, 'Falia Demo', 'Falia');

  await dialog.getByLabel('Note').fill(note);
  await dialog.getByRole('button', { name: 'OK' }).click();
  await accepted(page, 'Saved.');

  // Both cards now stand side by side, each saying what it holds.
  await expect(page.getByText('Related features')).toBeVisible();
  const card = linksCard(page);
  await expect(card).toBeVisible({ timeout: 15_000 });
  await expect(card.getByText('Related to')).toBeVisible();
  await expect(card.getByText('Falia Demo')).toBeVisible();
  // The page's own entity speaks for itself and is deliberately not repeated as a chip.
  await expect(card.getByText(anchorName)).toHaveCount(0);

  // Follow the row's own menu to the link's page, and keep the address it lands on.
  await card.getByRole('button', { name: 'Link actions' }).click();
  await page.getByRole('menuitem', { name: 'Open link page' }).click();
  await page.waitForURL(/\/links\/[0-9A-Za-z]{8}$/);
  const permalink = page.url();

  // The address on its own, in a window that has never seen the feature page — this is what
  // gets pasted into a chat, so it has to resolve from nothing but the short code.
  const pasted = await context.newPage();
  await pasted.goto(permalink);
  await expect(pasted.getByRole('heading', { name: 'Related to' })).toBeVisible({ timeout: 20_000 });
  await expect(pasted.getByText(permalink)).toBeVisible();
  await expect(pasted.getByRole('heading', { name: 'Members' })).toBeVisible();
  // Both ends are named here, where the page is about the link rather than about one member.
  await expect(pasted.getByText(anchorName)).toBeVisible();
  await expect(pasted.getByText('Falia Demo')).toBeVisible();
  await expect(pasted.getByText(note)).toBeVisible();
  // Both are features the reader may open, so each carries the button that goes there.
  await expect(pasted.getByRole('link', { name: 'Open' })).toHaveCount(2);
  // The address is offered for copying where a reader would look for it — and what lands on
  // the clipboard is the address itself, not the page's title or a route without an origin.
  await pasted.getByRole('button', { name: 'Copy link address' }).click();
  await expect(pasted.getByText('Link address copied')).toBeVisible({ timeout: 15_000 });
  expect(await pasted.evaluate(() => navigator.clipboard.readText())).toBe(permalink);
  await pasted.close();

  // The same offer from the row's own menu, which is the other place it is asked for: a
  // reader who never opened the link's page still has to be able to hand the address on.
  await page.goto(featureUrl);
  await linksCard(page).getByRole('button', { name: 'Link actions' }).click();
  await page.getByRole('menuitem', { name: 'Copy link address' }).click();
  await expect(page.getByText('Link address copied')).toBeVisible({ timeout: 15_000 });
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(permalink);

  // Clean up through the same menu. The section goes silent again — and because this
  // feature was made by this run, "no links" is a fact rather than a hope about the dataset.
  await deleteLinkRow(page, 'Falia Demo');
  await expect(page.getByText(/^Linked items \(\d+\)$/)).toHaveCount(0);
  await deleteFeature(page, anchorName);
});

test('a link can name a spot that had no record yet, with a height, and says who will see it', async ({
  page,
}) => {
  const stamp = Date.now();
  const anchorName = `E2E Point Anchor ${stamp}`;
  const pointName = `E2E point ${stamp}`;
  await login(page);

  await createPointFeature(page, anchorName);
  const featureUrl = await openFeature(page, anchorName);

  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();

  await pickOption(page, dialog.getByRole('combobox', { name: 'Relation' }), 'Related to');

  // The other end does not exist yet: it is a spot on the map, made as part of recording
  // the link. The kind of item stays "Feature", which is what a marked point is.
  await pickOption(page, dialog.getByRole('combobox', { name: 'Which item' }), 'Mark a new point');

  // Who will see the point is named before it is made, not discovered afterwards — and it is
  // the audience this account will actually get, resolved for them, rather than both halves
  // of a rule the reader would have to apply to themselves. Which of the two it resolves to
  // follows from the clubs the signed-in account belongs to, which this flow does not own;
  // what it holds the app to is that the promise made here is the audience the record ends up
  // with, and that neither of them is the open web.
  const promise = dialog.getByText(/^By default this point will be visible to /);
  await expect(promise).toBeVisible();
  const promised = (await promise.textContent()) ?? '';
  const promisedAudience = promised.includes('everyone signed in')
    ? 'Authenticated users'
    : 'Caving group';

  await dialog.getByRole('button', { name: 'Pick on the map' }).click();
  const picker = page.getByRole('dialog').filter({ hasText: 'Pick a point' });
  await expect(picker).toBeVisible({ timeout: 15_000 });
  // Typed rather than clicked: the coordinate has to be an exact, repeatable one, and the
  // dialog offers the same two boxes to anyone who would rather not aim with a mouse.
  await picker.getByLabel('Longitude').fill('25.61');
  await picker.getByLabel('Latitude').fill('45.66');
  await picker.getByRole('button', { name: 'Use this point' }).click();
  await expect(dialog.getByText('25.61')).toBeVisible();

  // A height, which the picker has no notion of and which is stated beside it. Nothing in
  // the app renders a point's height back, so the proof that it travels is the request the
  // form actually sends — the position it is stored in is the one the server reads.
  await dialog.getByLabel('Altitude (m)').fill('742');
  await dialog.getByLabel('Name', { exact: true }).fill(pointName);

  const memberAdd = page.waitForRequest(
    (request) => /\/reslinks\/[^/]+\/members$/.test(request.url()) && request.method() === 'POST',
  );
  await dialog.getByRole('button', { name: 'OK' }).click();
  const sent = (await memberAdd).postDataJSON() as { newGeoPoint?: { z?: number | null } };
  expect(sent.newGeoPoint?.z).toBe(742);
  await accepted(page, 'Saved.');

  // The point is a member like any other, and its chip leads to the record that now exists.
  const card = linksCard(page);
  await expect(card).toBeVisible({ timeout: 15_000 });
  await card.getByText(pointName).click();
  await page.waitForURL(/\/features\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: pointName })).toBeVisible({ timeout: 15_000 });
  // The audience the modal promised, as the record itself now reports it — and never the
  // public web, which no default may reach. Read from the record's own property table:
  // "Caving group" is also the start of a menu entry in the chrome around it.
  const properties = page.locator('.ant-descriptions');
  await expect(properties.getByText(promisedAudience, { exact: true })).toBeVisible();
  await expect(properties.getByText('Public', { exact: true })).toHaveCount(0);

  // Clean up: the link first, because the point cannot go while it is a member.
  await page.goto(featureUrl);
  await deleteLinkRow(page, pointName);
  await deleteFeature(page, pointName);
  await deleteFeature(page, anchorName);
});

test('a one-way link made around a new point can later be made to read both ways', async ({
  page,
}) => {
  const stamp = Date.now();
  const anchorName = `E2E Direction Anchor ${stamp}`;
  const pointName = `E2E marker ${stamp}`;
  await login(page);

  await createPointFeature(page, anchorName);
  const featureUrl = await openFeature(page, anchorName);

  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();

  // A relation that reads one way, around a spot that does not exist yet: the hardest
  // shape to record, because the link, the point and the end it reads from cannot all be
  // stated in a single call.
  await pickOption(page, dialog.getByRole('combobox', { name: 'Relation' }), 'Contains');
  await pickOption(page, dialog.getByRole('combobox', { name: 'Which item' }), 'Mark a new point');

  await dialog.getByRole('button', { name: 'Pick on the map' }).click();
  const picker = page.getByRole('dialog').filter({ hasText: 'Pick a point' });
  await expect(picker).toBeVisible({ timeout: 15_000 });
  await picker.getByLabel('Longitude').fill('25.62');
  await picker.getByLabel('Latitude').fill('45.67');
  await picker.getByRole('button', { name: 'Use this point' }).click();
  await dialog.getByLabel('Name', { exact: true }).fill(pointName);
  await dialog.getByRole('button', { name: 'OK' }).click();
  await accepted(page, 'Saved.');

  // Open the link's own page through the row menu, and check the marker stayed on the
  // entity the reader started from rather than on the point that was made for it.
  await linkRow(linksCard(page), pointName).getByRole('button', { name: 'Link actions' }).click();
  await page.getByRole('menuitem', { name: 'Open link page' }).click();
  await page.waitForURL(/\/links\/[0-9A-Za-z]{8}$/);
  const anchorCard = page.locator('.ant-card', { has: page.getByText(anchorName) }).last();
  await expect(anchorCard.getByText('Main member')).toBeVisible({ timeout: 15_000 });

  // Now change it to a relation that reads the same both ways. The marker has to come off
  // in the very act that changes the relation, or the change is refused.
  await page.getByRole('button', { name: 'Edit link' }).click();
  await pickOption(page, page.getByRole('combobox', { name: 'Relation' }), 'Related to');
  await page.getByRole('button', { name: 'Save' }).click();
  // The outcome, not the toast: this page reads its heading from the saved relation, so a
  // refusal would leave it saying "Contains" with the marker still sitting on a member.
  await expect(page.getByRole('heading', { name: 'Related to' })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Main member')).toHaveCount(0);

  // Clean up: the link first, because the point cannot go while it is a member.
  await page.goto(featureUrl);
  await deleteLinkRow(page, pointName);
  await deleteFeature(page, pointName);
  await deleteFeature(page, anchorName);
});

test('the map dock links the entrance that was clicked, not the cave it belongs to', async ({
  page,
}) => {
  const stamp = Date.now();
  const pointName = `E2E entrance point ${stamp}`;
  await login(page);

  // Put the demo cave in view, then pick one of its entrances out of the in-view list —
  // a click on the map itself would land on whichever of the two happened to be under the
  // cursor, and this flow is about which entity the dock then links.
  const caveRow = await featureRow(page, 'Peștera Demo Mare');
  await caveRow.getByRole('button', { name: 'aim' }).click();
  await expect(page.locator('.map-canvas')).toBeVisible({ timeout: 15_000 });

  const dock = page.locator('.map-right-tabs');
  await dock.getByRole('tab', { name: 'In view' }).click();
  const entranceRow = dock.getByRole('row', { name: 'Upper entrance' });
  await expect(entranceRow).toBeVisible({ timeout: 20_000 });
  // The name cell, not the row: every row also carries a selection checkbox, and a click
  // that lands on it ticks the row instead of selecting the entrance.
  await entranceRow.getByRole('cell', { name: 'Upper entrance' }).click();

  // The dock shows the cave the entrance belongs to — that is the card an entrance gets —
  // but the links it offers must be the entrance's own.
  await dock.getByRole('tab', { name: 'Selection' }).click();
  await expect(dock.getByRole('heading', { name: 'Peștera Demo Mare' })).toBeVisible({
    timeout: 15_000,
  });

  await expandDockLinks(dock);
  await dock.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();

  // A relation that reads one way makes the dialog name the end it reads from, and that
  // name is the whole point of this test: the entrance, not its cave.
  await pickOption(page, dialog.getByRole('combobox', { name: 'Relation' }), 'Contains');
  await expect(
    dialog.getByText('From Upper entrance: Contains · the other way round: Contained in'),
  ).toBeVisible();

  // The other end is a fresh point, so the row this run makes is addressable by a name
  // nothing else can carry — the demo cave's entrances are shared ground.
  await pickOption(page, dialog.getByRole('combobox', { name: 'Which item' }), 'Mark a new point');
  await dialog.getByRole('button', { name: 'Pick on the map' }).click();
  const picker = page.getByRole('dialog').filter({ hasText: 'Pick a point' });
  await expect(picker).toBeVisible({ timeout: 15_000 });
  await picker.getByLabel('Longitude').fill('25.45');
  await picker.getByLabel('Latitude').fill('45.53');
  await picker.getByRole('button', { name: 'Use this point' }).click();
  await dialog.getByLabel('Name', { exact: true }).fill(pointName);
  await dialog.getByRole('button', { name: 'OK' }).click();
  await accepted(page, 'Saved.');

  // In a dock there is no room for a section, so the links are counted on a button and the
  // rows sit behind it. The button carries an icon, and an icon's own label joins the
  // accessible name ("link Linked items (1)"), so this matches the counted wording rather
  // than anchoring on it.
  const counted = dock.getByRole('button', { name: /Linked items \(\d+\)/ });
  await expect(counted).toBeVisible({ timeout: 15_000 });
  await expandDockLinks(dock);
  const row = linkRow(dock, pointName);
  await expect(row).toBeVisible({ timeout: 15_000 });

  // Decisive: the link's own page names the entrance as a member. Had the dock linked the
  // cave, the cave would be standing here and this name would appear nowhere.
  await row.getByRole('button', { name: 'Link actions' }).click();
  await page.getByRole('menuitem', { name: 'Open link page' }).click();
  await page.waitForURL(/\/links\/[0-9A-Za-z]{8}$/);
  await expect(page.getByText('Upper entrance')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(pointName)).toBeVisible();

  // Clean up from the point's own page — it is a member of the same link, so the row is
  // there too, and the point cannot go while the link still names it.
  await openFeature(page, pointName);
  await deleteLinkRow(page, 'Upper entrance');
  await deleteFeature(page, pointName);
});

test('a trip log takes part in links from its own page', async ({ page }) => {
  const stamp = Date.now();
  const title = `E2E Trip Link ${stamp}`;
  await login(page);

  // A trip of this run's own, so the section's count is a fact about this trip alone.
  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });

  // A trip is documented by things, which is a relation that reads one way — the trip is
  // the end it reads from, because that is the page the link is being recorded on.
  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();
  await pickOption(page, dialog.getByRole('combobox', { name: 'Relation' }), 'Documented by');
  await expect(
    dialog.getByText(`From ${title}: Documented by · the other way round: Documents`),
  ).toBeVisible();
  await pickExistingItem(page, dialog, 'Falia Demo', 'Falia');
  await dialog.getByRole('button', { name: 'OK' }).click();
  await accepted(page, 'Saved.');

  const card = linksCard(page);
  await expect(card).toBeVisible({ timeout: 15_000 });
  await expect(card.getByText('Documented by')).toBeVisible();
  await expect(card.getByText('Falia Demo')).toBeVisible();

  // Deleting the link leaves the feature it named alone; deleting the trip then takes the
  // run's only remaining row with it.
  await deleteLinkRow(page, 'Falia Demo');
  await featureRow(page, 'Falia Demo');

  await page.goto('/trip-logs');
  await page.getByText(title).click();
  await page.getByRole('button', { name: /Delete/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await accepted(page, 'Deleted.');
});

test('an administrator adds a relation, a link records it, and it will not go while it is used', async ({
  page,
  consoleErrors,
}) => {
  const stamp = Date.now();
  const anchorName = `E2E Vocab Anchor ${stamp}`;
  const relationName = `E2E Feeds ${stamp}`;
  const inverseName = `E2E Fed by ${stamp}`;
  const relationCode = `e2e-feeds-${stamp}`;
  // Longest flow in the file: it authors a word, draws a feature, records a link with it,
  // is refused a deletion and then allowed one. That is four round-trips to the admin page
  // and back, which needs room when the rest of the suite is running beside it.
  test.slow();
  // Being refused is the point of this flow, and a browser writes a console error for every
  // request that failed regardless of how well the page handled the answer — so the refusal this
  // test exists to prove arrives as one. Declared rather than left to the console sweep, which
  // would otherwise report the application behaving exactly as this test requires.
  consoleErrors.allow(
    /status of 409/,
    'this flow asserts that a relation in use cannot be deleted; the refusal is the point',
  );
  await login(page);

  await page.goto('/admin/relation-types');
  await expect(page.getByRole('heading', { name: 'Link relations' })).toBeVisible({ timeout: 15_000 });

  // What ships with the product is listed in the reader's language and left alone: its
  // codes are what other installations read these links by.
  const seededRow = page.getByRole('row', { name: /Related to/ });
  await expect(seededRow.getByText('Ships with the product')).toBeVisible();
  await expect(seededRow.getByText('Not editable')).toBeVisible();

  // A relation of this installation's own, reading one way so both phrases are recorded.
  await page.getByRole('button', { name: 'Add a relation' }).click();
  const modal = page.getByRole('dialog');
  await modal.getByLabel('Reads as').fill(relationName);
  await modal.getByLabel('Code').fill(relationCode);
  // The direction switch is what makes the second phrase appear at all.
  await modal.getByRole('switch').click();
  await modal.getByLabel('The other way round').fill(inverseName);
  await modal.getByRole('button', { name: 'Save' }).click();
  await accepted(page, 'Saved.');

  const customRow = page.getByRole('row', { name: new RegExp(relationName) });
  await expect(customRow).toBeVisible({ timeout: 15_000 });
  await expect(customRow.getByText(inverseName)).toBeVisible();
  await expect(customRow.getByText(relationCode)).toBeVisible();
  await expect(customRow.getByText('Added here')).toBeVisible();

  // The vocabulary is one list wherever a link is recorded, so the new wording is offered
  // beside the shipped rows straight away — and stored exactly as it was written.
  await createPointFeature(page, anchorName);
  const featureUrl = await openFeature(page, anchorName);
  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await pickOption(page, dialog.getByRole('combobox', { name: 'Relation' }), relationName);
  await pickExistingItem(page, dialog, 'Falia Demo', 'Falia');
  await dialog.getByRole('button', { name: 'OK' }).click();
  await accepted(page, 'Saved.');
  await expect(linksCard(page).getByText(relationName)).toBeVisible({ timeout: 15_000 });

  // Now the refusal that matters: deleting the wording would leave that link saying
  // nothing, so the page says why instead of failing quietly or breaking the link.
  await page.goto('/admin/relation-types');
  await customRow.getByRole('button', { name: 'Delete' }).click();
  await page.locator('.ant-popconfirm:visible').getByRole('button', { name: 'Delete' }).click();
  await expect(
    page.getByText('Links already record this relation, so it cannot be changed that way or deleted.'),
  ).toBeVisible({ timeout: 15_000 });
  await expect(customRow).toBeVisible();

  // With the last link gone the relation can go too — and the run leaves the vocabulary
  // exactly as it found it.
  await page.goto(featureUrl);
  await deleteLinkRow(page, 'Falia Demo');
  await page.goto('/admin/relation-types');
  await customRow.getByRole('button', { name: 'Delete' }).click();
  await page.locator('.ant-popconfirm:visible').getByRole('button', { name: 'Delete' }).click();
  await accepted(page, 'Deleted.');
  await expect(customRow).toHaveCount(0, { timeout: 15_000 });

  await deleteFeature(page, anchorName);
});

test('an administrator may edit a link somebody else recorded', async ({ page, browser, request }) => {
  const stamp = Date.now();
  const otherEmail = `e2e-author-${stamp}@dev.local`;
  const otherPassword = 'e2e-author-pass-1';
  const otherName = `E2E Author ${stamp}`;
  const anchorName = `E2E Foreign Anchor ${stamp}`;
  const description = `Corrected by an administrator ${stamp}`;
  // Two people means two sign-ins, each its own browser and its own full authorisation
  // round-trip, on top of the ordinary work of making a feature and a link. It needs room.
  test.slow();

  // A second person is the whole premise, and the only way an installation mints one without
  // an operator is self-registration — no endpoint creates an account on an administrator's
  // behalf. A run that cannot stage it proves nothing about administrators reaching into
  // other people's links, so this is stated as a missing prerequisite rather than skipped:
  // a skip reads as a non-failure, and this flow is the only cover this behaviour has.
  const config = (await (await request.get('/api/v1/auth/config')).json()) as {
    openRegistration?: boolean;
  };
  expect(
    config.openRegistration,
    'this flow needs a second account: start the API with SILEXGIS__Auth__OpenRegistration=true',
  ).toBe(true);

  const registered = await request.post('/api/v1/auth/register', {
    data: { email: otherEmail, password: otherPassword, displayName: otherName },
  });
  expect(registered.ok()).toBeTruthy();

  await login(page);

  // A fresh account holds only the baseline reads, which do not include features — so it
  // could not name one in a link. Editors is the shipped ruleset for someone who records
  // content; it is emphatically not the full-administrator rank, which is what makes the
  // edit below an administrator reaching into somebody else's work.
  const drawer = page.locator('.ant-drawer-section');
  const pane = drawer.locator('[role="tabpanel"]:visible');
  const openEditorsMembers = async () => {
    await page.goto('/admin/permission-groups');
    await page
      .getByRole('row', { name: /Editors/ })
      .getByRole('button', { name: 'Manage' })
      .click();
    await drawer.getByRole('tab', { name: 'Members' }).click();
  };

  await openEditorsMembers();
  const userPicker = pane.locator('.ant-select').nth(1);
  await userPicker.click();
  // By name, not by address: an account's address is private by default and the search
  // refuses to match one nobody may see — that refusal is the guard that stops the picker
  // being used to ask whether an address has an account here, and it holds for an
  // administrator too. The display name carries the same stamp, so it is just as unique.
  await page.keyboard.type(otherName);
  const userOption = page.locator('.ant-select-item-option').filter({ hasText: otherName });
  await expect(userOption).toBeVisible({ timeout: 15_000 });
  await userOption.click();
  await pane.getByRole('button', { name: 'Add' }).click();
  await expect(pane.locator('.ant-list-item').filter({ hasText: otherName })).toBeVisible({
    timeout: 15_000,
  });
  await page.locator('.ant-drawer-close').click();

  // Everything from here runs inside the grant, so it is taken back whatever happens below.
  // A membership of a shipped ruleset is a rights grant rather than a stray row, and the
  // account holding it can be removed from no page at all — leaving one behind would hand
  // the next run of this suite an installation that quietly ships an extra editor.
  try {
    // The administrator makes the subject; the other person records the link on it, so the
    // link's author and the person editing it below are different people.
    await createPointFeature(page, anchorName);

    const authorContext = await browser.newContext();
    const authorPage = await authorContext.newPage();
    await login(authorPage, otherEmail, otherPassword);
    await openFeature(authorPage, anchorName);
    await authorPage.getByRole('button', { name: 'Link…' }).click();
    const dialog = authorPage.getByRole('dialog');
    await expect(dialog.getByText('Record a link')).toBeVisible();
    await pickOption(authorPage, dialog.getByRole('combobox', { name: 'Relation' }), 'Related to');
    await pickExistingItem(authorPage, dialog, 'Falia Demo', 'Falia');
    await dialog.getByRole('button', { name: 'OK' }).click();
    await accepted(authorPage, 'Saved.');
    await linksCard(authorPage).getByRole('button', { name: 'Link actions' }).click();
    await authorPage.getByRole('menuitem', { name: 'Open link page' }).click();
    await authorPage.waitForURL(/\/links\/[0-9A-Za-z]{8}$/);
    const permalink = authorPage.url();
    await authorContext.close();

    // The administrator opens the same address. The page names somebody else as the author
    // and still offers the edit — the rank decides, not authorship.
    await page.goto(permalink);
    await expect(page.getByRole('heading', { name: 'Related to' })).toBeVisible({ timeout: 20_000 });
    await expect(page.getByText(new RegExp(`by ${otherName}`))).toBeVisible({ timeout: 15_000 });
    await page.getByRole('button', { name: 'Edit link' }).click();
    await page.getByLabel('Description').fill(description);
    await page.getByRole('button', { name: 'Save' }).click();
    await accepted(page, 'Saved.');
    await page.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByText(description)).toBeVisible({ timeout: 15_000 });

    // Clean up everything this run made except the account, which no page can remove — its
    // address carries the stamp, so a later run never collides with it. The link goes from
    // the anchor's page, which is where the row with its menu lives.
    await openFeature(page, anchorName);
    await deleteLinkRow(page, 'Falia Demo');
    await deleteFeature(page, anchorName);
  } finally {
    // A run that has used up its time is torn down before this point, and there is no page
    // left to click on. Saying so is better than reporting the closed page as the failure,
    // which would bury whatever the body was actually stuck on.
    if (!page.isClosed()) {
      await openEditorsMembers();
      await pane
        .locator('.ant-list-item')
        .filter({ hasText: otherName })
        .getByRole('button')
        .last()
        .click();
      await page.locator('.ant-popconfirm:visible').getByRole('button', { name: 'OK' }).click();
      await expect(pane.locator('.ant-list-item').filter({ hasText: otherName })).toHaveCount(0, {
        timeout: 15_000,
      });
    }
  }
});

test('the Romanian interface renders link wording with its numbers and names in place', async ({
  page,
}) => {
  const stamp = Date.now();
  const anchorName = `E2E RO Anchor ${stamp}`;
  await login(page);

  await createPointFeature(page, anchorName);
  const featureUrl = await openFeature(page, anchorName);
  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await pickOption(page, dialog.getByRole('combobox', { name: 'Relation' }), 'Related to');
  await pickExistingItem(page, dialog, 'Falia Demo', 'Falia');
  await dialog.getByRole('button', { name: 'OK' }).click();
  await accepted(page, 'Saved.');

  // Switch the interface to Romanian from the header, the way a reader would.
  await pickOption(page, page.getByRole('combobox', { name: 'Language' }), 'RO');

  // A translated string with a number substituted into it — the shape that breaks when a
  // locale is missing a key and the raw key or the English fallback shows up instead.
  await expect(page.getByText('Elemente legate (1)')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('În legătură cu').first()).toBeVisible();

  // And a phrase with three substitutions, read back from the dialog that composes it.
  await page.getByRole('button', { name: 'Leagă…' }).click();
  const roDialog = page.getByRole('dialog');
  await expect(roDialog.getByText('Înregistrează o legătură')).toBeVisible();
  await pickOption(page, roDialog.getByRole('combobox', { name: 'Relație' }), 'Conține');
  await expect(
    roDialog.getByText(`De la ${anchorName}: Conține · în sens invers: Conținut în`),
  ).toBeVisible();
  // Dismissed with the key rather than the button: the dialog's own chrome is antd's, and
  // this flow is about the app's own wording, not about which locale antd shipped.
  await page.keyboard.press('Escape');

  // Back to English, so the language this browser profile carries into the next flow is
  // the one every other test assumes.
  await pickOption(page, page.getByRole('combobox', { name: 'Limbă' }), 'EN');
  await expect(page.getByText(/^Linked items \(\d+\)$/)).toBeVisible({ timeout: 15_000 });

  await page.goto(featureUrl);
  await deleteLinkRow(page, 'Falia Demo');
  await deleteFeature(page, anchorName);
});

/**
 * A link anchored at a passage, composed the way a reader composes one: open the document in
 * the dialog, drag across a sentence, and record it.
 *
 * This is the one flow that cannot be checked below a browser at all. The anchor's offsets are
 * into the text the **server** read out of the file, and the words come from a layer a real
 * rendering engine lays over a real drawing — so a test with no canvas and no worker can only
 * assert that some elements exist. What is asserted here is the whole round trip: the words are
 * selectable, the editor finds them in the server's text, the payload the server accepts is
 * composed from both halves, and the recorded link reads back as pointing at that page.
 *
 * It leans on the demo report `seed-demo` files, the way the other flows lean on the demo cave:
 * a document with real text in it is not something a browser test can conjure.
 */
test('a link is anchored at a passage selected in a document', async ({ page }) => {
  const stamp = Date.now();
  const anchorName = `E2E Quote Anchor ${stamp}`;
  const note = `E2E quote ${stamp}`;
  await login(page);

  await createPointFeature(page, anchorName);
  const featureUrl = await openFeature(page, anchorName);

  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();
  await pickOption(page, dialog.getByRole('combobox', { name: 'Relation' }), 'Related to');

  // The other member is a document, so the kind of item has to be said before the picker can
  // find one — it searches one world at a time.
  await pickOption(page, dialog.getByRole('combobox', { name: 'Kind of item' }), 'Document');
  await pickExistingItem(page, dialog, '1987 survey report', '1987');

  // Now the part of it: a passage rather than the whole document. This option was disabled
  // until the viewer that can express it existed, so its being choosable is itself the change.
  await pickOption(page, dialog.getByRole('combobox', { name: 'What it points at' }), 'A passage of text');

  // The document is laid out inside the dialog. Its words are real text, which is what makes
  // the next step possible at all.
  const layer = dialog.getByTestId('pdf-text-layer');
  await expect(layer).toBeVisible({ timeout: 60_000 });

  const selected = await layer.evaluate((el) => {
    const run = Array.from(el.querySelectorAll('span'))
      .find((span) => (span.textContent ?? '').includes('widens into a chamber'));
    if (!run) {
      return null;
    }
    const range = document.createRange();
    range.selectNodeContents(run);
    const selection = window.getSelection()!;
    selection.removeAllRanges();
    selection.addRange(range);
    // The editor reads a settled selection rather than every change, so a drag does not send
    // the server every prefix of the phrase being dragged over.
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
    return selection.toString();
  });
  expect(selected).toContain('widens into a chamber');

  // The editor echoes back what it captured, with the page it was found on — which it can only
  // know by having located those words in the text the server holds.
  // Scoped to the editor's own confirmation rather than to the dialog: the same words are on
  // the page behind it, which is the point, and matching either would prove nothing.
  const captured = dialog.locator('.ant-alert-title').filter({ hasText: /^Page 1: / });
  await expect(captured).toBeVisible({ timeout: 30_000 });
  await expect(captured).toContainText('widens into a chamber');

  await dialog.getByLabel('Note').fill(note);
  await dialog.getByRole('button', { name: 'OK' }).click();
  await accepted(page, 'Saved.');

  // Recorded, and reading back as a part rather than as the whole document: the server took
  // the payload, which it refuses outright without both the quote and the offsets.
  const card = linksCard(page);
  await expect(card).toBeVisible({ timeout: 15_000 });
  await expect(card.getByText('1987 survey report')).toBeVisible();
  await expect(card.getByText('quote, p. 1')).toBeVisible({ timeout: 15_000 });

  await page.goto(featureUrl);
  await deleteLinkRow(page, '1987 survey report');
  await deleteFeature(page, anchorName);
});
