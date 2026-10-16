// SPDX-License-Identifier: AGPL-3.0-or-later
import { AxeBuilder } from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { centreOnDemoCave, gotoRoute, login, mapAddress, openCaveFromRegistry, registryRow } from './helpers.ts';

/** The camera the map's address says it is at: `#zoom/lat/lon`. */
async function cameraLongitude(page: Page): Promise<number> {
  const hash = await page.evaluate(() => window.location.hash);
  const parts = hash.slice(1).split('/');
  return Number(parts[2]);
}

async function focusIsOn(page: Page, selector: string): Promise<boolean> {
  return page.evaluate((sel) => document.activeElement?.matches(sel) ?? false, selector);
}

/**
 * The workspace used from the keyboard alone, as somebody who cannot use a pointer uses it: past
 * the shell by the skip link, onto the map by Tab, the map moved with the arrows, and the list of
 * shortcuts opened with `?` and closed with Escape — handing focus back to where it was.
 */
test('the map workspace can be reached and driven from the keyboard', async ({ page }) => {
  await login(page);
  const camera = { lat: 45.7, lon: 25.3, zoom: 10 };
  await gotoRoute(page, mapAddress(camera));
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });

  // The shell's landmarks, in reading order: the skip link first, then the header, the rail's
  // navigation and the page.
  await expect(page.locator('body')).toMatchAriaSnapshot(`
    - link "Skip to main content"
    - banner
    - complementary:
      - navigation "Main navigation"
    - main
  `);

  // The first Tab lands on the skip link, which is then on screen; Enter puts focus in the page.
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: 'Skip to main content' });
  await expect(skip).toBeFocused();
  expect((await skip.boundingBox())!.y).toBeGreaterThanOrEqual(0);
  await page.keyboard.press('Enter');
  expect(await focusIsOn(page, 'main')).toBe(true);

  // From the page, Tab reaches the map — bounded, so a map that is not in the tab order fails
  // here rather than after the test's whole time.
  let reached = false;
  for (let step = 0; step < 200 && !reached; step++) {
    await page.keyboard.press('Tab');
    reached = await focusIsOn(page, '.map-canvas');
  }
  expect(reached, 'the map is in the tab order').toBe(true);
  const map = page.getByRole('application', { name: /^Map\./ });
  await expect(map).toBeFocused();
  // A keyboard focus shows: the ring is drawn for :focus-visible, which a Tab sets.
  expect(await focusIsOn(page, '.map-canvas:focus-visible')).toBe(true);

  // The arrows move the map. Read from the address, which the map writes once it comes to rest.
  const before = await cameraLongitude(page);
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowRight');
  await expect.poll(() => cameraLongitude(page), { timeout: 15_000 }).toBeGreaterThan(before);

  // `?` opens the list of shortcuts, read from what the workspace has bound.
  await page.keyboard.press('?');
  const help = page.getByRole('dialog', { name: 'Keyboard shortcuts' });
  await expect(help).toBeVisible();
  await expect(help).toMatchAriaSnapshot(`
    - dialog "Keyboard shortcuts":
      - table "On this screen":
        - rowgroup:
          - row /Show this list of shortcuts/
          - row /Clear the selection/
          - row /Show or hide the left panel/
          - row /Show or hide the right panel/
          - row /Previous selection/
          - row /Next selection/
      - table "With the map focused":
        - rowgroup:
          - row /Move the map/
          - row /Zoom in/
          - row /Zoom out/
  `);

  // Escape closes it, and focus goes back to the map it was opened from.
  await page.keyboard.press('Escape');
  await expect(help).toBeHidden();
  await expect(map).toBeFocused();
});

/**
 * The map's own Escape clears the selection, and the help is opened over the map — so one press
 * must close the help and leave the selection alone: losing what one had selected to dismiss a
 * list of keys is the kind of surprise that makes a keyboard user stop trusting Escape.
 */
test('Escape closes the help, and only it', async ({ page }) => {
  await login(page);
  // Something selected to lose: the feature list's "show on map" selects what it frames.
  await centreOnDemoCave(page);
  const selected = page.getByRole('heading', { name: 'Peștera Demo Mare' });
  await expect(selected).toBeVisible({ timeout: 15_000 });

  const map = page.getByRole('application', { name: /^Map\./ });
  await map.focus();
  await page.keyboard.press('?');
  const help = page.getByRole('dialog', { name: 'Keyboard shortcuts' });
  await expect(help).toBeVisible();

  await page.keyboard.press('Escape');
  await expect(help).toBeHidden();
  await expect(selected).toBeVisible();

  // And the selection was one Escape could clear: with the help gone, the same key clears it —
  // so the assertion above is about the help, not about a selection that would have stayed anyway.
  await page.keyboard.press('Escape');
  await expect(selected).toBeHidden();
});

/**
 * The map's menu — add here, new cave here, copy the coordinates — opened from the keyboard: the
 * keys a desktop uses for a context menu, on the focused map, at the middle of the view, with the
 * menu taking focus so its items can be walked, and handing it back to the map when closed.
 */
test('the map menu opens from the keyboard, and gives focus back', async ({ page }) => {
  await login(page);
  const map = page.getByRole('application', { name: /^Map\./ });
  await map.focus();

  await page.keyboard.press('Shift+F10');
  const copy = page.getByRole('menuitem', { name: 'Copy coordinates' });
  await expect(copy).toBeVisible({ timeout: 15_000 });
  await expect.poll(() => focusIsOn(page, '.ant-dropdown-menu, .ant-dropdown-menu *')).toBe(true);

  await page.keyboard.press('Escape');
  await expect(copy).toBeHidden();
  await expect(map).toBeFocused();
});

test('the shortcut list also opens from a visible control on the map', async ({ page }) => {
  await login(page);
  const opener = page.getByRole('button', { name: 'Keyboard shortcuts' });
  await opener.focus();
  await page.keyboard.press('Enter');
  const help = page.getByRole('dialog', { name: 'Keyboard shortcuts' });
  await expect(help).toBeVisible();
  await help.getByRole('button', { name: 'Close', exact: true }).last().click();
  await expect(help).toBeHidden();
  await expect(opener).toBeFocused();
});

/**
 * Whether keyboard focus is inside the open dialog — or with the browser's own controls, which is
 * where Tab past a modal dialog's last control goes in a browser's native dialog too (the page
 * reports its body as focused), and from where the next Tab comes back into the dialog. What must
 * never happen is focus landing on an element of the page behind it.
 */
async function focusIsInDialog(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const element = document.activeElement;
    return element === null || element === document.body || element.closest('[role="dialog"]') !== null;
  });
}

/** Where focus is, by element kind and class only — enough to find it, nothing of what it says. */
async function focusedElement(page: Page): Promise<string> {
  return page.evaluate(() => {
    const element = document.activeElement;
    return element ? `${element.tagName.toLowerCase()}.${Array.from(element.classList).slice(0, 3).join('.')}` : 'none';
  });
}

/**
 * A dialog opened from the keyboard takes focus, keeps it while Tab goes round, and gives it back
 * to the control that opened it when Escape closes it — so a keyboard user is never left on the
 * page's first element, having to find their place again.
 */
async function expectFocusKeptAndReturned(page: Page, opener: ReturnType<Page['getByRole']>, dialogName: string) {
  await opener.focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: dialogName });
  await expect(dialog).toBeVisible({ timeout: 15_000 });
  await expect.poll(() => focusIsInDialog(page), { message: `focus moves into "${dialogName}"` }).toBe(true);

  for (let step = 0; step < 40; step++) {
    await page.keyboard.press('Tab');
    expect(
      await focusIsInDialog(page),
      `Tab ${step + 1} stays inside "${dialogName}" (focus is on ${await focusedElement(page)})`,
    ).toBe(true);
  }

  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(opener).toBeFocused();
}

test('the feature editor and the share-links dialog keep focus inside, and give it back', async ({ page }) => {
  await login(page);

  // A surface feature selected on the map: the feature list's "show on map" selects what it frames.
  const row = await registryRow(page, 'Dolina Demo');
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByRole('button', { name: 'aim' }).click();
  await expect(page.getByRole('heading', { name: 'Dolina Demo' })).toBeVisible({ timeout: 15_000 });
  await expectFocusKeptAndReturned(page, page.getByRole('button', { name: 'Edit', exact: true }), 'Edit feature');

  await openCaveFromRegistry(page, 'Peștera Demo Mare');
  await expectFocusKeptAndReturned(page, page.getByRole('button', { name: 'Share', exact: true }), 'Share links');
});

/** The WCAG 2.0, 2.1 and 2.2 level A and AA rules — the yardstick, with no conformance claimed. */
const WCAG_AA_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/**
 * Findings the scan knows about and accepts for now, each by rule AND by the element it was found
 * on — never a rule turned off for a page, so the same mistake made anywhere else still fails.
 * Each says why it stands and where it is registered to be fixed.
 */
interface AcceptedFinding {
  rule: string;
  /** Matched against the element's selector path as the scanner reports it. */
  target: RegExp;
  why: string;
  register: string;
}

const ACCEPTED: AcceptedFinding[] = [];

/**
 * What kind of element a finding is on, and for a contrast finding the two colours and their
 * ratio — enough to find and fix it; the element's text and attributes are never reported.
 */
function shapeOf(node: { html: string; any: { id: string; data?: unknown }[] }): string {
  const tag = /^<([a-z0-9-]+)/i.exec(node.html)?.[1] ?? '?';
  const role = /\srole="([^"]*)"/.exec(node.html)?.[1];
  const type = /\stype="([^"]*)"/.exec(node.html)?.[1];
  const contrast = node.any.find((check) => check.id === 'color-contrast')?.data as
    | { fgColor?: string; bgColor?: string; contrastRatio?: number }
    | undefined;
  return [
    ` <${tag}${role ? ` role=${role}` : ''}${type ? ` type=${type}` : ''}>`,
    contrast?.fgColor ? ` ${contrast.fgColor} on ${contrast.bgColor} = ${contrast.contrastRatio}:1` : '',
  ].join('');
}

/**
 * Scans what is on the page now and fails on any finding not accepted above. Only rule ids and
 * selectors are reported — never the markup the scanner quotes, which carries whatever the page
 * was showing.
 */
async function expectNoUnacceptedFindings(page: Page, screen: string) {
  const results = await new AxeBuilder({ page }).withTags(WCAG_AA_TAGS).analyze();
  const found = results.violations.flatMap((violation) =>
    violation.nodes
      .map((node) => ({ violation, node, target: node.target.map(String).join(' ') }))
      .filter(({ target }) => !ACCEPTED.some((accepted) => accepted.rule === violation.id && accepted.target.test(target))),
  );
  const unaccepted: string[] = [];
  for (const { violation, node, target } of found) {
    // The nearest test id around the element: where on the screen it is, without what it says.
    const within = await page
      .evaluate((selector) => document.querySelector(selector)?.closest('[data-testid]')?.getAttribute('data-testid'), target)
      .catch(() => undefined);
    unaccepted.push(
      `${violation.id} (${violation.impact ?? 'n/a'}) at ${target}${shapeOf(node)}${within ? ` within [data-testid=${within}]` : ''}`,
    );
  }
  expect(unaccepted, `accessibility findings on ${screen}`).toEqual([]);
}

test.describe('scanned against WCAG 2.2 A and AA', () => {
  test('the sign-in page', async ({ page }) => {
    await page.goto('/');
    await page.waitForURL(/\/login\?returnUrl=/, { timeout: 60_000 });
    await expect(page.getByLabel('Email')).toBeVisible();
    await expectNoUnacceptedFindings(page, 'the sign-in page');
  });

  test('the map workspace and its shortcut help', async ({ page }) => {
    await login(page);
    await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
    await expectNoUnacceptedFindings(page, 'the map workspace');

    await page.getByRole('button', { name: 'Keyboard shortcuts' }).click();
    await expect(page.getByRole('dialog', { name: 'Keyboard shortcuts' })).toBeVisible();
    await expectNoUnacceptedFindings(page, 'the shortcut help');
  });

  test('the cave registry and a cave page', async ({ page }) => {
    await login(page);
    await gotoRoute(page, '/caves');
    await expect(page.getByRole('row').nth(1)).toBeVisible({ timeout: 30_000 });
    await expectNoUnacceptedFindings(page, 'the cave registry');

    await openCaveFromRegistry(page, 'Peștera Demo Mare');
    await expect(page.getByRole('heading', { name: 'Peștera Demo Mare' }).first()).toBeVisible({ timeout: 30_000 });
    await expectNoUnacceptedFindings(page, 'a cave page');
  });

  test('the trip list', async ({ page }) => {
    await login(page);
    await gotoRoute(page, '/trip-logs');
    await expect(page.getByRole('table').first()).toBeVisible({ timeout: 30_000 });
    await expectNoUnacceptedFindings(page, 'the trip list');
  });

  test('the profile and accessibility settings', async ({ page }) => {
    await login(page);
    await gotoRoute(page, '/settings/profile');
    await expect(page.getByLabel('Surname')).toBeVisible({ timeout: 30_000 });
    await expectNoUnacceptedFindings(page, 'the profile settings');

    await gotoRoute(page, '/settings/accessibility');
    await expect(page.getByRole('main')).toBeVisible();
    await expectNoUnacceptedFindings(page, 'the accessibility settings');
  });
});
