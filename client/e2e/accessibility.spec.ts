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
  // A control can be drawn before it may be used — the panel's Edit waits for the feature's own
  // rights — and a disabled button takes no focus.
  await expect(opener).toBeEnabled({ timeout: 15_000 });
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
 * Findings the scan knows about and accepts for now, each by screen, by rule AND by the one element
 * it was found on — never a rule turned off for a page, so the same mistake made anywhere else
 * still fails. Each says why it stands and where it is registered to be fixed, and an entry the
 * scan no longer finds fails the scan too: an exception outliving its finding would accept the
 * next one made on that element unseen.
 */
interface AcceptedFinding {
  /** The screen it was found on, as the scan below names it. */
  screen: string;
  rule: string;
  /**
   * The element's selector path exactly as the scanner reports it, or a pattern anchored at both
   * ends — a loose pattern would accept the same finding on elements nobody looked at.
   */
  target: string | RegExp;
  why: string;
  register: string;
}

const ACCEPTED: AcceptedFinding[] = [];

for (const accepted of ACCEPTED) {
  if (accepted.target instanceof RegExp && !/^\^.*\$$/.test(accepted.target.source)) {
    throw new Error(`ACCEPTED: the pattern for ${accepted.rule} on ${accepted.screen} must be anchored with ^ and $`);
  }
}

function accepts(accepted: AcceptedFinding, screen: string, rule: string, target: string): boolean {
  return (
    accepted.screen === screen &&
    accepted.rule === rule &&
    (typeof accepted.target === 'string' ? accepted.target === target : accepted.target.test(target))
  );
}

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

type Scheme = 'light' | 'dark';

/**
 * Scans what is on the page now, in the theme the page is in, and fails on any finding not
 * accepted above — and on any entry above for this screen that it no longer finds. Only rule ids
 * and selectors are reported — never the markup the scanner quotes, which carries whatever the
 * page was showing.
 */
async function expectNoUnacceptedFindings(page: Page, screenName: string, scheme: Scheme = 'light') {
  // The theme the scan is about, checked rather than assumed: the theme follows the account's
  // stored appearance, and a scan meant for the dark theme that ran in the light one would pass
  // for the wrong reason.
  await expect(page.locator('html')).toHaveAttribute('data-theme', scheme);
  const screen = scheme === 'light' ? screenName : `${screenName}, dark theme`;
  const results = await new AxeBuilder({ page }).withTags(WCAG_AA_TAGS).analyze();
  const used = new Set<AcceptedFinding>();
  const found = results.violations.flatMap((violation) =>
    violation.nodes
      .map((node) => ({ violation, node, target: node.target.map(String).join(' ') }))
      .filter(({ target }) => {
        const accepted = ACCEPTED.find((entry) => accepts(entry, screen, violation.id, target));
        if (accepted) {
          used.add(accepted);
        }
        return !accepted;
      }),
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
  const stale = ACCEPTED.filter((entry) => entry.screen === screen && !used.has(entry)).map(
    (entry) => `${entry.rule} at ${String(entry.target)} (${entry.register})`,
  );
  expect(stale, `accepted findings the scan no longer finds on ${screen} — remove them from ACCEPTED`).toEqual([]);
}

for (const scheme of ['light', 'dark'] as const) {
  test.describe(`scanned against WCAG 2.2 A and AA, ${scheme} theme`, () => {
    // The theme the system asks for, which the account's default appearance follows.
    test.use({ colorScheme: scheme });
    const scan = (page: Page, screen: string) => expectNoUnacceptedFindings(page, screen, scheme);

    test('the sign-in page', async ({ page }) => {
      await page.goto('/');
      await page.waitForURL(/\/login\?returnUrl=/, { timeout: 60_000 });
      await expect(page.getByLabel('Email')).toBeVisible();
      await scan(page, 'the sign-in page');
    });

    test('the map workspace, its shortcut help and a filled selection panel', async ({ page }) => {
      await login(page);
      await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
      await scan(page, 'the map workspace');

      await page.getByRole('button', { name: 'Keyboard shortcuts' }).click();
      const help = page.getByRole('dialog', { name: 'Keyboard shortcuts' });
      await expect(help).toBeVisible();
      await scan(page, 'the shortcut help');
      await page.keyboard.press('Escape');
      await expect(help).toBeHidden();

      // The panel with a cave in it: its details, sections and actions, not only its empty state.
      await centreOnDemoCave(page);
      await expect(page.getByRole('heading', { name: 'Peștera Demo Mare' })).toBeVisible({ timeout: 15_000 });
      await scan(page, 'the selection panel with a cave');
    });

    test('the cave registry, a cave page and its share links', async ({ page }) => {
      await login(page);
      await gotoRoute(page, '/caves');
      await expect(page.getByRole('row').nth(1)).toBeVisible({ timeout: 30_000 });
      await scan(page, 'the cave registry');

      await openCaveFromRegistry(page, 'Peștera Demo Mare');
      await expect(page.getByRole('heading', { name: 'Peștera Demo Mare' }).first()).toBeVisible({ timeout: 30_000 });
      await scan(page, 'a cave page');

      await page.getByRole('button', { name: 'Share', exact: true }).click();
      await expect(page.getByRole('dialog', { name: 'Share links' })).toBeVisible({ timeout: 15_000 });
      await scan(page, 'the share-links dialog');
    });

    test('the trip list', async ({ page }) => {
      await login(page);
      await gotoRoute(page, '/trip-logs');
      await expect(page.getByRole('table').first()).toBeVisible({ timeout: 30_000 });
      await scan(page, 'the trip list');
    });

    test('the profile and accessibility settings', async ({ page }) => {
      await login(page);
      await gotoRoute(page, '/settings/profile');
      await expect(page.getByLabel('Surname')).toBeVisible({ timeout: 30_000 });
      await scan(page, 'the profile settings');

      await gotoRoute(page, '/settings/accessibility');
      await expect(page.getByRole('main')).toBeVisible();
      await scan(page, 'the accessibility settings');
    });
  });
}
