// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test, type Page } from '@playwright/test';
import { deleteFeature, login } from './helpers.ts';

/**
 * One link, recorded the way a caver would: from the page of the thing they are standing on,
 * naming something else they searched for by name. It then has to be reachable by its own
 * address from a cold load, because that address is the whole point of a link having a page.
 *
 * The two demo features it uses are public and are never edited here, so the run leaves the
 * dataset as it found it — the link it makes is the only new row, and it is removed at the
 * end through the same menu a reader would use.
 */

/** Opens a feature's detail page from the registry and hands back its address. */
async function openFeature(page: Page, name: string): Promise<string> {
  await page.goto('/features');
  const row = page.getByRole('row', { name: new RegExp(name) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByRole('cell', { name }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
  return page.url();
}

test('a link is recorded from a feature page and reached again by its own address', async ({
  page,
  context,
}) => {
  // The note is what tells this run's link apart from anything a previous one left behind.
  const note = `E2E link ${Date.now()}`;
  await login(page);

  const featureUrl = await openFeature(page, 'Dolina Demo');

  // Nothing is linked yet, so the section is just the invitation to record the first one.
  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();

  // Relation: an undirected one, so neither end has to be marked as the one it reads from.
  await dialog.getByRole('combobox', { name: 'Relation' }).click();
  await page.locator('.ant-select-item-option').filter({ hasText: 'Related to' }).click();

  // The other member, found the way a reader finds it: by typing part of its name. The kind
  // of item is already "Feature", which is what this one is.
  await dialog.getByRole('combobox', { name: 'Item', exact: true }).click();
  await page.keyboard.type('Falia');
  const suggestion = page.locator('.ant-select-item-option').filter({ hasText: 'Falia Demo' });
  await expect(suggestion).toBeVisible({ timeout: 15_000 });
  await suggestion.click();

  await dialog.getByLabel('Note').fill(note);
  await dialog.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The section now counts the link and shows the other end as a chip. The count is
  // whatever the server admits, so it is read as a number rather than asserted to be one:
  // a link recorded by an earlier run and not cleaned up would still be honestly counted.
  const linksCard = page.locator('.ant-card', { has: page.getByText(/^Links \(\d+\)$/) }).last();
  await expect(linksCard).toBeVisible({ timeout: 15_000 });
  await expect(linksCard.getByText('Related to')).toBeVisible();
  const chip = linksCard.getByText('Falia Demo');
  await expect(chip).toBeVisible();
  // The page's own entity speaks for itself and is deliberately not repeated as a chip.
  await expect(linksCard.getByText('Dolina Demo')).toHaveCount(0);

  // Follow the row's own menu to the link's page, and keep the address it lands on.
  await linksCard.getByRole('button', { name: 'Link actions' }).click();
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
  await expect(pasted.getByText('Dolina Demo')).toBeVisible();
  await expect(pasted.getByText('Falia Demo')).toBeVisible();
  await expect(pasted.getByText(note)).toBeVisible();
  // Both are features the reader may open, so each carries the button that goes there.
  await expect(pasted.getByRole('link', { name: 'Open' })).toHaveCount(2);
  await pasted.close();

  // Clean up through the same menu, and check the section goes quiet again.
  await page.goto(featureUrl);
  await page.locator('.ant-card', { has: page.getByText(/^Links \(\d+\)$/) })
    .last()
    .getByRole('button', { name: 'Link actions' })
    .click();
  await page.getByRole('menuitem', { name: 'Delete link' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete link' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(/^Links \(\d+\)$/)).toHaveCount(0);
});

test('a link can name a spot that had no record yet, and says who will see it', async ({ page }) => {
  // Both rows this run makes carry the stamp, so a failed run leaves nothing ambiguous behind.
  const stamp = Date.now();
  const pointName = `E2E point ${stamp}`;
  await login(page);

  const featureUrl = await openFeature(page, 'Dolina Demo');

  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();

  await dialog.getByRole('combobox', { name: 'Relation' }).click();
  await page.locator('.ant-select-item-option').filter({ hasText: 'Related to' }).click();

  // The other end does not exist yet: it is a spot on the map, made as part of recording
  // the link. The kind of item stays "Feature", which is what a marked point is.
  await dialog.getByRole('combobox', { name: 'Which item' }).click();
  await page.locator('.ant-select-item-option').filter({ hasText: 'Mark a new point' }).click();

  // Who will see the point is named before it is made, not discovered afterwards — and it
  // is the audience this account will actually get rather than both halves of a rule the
  // reader would have to apply to themselves. This account belongs to no single club, so
  // that audience is every signed-in reader, never the whole public web.
  await expect(
    dialog.getByText('By default this point will be visible to everyone signed in.'),
  ).toBeVisible();

  await dialog.getByRole('button', { name: 'Pick on the map' }).click();
  const picker = page.getByRole('dialog').filter({ hasText: 'Pick a point' });
  await expect(picker).toBeVisible({ timeout: 15_000 });
  // Typed rather than clicked: the coordinate has to be an exact, repeatable one, and the
  // dialog offers the same two boxes to anyone who would rather not aim with a mouse.
  await picker.getByLabel('Longitude').fill('25.61');
  await picker.getByLabel('Latitude').fill('45.66');
  await picker.getByRole('button', { name: 'Use this point' }).click();
  await expect(dialog.getByText('25.61')).toBeVisible();

  await dialog.getByLabel('Name', { exact: true }).fill(pointName);
  await dialog.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The point is a member like any other, and its chip leads to the record that now exists.
  const linksCard = page.locator('.ant-card', { has: page.getByText(/^Links \(\d+\)$/) }).last();
  await expect(linksCard).toBeVisible({ timeout: 15_000 });
  await linksCard.getByText(pointName).click();
  await page.waitForURL(/\/features\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: pointName })).toBeVisible({ timeout: 15_000 });
  // The audience the modal promised, as the record itself now reports it: this account
  // belongs to no club, so the default lands on every signed-in reader — not on the public.
  await expect(page.getByText('Authenticated users')).toBeVisible();

  // Clean up both rows: the link first, because the point cannot go while it is a member.
  await page.goto(featureUrl);
  await page.locator('.ant-card', { has: page.getByText(/^Links \(\d+\)$/) })
    .last()
    .getByRole('button', { name: 'Link actions' })
    .click();
  await page.getByRole('menuitem', { name: 'Delete link' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete link' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await deleteFeature(page, pointName);
});

test('a one-way link made around a new point can later be made to read both ways', async ({
  page,
}) => {
  const stamp = Date.now();
  const pointName = `E2E marker ${stamp}`;
  await login(page);

  const featureUrl = await openFeature(page, 'Dolina Demo');

  await page.getByRole('button', { name: 'Link…' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText('Record a link')).toBeVisible();

  // A relation that reads one way, around a spot that does not exist yet: the hardest
  // shape to record, because the link, the point and the end it reads from cannot all be
  // stated in a single call.
  await dialog.getByRole('combobox', { name: 'Relation' }).click();
  await page.locator('.ant-select-item-option').filter({ hasText: 'Contains' }).click();

  await dialog.getByRole('combobox', { name: 'Which item' }).click();
  await page.locator('.ant-select-item-option').filter({ hasText: 'Mark a new point' }).click();
  await dialog.getByRole('button', { name: 'Pick on the map' }).click();
  const picker = page.getByRole('dialog').filter({ hasText: 'Pick a point' });
  await expect(picker).toBeVisible({ timeout: 15_000 });
  await picker.getByLabel('Longitude').fill('25.62');
  await picker.getByLabel('Latitude').fill('45.67');
  await picker.getByRole('button', { name: 'Use this point' }).click();
  await dialog.getByLabel('Name', { exact: true }).fill(pointName);
  await dialog.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // Open the link's own page through the row menu, and check the marker stayed on the
  // entity the reader started from rather than on the point that was made for it.
  // Addressed by the point it names rather than by position: this feature may already
  // carry links, and the row this run made is the one holding this run's point.
  const linksCard = page.locator('.ant-card', { has: page.getByText(/^Links \(\d+\)$/) }).last();
  const row = linksCard.locator('.ant-flex').filter({ hasText: pointName }).first();
  await row.getByRole('button', { name: 'Link actions' }).click();
  await page.getByRole('menuitem', { name: 'Open link page' }).click();
  await page.waitForURL(/\/links\/[0-9A-Za-z]{8}$/);
  const dolinaCard = page.locator('.ant-card', { has: page.getByText('Dolina Demo') }).last();
  await expect(dolinaCard.getByText('Main member')).toBeVisible({ timeout: 15_000 });

  // Now change it to a relation that reads the same both ways. The marker has to come off
  // in the very act that changes the relation, or the change is refused.
  await page.getByRole('button', { name: 'Edit link' }).click();
  await page.getByRole('combobox', { name: 'Relation' }).click();
  await page.locator('.ant-select-item-option').filter({ hasText: 'Related to' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  // The outcome, not the toast: this page reads its heading from the saved relation, so a
  // refusal would leave it saying "Contains" with the marker still sitting on a member.
  await expect(page.getByRole('heading', { name: 'Related to' })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Main member')).toHaveCount(0);

  // Clean up: the link first, because the point cannot go while it is a member.
  await page.goto(featureUrl);
  await page
    .locator('.ant-card', { has: page.getByText(/^Links \(\d+\)$/) })
    .last()
    .locator('.ant-flex')
    .filter({ hasText: pointName })
    .first()
    .getByRole('button', { name: 'Link actions' })
    .click();
  await page.getByRole('menuitem', { name: 'Delete link' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete link' }).click();
  await expect(page.getByText(pointName)).toHaveCount(0, { timeout: 15_000 });
  await deleteFeature(page, pointName);
});
