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

test('login, map workspace and cave registry work end to end', async ({ page }) => {
  await login(page);
  await expect(page.getByRole('radio', { name: 'OpenStreetMap' })).toBeChecked();

  // Layer panel shows the entrance overlay toggle. Visibility matters here:
  // checked-state assertions pass even when the dock is collapsed to a sliver.
  await expect(page.getByRole('checkbox', { name: 'Cave entrances' })).toBeChecked();
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

test('surface feature draw, attributes, selection and table round-trip', async ({ page }) => {
  const featureName = `E2E Sinkhole ${Date.now()}`;
  await login(page);

  const toolbar = page.locator('.map-edit-overlay');
  await expect(toolbar).toBeVisible();

  // Pick the feature type that carries a typed-properties schema.
  await toolbar.locator('.ant-select').click();
  await page.locator('.ant-select-item-option', { hasText: 'Sinkhole / Doline' }).click();

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
