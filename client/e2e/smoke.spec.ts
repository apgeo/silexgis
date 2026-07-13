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

test('cluster click lists its member entrances in the panel', async ({ page }) => {
  await login(page);

  // Center on the demo cave via the map search (stays inside the SPA session),
  // then zoom out below the clustering threshold: 15 → 9. The cave's two
  // entrances aggregate into one cluster ~1px from the view center.
  await page.getByPlaceholder('Search caves or places…').fill('Peștera Demo');
  await page.locator('.ant-select-dropdown').getByText(/Peștera Demo Mare/).first().click();
  for (let i = 0; i < 6; i++) {
    await page.locator('.ol-zoom-out').click();
  }

  // Retry the click until the cluster card shows — zoom animations and the
  // debounced bbox loader settle at their own pace.
  const canvas = page.locator('.map-canvas');
  await expect(async () => {
    await canvas.click();
    await expect(page.getByText(/entrances in this area/)).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });

  // Picking a member selects the entrance and the cave card takes over.
  await page.getByRole('button', { name: /entrance|Peșter/i }).first().click();
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
  await modal.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });

  // The new cave is selected in the right dock; its entrance count proves the
  // first entrance landed with the create.
  await expect(page.getByRole('heading', { name: caveName })).toBeVisible({ timeout: 15_000 });
  await expect(
    page.locator('.map-right-tabs .ant-descriptions-row', { hasText: 'Entrances' }),
  ).toContainText('1');

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
  await expect(modal.getByText('New surface feature')).toBeVisible();
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
  await expect(modal.getByText('New surface feature')).toBeVisible();
  await modal.getByLabel('Name').fill(featureName);
  await modal.getByRole('button', { name: 'OK' }).click();

  // The placement is a pending edit saved through the normal batched flow.
  const toolbar = page.locator('.map-edit-overlay');
  const reloaded = page.waitForResponse((r) => r.url().includes('/api/v1/map/surface-features') && r.ok());
  await toolbar.getByRole('button', { name: /Save/ }).click();
  await expect(page.getByText('Saved.')).toBeVisible({ timeout: 15_000 });
  await reloaded;

  // "New cave here" opens the create dialog without needing a second click.
  await canvas.click({ button: 'right', position: { x: 300, y: 200 } });
  await page.getByRole('menuitem', { name: 'New cave here' }).click();
  await expect(page.getByRole('dialog').getByText(/New cave here/)).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click();

  // Cleanup: remove the created feature via the registry.
  await page.goto('/features');
  const row = page.getByRole('row', { name: new RegExp(featureName) });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByRole('button', { name: 'delete' }).click();
  await page.getByRole('button', { name: 'OK' }).click();
  await expect(page.getByText('Deleted.')).toBeVisible({ timeout: 15_000 });
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
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
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
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(rows).toHaveCount(remaining - 1, { timeout: 15_000 });
  }
});
