// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
// Straight from Playwright this spec would run unwatched: the guard is what records uncaught
// errors, unhandled rejections and console errors across the whole browser context.
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

/**
 * The application's frame on a phone, for an account with a long name.
 *
 * The header drew the account's whole name beside its avatar, and on a 390px phone a long one ran
 * past the right edge and made every signed-in page wider than the screen; the product name beside
 * the logo, squeezed from the other side, broke into one letter per line. Below the header, the tab
 * antd hangs off the closed navigation rail floated over the page and hid the first letter of
 * page titles. Each page is reached at a desktop width (the address round trip waits for the
 * desktop frame), then narrowed to the phone's width and measured.
 */
const PHONE = { width: 390, height: 844 };
const LONG_NAME = 'Maximilian-Alexandru Constantinescu-Bărăganu';

const pages = [
  { path: '/caves', heading: 'Caves' },
  { path: '/trip-logs', heading: 'Trip logs' },
  { path: '/features', heading: 'Features' },
  { path: '/saved-filters', heading: 'Saved filters' },
];

test('signed-in pages fit a phone with a long account name, and nothing covers the page title', async ({ page }) => {
  test.setTimeout(240_000);
  // The account's own record answered with a long display name, so no other spec's account is
  // renamed; the header reads its name from nowhere else.
  await page.route('**/api/v1/me', async (route) => {
    if (route.request().method() !== 'GET') {
      return route.fallback();
    }
    const response = await route.fetch();
    const me = await response.json();
    await route.fulfill({ response, json: { ...me, displayName: LONG_NAME } });
  });
  await login(page);

  for (const { path, heading } of pages) {
    await page.setViewportSize({ width: 1280, height: 800 });
    await gotoRoute(page, path);
    await page.setViewportSize(PHONE);
    const title = page.getByRole('heading', { name: heading, exact: true });
    await expect(title, path).toBeVisible({ timeout: 30_000 });

    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow, `${path} is no wider than the phone`).toBeLessThanOrEqual(0);

    // Every control in the header ends inside the screen.
    const furthest = await page.locator('.ant-layout-header').evaluate((header) =>
      Math.max(...Array.from(header.querySelectorAll('button, .ant-select')).map((c) => c.getBoundingClientRect().right)),
    );
    expect(furthest, `${path} keeps the header's controls on screen`).toBeLessThanOrEqual(PHONE.width);

    // Whatever is drawn at the title's first letter is the title itself, not a control above it.
    const coveredBy = await title.evaluate((element) => {
      const box = element.getBoundingClientRect();
      const top = document.elementFromPoint(box.left + 3, box.top + box.height / 2);
      return top && element.contains(top) ? null : (top?.outerHTML.slice(0, 120) ?? 'nothing');
    });
    expect(coveredBy, `${path} leaves the title's first letter uncovered`).toBeNull();
  }

  // The name is not drawn in the header but the account menu still says who is signed in.
  await expect(page.getByTestId('account-menu')).not.toContainText(LONG_NAME);
  await page.getByTestId('account-menu').click();
  await expect(page.getByRole('menuitem', { name: LONG_NAME })).toBeVisible();
  // Visible is not on screen: the long name is what would push the menu past the right edge.
  const menu = await page.locator('.ant-dropdown:visible').boundingBox();
  expect(menu, 'the account menu is drawn').not.toBeNull();
  expect(menu!.x, 'the account menu starts on screen').toBeGreaterThanOrEqual(0);
  expect(menu!.x + menu!.width, 'the account menu ends on screen').toBeLessThanOrEqual(PHONE.width);
  await page.keyboard.press('Escape');

  // The navigation is reached from the header's button, and a destination in it can be opened.
  await page.getByTestId('nav-toggle').click();
  await expect(page.getByTestId('app-sider')).toBeVisible();
  await expect(page.getByTestId('nav-toggle')).toHaveAttribute('aria-expanded', 'true');
});
