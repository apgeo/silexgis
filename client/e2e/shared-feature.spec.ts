// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { ownContext, test } from './consoleGuard.ts';
import { asPerson, registerAccount } from './arrange.ts';
import { login } from './helpers.ts';

interface FeatureType {
  id: number;
  category: string;
  boundKind: string;
  requiresParent: boolean;
  acceptedGeometryClasses: string[];
}

/**
 * A record's share link, opened on a phone by somebody who holds nothing but the link.
 *
 * Three links of one record, each opened cold. A public one shows the record and fits the screen.
 * One that needs an account says so, and signing in from it comes back to the same link — query
 * and all — where the record is read with the new sign-in; the way out it offered before went to
 * the password page, which ends on the home page, and the link was lost every time. The record
 * is open to any signed-in account, so a reader with an account of their own reads it. A withdrawn
 * one says the link does not exist. Placed far from the demonstration data, as an ordinary
 * record nobody has protected.
 */
test('a shared record opens from its link, and one that needs an account comes back to it after signing in', async ({
  page,
  browser,
  consoleErrors,
}) => {
  test.setTimeout(180_000);
  consoleErrors.allow(
    /\/api\/v1\/shared\/features\/[^/?:\s]+(\?|:|$)/,
    'the link that needs an account, opened before signing in on purpose',
  );
  consoleErrors.allow(/\/api\/v1\/shared\/features\/[^/?:\s]+(\?|:|$)/, 'the withdrawn link this test opens on purpose');
  await login(page);

  const stamp = Date.now();
  const name = `E2E shared record ${stamp}`;
  const featureTypes = await asPerson<FeatureType[]>(page, 'GET', '/api/v1/feature-types');
  const surfaceType = featureTypes.find(
    (type) =>
      type.category === 'surface' &&
      type.boundKind === 'generic' &&
      !type.requiresParent &&
      type.acceptedGeometryClasses.includes('point'),
  );
  expect(surfaceType, 'the catalogue has a kind of surface feature placed as a point').toBeDefined();
  const feature = await asPerson<{ id: string }>(page, 'POST', '/api/v1/features', {
    kind: 'generic',
    name,
    featureTypeId: surfaceType!.id,
    geometry: { type: 'Point', coordinates: [13.6543, 62.4321] },
    description: null,
    properties: null,
    parents: null,
    locationProtected: false,
    cavingGroupId: null,
    visibility: 'authenticated',
  });
  const share = (mode: 'public' | 'requiresLogin') =>
    asPerson<{ id: string; token: string }>(page, 'POST', `/api/v1/features/${feature.id}/shares`, {
      mode,
      includeSubtree: false,
    });
  const publicLink = await share('public');
  // The reader signs in as an account of the flow's own: a sign-in from a fresh browser writes to
  // the account's preferences, and the administrator's account is every other spec's.
  const reader = await registerAccount(page.request, 'share-reader');
  const accountsLink = await share('requiresLogin');

  const visitorContext = await ownContext(browser, { viewport: { width: 360, height: 740 } });
  try {
    const visitor = await visitorContext.newPage();

    // ---- a public link: the record, on a phone, without a sideways scroll ----
    await visitor.goto(`/shared/features/${publicLink.token}`);
    await expect(visitor.getByRole('heading', { name })).toBeVisible({ timeout: 30_000 });
    const overflow = await visitor.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow, 'the shared record fits a phone screen').toBeLessThanOrEqual(0);

    // ---- a link that needs an account: sign in from it, and land back on it ----
    const accountsPath = `/shared/features/${accountsLink.token}`;
    await visitor.goto(`${accountsPath}?from=chat`);
    await expect(visitor.getByTestId('shared-feature-login-required')).toBeVisible({ timeout: 30_000 });
    await visitor.getByTestId('shared-feature-sign-in').click();
    await visitor.waitForURL(/\/login\?returnUrl=/, { timeout: 60_000 });
    await visitor.getByLabel('Email').fill(reader.email);
    await visitor.getByLabel('Password').fill(reader.password);
    await visitor.getByRole('button', { name: 'Sign in' }).click();
    await visitor.waitForURL((url) => url.pathname === accountsPath && url.search === '?from=chat', {
      timeout: 60_000,
    });
    await expect(visitor.getByRole('heading', { name })).toBeVisible({ timeout: 30_000 });

    // ---- a withdrawn link says the link does not exist ----
    await asPerson(page, 'DELETE', `/api/v1/features/${feature.id}/shares/${publicLink.id}`);
    await visitor.goto(`/shared/features/${publicLink.token}`);
    await expect(visitor.getByTestId('shared-feature-not-found')).toBeVisible({ timeout: 30_000 });
  } finally {
    await visitorContext.close();
    await asPerson(page, 'DELETE', `/api/v1/features/${feature.id}`).catch(() => undefined);
  }
});
