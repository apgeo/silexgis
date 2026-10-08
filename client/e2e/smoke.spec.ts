// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { ownContext, test } from './consoleGuard.ts';
import { gotoRoute, login, openCaveFromRegistry, overlayTreeNode } from './helpers.ts';

test('login, map workspace and cave registry work end to end', async ({ page }) => {
  await login(page);
  // Exact: the drawn default's group is open, and it also holds "OpenStreetMap Humanitarian" and
  // "OpenStreetMap France", so all three are on screen.
  await expect(page.getByRole('radio', { name: 'OpenStreetMap', exact: true })).toBeChecked();

  // The composer tree lists the entrance overlay with its checkbox on.
  await expect(overlayTreeNode(page, 'Cave entrances').locator('.ant-tree-checkbox-checked')).toBeVisible();
  await expect(page.getByText('Base layers')).toBeVisible();
  await expect(page.getByText(/Click a feature on the map/)).toBeVisible();

  // Cave registry lists demo data.
  await page.goto('/caves');
  // Narrowed to the demonstration caves: the registry shows twenty at a time by name, and the
  // caves other specs leave behind sort ahead of these.
  await page.getByPlaceholder('Search name or toponym…').fill('Demo');
  await expect(page.getByText('Peștera Demo Mare')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Avenul Demo Protejat')).toBeVisible();

  // Detail page of the protected demo cave, with the entrance editor available.
  await page.getByText('Avenul Demo Protejat').click();
  await expect(page.getByRole('heading', { name: 'Avenul Demo Protejat' })).toBeVisible();
  await expect(page.getByRole('button', { name: /Add entrance/ })).toBeVisible();
});

test('cave and entrance create/edit round-trip', async ({ page }) => {
  const caveName = `E2E Smoke Cave ${Date.now()}`;
  await login(page);

  // Create a cave with the minimum required fields.
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  // antd Select options: role="option" is its hidden a11y mirror; click the visible item.
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });

  // Add an entrance: place the point by clicking the mini map (draw), then override
  // with exact manual coordinates — both input paths of the editor.
  await page.getByRole('button', { name: /Add entrance/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.locator('.ol-viewport')).toBeVisible();
  const lonInput = page.getByLabel('Longitude');
  const initialLon = await lonInput.inputValue();
  await dialog.locator('.ol-viewport').click({ position: { x: 480, y: 90 } });
  await expect(lonInput).not.toHaveValue(initialLon);
  await lonInput.fill('25.123456');
  await page.getByLabel('Latitude').fill('45.654321');
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('45.65432°N 25.12346°E')).toBeVisible({ timeout: 15_000 });

  // Edit the cave and verify the change lands on the detail page. Text filtering keeps
  // the header buttons distinct from the entrance row's text-less icon buttons.
  await page.locator('button', { hasText: 'Edit' }).click();
  await page.getByLabel('Region').fill('Testland');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  // Scoped to the detail table: the history timeline beside it reports the same new value.
  await expect(page.locator('.ant-descriptions').getByText('Testland')).toBeVisible();

  // Clean up: delete the cave (entrances cascade server-side).
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await page.waitForURL(/\/caves$/);
  // Counted rather than "not visible": the detail page stays mounted for a tick after the
  // URL changes, and its heading and its history timeline both carry the name. Two matches
  // fail a visibility assertion outright, where a count waits for the list to render — and
  // then proves the row is really gone rather than merely hidden.
  await expect(page.getByText(caveName)).toHaveCount(0);
});

/**
 * Every control on the cave page that is supposed to ask for a file actually asks for one.
 *
 * The existing tests reach past these controls: they set files on the hidden input directly,
 * which is the right way to test what happens to an upload, and which passes just as happily
 * when the visible trigger has stopped opening anything. That failure is invisible from
 * everywhere else — nothing throws, nothing is logged, and the page looks correct — so it is
 * checked here for what it is: pressing the thing a person presses, and requiring the browser
 * to raise a file chooser.
 */
test('the cave page controls that ask for a file open a file chooser', async ({ page }) => {
  await login(page);

  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  await expect(page.getByText('Photos & documents')).toBeVisible({ timeout: 15_000 });

  // A survey model does not go straight to a file chooser: it comes with questions about where
  // the survey sits, so "Upload model" opens a dialog first and the drop zone inside that dialog is
  // what asks for the file. Both presses are the person's, so both are made here.
  await page.getByRole('button', { name: 'Upload model' }).click();
  const modelDialog = page.getByRole('dialog');
  const modelDropZone = modelDialog.locator('.ant-upload-drag');
  await expect(modelDropZone).toBeVisible({ timeout: 15_000 });
  const modelChooser = page.waitForEvent('filechooser', { timeout: 10_000 });
  await modelDropZone.click();
  await modelChooser;
  await modelDialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(modelDialog).toBeHidden();

  const triggers = [
    page.getByRole('button', { name: 'Upload centerline' }),
    page.locator('.ant-upload-drag').first(),
  ];

  for (const trigger of triggers) {
    await expect(trigger).toBeVisible({ timeout: 15_000 });
    // Playwright intercepts the chooser rather than letting the operating system draw it, so
    // the event arriving is the assertion; nothing is chosen and no upload follows.
    const chooser = page.waitForEvent('filechooser', { timeout: 10_000 });
    await trigger.click();
    await chooser;
  }
});

test('pop-out registry drives the main map across windows', async ({ page, context }) => {
  await login(page);

  // Open the registry pop-out; it shares the session and the workspace bus.
  const popupPromise = context.waitForEvent('page');
  await page.getByRole('button', { name: 'export' }).click();
  const popup = await popupPromise;
  await expect(popup.getByText('Cave registry')).toBeVisible({ timeout: 15_000 });

  // Picking a cave in the pop-out selects it in the MAIN window's dock. Narrowed to first: the
  // registry shows a page at a time, and which page a demonstration cave is on depends on how
  // many caves the tests before this one happened to make.
  await popup.getByPlaceholder('Search name or toponym…').fill('Peștera Demo Mare');
  await popup.getByText('Peștera Demo Mare').click();
  await expect(page.getByRole('heading', { name: 'Peștera Demo Mare' })).toBeVisible({ timeout: 15_000 });
  await popup.close();
});

test('3D survey model: upload, embedded viewer and cross-window 3D panel', async ({ page, context }) => {
  const caveName = `E2E 3D Cave ${Date.now()}`;
  await login(page);

  // Create a cave to hold the model.
  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });

  // Upload the committed Survex .3d fixture; the row appears with its format tag. "Upload model"
  // opens a dialog whose drop zone asks for the file; a Survex .3d may carry its own placement,
  // so the dialog's submit is enabled with no position answered.
  await page.getByRole('button', { name: 'Upload model' }).click();
  const uploadDialog = page.getByRole('dialog');
  await expect(uploadDialog.locator('.ant-upload-drag')).toBeVisible({ timeout: 15_000 });
  const fileChooserPromise = page.waitForEvent('filechooser');
  await uploadDialog.locator('.ant-upload-drag').click();
  await (await fileChooserPromise).setFiles('e2e/fixtures/P8_Master.3d');
  await uploadDialog.getByRole('button', { name: 'Upload model' }).click();
  await expect(page.getByText('Survex .3d')).toBeVisible({ timeout: 15_000 });

  // The embedded viewer parses the survey: the canvas mounts and the spinner clears
  // only after CaveView fires its load-complete event.
  await page.getByRole('button', { name: /View in 3D/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByTestId('caveview-container').locator('canvas').first())
    .toBeAttached({ timeout: 30_000 });
  await expect(dialog.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
  await page.keyboard.press('Escape');
  await expect(dialog).not.toBeVisible();

  // Multi-monitor scenario: a registry window and a 3D window, synced over the bus —
  // picking the cave in one renders its model in the other.
  const viewer = await context.newPage();
  await viewer.goto('/panel/viewer3d');
  await expect(viewer.getByText(/Select a cave with a 3D model/)).toBeVisible({ timeout: 20_000 });

  const registry = await context.newPage();
  await registry.goto('/panel/registry');
  await registry.getByPlaceholder(/Search/).fill(caveName);
  await registry.getByText(caveName).click();
  await expect(viewer.getByTestId('caveview-container').locator('canvas').first())
    .toBeAttached({ timeout: 30_000 });
  await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
  await registry.close();
  await viewer.close();

  // Clean up: delete the model, then the cave.
  await page.locator('.ant-card', { hasText: '3D survey models' })
    .getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('No 3D models yet')).toBeVisible({ timeout: 15_000 });
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await page.waitForURL(/\/caves$/);
});

test('cave centerline: upload, computed length and map overlay toggle', async ({ page }) => {
  const caveName = `E2E Centerline Cave ${Date.now()}`;
  await login(page);

  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });

  // Upload the GPX track; the row shows the PostGIS-computed geodesic length.
  const card = page.locator('.ant-card', { hasText: 'Centerlines' });
  const fileChooserPromise = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: /Upload centerline/ }).click();
  await (await fileChooserPromise).setFiles('e2e/fixtures/e2e-track.gpx');
  await expect(card.getByText('e2e-track')).toBeVisible({ timeout: 15_000 });
  await expect(card.getByText(/[\d,.]+ m/)).toBeVisible();

  // The workspace offers the centerline overlay but leaves it off: it is the heaviest overlay
  // there is, so it is opt-in. Switching it on loads it and says which representation arrived.
  // The map by its own address, and waited for: a hard navigation signs in again before anything
  // is drawn, and the root address is whatever the account's landing preference says.
  await gotoRoute(page, '/map');
  const centerlineNode = overlayTreeNode(page, 'Cave centerlines');
  await expect(centerlineNode).toBeVisible({ timeout: 15_000 });
  await expect(centerlineNode.locator('.ant-tree-checkbox-checked')).toHaveCount(0);

  await centerlineNode.locator('.ant-tree-checkbox').click();
  await expect(centerlineNode.locator('.ant-tree-checkbox-checked')).toBeVisible();
  await expect(page.getByText(/Showing (passage outlines|full survey detail)/)).toBeVisible({ timeout: 15_000 });

  // Clean up: delete the centerline, then the cave.
  await openCaveFromRegistry(page, caveName);
  await card.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(card.getByText('No centerlines yet')).toBeVisible({ timeout: 15_000 });
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await page.waitForURL(/\/caves$/);
});

test('saved views: save, share anonymously, delete', async ({ page, browser, context }) => {
  const viewName = `E2E View ${Date.now()}`;
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await login(page);

  // Save the current workspace as a named view.
  const dock = page.locator('.map-workspace-panel').first();
  await dock.getByPlaceholder('View name…').fill(viewName);
  await dock.getByRole('button', { name: 'save' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  // Typography.Link without href carries no link role; match by text.
  const viewLink = page.locator('.map-workspace-panel').first().getByText(viewName);
  await expect(viewLink).toBeVisible();

  // Share: the link lands on the clipboard; an anonymous browser can open it.
  await page.getByRole('listitem').filter({ hasText: viewName })
    .getByRole('button', { name: 'link' }).click();
  await expect(page.getByText('Share link copied.')).toBeVisible({ timeout: 15_000 });
  const sharedUrl = await page.evaluate(() => navigator.clipboard.readText());
  expect(sharedUrl).toContain('/shared/view/');

  const anonymous = await ownContext(browser);
  const anonymousPage = await anonymous.newPage();
  await anonymousPage.goto(sharedUrl);
  await anonymousPage.waitForURL(/\/shared\/view\//); // no login redirect
  await expect(anonymousPage.getByRole('heading', { name: viewName })).toBeVisible({ timeout: 15_000 });
  await expect(anonymousPage.locator('.ol-viewport')).toBeVisible();
  await anonymous.close();

  // Cleanup.
  await page.getByRole('listitem').filter({ hasText: viewName })
    .getByRole('button', { name: 'delete' }).click();
  await expect(viewLink).not.toBeVisible({ timeout: 15_000 });
});

test('caving groups and per-object permission grants', async ({ page }) => {
  const cavingGroupName = `E2E Caving Group ${Date.now()}`;
  await login(page);

  // Create a caving group (the admin holds Manager rights) and see ourselves as owner.
  await page.goto('/caving-groups');
  await page.getByRole('button', { name: /New caving group/ }).click();
  await page.getByLabel('Name', { exact: true }).fill(cavingGroupName);
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });
  const cavingGroupRow = page.getByRole('row', { name: new RegExp(cavingGroupName) });
  await expect(cavingGroupRow).toBeVisible();
  await cavingGroupRow.getByRole('button', { name: 'Manage' }).click();
  await expect(page.getByText('Owner')).toBeVisible({ timeout: 15_000 });
  await page.keyboard.press('Escape');

  // Grant the caving group Read on a cave through the permissions modal.
  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  await page.getByRole('button', { name: /Permissions/ }).click();
  const modal = page.getByRole('dialog');
  await modal.locator('.ant-select').first().click();
  await page.locator('.ant-select-item-option', { hasText: 'Caving group' }).click();
  await modal.locator('.ant-select').nth(1).click();
  // Typed, not scrolled to: the dropdown virtualises, and every earlier run leaves its
  // caving group behind, so the newest one is far below the rendered window.
  await page.keyboard.type(cavingGroupName);
  await page.locator('.ant-select-item-option', { hasText: cavingGroupName }).click();
  await modal.getByRole('button', { name: /Add/ }).click();
  await expect(modal.getByText(cavingGroupName)).toBeVisible();
  await modal.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });

  // Reopen: the grant persisted; then remove it — sweeping grants left behind by
  // previously aborted runs too (they accumulate on the shared demo cave and make a
  // bare "delete" click ambiguous).
  await page.getByRole('button', { name: /Permissions/ }).click();
  await expect(modal.getByText(cavingGroupName)).toBeVisible({ timeout: 15_000 });
  const e2eGrantRows = modal.getByRole('row', { name: /E2E Caving Group/ });
  while ((await e2eGrantRows.count()) > 0) {
    await e2eGrantRows.first().getByRole('button', { name: 'delete' }).click();
  }
  await modal.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });
});

test('trip log with participants, tags and the audit trail', async ({ page }) => {
  const title = `E2E Trip ${Date.now()}`;
  const tagName = `e2e-tag-${Date.now()}`;
  await login(page);

  // Create a trip with a guest participant.
  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  // Scoped to the dialog and matched by pattern: the listing behind it carries a sortable column
  // header that also announces itself as "Title", and the field's own accessible name is
  // "* Title" because the required marker is part of the label text rather than decoration
  // beside it. A bare label lookup matches two elements; matching "Title" exactly matches none.
  await page
    .getByRole('dialog', { name: 'New trip log' })
    .getByRole('textbox', { name: /^\*?\s*Title$/ })
    .fill(title);
  await page.getByRole('button', { name: /Add participant/ }).click();
  await page.getByPlaceholder('Participant name').fill('Guest Caver');
  await page.getByRole('button', { name: /Add proposer/ }).click();
  await page.getByPlaceholder('Proposer name').fill('Ana Proposer');
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  // Scope to the detail-page tags — the names also surface in the history timeline below.
  await expect(page.locator('.ant-tag', { hasText: 'Guest Caver' })).toBeVisible();
  // The proposer (a distinct role from attendee) round-trips to the detail page.
  await expect(page.locator('.ant-tag', { hasText: 'Ana Proposer' })).toBeVisible();

  // Tag it inline (the input autofocuses; Enter submits).
  await page.getByText('Add tag').click();
  await page.keyboard.type(tagName);
  await page.keyboard.press('Enter');
  await expect(page.locator('.ant-tag', { hasText: tagName })).toBeVisible({ timeout: 15_000 });

  // The admin audit trail recorded the creation.
  await page.goto('/admin/audit');
  await expect(page.getByText('Audit trail')).toBeVisible();
  await expect(page.getByRole('cell', { name: 'TripLog' }).first()).toBeVisible({ timeout: 15_000 });

  // Cleanup: delete the trip from its detail page.
  await gotoRoute(page, '/trip-logs');
  await page.getByText(title).click();
  // The trip's page first, and then its own delete by its own name. The list this click leaves
  // carries a "Deleted trips" button, which is a button whose name contains "Delete" and which
  // is still on screen for the moment the trip's page takes to arrive: reached for by a pattern,
  // it is the one that gets pressed, and the run stands on the list of deleted trips waiting
  // for a confirmation nobody asked.
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  await page.getByTestId('trip-delete').click();
  await page.getByRole('tooltip').getByRole('button', { name: 'OK', exact: true }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
  // Back on the list, the deleted trip's row is gone (scope to a table cell — the title also
  // lingered briefly in the detail heading/timeline during the post-delete navigation).
  await expect(page.getByRole('cell', { name: title })).toHaveCount(0, { timeout: 15_000 });
});

test('georeferenced raster upload, COG processing, map overlay and delete', async ({ page }) => {
  await login(page);

  // Upload a small georeferenced GeoTIFF on the raster tab; a background job
  // converts it to a Cloud-Optimized GeoTIFF.
  await page.goto('/geodata');
  await page.getByRole('tab', { name: 'Raster maps' }).click();
  await page.locator('input[type=file][accept=".tif,.tiff"]').setInputFiles('e2e/fixtures/e2e-map.tif');
  // Newest first (the table sorts by update time) — robust to leftovers from aborted runs.
  const row = page.getByRole('row', { name: /e2e-map/ }).first();
  await expect(row).toBeVisible({ timeout: 15_000 });
  await expect(row.getByText('Ready')).toBeVisible({ timeout: 30_000 });

  // Show on map: the raster's catalog checkbox is on and its layer joins the
  // composer tree, where every active overlay row carries a transparency slider.
  await row.getByRole('button', { name: 'aim' }).click();
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('checkbox', { name: 'e2e-map' }).first()).toBeChecked();
  const treeRow = page
    .locator('.layer-composer .ant-tree-treenode')
    .filter({ hasText: 'e2e-map' })
    .first();
  await expect(treeRow).toBeVisible();
  await expect(treeRow.locator('.ant-slider')).toBeVisible();

  // Swipe-compare: toggling shows the draggable divider over the canvas.
  await page.getByTestId('raster-swipe-toggle').click();
  await expect(page.getByTestId('raster-swipe-handle')).toBeVisible();
  await page.getByTestId('raster-swipe-toggle').click();
  await expect(page.getByTestId('raster-swipe-handle')).not.toBeVisible();

  // Cleanup: remove every e2e raster (earlier aborted runs may have left extras).
  await page.goto('/geodata');
  await page.getByRole('tab', { name: 'Raster maps' }).click();
  const rows = page.getByRole('row', { name: /e2e-map/ });
  await expect(rows.first()).toBeVisible({ timeout: 15_000 });
  for (let remaining = await rows.count(); remaining > 0; remaining--) {
    await rows.first().getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK', exact: true }).click();
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
});

test('layer composer: entrance heatmap toggle and per-base opacity', async ({ page }) => {
  await login(page);

  // The entrance-density heatmap joins the composer like any overlay, off by default.
  const heatmapRow = overlayTreeNode(page, 'Entrance heatmap');
  await expect(heatmapRow).toBeVisible();
  await expect(heatmapRow.locator('.ant-tree-checkbox-checked')).toHaveCount(0);

  // Enabling it renders a Heatmap canvas over the viewport; toggling off removes it.
  const viewportCanvases = page.locator('.ol-viewport canvas');
  const canvasesBefore = await viewportCanvases.count();
  await heatmapRow.locator('.ant-tree-checkbox').click();
  await expect(heatmapRow.locator('.ant-tree-checkbox-checked')).toBeVisible();
  await expect(async () => {
    expect(await viewportCanvases.count()).toBeGreaterThan(canvasesBefore);
  }).toPass({ timeout: 10_000 });
  await heatmapRow.locator('.ant-tree-checkbox').click();
  await expect(heatmapRow.locator('.ant-tree-checkbox-checked')).toHaveCount(0);
  await expect(async () => {
    expect(await viewportCanvases.count()).toBe(canvasesBefore);
  }).toPass({ timeout: 10_000 });

  // Each base row carries its own opacity slider; dimming the active base (OSM)
  // drops its handle below 100% — the base is not radio-only anymore.
  // Found by its radio's exact name: the rows of the other OpenStreetMap sources contain that
  // text too, and their sliders are just as much at 100%.
  const osmRow = page
    .locator('.base-layer-row')
    .filter({ has: page.getByRole('radio', { name: 'OpenStreetMap', exact: true }) });
  const handle = osmRow.locator('.ant-slider-handle');
  await expect(handle).toHaveAttribute('aria-valuenow', '100');
  await handle.click();
  await page.keyboard.press('ArrowLeft');
  await expect(handle).toHaveAttribute('aria-valuenow', '99');
});

test('geofile upload, background import, map layer, export and delete', async ({ page }) => {
  await login(page);

  // Upload a GPX through the drag&drop zone; the import runs as a background job
  // and the table polls until it settles. Newest first — .first() keeps the flow
  // robust to leftovers from aborted earlier runs (mirrors the raster test).
  await page.goto('/geodata');
  await page.locator('input[type=file]').setInputFiles('e2e/fixtures/e2e-track.gpx');
  const row = page.getByRole('row', { name: /e2e-track/ }).first();
  await expect(row).toBeVisible({ timeout: 15_000 });
  await expect(row.getByText('Imported')).toBeVisible({ timeout: 30_000 });
  await expect(row.getByText('3', { exact: true })).toBeVisible(); // 2 waypoints + 1 track

  // Re-export of the imported rows downloads a real file.
  const downloadPromise = page.waitForEvent('download');
  await row.getByRole('button', { name: 'download' }).click();
  await page.getByRole('menuitem', { name: 'GEOJSON' }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/\.geojson$/);

  // Show on map: the workspace opens with the geofile overlay toggled on.
  // exact:true pins the catalog checkbox — the composer tree row for the same
  // geofile also exposes role=checkbox (labelled "holder e2e-track").
  const featuresLoaded = page.waitForResponse((r) => r.url().includes('/features?bbox=') && r.ok());
  await row.getByRole('button', { name: 'aim' }).click();
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('checkbox', { name: 'e2e-track', exact: true }).first()).toBeChecked();
  await expect(
    overlayTreeNode(page, 'e2e-track').locator('.ant-tree-checkbox-checked').first(),
  ).toBeVisible();
  await featuresLoaded;

  // Cleanup: remove every e2e geofile (earlier aborted runs may have left extras).
  await page.goto('/geodata');
  const rows = page.getByRole('row', { name: /e2e-track/ });
  await expect(rows.first()).toBeVisible({ timeout: 15_000 });
  for (let remaining = await rows.count(); remaining > 0; remaining--) {
    await rows.first().getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK', exact: true }).click();
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
});

test('geotagged photos overlay toggles on and loads the photo map layer', async ({ page }) => {
  await login(page);
  // Enabling the opt-in overlay in the composer tree triggers a bbox load of the photo endpoint.
  const photoRequest = page.waitForRequest((r) => r.url().includes('/api/v1/map/photos'), { timeout: 15_000 });
  const photosRow = overlayTreeNode(page, 'Geotagged photos');
  await photosRow.locator('.ant-tree-checkbox').click();
  await expect(photosRow.locator('.ant-tree-checkbox-checked')).toBeVisible();
  await photoRequest;
});

test('cave history records edits and restores a previous value', async ({ page }) => {
  const caveName = `E2E History Cave ${Date.now()}`;
  await login(page);

  await page.goto('/caves/new');
  await page.getByLabel('Name', { exact: true }).fill(caveName);
  await page.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-item-option').first().click();
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });

  // Two edits so the newest event carries a real old→new region pair.
  await editRegion(page, 'Region One');
  await editRegion(page, 'Region Two');

  // The History card records the change as an old→new row (the value shows in two
  // adjacent events, so target the specific row rather than the bare text).
  const history = page.locator('.ant-card').filter({ hasText: 'History' }).last();
  const latest = history.getByRole('row', { name: /Region One.*Region Two/ });
  await expect(latest).toBeVisible({ timeout: 15_000 });

  // Restore the previous value; the main details revert and a toast confirms.
  await latest.getByLabel('Restore this value').click();
  await expect(page.getByText('Value restored.')).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.ant-descriptions').first().getByText('Region One')).toBeVisible({ timeout: 15_000 });

  // Cleanup.
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK', exact: true }).click();
  await page.waitForURL(/\/caves$/);
});

async function editRegion(page: Page, value: string) {
  await page.locator('button', { hasText: 'Edit' }).click();
  await page.getByLabel('Region').fill(value);
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.locator('.ant-descriptions').first().getByText(value)).toBeVisible({ timeout: 15_000 });
}

test('dashboard: counts, activity, saved-view jump and the landing preference', async ({ page }) => {
  const viewName = `E2E Dash View ${Date.now()}`;
  await login(page);

  // Save a view first so the dashboard has one to list.
  const dock = page.locator('.map-workspace-panel').first();
  await dock.getByPlaceholder('View name…').fill(viewName);
  await dock.getByRole('button', { name: 'save' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  await page.getByRole('menuitem', { name: 'Dashboard' }).click();
  await page.waitForURL(/\/dashboard/);
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();

  // Summary tiles paint from the aggregate endpoint: the seeded registry is non-empty.
  const cavesTile = page.locator('.ant-statistic').filter({ hasText: 'Caves' });
  await expect(cavesTile).toBeVisible({ timeout: 15_000 });
  // Assert positively on the value node: while the summary is loading antd swaps it for a
  // Skeleton, so a negated assertion here would pass against a tile that never painted a count.
  await expect(cavesTile.locator('.ant-statistic-content-value')).toHaveText(/[1-9]\d*/, {
    timeout: 15_000,
  });
  await expect(page.getByText('Recent activity')).toBeVisible();

  // A quick action reaches the cave form (admin may create).
  await page.getByRole('button', { name: 'New cave' }).click();
  await page.waitForURL(/\/caves\/new/);
  await page.goBack();

  // Clicking a saved view applies it and lands on the map. The request is a one-shot: the
  // param is consumed once applied, so a later reload cannot re-apply it over a shared position.
  await page.getByRole('button', { name: viewName }).click();
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 15_000 });
  await page.waitForURL((url) => url.pathname === '/map' && !url.searchParams.has('view'));

  // Opting in makes "/" dispatch to the dashboard; the map stays reachable at /map.
  await page.getByRole('menuitem', { name: 'Dashboard' }).click();
  await page.getByRole('switch').click();
  await page.goto('/');
  await page.waitForURL(/\/dashboard/);
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();

  // A shared map link still shows the map despite the preference.
  await page.goto('/#12/45.50000/25.40000');
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 15_000 });

  // Reset the preference and clean up the view.
  await page.getByRole('menuitem', { name: 'Dashboard' }).click();
  await page.getByRole('switch').click();
  await page.getByRole('menuitem', { name: 'Map' }).click();
  await page.locator('.map-workspace-panel').first()
    .getByRole('listitem').filter({ hasText: viewName })
    .getByRole('button', { name: 'delete' }).click();
  await expect(page.getByText(viewName)).not.toBeVisible({ timeout: 15_000 });
});
