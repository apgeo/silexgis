// SPDX-License-Identifier: AGPL-3.0-or-later
import { AxeBuilder } from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';
import {
  centreOnDemoCave,
  gotoRoute,
  login,
  mapAddress,
  openCaveFromRegistry,
  overlayTreeNode,
  registryRow,
} from './helpers.ts';
import { localDay, registerAccount, tripBody } from './arrange.ts';
import { apiJson, bearerToken, relationId, uploadMapPng } from './rastermapApi.ts';

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
 * the shell and the layer panel by the skip link, straight onto the map, the map moved with the
 * arrows, and the list of shortcuts opened with `?` and closed with Escape — handing focus back to
 * where it was.
 */
test('the map workspace can be reached and driven from the keyboard', async ({ page }) => {
  await login(page);
  const camera = { lat: 45.7, lon: 25.3, zoom: 10 };
  await gotoRoute(page, mapAddress(camera));
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });

  // The shell's landmarks, in reading order: the skip link first, then the header, the rail's
  // navigation and the page.
  await expect(page.locator('body')).toMatchAriaSnapshot(`
    - link "Skip to map"
    - banner
    - complementary:
      - navigation "Main navigation"
    - main
  `);

  // The first Tab lands on the skip link, which is then on screen; Enter puts focus on the map —
  // two key presses from the page's start, whatever the header, the rail and the layer panel hold.
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: 'Skip to map' });
  await expect(skip).toBeFocused();
  expect((await skip.boundingBox())!.y).toBeGreaterThanOrEqual(0);
  await page.keyboard.press('Enter');
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

/** How many presses of Tab it takes from the current focus until `selector` has it, or `null`. */
async function tabsUntil(page: Page, selector: string, bound: number): Promise<number | null> {
  for (let step = 1; step <= bound; step++) {
    await page.keyboard.press('Tab');
    if (await focusIsOn(page, selector)) {
      return step;
    }
  }
  return null;
}

/**
 * The way from the page's start to the map. Walked without the skip link it crosses the header,
 * the rail and every control of the layer panel, which is drawn before the map; the skip link
 * makes it two presses. Past the map the page goes on in the order it is drawn: the map's own
 * zoom buttons, then the search strip over it.
 */
test('the map is two key presses from the page start, and the page reads on in order past it', async ({ page }) => {
  await login(page);
  // Signing in lands on "/", which shows the map for an account that has not chosen the
  // dashboard: the skip link offers the map there too, not only on "/map".
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 });
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Skip to map' }), 'the skip link offers the map at "/"').toBeFocused();
  await page.keyboard.press('Enter');
  expect(await focusIsOn(page, '.map-canvas:focus-visible'), 'Enter at "/" lands on the map').toBe(true);

  await gotoRoute(page, '/map');
  await expect(page.locator('.ol-viewport')).toBeVisible({ timeout: 45_000 });
  await expect(overlayTreeNode(page, 'Cave centerlines')).toBeVisible({ timeout: 15_000 });

  // The short way: one Tab to the skip link, Enter onto the map. First, while nothing has had
  // focus yet — a blurred element stays the browser's starting point for the next Tab.
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  expect(await tabsUntil(page, '.skip-link', 1), 'the skip link is the first stop').toBe(1);
  await page.keyboard.press('Enter');
  expect(await focusIsOn(page, '.map-canvas:focus-visible')).toBe(true);

  // The long way, from the skip link on by Tab alone — measured and kept as a note on the run: it
  // is what the skip link saves.
  await page.locator('.skip-link').focus();
  const walked = await tabsUntil(page, '.map-canvas', 300);
  expect(walked, 'the map is in the tab order').not.toBeNull();
  test.info().annotations.push({ type: 'Tab presses from the skip link to the map, walked', description: String(walked) });
  process.stdout.write(`Tab presses from the skip link to the map, walked: ${walked}\n`);

  // On from the map: out of it within its own few controls, and into the search strip next.
  let left = false;
  for (let step = 0; step < 10 && !left; step++) {
    await page.keyboard.press('Tab');
    left = await page.evaluate(() => document.activeElement?.closest('.map-canvas') === null);
  }
  expect(left, 'Tab leaves the map within its own controls').toBe(true);
  expect(await focusIsOn(page, '.map-search-overlay *'), 'the search strip comes next').toBe(true);

  // And back: one Shift+Tab from the map is in the docked layer panel's side of the workspace (its
  // resize handle or its last control) — the part the skip link stepped over — not in the header.
  await page.getByRole('application', { name: /^Map\./ }).focus();
  await page.keyboard.press('Shift+Tab');
  const behindMap = await page.evaluate(() => {
    const active = document.activeElement;
    const map = document.querySelector('.map-canvas');
    return (
      active !== null &&
      map !== null &&
      active.closest('.map-workspace') !== null &&
      (map.compareDocumentPosition(active) & Node.DOCUMENT_POSITION_PRECEDING) !== 0
    );
  });
  expect(behindMap, 'Shift+Tab from the map goes back into the docked panel').toBe(true);
});

/**
 * Opens the row whose name link or button `opener` is, from the keyboard: Tab from `start` until
 * the opener has focus — bounded, so a row the keyboard cannot reach fails here — then Enter.
 */
async function tabToAndPress(page: Page, start: ReturnType<Page['getByRole']>, opener: ReturnType<Page['getByRole']>) {
  await expect(opener).toBeVisible({ timeout: 30_000 });
  await start.focus();
  let reached = false;
  for (let step = 0; step < 60 && !reached; step++) {
    await page.keyboard.press('Tab');
    reached = await opener.evaluate((element) => element === document.activeElement);
  }
  expect(reached, 'the row is reached with Tab').toBe(true);
  await page.keyboard.press('Enter');
}

test('a row of the cave registry opens with Enter', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/caves');
  const search = page.getByPlaceholder('Search name or toponym…');
  await search.fill('Peștera Demo Mare');
  const link = page.locator('.ant-table-row').getByRole('link', { name: 'Peștera Demo Mare' });
  const depth = await page.evaluate(() => window.history.length);

  await tabToAndPress(page, search, link);
  await page.waitForURL(/\/caves\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: 'Peștera Demo Mare' }).first()).toBeVisible({ timeout: 30_000 });
  // One page pushed, not two: the link's own click does not also run the row's.
  expect(await page.evaluate(() => window.history.length)).toBe(depth + 1);
});

test('a row of the feature list opens with Enter', async ({ page }) => {
  await login(page);
  await registryRow(page, 'Dolina Demo');
  const link = page.locator('.ant-table-row').getByRole('link', { name: 'Dolina Demo' });
  const depth = await page.evaluate(() => window.history.length);

  await tabToAndPress(page, page.getByPlaceholder('Search by name'), link);
  await page.waitForURL(/\/features\/[0-9a-f-]{36}$/);
  expect(await page.evaluate(() => window.history.length)).toBe(depth + 1);
});

test('a row of the map’s in-view list is picked with Enter', async ({ page }) => {
  await login(page);
  await centreOnDemoCave(page);
  const dock = page.locator('.map-right-tabs');
  const tab = dock.getByRole('tab', { name: 'In view' });
  await tab.click();

  await tabToAndPress(page, tab, dock.getByRole('button', { name: 'Upper entrance' }));
  // What the row's click does: the entrance's cave in the selection.
  await dock.getByRole('tab', { name: 'Selection' }).click();
  await expect(dock.getByRole('heading', { name: 'Peștera Demo Mare' })).toBeVisible({ timeout: 15_000 });
});

/**
 * Survey lines the map holds back at this zoom, said on the map while the layer panel is closed:
 * reached with Tab from the map, announced once, and opening the panel at its survey-line block.
 */
test('withheld survey lines are said on the map while the layer panel is closed, and the notice opens it', async ({
  page,
}) => {
  // As an account of its own: closing the layer panel is a dock preference, kept with the account.
  const member = await registerAccount(page.request, 'withheld-notice');
  await login(page, member.email, member.password);
  // The demonstration data has far fewer survey lines than any line budget, so the answer is held
  // to say three were left out: what is driven here is what the map says about a count, not how
  // the server arrives at one. The lines it does send are drawn as sent.
  await page.route(
    (url) => url.pathname === '/api/v1/map/cave-centerlines',
    async (route) => {
      const response = await route.fetch();
      const body = (await response.json()) as Record<string, unknown>;
      await route.fulfill({ response, json: { ...body, withheldCount: 3 } });
    },
  );
  await gotoRoute(page, '/map');
  const node = overlayTreeNode(page, 'Cave centerlines');
  await expect(node).toBeVisible({ timeout: 15_000 });
  await node.locator('.ant-tree-checkbox').click();

  // With the panel open the panel says it, and the map does not.
  await expect(page.getByText(/^3 more centerline/)).toBeVisible({ timeout: 15_000 });
  const notice = page.getByTestId('map-centerline-notice');
  await expect(notice).toHaveCount(0);

  await page.getByTestId('map-dock-toggle-left').click();
  await expect(notice).toBeVisible({ timeout: 15_000 });
  await expect(notice).toHaveText(/3 survey line\(s\) hidden at this zoom/);
  await expect(page.getByTestId('map-centerline-notice-live')).toHaveText(/3 survey line\(s\) hidden/);

  await page.getByRole('application', { name: /^Map\./ }).focus();
  expect(await tabsUntil(page, '[data-testid="map-centerline-notice"]', 20), 'the notice is reached with Tab').not.toBeNull();
  await page.keyboard.press('Enter');

  // The panel is open at the survey-line block, where the same count and the line budget are.
  await expect(page.getByTestId('map-centerline-status')).toBeFocused({ timeout: 15_000 });
  await expect(page.getByTestId('map-centerline-status')).toContainText(/3 more centerline/);
  await expect(notice).toHaveCount(0);
  await expect(page.getByTestId('map-centerline-notice-live')).toHaveText('');
  // The map keeps loading lines after the test is done with it; an answer still in flight when the
  // page closes must not be reported against whichever test runs next.
  await page.unrouteAll({ behavior: 'ignoreErrors' });
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

/**
 * A region of a picture is reached and opened without a pointer: Tab lands on it — named by what
 * it links to, not by its colour — and Enter opens its card, as a click on it does. The picture is
 * generated noise, and the link and the trip it reaches are this run's own: a borrowed "newest trip"
 * may be one another spec running beside this one is about to delete, and a link to a deleted trip
 * is rightly drawn nowhere.
 */
test('a region of a picture is reached with Tab and opened with Enter', async ({ page }) => {
  await login(page);
  const token = await bearerToken(page);
  const picture = await uploadMapPng(page, token, `E2E region keyboard ${Date.now()}.png`);
  const trip = (await apiJson(
    page,
    token,
    'POST',
    '/api/v1/trip-logs',
    tripBody(`E2E region keyboard trip ${Date.now()}`, localDay(0)),
  )) as { id: string };
  const relationTypes = (await apiJson(page, token, 'GET', '/api/v1/reslinks/relation-types')) as {
    id: number;
    code: string;
  }[];
  await apiJson(page, token, 'POST', '/api/v1/reslinks', {
    relationTypeId: relationId(relationTypes, 'related-to'),
    description: null,
    members: [
      {
        targetType: 'document',
        targetId: picture.documentId,
        // An undirected relation has no main member.
        isMain: false,
        sortOrder: 0,
        note: null,
        anchorKind: 'imageRegion',
        anchor: { shape: 'rect', x: 0.2, y: 0.2, w: 0.4, h: 0.4 },
        anchorFileId: picture.id,
      },
      {
        targetType: 'tripLog',
        targetId: trip.id,
        isMain: false,
        sortOrder: 1,
        note: null,
        anchorKind: 'whole',
        anchor: null,
        anchorFileId: null,
      },
    ],
  });

  await gotoRoute(page, `/documents/${picture.documentId}`);
  const region = page.getByRole('button', { name: /^Related to/ });
  await expect(region).toBeVisible({ timeout: 20_000 });

  // Reached by Tab from the page, bounded so a region outside the tab order fails here.
  await page.locator('main').focus();
  let reached = false;
  for (let step = 0; step < 60 && !reached; step++) {
    await page.keyboard.press('Tab');
    reached = await region.evaluate((element) => element === document.activeElement);
  }
  expect(reached, 'the region is in the tab order').toBe(true);
  await expect(page.locator('.ant-popover:visible')).toHaveCount(0);
  await page.keyboard.press('Enter');
  await expect(page.locator('.ant-popover:visible')).toHaveCount(1, { timeout: 10_000 });
  await apiJson(page, token, 'DELETE', `/api/v1/trip-logs/${trip.id}`);
});

/**
 * A table whose rows open on a click opens from the keyboard too: the cell naming the record is a
 * link, which Tab reaches and Enter follows — to the same page the click goes to.
 */
test('a row of the trip list opens with Enter', async ({ page }) => {
  await login(page);
  await gotoRoute(page, '/trip-logs');
  const firstLink = page.locator('.ant-table-row').first().getByRole('link').first();
  await expect(firstLink).toBeVisible({ timeout: 20_000 });
  const target = await firstLink.getAttribute('href');
  expect(target).toMatch(/^\/trip-logs\/[0-9a-f-]+$/);

  await page.locator('main').focus();
  let reached = false;
  for (let step = 0; step < 80 && !reached; step++) {
    await page.keyboard.press('Tab');
    reached = await page.evaluate(() => document.activeElement?.closest('.ant-table-row') !== null
      && document.activeElement?.tagName === 'A');
  }
  expect(reached, 'a link inside a row is in the tab order').toBe(true);
  const focusedTarget = await page.evaluate(() => document.activeElement?.getAttribute('href'));
  await page.keyboard.press('Enter');
  await page.waitForURL((url) => url.pathname === focusedTarget, { timeout: 15_000 });
  // One step in the history, not two: the row's own click handler must not have run as well.
  await page.goBack();
  await page.waitForURL((url) => url.pathname === '/trip-logs', { timeout: 15_000 });
});

/** The WCAG 2.0, 2.1 and 2.2 level A and AA rules — the yardstick, with no conformance claimed. */
const WCAG_AA_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];


/**
 * The higher-contrast appearance, switched on in the accessibility settings, redraws a field's
 * outline in the readable grey at once and keeps it after a reload, because it is kept with the
 * account. A registered account of its own: the switch is an account preference, and the shared
 * administrator's must not change under the other specs.
 */
test('turning higher contrast on draws a field outline in the readable grey', async ({ page }) => {
  const member = await registerAccount(page.request, 'high-contrast');
  await login(page, member.email, member.password);

  const outline = async () => {
    await gotoRoute(page, '/tools/coordinates');
    const field = page.getByTestId('converter-input');
    await expect(field).toBeVisible();
    // A field that can be cleared draws its outline on the wrapper around the input.
    return field.evaluate((element) =>
      getComputedStyle(element.closest('.ant-input-affix-wrapper') ?? element).borderTopColor);
  };
  expect(await outline()).toBe('rgb(217, 217, 217)');

  await gotoRoute(page, '/settings/accessibility');
  await page.getByRole('combobox', { name: 'Theme' }).click();
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: /^Light$/ }).click();
  const written = page.waitForResponse(
    (response) => response.url().endsWith('/api/v1/me/preferences') && response.request().method() === 'PATCH',
  );
  await page.getByRole('switch', { name: 'Higher contrast' }).click();
  expect((await written).ok()).toBe(true);
  await expect(page.locator('html')).toHaveAttribute('data-contrast', 'high');

  expect(await outline()).toBe('rgb(140, 140, 140)');
});

/**
 * The one rule the scan leaves out, everywhere: colour contrast. The screens are drawn in the
 * component library's own colours, several of which read below the 4.5:1 (text) and 3:1 (field
 * outline) the rule asks for — the owner chose on 2026-10-18 to keep that look for now and to
 * postpone the contrast changes, which are kept aside to come back as an optional higher-contrast
 * appearance. Every other rule is still scanned on every screen in both themes; the keyboard focus
 * ring, the one colour kept from that work, is held at 3:1 by the theme's unit tests instead.
 */
const POSTPONED_RULES = ['color-contrast'];

/**
 * Findings the scan knows about and accepts for now, each by screen, by rule AND by the one element
 * it was found on — apart from the postponed rule above, never a rule turned off for a page, so the
 * same mistake made anywhere else still fails. Each says why it stands and where it is registered to be fixed, and an entry the
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
  const results = await new AxeBuilder({ page }).withTags(WCAG_AA_TAGS).disableRules(POSTPONED_RULES).analyze();
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

    test('the feature list and the map’s in-view list', async ({ page }) => {
      await login(page);
      // Unnarrowed, as the cave registry is scanned: a typed search adds the input's clear icon,
      // which is registered on its own.
      await gotoRoute(page, '/features');
      await expect(page.locator('.ant-table-row').first()).toBeVisible({ timeout: 30_000 });
      await scan(page, 'the feature list');

      await centreOnDemoCave(page);
      const dock = page.locator('.map-right-tabs');
      await dock.getByRole('tab', { name: 'In view' }).click();
      await expect(dock.getByRole('button', { name: 'Upper entrance' })).toBeVisible({ timeout: 20_000 });
      await scan(page, 'the map’s in-view list');
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

      await gotoRoute(page, '/settings/filters');
      await expect(page.getByTestId('saved-filter-default-audience-explanation')).toBeVisible({ timeout: 30_000 });
      await scan(page, 'the saved-filter settings');
    });
  });
}
