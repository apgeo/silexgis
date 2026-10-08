// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import {
  centreOnDemoCave,
  deleteFeature,
  gotoRoute,
  login,
  openCaveFromRegistry,
  registryRow,
} from './helpers.ts';

// Working on the map by hand: picking a cluster, placing a cave, drawing, describing and
// restoring a surface feature, the pinned shortcuts, the context menu and the placement dialog.
//
// These seven were part of the smoke spec and are a file of their own for one reason: each acts
// on the map by clicking a fixed pixel of it, and the pixels are a few steps apart. One after
// another, each finds the map as it left it. Side by side, one test's click lands on the feature
// another has just drawn there — a context menu about that feature instead of about the empty
// ground, a selection of somebody else's point. So they take turns whatever the rest of the suite
// does, including in the runner's fast form, which otherwise spreads the tests of a file over the
// workers.
test.describe.configure({ mode: 'default' });

test('cluster click lists its member entrances in the panel', async ({ page }) => {
  await login(page);

  // Centre on the demo cave from the feature list. Search results deliberately carry no
  // coordinates, so picking one opens the record rather than moving the map — "show on
  // map" is the action that centres it.
  await centreOnDemoCave(page);

  // Get below the clustering threshold BEFORE clicking anything. Interleaving the two
  // cannot work: a click that lands on a single entrance selects it, and selecting one
  // flies the map back to zoom 15 — so a loop that zooms out and clicks in the same
  // breath undoes its own progress and never reaches a cluster. "Show on map" fits a
  // point, which lands at max zoom, so come down far enough to be sure.
  const canvas = page.locator('.map-canvas');
  for (let i = 0; i < 12; i++) {
    await page.locator('.ol-zoom-out').click();
  }

  // Now only the click is retried — for the zoom animation and the debounced bbox
  // loader, which settle at their own pace.
  await expect(async () => {
    await canvas.click();
    await expect(page.getByText(/entrances in this area/)).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });

  // Picking a member selects the entrance and the cave card takes over. Named exactly rather than
  // taken as the first: the members are listed alphabetically, and at this zoom the cluster holds
  // the other demo caves' entrances too ("Avenul Demo Vântului entrance" sorts first), so "the
  // first entrance" is whichever cave happens to sort ahead — not the one this test centred on.
  // "Main entrance" is the demo cave's own; a second entrance by that name in the cluster fails
  // here as ambiguous instead of quietly selecting the wrong cave.
  await page.getByRole('button', { name: 'Main entrance', exact: true }).click();
  await expect(page.getByRole('heading', { name: /Peștera Demo Mare/ })).toBeVisible({ timeout: 15_000 });
});

test('cave add on map: place a new cave with its entrance by clicking the canvas', async ({ page }) => {
  const caveName = `E2E Map Cave ${Date.now()}`;
  await login(page);

  const toolbar = page.locator('.map-edit-overlay');
  await expect(toolbar).toBeVisible();

  // Arm the tool, click the map, and fill the quick-create dialog.
  await toolbar.getByTestId('tool-add-cave').click();
  await page.locator('.map-canvas').click({ position: { x: 400, y: 240 } });
  const modal = page.getByRole('dialog');
  await expect(modal.getByText(/New cave here/)).toBeVisible();
  await modal.getByLabel('Name').fill(caveName);
  // Keyboard-select the first option: clicking dropdown items inside a modal is
  // flaky (the animated dropdown can close mid-click and swallow the action).
  await modal.getByLabel('Type', { exact: true }).click();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await modal.getByLabel('Entrance type').click();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await modal.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The new cave is selected in the right dock; its entrance count proves the
  // first entrance landed with the create.
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  await expect(
    page.locator('.map-right-tabs .ant-descriptions-row', { hasText: 'Entrances' }),
  ).toContainText('1');

  // Clean up from the registry (entrances cascade).
  await openCaveFromRegistry(page, caveName);
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await page.waitForURL(/\/caves$/);
  // Counted rather than "not visible": the detail page stays mounted for a tick after the
  // URL changes, and its heading and its history timeline both carry the name. Two matches
  // fail a visibility assertion outright, where a count waits for the list to render — and
  // then proves the row is really gone rather than merely hidden.
  await expect(page.getByText(caveName)).toHaveCount(0);
});

test('surface feature draw, attributes, selection and table round-trip', async ({ page }) => {
  const featureName = `E2E Sinkhole ${Date.now()}`;
  await login(page);

  const toolbar = page.locator('.map-edit-overlay');
  await expect(toolbar).toBeVisible();

  // Pick the feature type from the symbol palette (carries a typed-properties schema).
  // Picking a symbol arms drawing; the explicit draw click below just re-affirms it.
  await toolbar.getByRole('button', { name: /Feature type/ }).click();
  await page.getByRole('button', { name: 'Sinkhole / Doline' }).click();

  // Draw a point by clicking the map canvas (icon-only button → name "edit").
  await toolbar.getByRole('button', { name: 'edit' }).click();
  const canvas = page.locator('.map-canvas');
  await canvas.click({ position: { x: 420, y: 260 } });

  // Attribute modal opens for the freshly drawn feature; schema fields render.
  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible();
  await modal.getByLabel('Name').fill(featureName);
  await modal.getByLabel('Depth (m)').fill('12.5');
  await modal.getByRole('button', { name: 'OK', exact: true }).click();

  // Batched save posts the feature and reloads the layer.
  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
  await toolbar.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await reloaded;

  // Click the same spot: the saved feature is selected and the detail card
  // shows the schema-labeled property.
  await canvas.click({ position: { x: 420, y: 260 } });
  await expect(page.getByRole('heading', { name: featureName })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Depth (m)')).toBeVisible();
  // Scoped to the property table: the panel's history timeline logs the same value.
  await expect(page.locator('.ant-descriptions').getByText('12.5')).toBeVisible();

  // The features table lists it; delete from the row actions (cleanup).
  const row = await registryRow(page, featureName);
  await expect(row).toBeVisible({ timeout: 15_000 });
  await expect(row.getByText('Sinkhole / Doline')).toBeVisible();
  await row.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(featureName)).not.toBeVisible();
});

test('pinned feature-type shortcuts and the map chrome toggle', async ({ page }) => {
  await login(page);

  const toolbar = page.locator('.map-edit-overlay');
  await expect(toolbar).toBeVisible();
  const paletteTrigger = toolbar.getByTestId('feature-palette-trigger');

  // Pin a type from the palette: a one-click arm shortcut appears on the toolbar.
  await paletteTrigger.click();
  const paletteItem = page.locator('.feature-palette-item-wrap').filter({ hasText: 'Sinkhole / Doline' });
  await paletteItem.hover();
  await paletteItem.getByRole('button', { name: 'Pin to toolbar' }).click();
  await paletteTrigger.click(); // close the palette

  const shortcut = toolbar.locator('[data-testid^="pinned-type-"]');
  await expect(shortcut).toBeVisible();

  // The shortcut arms drawing for its type in one click.
  await shortcut.click();
  await expect(shortcut).toHaveClass(/ant-btn-primary/);

  // Draw a point so there is a pending edit, then dismiss the attribute dialog.
  await page.locator('.map-canvas').click({ position: { x: 400, y: 240 } });
  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible();
  await modal.getByRole('button', { name: 'Cancel' }).click();

  // Hiding the chrome hides the on-canvas toolbars but keeps the unsaved-edits pill.
  await page.getByTestId('map-chrome-toggle').click();
  await expect(toolbar).toBeHidden();
  await expect(page.locator('.map-search-overlay')).toBeHidden();
  const pill = page.getByTestId('map-dirty-pill');
  await expect(pill).toBeVisible();

  // The pill brings the chrome (and with it Save/Discard) back.
  await pill.click();
  await expect(toolbar).toBeVisible();

  // Cleanup: discard the pending edit; unpinning removes the shortcut.
  await toolbar.getByRole('button', { name: 'close' }).click();
  await paletteTrigger.click();
  await paletteItem.hover();
  await paletteItem.getByRole('button', { name: 'Unpin from toolbar' }).click();
  await paletteTrigger.click();
  await expect(shortcut).toHaveCount(0);
});

test('map context menu: typed add-here, cave placement and coordinate copy', async ({ page }) => {
  const featureName = `E2E Context Sinkhole ${Date.now()}`;
  await login(page);

  const canvas = page.locator('.map-canvas');

  // Right-click opens the typed add menu; picking a point type places it there.
  await canvas.click({ button: 'right', position: { x: 430, y: 250 } });
  await expect(page.getByText('Copy coordinates')).toBeVisible();
  await page.getByText('Add feature').hover();
  await page.getByRole('menuitem', { name: 'Sinkhole / Doline' }).click();

  // Picking closes the menu entirely (its layers would sit above the dialog).
  await expect(page.getByRole('menuitem', { name: 'Sinkhole / Doline' })).toBeHidden();
  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible();
  await modal.getByLabel('Name').fill(featureName);
  await modal.getByRole('button', { name: 'OK', exact: true }).click();

  // The placement is a pending edit saved through the normal batched flow.
  const toolbar = page.locator('.map-edit-overlay');
  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
  await toolbar.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await reloaded;

  // "New cave here" opens the create dialog without needing a second click.
  await canvas.click({ button: 'right', position: { x: 300, y: 200 } });
  await page.getByRole('menuitem', { name: 'New cave here' }).click();
  await expect(page.getByRole('dialog').getByText(/New cave here/)).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();

  // Cleanup: remove the created feature via the registry.
  await deleteFeature(page, featureName);
});

test('dialog placement flip: cave-add continues as a side panel with values intact', async ({ page }) => {
  const caveName = `E2E Flip Cave ${Date.now()}`;
  await login(page);

  const toolbar = page.locator('.map-edit-overlay');
  await toolbar.getByTestId('tool-add-cave').click();
  await page.locator('.map-canvas').click({ position: { x: 380, y: 300 } });

  const modal = page.locator('.ant-modal');
  await expect(modal.getByText(/New cave here/)).toBeVisible();
  await modal.getByLabel('Name').fill(caveName);

  // Flip to the side panel mid-edit: same dialog, docked, values kept, no mask.
  await modal.getByTestId('dialog-placement-flip').click();
  const drawer = page.locator('.ant-drawer');
  await expect(drawer.getByText(/New cave here/)).toBeVisible();
  await expect(drawer.getByLabel('Name')).toHaveValue(caveName);
  await expect(page.locator('.ant-drawer-mask')).toHaveCount(0);

  // Finish the create from the drawer (keyboard-select per the modal flow above).
  await drawer.getByLabel('Type', { exact: true }).click();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await drawer.getByLabel('Entrance type').click();
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await drawer.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });

  // The preference persisted; restore the default for other flows and clean up.
  await toolbar.getByTestId('tool-add-cave').click();
  await page.locator('.map-canvas').click({ position: { x: 420, y: 320 } });
  await expect(page.locator('.ant-drawer').getByText(/New cave here/)).toBeVisible();
  await page.locator('.ant-drawer').getByTestId('dialog-placement-flip').click();
  await expect(page.locator('.ant-modal').getByText(/New cave here/)).toBeVisible();
  await page.locator('.ant-modal').getByRole('button', { name: 'Cancel' }).click();

  await openCaveFromRegistry(page, caveName);
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await page.waitForURL(/\/caves$/);
  // Counted rather than "not visible": the detail page stays mounted for a tick after the
  // URL changes, and its heading and its history timeline both carry the name. Two matches
  // fail a visibility assertion outright, where a count waits for the list to render — and
  // then proves the row is really gone rather than merely hidden.
  await expect(page.getByText(caveName)).toHaveCount(0);
});

test('surface feature history records edits and restores in the map panel', async ({ page }) => {
  const featureName = `E2E Hist Feat ${Date.now()}`;
  await login(page);

  // Selection is by map click at a fixed pixel, so purge any e2e features an earlier
  // aborted run left stacked on that spot — otherwise the click selects a stale one. Named
  // rather than swept: only these two flows draw on this pixel, and sweeping everything
  // stamped E2E takes live subjects out from under whatever else is running beside this.
  await deleteFeatureRows(page, /E2E (Hist Feat|Sinkhole) /);
  // The map by its own address, and waited for: a hard navigation signs in again before anything
  // is drawn, which under load outlasts the five seconds an unqualified expectation allows, and
  // the root address is whatever the account's landing preference says — which another test of
  // this file changes for the same account.
  await gotoRoute(page, '/map');

  const toolbar = page.locator('.map-edit-overlay');
  await expect(toolbar).toBeVisible({ timeout: 15_000 });
  await toolbar.getByRole('button', { name: /Feature type/ }).click();
  await page.getByRole('button', { name: 'Sinkhole / Doline' }).click();

  // Draw and save a point feature.
  await toolbar.getByRole('button', { name: 'edit' }).click();
  const canvas = page.locator('.map-canvas');
  await canvas.click({ position: { x: 420, y: 260 } });
  const modal = page.getByRole('dialog');
  await expect(modal.getByText('New feature')).toBeVisible();
  await modal.getByLabel('Name').fill(featureName);
  await modal.getByRole('button', { name: 'OK', exact: true }).click();
  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/features') && r.ok());
  await toolbar.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await reloaded;

  // Select it; edit the description twice so the newest event carries an old→new pair.
  await canvas.click({ position: { x: 420, y: 260 } });
  await expect(page.getByRole('heading', { name: featureName })).toBeVisible({ timeout: 15_000 });
  await editFeatureDescription(page, 'Desc One');
  await editFeatureDescription(page, 'Desc Two');

  // The selection panel's history section records the change as an old→new row. It starts
  // closed — long, rarely read, and it used to push the fields people do read below the fold —
  // so opening it is part of the flow now, and a closed section has fetched nothing until then.
  const historySection = page.getByTestId('panel-section-history');
  await expect(historySection).toBeVisible({ timeout: 15_000 });
  if ((await historySection.getAttribute('data-open')) !== 'true') {
    await historySection.getByRole('button', { expanded: false }).click();
  }
  const latest = historySection.getByRole('row', { name: /Desc One.*Desc Two/ });
  await expect(latest).toBeVisible({ timeout: 15_000 });

  // Restore the previous value; the feature detail reverts and a toast confirms.
  await latest.getByLabel('Restore this value').click();
  await expect(page.getByText('Value restored.')).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.ant-descriptions').first().getByText('Desc One')).toBeVisible({ timeout: 15_000 });

  // Cleanup from the features table.
  await deleteFeatureRows(page, new RegExp(featureName));
});

// Deletes every features-table row matching `pattern` (tolerant of leftovers from aborted runs).
async function deleteFeatureRows(page: Page, pattern: RegExp) {
  const listed = page.waitForResponse((r) => r.url().includes('/api/v1/features') && r.ok());
  await page.goto('/features');
  await listed;
  // Wait for the table body to actually paint (a data row or the empty placeholder) before
  // the non-retrying count() — avoids racing the response→render gap without a fixed sleep.
  // A horizontally scrollable table also carries a zero-height aria-hidden measure row as
  // its first tbody child, so exclude that or the visibility wait resolves to the invisible.
  await expect(page.locator('.ant-table-tbody tr:not(.ant-table-measure-row)').first()).toBeVisible();
  const rows = page.getByRole('row', { name: pattern });
  for (let remaining = await rows.count(); remaining > 0; remaining--) {
    await rows.first().getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK', exact: true }).click();
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
}

async function editFeatureDescription(page: Page, value: string) {
  // antd folds an icon button's icon aria-label into its accessible name ("edit Edit"),
  // so match the visible text instead of an exact role name.
  await page.locator('button', { hasText: 'Edit' }).click();
  const modal = page.getByRole('dialog');
  await expect(modal.getByText('Edit feature')).toBeVisible();
  await modal.getByLabel('Description').fill(value);
  await modal.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.locator('.ant-descriptions').getByText(value)).toBeVisible({ timeout: 15_000 });
}
