// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, test, type Page } from '@playwright/test';

// Demo credentials/data: `dotnet run -- seed-demo` with the dev admin bootstrap.
// The full OIDC code+PKCE flow runs in the real browser.
const adminEmail = 'admin@dev.local';
const adminPassword = 'dev-admin-pass-1';

async function login(page: Page) {
  await page.goto('/');
  // Unauthenticated → OIDC authorize → SPA login page with returnUrl.
  await page.waitForURL(/\/login\?returnUrl=/);
  await page.getByLabel('Email').fill(adminEmail);
  await page.getByLabel('Password').fill(adminPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();
  // Authorize completes, callback exchanges the code, workspace renders.
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 20_000 });
}

/** A named overlay row in the layer composer tree (left dock). */
function overlayTreeNode(page: Page, name: string) {
  return page.locator('.layer-composer .ant-tree-treenode').filter({ hasText: name });
}

test('login, map workspace and cave registry work end to end', async ({ page }) => {
  await login(page);
  await expect(page.getByRole('radio', { name: 'OpenStreetMap' })).toBeChecked();

  // The composer tree lists the entrance overlay with its checkbox on.
  await expect(overlayTreeNode(page, 'Cave entrances').locator('.ant-tree-checkbox-checked')).toBeVisible();
  await expect(page.getByText('Base layers')).toBeVisible();
  await expect(page.getByText(/Click a feature on the map/)).toBeVisible();

  // Cave registry lists demo data.
  await page.goto('/caves');
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
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('45.65432°N 25.12346°E')).toBeVisible({ timeout: 15_000 });

  // Edit the cave and verify the change lands on the detail page. Text filtering keeps
  // the header buttons distinct from the entrance row's text-less icon buttons.
  await page.locator('button', { hasText: 'Edit' }).click();
  await page.getByLabel('Region').fill('Testland');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Testland')).toBeVisible();

  // Clean up: delete the cave (entrances cascade server-side).
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await page.waitForURL(/\/caves$/);
  await expect(page.getByText(caveName)).not.toBeVisible();
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
  await modal.getByLabel('Type', { exact: true }).click();
  await page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden) .ant-select-item-option').first().click();
  await modal.getByLabel('Entrance type').click();
  await page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden) .ant-select-item-option').first().click();
  await modal.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The new cave is selected in the right dock; its entrance count proves the
  // first entrance landed with the create.
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Entrances').locator('..')).toContainText('1');

  // Clean up from the registry (entrances cascade).
  await page.goto('/caves');
  await page.getByText(caveName).click();
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await page.waitForURL(/\/caves$/);
  await expect(page.getByText(caveName)).not.toBeVisible();
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
  await expect(modal.getByText('New surface feature')).toBeVisible();
  await modal.getByLabel('Name').fill(featureName);
  await modal.getByLabel('Depth (m)').fill('12.5');
  await modal.getByRole('button', { name: 'OK' }).click();

  // Batched save posts the feature and reloads the layer.
  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/surface-features') && r.ok());
  await toolbar.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await reloaded;

  // Click the same spot: the saved feature is selected and the detail card
  // shows the schema-labeled property.
  await canvas.click({ position: { x: 420, y: 260 } });
  await expect(page.getByRole('heading', { name: featureName })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Depth (m)')).toBeVisible();
  await expect(page.getByText('12.5')).toBeVisible();

  // The features table lists it; delete from the row actions (cleanup).
  await page.goto('/features');
  const row = page.getByRole('row', { name: new RegExp(featureName) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await expect(row.getByText('Sinkhole / Doline')).toBeVisible();
  await row.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(featureName)).not.toBeVisible();
});

test('cave photo attachment round-trip', async ({ page }) => {
  await login(page);

  // Any visible demo cave works; the gallery lives on the detail page.
  await page.goto('/caves');
  await page.getByText('Peștera Demo Mare').click();
  await expect(page.getByText('Photos & documents')).toBeVisible({ timeout: 15_000 });

  // Upload a photo through the attachment drop zone (scoped to the gallery card).
  const gallery = page.locator('.ant-card', { hasText: 'Photos & documents' });
  await gallery.locator('input[type=file]').setInputFiles('e2e/fixtures/e2e-photo.png');
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The gallery renders the thumbnail through the token-authenticated URL.
  const photo = page.locator('.ant-image img[src*="/thumbnail"]').first();
  await expect(photo).toBeVisible({ timeout: 15_000 });
  await expect(photo).toHaveJSProperty('naturalWidth', 4); // decoded, not a broken image

  // Cleanup: remove every e2e photo (earlier aborted runs may have left extras).
  const figures = page.locator('figure').filter({ hasText: 'e2e-photo' });
  for (let remaining = await figures.count(); remaining > 0; remaining--) {
    await figures.first().getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(figures).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
  await expect(page.getByText('e2e-photo.png')).not.toBeVisible();
});

test('pop-out registry drives the main map across windows', async ({ page, context }) => {
  await login(page);

  // Open the registry pop-out; it shares the session and the workspace bus.
  const popupPromise = context.waitForEvent('page');
  await page.getByRole('button', { name: 'export' }).click();
  const popup = await popupPromise;
  await expect(popup.getByText('Cave registry')).toBeVisible({ timeout: 15_000 });

  // Picking a cave in the pop-out selects it in the MAIN window's dock.
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

  // Upload the committed Survex .3d fixture; the row appears with its format tag.
  const fileChooserPromise = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: /Upload model/ }).click();
  await (await fileChooserPromise).setFiles('e2e/fixtures/P8_Master.3d');
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
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('No 3D models yet')).toBeVisible({ timeout: 15_000 });
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
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

  // The workspace gains the centerline overlay toggle, on by default.
  await page.goto('/');
  await expect(overlayTreeNode(page, 'Cave centerlines').locator('.ant-tree-checkbox-checked')).toBeVisible();

  // Clean up: delete the centerline, then the cave.
  await page.goto('/caves');
  await page.getByText(caveName).click();
  await card.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(card.getByText('No centerlines yet')).toBeVisible({ timeout: 15_000 });
  await page.locator('button', { hasText: 'Delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
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

  const anonymous = await browser.newContext();
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

test('teams and per-object permission grants', async ({ page }) => {
  const teamName = `E2E Team ${Date.now()}`;
  await login(page);

  // Create a team (the admin holds Manager rights) and see ourselves as owner.
  await page.goto('/teams');
  await page.getByRole('button', { name: /New team/ }).click();
  await page.getByLabel('Name', { exact: true }).fill(teamName);
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });
  const teamRow = page.getByRole('row', { name: new RegExp(teamName) });
  await expect(teamRow).toBeVisible();
  await teamRow.getByRole('button', { name: 'Manage' }).click();
  await expect(page.getByText('Owner')).toBeVisible({ timeout: 15_000 });
  await page.keyboard.press('Escape');

  // Grant the team Read on a cave through the permissions modal.
  await page.goto('/caves');
  await page.getByText('Peștera Demo Mare').click();
  await page.getByRole('button', { name: /Permissions/ }).click();
  const modal = page.getByRole('dialog');
  await modal.locator('.ant-select').first().click();
  await page.locator('.ant-select-item-option', { hasText: 'Team' }).click();
  await modal.locator('.ant-select').nth(1).click();
  await page.locator('.ant-select-item-option', { hasText: teamName }).click();
  await modal.getByRole('button', { name: /Add/ }).click();
  await expect(modal.getByText(teamName)).toBeVisible();
  await modal.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });

  // Reopen: the grant persisted; then remove it — sweeping grants left behind by
  // previously aborted runs too (they accumulate on the shared demo cave and make a
  // bare "delete" click ambiguous).
  await page.getByRole('button', { name: /Permissions/ }).click();
  await expect(modal.getByText(teamName)).toBeVisible({ timeout: 15_000 });
  const e2eGrantRows = modal.getByRole('row', { name: /E2E Team/ });
  while ((await e2eGrantRows.count()) > 0) {
    await e2eGrantRows.first().getByRole('button', { name: 'delete' }).click();
  }
  await modal.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.').first()).toBeVisible({ timeout: 15_000 });
});

test('trip log with participants, tags and the audit trail', async ({ page }) => {
  const title = `E2E Trip ${Date.now()}`;
  const tagName = `e2e-tag-${Date.now()}`;
  await login(page);

  // Create a trip with a guest participant.
  await page.goto('/trip-logs');
  await page.getByRole('button', { name: /New trip log/ }).click();
  await page.getByLabel('Title', { exact: true }).fill(title);
  await page.getByRole('button', { name: /Add participant/ }).click();
  await page.getByPlaceholder('Participant name').fill('Guest Caver');
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('Guest Caver')).toBeVisible();

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
  await page.goto('/trip-logs');
  await page.getByText(title).click();
  await page.getByRole('button', { name: /Delete/ }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(title)).not.toBeVisible();
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

  // Cleanup: remove every e2e raster (earlier aborted runs may have left extras).
  await page.goto('/geodata');
  await page.getByRole('tab', { name: 'Raster maps' }).click();
  const rows = page.getByRole('row', { name: /e2e-map/ });
  await expect(rows.first()).toBeVisible({ timeout: 15_000 });
  for (let remaining = await rows.count(); remaining > 0; remaining--) {
    await rows.first().getByRole('button', { name: 'delete' }).click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
});

test('geofile upload, background import, map layer, export and delete', async ({ page }) => {
  await login(page);

  // Upload a GPX through the drag&drop zone; the import runs as a background job
  // and the table polls until it settles.
  await page.goto('/geodata');
  await page.locator('input[type=file]').setInputFiles('e2e/fixtures/e2e-track.gpx');
  const row = page.getByRole('row', { name: /e2e-track/ });
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
  const featuresLoaded = page.waitForResponse((r) => r.url().includes('/features?bbox=') && r.ok());
  await row.getByRole('button', { name: 'aim' }).click();
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('checkbox', { name: 'e2e-track' })).toBeChecked();
  await featuresLoaded;

  // Cleanup: delete the geofile (imported rows cascade).
  await page.goto('/geodata');
  const rowAgain = page.getByRole('row', { name: /e2e-track/ });
  await rowAgain.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText('e2e-track')).not.toBeVisible();
});
