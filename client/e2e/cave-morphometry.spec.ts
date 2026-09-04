// SPDX-License-Identifier: AGPL-3.0-or-later
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

const fixtures = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures');

async function createCave(page: Page, name: string) {
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible({ timeout: 15_000 });
}

/**
 * Both panels work their figures out when the page is opened, and nothing tells a page already on
 * screen that a survey finished being read. Coming back to the cave by address is what a person
 * does, and it is what makes the figures reflect the line work that now exists.
 */
async function reopenCave(page: Page) {
  await gotoRoute(page, new URL(page.url()).pathname);
}

/**
 * How big a cave's passages are and what kind of cave its shape suggests, on the page, from a
 * survey a person really uploaded.
 *
 * What this shows that no unit test can. Every unit test on these two panels hands them a response
 * somebody wrote by hand, so they prove the drawing and not the measurement. Here the wall
 * distances come out of a real export and travel the whole server — upload, background reading,
 * extraction, arithmetic — and what is checked is that the two things this design exists to protect
 * arrive intact at the reader: a size figure that says how many stations it was worked out over,
 * and a suggested pattern whose reasoning is on screen beside it rather than behind it. A label
 * without its trace is an assertion a reader cannot check, and only a run through the real page can
 * show whether the trace is actually there.
 */
test('a surveyed cave shows its passage sizes and the rules behind its suggested pattern', async ({
  page,
}) => {
  const caveName = `E2E Passage Shape Cave ${Date.now()}`;
  await login(page);
  await createCave(page, caveName);

  await page.getByRole('button', { name: 'Upload model' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.locator('input[type="file"]').setInputFiles(path.join(fixtures, 'P8_Master.3d'));
  await dialog.getByRole('button', { name: 'Upload model' }).click();

  // The reading has to have finished before there is anything to measure: the sizes come from the
  // wall distances the parser stored, not from the uploaded bytes.
  await expect(
    page.getByRole('table').filter({ hasText: 'Survex .3d' }).getByText('Ready'),
  ).toBeVisible({ timeout: 60_000 });

  await reopenCave(page);

  const sizes = page.getByTestId('cave-cross-section');
  await expect(sizes).toBeVisible({ timeout: 30_000 });

  // Every size is shown with the number of stations that produced it. That count is the whole
  // safeguard: a median width over four stations and a median width over four hundred are the same
  // number saying two entirely different things, and nothing else on the card separates them.
  await expect(sizes.getByText(/over \d+ stations/).first()).toBeVisible();

  // And the volume says how much of the cave it describes. A station where a wall was never
  // reached leaves the estimate rather than counting as a wall at distance zero, so the measured
  // length is the figure that says what the estimate is true of.
  await expect(sizes.getByText(/Computed over .* of .* of passage/)).toBeVisible();

  // Measured from the survey file's own flags, not approximated from a drawing — the same sentence
  // every other survey-derived panel uses, from the one component that owns it. It sits in the
  // card rather than among the figures, which is why the card and not the figures is the scope.
  const sizesCard = page.locator('.ant-card').filter({ hasText: 'Passage size' });
  await expect(sizesCard.getByText(/Measured from the compiled survey/)).toBeVisible();

  const pattern = page.getByTestId('cave-pattern');
  await expect(pattern).toBeVisible();
  await expect(page.getByTestId('cave-pattern-label')).not.toBeEmpty();

  // The reasoning is on screen without anything being opened, because the reasoning is the point.
  const rules = page.getByTestId('cave-pattern-rules');
  await expect(rules).toBeVisible();
  await expect(rules.getByText('Fired').first()).toBeVisible();

  // Including the rules that looked and stayed silent: a rule never shown not firing is not a
  // rule, it is a constant, and a reader cannot tell the difference from the label alone.
  await expect(rules.getByText('Did not fire').first()).toBeVisible();

  // And the label is offered as a reading of the survey rather than as a fact about the cave.
  await expect(pattern.getByText(/It is a proposal about this survey/)).toBeVisible();
});
