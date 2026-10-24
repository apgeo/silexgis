// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { asPerson, registerAccount, signedInElsewhere, tryAsPerson } from './arrange.ts';
import { gotoRoute, login, overlayTreeNode, waitForScene3dReady } from './helpers.ts';
import { bearerToken } from './rastermapApi.ts';
import {
  boxOf,
  chooseInWorkspaceMenu,
  CORNER_CONTROLS,
  countGraphicsContexts,
  expectBeside,
  expectFillsWorkspace,
  graphics,
  markCanvas,
  markedCanvasStillThere,
  movePane,
  overlapsWith,
  pane,
  withheldLines,
} from './workspaceFlow.ts';

/**
 * The map workspace's panes: moved about by their own menus, the strip under the map, the docks
 * swapped, a pane filling the workspace and given its size back, the presets, and a saved
 * arrangement — every control pressed, by pointer or by key.
 *
 * What only a browser shows. The unit tests prove which arrangement each preset names and that a
 * moved pane's content is the same element; they cannot show the panes landing where the menu said,
 * the map keeping its canvas through every move, or the 3D scene and the survey viewer keeping
 * their graphics contexts — which is the one property a move must never cost.
 *
 * Every flow signs in as an account of its own: the arrangement is kept with the account, and the
 * administrator's is on screen in every other test of the run.
 */

test('the panes move by their own menus, the docks swap, and a pane fills the workspace and gives it back', async ({
  page,
}) => {
  test.setTimeout(300_000);
  const member = await registerAccount(page.request, 'workspace-panes');
  await login(page, member.email, member.password);
  await gotoRoute(page, '/map');

  // The 3D scene, opened by its button, sits right of the map as it always did.
  await page.getByTestId('map-scene3d-toggle').click();
  await waitForScene3dReady(page, 120_000);
  await expectBeside(page, 'scene3d', 'right');

  // Its menu sends it to the other side, under the map, and back.
  await movePane(page, 'workspace-scene3d', 'Left of the map');
  await expectBeside(page, 'scene3d', 'left');
  await movePane(page, 'workspace-scene3d', 'Under the map');
  await expectBeside(page, 'scene3d', 'bottom');
  // The strip under the map is resized from the keyboard, like every other separator.
  const strip = page.getByRole('separator', { name: 'Resize the map and the strip under it' });
  await expect(strip).toBeVisible();
  const stripBefore = (await boxOf(pane(page, 'scene3d'))).height;
  await strip.focus();
  for (let step = 0; step < 5; step++) {
    await page.keyboard.press('ArrowUp');
  }
  await expect.poll(async () => (await boxOf(pane(page, 'scene3d'))).height).toBeGreaterThan(stripBefore + 10);
  await movePane(page, 'workspace-scene3d', 'Right of the map');
  await expectBeside(page, 'scene3d', 'right');

  // Filled by its button, it covers the workspace and the panes under it cannot be reached.
  await page.getByTestId('workspace-scene3d-maximise').click();
  await expectFillsWorkspace(page, 'scene3d');
  await expect(pane(page, 'map')).toHaveAttribute('inert', '');
  await expect(pane(page, 'layers')).toHaveAttribute('inert', '');
  // The key gives it its size back.
  await page.keyboard.press('m');
  await expect(pane(page, 'map')).not.toHaveAttribute('inert', '');
  await expectBeside(page, 'scene3d', 'right');

  // The key fills the pane focus is in: the map, from its own canvas.
  await page.getByRole('application', { name: /^Map\./ }).focus();
  await page.keyboard.press('m');
  await expectFillsWorkspace(page, 'map');
  await expect(pane(page, 'scene3d')).toHaveAttribute('inert', '');
  await page.keyboard.press('m');
  await expect(pane(page, 'scene3d')).not.toHaveAttribute('inert', '');
  // And from the layer panel, the layer panel.
  await pane(page, 'layers').locator('button:not([disabled])').first().focus();
  await page.keyboard.press('m');
  await expectFillsWorkspace(page, 'layers');
  await page.getByTestId('workspace-layers-restore').click();
  await expect(pane(page, 'map')).not.toHaveAttribute('inert', '');
  // The map's own button does the same as the key.
  await page.getByTestId('map-maximise').click();
  await expectFillsWorkspace(page, 'map');
  await page.getByTestId('map-maximise').click();
  await expect(pane(page, 'scene3d')).not.toHaveAttribute('inert', '');

  // The key is in the shortcut help.
  await page.keyboard.press('?');
  const help = page.getByRole('dialog');
  await expect(help.getByText('Fill the workspace with the pane you are in')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(help).toHaveCount(0);

  // Swapping the docks puts the layer panel right of the map and the details panel left of it.
  await chooseInWorkspaceMenu(page, 'Swap the side panels');
  await expectBeside(page, 'layers', 'right');
  await expectBeside(page, 'details', 'left');
  // The bracket keys follow the sides: `[` folds what is on the left now, the details panel.
  await page.getByRole('application', { name: /^Map\./ }).focus();
  await page.keyboard.press('[');
  await expect.poll(async () => (await boxOf(pane(page, 'details'))).width).toBeLessThan(5);
  await page.keyboard.press('[');
  await expect.poll(async () => (await boxOf(pane(page, 'details'))).width).toBeGreaterThan(40);
  await chooseInWorkspaceMenu(page, 'Swap the side panels');
  await expectBeside(page, 'layers', 'left');
  await expectBeside(page, 'details', 'right');

  // The 3D scene's own window: the pane shuts as the window opens.
  const popup = page.waitForEvent('popup');
  await movePane(page, 'workspace-scene3d', 'In its own window');
  const window3d = await popup;
  await window3d.waitForURL(/\/panel\/scene3d/);
  await window3d.close();
  await expect(pane(page, 'scene3d')).toHaveCount(0);
  await expect(page.getByTestId('map-scene3d-toggle')).toHaveAttribute('aria-pressed', 'false');
});

test('the table under the map lists what is in view and what is picked, and the presets arrange the workspace in one press', async ({
  page,
}) => {
  test.setTimeout(300_000);
  const member = await registerAccount(page.request, 'workspace-presets');
  await login(page, member.email, member.password);
  await gotoRoute(page, '/map');

  // The table under the map takes the "In view" list over from the details panel.
  const rightTabs = page.locator('.map-right-tabs');
  await expect(rightTabs.getByRole('tab', { name: 'In view' })).toBeVisible();
  await chooseInWorkspaceMenu(page, 'Table under the map');
  await expectBeside(page, 'table', 'bottom');
  const table = page.getByTestId('workspace-table');
  await expect(table.getByRole('tab', { name: 'In view' })).toBeVisible();
  await expect(table.getByTestId('feature-list-panel')).toBeVisible();
  await expect(rightTabs.getByRole('tab', { name: 'In view' })).toHaveCount(0);
  await table.getByRole('tab', { name: /^Picked/ }).click();
  await expect(table.getByText('Nothing picked.', { exact: false })).toBeVisible();
  // Filled and given back by its own button, then shut by its own close button.
  await page.getByTestId('workspace-table-maximise').click();
  await expectFillsWorkspace(page, 'table');
  await page.getByTestId('workspace-table-maximise').click();
  await expectBeside(page, 'table', 'bottom');
  await page.getByTestId('workspace-table-close').click();
  await expect(pane(page, 'table')).toHaveCount(0);
  await expect(rightTabs.getByRole('tab', { name: 'In view' })).toBeVisible();

  // Map + 3D.
  await chooseInWorkspaceMenu(page, 'The 3D scene beside the map');
  await waitForScene3dReady(page, 120_000);
  await expectBeside(page, 'scene3d', 'right');

  // Trip planning: the table and the 3D scene, both under the map.
  await chooseInWorkspaceMenu(page, 'Under the map: what is in view');
  await expectBeside(page, 'table', 'bottom');
  await expectBeside(page, 'scene3d', 'bottom');
  // The menu marks the arrangement in force.
  await page.getByTestId('workspace-menu').click();
  await expect(
    page.getByRole('menuitem').filter({ hasText: 'Under the map: what is in view' }).locator('.anticon-check'),
  ).toHaveCount(1);
  await page.keyboard.press('Escape');

  // Survey review: the survey pane, empty and saying how to fill it; the layer panel folded.
  await chooseInWorkspaceMenu(page, "A cave's survey model beside the map");
  await expectBeside(page, 'caveview', 'right');
  await expect(pane(page, 'caveview').getByText('Pick a cave on the map')).toBeVisible();
  await expect(pane(page, 'scene3d')).toHaveCount(0);
  await expect(pane(page, 'table')).toHaveCount(0);
  await expect.poll(async () => (await boxOf(pane(page, 'layers'))).width).toBeLessThan(5);
  // With nothing open in it, the pane has nothing to send to a window.
  await page.getByTestId('map-caveview-move').click();
  await expect(page.getByRole('menuitem', { name: 'In its own window', exact: true })).toHaveAttribute(
    'aria-disabled',
    'true',
  );
  await page.keyboard.press('Escape');

  // Map: the workspace as it ships.
  await chooseInWorkspaceMenu(page, 'The map between its two panels');
  await expect(pane(page, 'caveview')).toHaveCount(0);
  await expect.poll(async () => (await boxOf(pane(page, 'layers'))).width).toBeGreaterThan(40);

  // Two screens: the 3D scene opens in a window of its own, and not here.
  const popup = page.waitForEvent('popup');
  await chooseInWorkspaceMenu(page, 'The map here, the 3D scene in a window');
  const window3d = await popup;
  await window3d.waitForURL(/\/panel\/scene3d/);
  await window3d.close();
  await expect(pane(page, 'scene3d')).toHaveCount(0);

  // Save the current arrangement, change it, and bring it back from the list.
  await chooseInWorkspaceMenu(page, 'Table under the map');
  await expectBeside(page, 'table', 'bottom');
  const name = `E2E arrangement ${Date.now()}`;
  const saved = page.waitForResponse(
    (response) => response.url().endsWith('/api/v1/me/preferences') && response.request().method() === 'PATCH',
  );
  await chooseInWorkspaceMenu(page, 'Save the current arrangement');
  await page.getByTestId('workspace-save-name').fill(name);
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  expect((await saved).ok()).toBe(true);
  await chooseInWorkspaceMenu(page, 'The map between its two panels');
  await expect(pane(page, 'table')).toHaveCount(0);
  await chooseInWorkspaceMenu(page, name);
  await expectBeside(page, 'table', 'bottom');

  // The arrangement is the account's: another browser signed in as the same member has it.
  const elsewhere = await signedInElsewhere(page.context().browser()!, member);
  try {
    await gotoRoute(elsewhere.page, '/map');
    await expect(elsewhere.page.locator('[data-workspace-pane="table"]')).toBeVisible({ timeout: 30_000 });
  } finally {
    await elsewhere.context.close();
  }
});

test('moving the 3D scene and a survey model never builds another graphics context, and closing one leaves none behind', async ({
  page,
  browser,
}) => {
  test.setTimeout(480_000);
  await countGraphicsContexts(page);

  // A cave with a survey model, arranged by the administrator in a browser of their own and
  // readable by any signed-in member.
  const admin = await signedInElsewhere(browser);
  const stamp = Date.now();
  let caveId: string | undefined;
  try {
    const caveTypes = await asPerson<{ id: number }[]>(admin.page, 'GET', '/api/v1/cave-types');
    caveId = (
      await asPerson<{ id: string }>(admin.page, 'POST', '/api/v1/caves', {
        name: `E2E Workspace Cave ${stamp}`,
        caveTypeId: caveTypes[0].id,
        visibility: 'authenticated',
        locationProtected: false,
        explorationStatus: 'unknown',
        isShowCave: false,
      })
    ).id;
    const upload = await uploadModel(admin.page, caveId);

    const member = await registerAccount(page.request, 'workspace-contexts');
    await login(page, member.email, member.password);

    // ---- The 3D scene ----
    await gotoRoute(page, '/map');
    await page.getByTestId('map-scene3d-toggle').click();
    await waitForScene3dReady(page, 120_000);
    const scene = '[data-workspace-pane="scene3d"]';
    await markCanvas(page, scene);
    const opened = await graphics(page);

    for (const place of ['Left of the map', 'Under the map', 'Right of the map', 'Under the map', 'Left of the map', 'Right of the map']) {
      await movePane(page, 'workspace-scene3d', place);
      await expectBeside(page, 'scene3d', place === 'Left of the map' ? 'left' : place === 'Under the map' ? 'bottom' : 'right');
    }
    await page.getByTestId('workspace-scene3d-maximise').click();
    await expectFillsWorkspace(page, 'scene3d');
    await page.getByTestId('workspace-scene3d-maximise').click();

    // Not one context made by six moves and a fill: the scene drawing now is the one first built.
    expect(await graphics(page)).toEqual(opened);
    expect(await markedCanvasStillThere(page, scene)).toBe(true);
    await expect(page.getByText('The 3D view could not be started')).toHaveCount(0);

    // Shutting the pane and opening it again does build a scene afresh — the scene is torn down
    // with its last view — and leaves no context of the old one alive in the page, however often.
    for (let round = 0; round < 3; round++) {
      await page.getByTestId('workspace-scene3d-close').click();
      await expect(pane(page, 'scene3d')).toHaveCount(0);
      await page.getByTestId('map-scene3d-toggle').click();
      await waitForScene3dReady(page, 120_000);
      expect((await graphics(page)).alive).toBeLessThanOrEqual(opened.alive);
    }
    await page.getByTestId('workspace-scene3d-close').click();

    // ---- A survey model ----
    await gotoRoute(page, `/map?model=${upload}`);
    const viewer = '[data-workspace-pane="caveview"]';
    await expect(page.locator(`${viewer} [data-testid="caveview-container"] canvas`).first()).toBeAttached({
      timeout: 60_000,
    });
    await expect(page.locator(`${viewer} [data-testid="caveview-loading"]`)).toHaveCount(0, { timeout: 60_000 });
    await markCanvas(page, `${viewer} [data-testid="caveview-container"]`);
    const modelOpened = await graphics(page);
    for (const place of ['Left of the map', 'Under the map', 'Right of the map']) {
      await movePane(page, 'map-caveview', place);
      await expectBeside(page, 'caveview', place === 'Left of the map' ? 'left' : place === 'Under the map' ? 'bottom' : 'right');
    }
    await page.getByTestId('map-caveview-maximise').click();
    await expectFillsWorkspace(page, 'caveview');
    await page.keyboard.press('m');
    expect(await graphics(page)).toEqual(modelOpened);
    expect(await markedCanvasStillThere(page, `${viewer} [data-testid="caveview-container"]`)).toBe(true);

    // Sent to its own window, the pane shuts and its context goes with it.
    const popup = page.waitForEvent('popup');
    await movePane(page, 'map-caveview', 'In its own window');
    const modelWindow = await popup;
    await modelWindow.waitForURL(/\/panel\/viewer3d\?model=/);
    await modelWindow.close();
    await expect(pane(page, 'caveview')).toHaveCount(0);
    await expect.poll(async () => (await graphics(page)).alive).toBeLessThan(modelOpened.alive);
  } finally {
    if (caveId) {
      await tryAsPerson(admin.page, 'DELETE', `/api/v1/caves/${caveId}`);
    }
    await admin.context.close();
  }
});

/** Uploads the demonstration survey to a cave as the person signed in on `page`; its id. */
async function uploadModel(page: Page, caveId: string): Promise<string> {
  const upload = await page.request.post(`/api/v1/caves/${caveId}/survey-models`, {
    headers: { Authorization: `Bearer ${await bearerToken(page)}` },
    multipart: {
      file: {
        name: 'P8_Master.3d',
        mimeType: 'application/octet-stream',
        buffer: readFileSync('e2e/fixtures/P8_Master.3d'),
      },
    },
  });
  expect(upload.ok(), `the survey upload answered ${upload.status()}`).toBeTruthy();
  return ((await upload.json()) as { id: string }).id;
}

test('the hidden survey lines notice never lies over the map controls, beside a pane or not', async ({ page }) => {
  test.setTimeout(240_000);
  const member = await registerAccount(page.request, 'workspace-notice');
  await withheldLines(page);
  await login(page, member.email, member.password);
  await gotoRoute(page, '/map');
  const node = overlayTreeNode(page, 'Cave centerlines');
  await expect(node).toBeVisible({ timeout: 15_000 });
  await node.locator('.ant-tree-checkbox').click();
  await page.getByTestId('map-dock-toggle-left').click();
  const notice = page.getByTestId('map-centerline-notice');
  await expect(notice).toBeVisible({ timeout: 15_000 });

  const clear = Object.fromEntries(CORNER_CONTROLS.map((selector) => [selector, false]));
  // The whole map's width.
  expect(await overlapsWith(page, notice, CORNER_CONTROLS)).toEqual(clear);
  // A refused scale adds a line under the scale box; the notice moves up rather than staying put.
  const scaleInput = page.getByLabel('Map scale');
  await scaleInput.fill('not a scale');
  await scaleInput.press('Enter');
  await expect(page.locator('.map-scale-error')).toBeVisible();
  expect(await overlapsWith(page, notice, [...CORNER_CONTROLS, '.map-scale-error'])).toEqual({
    ...clear,
    '.map-scale-error': false,
  });
  await scaleInput.fill('1:25 000');
  await scaleInput.press('Enter');
  await expect(page.locator('.map-scale-error')).toHaveCount(0);
  // A narrow map: the 3D scene and the table beside and under it, the map down to its least width.
  await page.setViewportSize({ width: 1024, height: 700 });
  await chooseInWorkspaceMenu(page, 'Under the map: what is in view');
  await expectBeside(page, 'table', 'bottom');
  await movePane(page, 'workspace-scene3d', 'Right of the map');
  await expectBeside(page, 'scene3d', 'right');
  // The preset gave the layer panel back, which says the same thing itself; fold it again.
  await page.getByTestId('map-dock-toggle-left').click();
  await expect(notice).toBeVisible({ timeout: 15_000 });
  expect(await overlapsWith(page, notice, CORNER_CONTROLS)).toEqual(clear);
  // The 3D scene's own controls are in its pane, never under the notice.
  expect((await overlapsWith(page, notice, ['[data-workspace-pane="scene3d"]']))['[data-workspace-pane="scene3d"]']).toBe(false);
  expect((await overlapsWith(page, notice, ['[data-workspace-pane="table"]']))['[data-workspace-pane="table"]']).toBe(false);
});

test('the side panels follow the theme: white in the light theme, the dark container colour in the dark one', async ({
  page,
}) => {
  const member = await registerAccount(page.request, 'workspace-theme');
  await login(page, member.email, member.password);
  await page.emulateMedia({ colorScheme: 'light' });
  await gotoRoute(page, '/map');
  const dock = page.locator('.map-workspace-panel').first();
  await expect(dock).toHaveCSS('background-color', 'rgb(255, 255, 255)');
  // The table under the map is a panel like the others.
  await chooseInWorkspaceMenu(page, 'Table under the map');
  await expect(page.locator('[data-panel]:has([data-workspace-pane="table"]) .map-workspace-panel').first()).toHaveCSS(
    'background-color',
    'rgb(255, 255, 255)',
  );
  // The theme follows the system unless chosen, as the page starts; the system says dark.
  await page.emulateMedia({ colorScheme: 'dark' });
  await gotoRoute(page, '/map');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(dock).not.toHaveCSS('background-color', 'rgb(255, 255, 255)');
  const darkBackground = await dock.evaluate((element) => getComputedStyle(element).backgroundColor);
  // Near-black: every channel low.
  const channels = (darkBackground.match(/\d+/g) ?? []).slice(0, 3).map(Number);
  expect(channels.every((channel) => channel < 64), `the dark panel background ${darkBackground}`).toBe(true);
  await chooseInWorkspaceMenu(page, 'Table under the map');
});

test("a new account starts on the installation's starting arrangement, once, and an arranged one is left alone", async ({
  page,
}) => {
  test.setTimeout(240_000);
  // The installation's answer is stood in for rather than published: publishing it would start
  // every other test's new account on it, in every worker of the run.
  await page.route(
    (url) => url.pathname === '/api/v1/ui-defaults',
    async (route) => {
      const response = await route.fetch();
      const body = (await response.json()) as { panel: Record<string, unknown> };
      await route.fulfill({ response, json: { ...body, panel: { ...body.panel, workspacePreset: 'trip' } } });
    },
  );
  const member = await registerAccount(page.request, 'workspace-start');
  try {
    // Signing in lands a new account on the map, and that first map is where the starting
    // arrangement is applied — not a later one. Applying it stores the arrangement, so a map
    // opened after it finds the table already placed and opens no scene: the scene is a visit's,
    // never stored. Going to the map again here raced the landing map's own answer, and lost
    // whenever the landing map had applied it first.
    await login(page, member.email, member.password);
    await expectBeside(page, 'table', 'bottom');
    await expectBeside(page, 'scene3d', 'bottom');
    await waitForScene3dReady(page, 120_000);

    // Arranged differently, the account keeps its own arrangement on the next visit.
    await page.getByTestId('workspace-table-close').click();
    await expect(pane(page, 'table')).toHaveCount(0);
    await gotoRoute(page, '/dashboard');
    await gotoRoute(page, '/map');
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 30_000 });
    await expect(pane(page, 'table')).toHaveCount(0);
    // Nor does the scene open by itself: only the starting arrangement, applied once, opened it.
    await expect(pane(page, 'scene3d')).toHaveCount(0);
  } finally {
    // Every page load asks for the installation's answer again, so one is usually still being
    // fetched through the stand-in when the test is done; it must not be reported against
    // whichever test this worker runs next.
    await page.unrouteAll({ behavior: 'ignoreErrors' });
  }
});
