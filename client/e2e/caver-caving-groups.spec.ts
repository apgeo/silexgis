// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, tryAsPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The groups a person belongs to, changed from the person's own entry.
 *
 * <b>Why this is a browser flow and not only a component suite.</b> The entry's save is three
 * writes to two parts of the server — the person, then a join and a departure on two different
 * groups' rosters — and the component suite stubs all three. What it cannot show is that the
 * rosters the form wrote are the ones the list then reads: the list's column comes from the
 * person's side and the writes go to the groups' side, with nothing in the client tying the two
 * together. Here the groups are real, the person is real, and what the list says afterwards is
 * what the server holds.
 *
 * Everything is this run's own — two groups and one person, made over the API and removed again —
 * so the choice is made among names no other check writes to.
 */

interface CaverRow {
  id: string;
  name: string;
  cavingGroups: { cavingGroupId: string; name: string }[];
}

/** A caving group of this run's own. */
async function group(page: Page, name: string): Promise<string> {
  return (
    await asPerson<{ id: string }>(page, 'POST', '/api/v1/caving-groups', {
      name,
      type: 'cavingClub',
      description: null,
      website: null,
    })
  ).id;
}

/** The person as the list's own read answers for them. */
async function entryOf(page: Page, name: string): Promise<CaverRow> {
  const found = await asPerson<CaverRow[]>(page, 'GET', `/api/v1/cavers?search=${encodeURIComponent(name)}`);
  const row = found.find((caver) => caver.name === name);
  expect(row, 'the person this run made is in the directory').toBeTruthy();
  return row!;
}

test("a person's caving groups are changed from their own entry, after a warning of what that means", async ({
  page,
}) => {
  test.setTimeout(120_000);
  await login(page);

  const stamp = Date.now();
  const person = `E2E Groups Person ${stamp}`;
  const old = { name: `E2E Old Group ${stamp}`, id: '' };
  const next = { name: `E2E New Group ${stamp}`, id: '' };
  old.id = await group(page, old.name);
  next.id = await group(page, next.name);
  const caver = await asPerson<{ id: string }>(page, 'POST', '/api/v1/cavers', {
    fullName: person,
    email: null,
    phone: null,
    notes: null,
    shortName: null,
  });
  await asPerson(page, 'POST', `/api/v1/caving-groups/${old.id}/members`, {
    caverId: caver.id,
    role: 'member',
  });

  try {
    await gotoRoute(page, '/cavers');
    await page.getByPlaceholder('Search by name…').fill(person);
    const rows = page.locator('.ant-table-tbody tr.ant-table-row');
    // The narrowed list is waited for: its rows are taken down while the answer is on its way, and
    // a dialog opened on the list as it was would be opened on somebody else.
    await expect(rows).toHaveCount(1, { timeout: 20_000 });
    const row = page.getByRole('row', { name: new RegExp(person) });
    await expect(row.locator('.ant-tag', { hasText: old.name })).toBeVisible();

    const entry = page.getByRole('dialog', { name: 'Edit caver' });
    const field = entry.locator('.ant-form-item').filter({ has: page.getByTestId('caver-caving-groups') });
    const chosen = field.locator('.ant-select-selection-item');
    const warning = entry.getByTestId('caver-caving-groups-warning');
    /**
     * Closes the open list of groups by leaving the field. A list of several stays open after a
     * choice, over the dialog's own buttons, and Escape is not the way out of it: the dialog
     * listens for that key too.
     */
    const putListAway = async () => {
      await entry.locator('.ant-modal-title').click();
      await expect(page.locator('.ant-select-dropdown:visible')).toHaveCount(0);
    };
    /** Chooses a group by typing its name, which is how a long list of groups is chosen from. */
    const choose = async (name: string) => {
      await field.locator('input').fill(name);
      const option = page.locator(`.ant-select-dropdown:visible .ant-select-item-option[title="${name}"]`);
      await expect(option).toBeVisible({ timeout: 15_000 });
      // Taken with the keyboard once it is the highlighted one: the list commits on mousedown, so
      // a click that straddles a re-render chooses nothing and says nothing.
      await expect(option).toHaveClass(/ant-select-item-option-active/);
      await page.keyboard.press('Enter');
      await expect(chosen.filter({ hasText: name })).toHaveCount(1);
      await putListAway();
    };
    /** The group changes one save wrote, as the server was asked for them. */
    const rosterWrites = () => {
      const seen: string[] = [];
      const listen = (request: { method(): string; url(): string }) => {
        const match = /\/api\/v1\/caving-groups\/([0-9a-f-]{36})\/members(\/[0-9a-f-]{36})?$/.exec(request.url());
        if (match && request.method() !== 'GET') {
          seen.push(`${request.method()} ${match[1]}`);
        }
      };
      page.on('request', listen);
      return { seen, stop: () => page.off('request', listen) };
    };

    // ---- Opened and saved untouched: no warning, and no roster is written ----
    await row.getByRole('button', { name: 'Edit', exact: true }).click();
    await expect(chosen).toHaveCount(1);
    await expect(chosen).toContainText(old.name);
    await expect(warning).toHaveCount(0);
    const untouched = rosterWrites();
    await entry.getByRole('button', { name: 'OK' }).click();
    await expect(entry).toBeHidden({ timeout: 15_000 });
    untouched.stop();
    expect(untouched.seen).toEqual([]);
    expect((await entryOf(page, person)).cavingGroups.map((g) => g.cavingGroupId)).toEqual([old.id]);

    // ---- A change typed and abandoned is written nowhere ----
    await row.getByRole('button', { name: 'Edit', exact: true }).click();
    await choose(next.name);
    // Said with the first difference from what is stored, and in words about the account's rights.
    await expect(warning).toBeVisible();
    await expect(warning).toContainText("This changes what the person's account may do");
    await entry.getByRole('button', { name: 'Cancel' }).click();
    await expect(entry).toBeHidden({ timeout: 15_000 });
    expect((await entryOf(page, person)).cavingGroups.map((g) => g.cavingGroupId)).toEqual([old.id]);

    // ---- Moved to the other group: one join, one departure, and the list says so ----
    await row.getByRole('button', { name: 'Edit', exact: true }).click();
    // The form opens on what is stored, not on what was abandoned a moment ago.
    await expect(chosen).toHaveCount(1);
    await expect(warning).toHaveCount(0);
    await choose(next.name);
    await expect(warning).toBeVisible();
    await chosen.filter({ hasText: old.name }).locator('.anticon-close').click();
    await putListAway();
    await expect(chosen).toHaveCount(1);
    await expect(chosen).toContainText(next.name);
    // Still a difference from what is stored, so still said.
    await expect(warning).toBeVisible();

    const moved = rosterWrites();
    await entry.getByRole('button', { name: 'OK' }).click();
    await expect(entry).toBeHidden({ timeout: 15_000 });
    moved.stop();
    expect([...moved.seen].sort()).toEqual([`DELETE ${old.id}`, `POST ${next.id}`].sort());

    // The list is read again after the save, and its column is the person's side of what the
    // groups' rosters now hold.
    await expect(row.locator('.ant-tag', { hasText: next.name })).toBeVisible({ timeout: 15_000 });
    await expect(row.locator('.ant-tag', { hasText: old.name })).toHaveCount(0);
    expect((await entryOf(page, person)).cavingGroups.map((g) => g.cavingGroupId)).toEqual([next.id]);
  } finally {
    // The person first: a group is not deleted from under a member this run put in it.
    await tryAsPerson(page, 'DELETE', `/api/v1/cavers/${caver.id}`);
    await tryAsPerson(page, 'DELETE', `/api/v1/caving-groups/${old.id}`);
    await tryAsPerson(page, 'DELETE', `/api/v1/caving-groups/${next.id}`);
  }
});
