// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { asPerson, tryAsPerson } from './arrange.ts';
import { gotoRoute, login } from './helpers.ts';

interface FeatureTypeRow {
  id: number;
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
  category: string;
  acceptedGeometryClasses: string[];
  requiresParent: boolean;
  protectedDisplay: string;
  symbolFile: string | null;
  style: string | null;
  propertiesSchema: string | null;
  boundKind: string;
}

/**
 * A club's own cave field, from the type every cave carries to the cave's own page.
 *
 * Every cave carries one feature type, bound to the cave kind, and a club gives caves fields of
 * its own by adding them to that type's schema. The flow adds one through the API, then drives
 * what a person sees: the type marked as the one every cave carries on the feature-kinds page,
 * the field offered on the cave form under the club's fields, and the value it was given on the
 * cave's page. The catalogue's own codes the type ships with are not offered on the form.
 */
test('a field the club adds to the cave type is offered on the cave form and shown on the cave', async ({
  page,
}) => {
  test.slow();
  const stamp = `${Date.now()}`;
  const key = `rope${stamp}`;
  const label = `Rope needed ${stamp}`;
  const name = `E2E Club Field Cave ${stamp}`;

  await login(page);
  const types = await asPerson<FeatureTypeRow[]>(page, 'GET', '/api/v1/feature-types');
  const caveType = types.find((type) => type.boundKind === 'cave');
  expect(caveType, 'the type every cave carries').toBeDefined();
  const shipped = caveType!.propertiesSchema!;
  const schema = JSON.parse(shipped) as { properties: Record<string, unknown> };
  schema.properties[key] = { type: 'number', title: label, minimum: 0 };
  const write = (propertiesSchema: string) =>
    asPerson(page, 'PUT', `/api/v1/feature-types/${caveType!.id}`, {
      code: caveType!.code,
      name: caveType!.name,
      description: caveType!.description,
      sortOrder: caveType!.sortOrder,
      category: caveType!.category,
      acceptedGeometryClasses: caveType!.acceptedGeometryClasses,
      requiresParent: caveType!.requiresParent,
      protectedDisplay: caveType!.protectedDisplay,
      symbolFile: caveType!.symbolFile,
      style: caveType!.style,
      propertiesSchema,
    });

  let caveId: string | undefined;
  await write(JSON.stringify(schema));
  try {
    // The feature-kinds page says which type that is.
    await gotoRoute(page, '/admin/feature-types');
    const row = page.getByRole('row', { name: new RegExp(`\\b${caveType!.code}\\b`) }).first();
    await expect(row.getByText('Every cave')).toBeVisible({ timeout: 30_000 });

    // The cave form offers the club's field, and not the catalogue's codes.
    await gotoRoute(page, '/caves/new');
    const fields = page.getByTestId('cave-club-fields');
    await expect(fields).toBeVisible({ timeout: 30_000 });
    await expect(fields.getByText(label)).toBeVisible();
    await expect(fields.getByText('Catalogue id')).toHaveCount(0);

    const caveTypes = await asPerson<{ id: number; name: string }[]>(page, 'GET', '/api/v1/cave-types');
    await page.getByLabel('Name').first().fill(name);
    await page.getByLabel('Type', { exact: true }).click();
    await page.getByTitle(caveTypes[0].name).first().click();
    await page.getByTestId(`cave-club-field-${key}`).fill('45');
    const saved = page.waitForResponse(
      (r) => r.url().endsWith('/api/v1/caves') && r.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Save' }).click();
    const created = await saved;
    expect(created.status()).toBe(201);
    caveId = ((await created.json()) as { id: string }).id;

    // Its page shows the value beside the cave's own record.
    await expect(page).toHaveURL(new RegExp(`/caves/${caveId}`), { timeout: 30_000 });
    await expect(page.getByText(label)).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('45', { exact: true })).toBeVisible();

    // And the server holds it in the cave's document, typed as the schema says.
    const cave = await asPerson<{ properties: Record<string, unknown> }>(page, 'GET', `/api/v1/caves/${caveId}`);
    expect(cave.properties[key]).toBe(45);
  } finally {
    await write(shipped);
    if (caveId) {
      await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
    }
  }
});
