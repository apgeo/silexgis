// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Locator, type Page } from '@playwright/test';
import { gotoRoute } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';

/**
 * What the two specs about the size of station names share: a cave with the committed survey on
 * it, that survey open in the viewer dialog, and the control read and pressed.
 *
 * The control is a button this application lays over a surface the viewer fills with controls of
 * its own, in corners and along edges that move with the width of the window and the kind of
 * pointer. It was first placed under the viewer's column of tabs: drawn, listed by every unit
 * test, and impossible to press. So both specs press it, and both ask the page what is on top of
 * it and of everything around it before they do.
 */

const fixture = path.join(path.dirname(fileURLToPath(import.meta.url)), 'fixtures', 'P8_Master.3d');
/** The name the survey is listed by. */
const SURVEY = 'Label sizes';

/** A cave of this run's own with the survey on it; the caller removes the cave. */
export async function caveWithSurvey(page: Page, made: string[]): Promise<string> {
  await page.goto('/caves/new');
  const caveName = `E2E Label Sizes ${Date.now()}`;
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  const caveId = /\/caves\/([0-9a-f-]+)/.exec(page.url())![1];
  made.push(caveId);
  const uploaded = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${await bearerToken(page)}` },
    multipart: {
      file: { name: `${SURVEY}.3d`, mimeType: 'application/octet-stream', buffer: readFileSync(fixture) },
    },
  });
  expect(uploaded.status(), await uploaded.text()).toBe(201);
  return caveId;
}

/** Opens the cave's one survey in the viewer dialog and waits until it is drawn. */
export async function openModel(page: Page, caveId: string): Promise<Locator> {
  await gotoRoute(page, `/caves/${caveId}`);
  const listed = page
    .locator('.ant-card', { hasText: '3D survey models' })
    .getByRole('row')
    .filter({ hasText: SURVEY });
  await expect(listed).toBeVisible({ timeout: 30_000 });
  await listed.getByRole('button', { name: 'View in 3D' }).click();
  const viewer = page.getByRole('dialog').filter({ has: page.getByTestId('caveview-container') });
  await expect(viewer.getByTestId('caveview-container').locator('canvas').first()).toBeAttached({
    timeout: 30_000,
  });
  await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
  await expect(viewer.getByTestId('caveview-label-scale')).toBeVisible({ timeout: 30_000 });
  return viewer;
}

/**
 * Everything over the model that is pressed, and what — if anything — lies on top of each: the
 * size control, the viewer's tabs, and every control of its toolbar. Empty when each of them is
 * the topmost thing at its own middle, which is what a press there needs.
 *
 * Asked of the page rather than proved by pressing each: a press that lands on the wrong control
 * does something, and a test that pressed fifteen buttons to see which answered would be a test
 * of fifteen buttons.
 */
export async function coveredControls(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const named = (element: Element) =>
      element.getAttribute('data-testid') ?? element.getAttribute('title') ?? String(element.className);
    const all = (selector: string) => Array.from(document.querySelectorAll(selector));
    const pressed = [
      ...all('[data-testid="caveview-label-scale"]'),
      ...all('.cv-tab-box .tab').filter((tab) => getComputedStyle(tab).display !== 'none'),
      ...all('.cv-toolbar button, .cv-toolbar select'),
    ];
    if (pressed.length < 3) {
      return ['the model has none of the controls this looks for'];
    }
    return pressed.flatMap((control) => {
      const box = control.getBoundingClientRect();
      const top = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
      return top !== null && (top === control || control.contains(top))
        ? []
        : [`${named(control)} is under ${top === null ? 'nothing on the page' : named(top)}`];
    });
  });
}

/** Chooses a size from the control's menu. */
export async function chooseSize(page: Page, viewer: Locator, percent: number) {
  await viewer.getByTestId('caveview-label-scale').click();
  await page.getByRole('menuitem', { name: `${percent}%`, exact: true }).click();
  await expect(page.locator('.ant-dropdown:visible')).toHaveCount(0);
}

/** The size the control's menu marks as the one in force. */
export async function markedSize(page: Page, viewer: Locator): Promise<string> {
  const control = viewer.getByTestId('caveview-label-scale');
  await control.click();
  const selected = page.locator('.ant-dropdown:visible .ant-dropdown-menu-item-selected');
  await expect(selected).toHaveCount(1);
  const text = (await selected.innerText()).trim();
  // Put away by the button that opened it: Escape here is also the dialog's key.
  await control.click();
  await expect(page.locator('.ant-dropdown:visible')).toHaveCount(0);
  return text;
}
