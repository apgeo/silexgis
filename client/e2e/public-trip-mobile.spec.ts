// SPDX-License-Identifier: AGPL-3.0-or-later
import { join } from 'node:path';
import { expect, type Page } from '@playwright/test';
import { CHOICE_KEY } from '../src/i18n/languageStorage.ts';
import { PUBLIC_CHOICE_KEY } from '../src/pages/public/publicLanguageStorage.ts';
import { firstVisitContext, test } from './consoleGuard.ts';

/**
 * A published trip on the device it is actually opened on.
 *
 * <b>This is the one surface of this application whose reader is not a caver.</b> A follow link is
 * sent to a family while a party is underground, and it is opened on a phone, by somebody with no
 * account, who wants to know one thing: are they out yet. So it is driven here as a real
 * navigation in a phone viewport and signed out — nothing about that can be shown by rendering the
 * component in a unit test, which is where every other fact about this page is proved.
 *
 * <b>The envelope is served by this test rather than by a trip.</b> Standing a publishable trip up
 * through the interface means a survey model, a tracking configuration, an armed watch, five
 * reports and a mint — most of a minute of clicking to arrive at one answer whose shape the server
 * already has its own tests for. What this spec is for is the browser: the layout at 360px, the
 * three states of a party as they read on screen, a page that stops asking when the trip is over,
 * and the headers that decide whether any of it can be framed. Two of those cannot be faked, and
 * they are taken from the real server: the refusal a bad token gets, and the framing policy.
 */

const TOKEN = 'e2e-follow-token';
const TEAM_A = '11111111-1111-1111-1111-111111111111';
const TEAM_B = '22222222-2222-2222-2222-222222222222';

/** The narrowest screen this page is designed for; the project's own device is wider. */
const NARROWEST = { width: 360, height: 740 };

const minutesAgo = (minutes: number) =>
  new Date(Date.now() - minutes * 60_000).toISOString();

function envelope(overrides: Record<string, unknown> = {}) {
  return {
    title: 'Peștera Demo Mare — explorare',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: minutesAgo(180),
    closedAt: null,
    positionsWithheld: false,
    model: null,
    teams: [
      { id: TEAM_A, title: 'Advance' },
      { id: TEAM_B, title: 'Survey' },
    ],
    participants: [
      {
        ordinal: 1,
        label: 'Ana',
        teamId: TEAM_A,
        stationName: 'p.g.7',
        depthM: null,
        lastRecordedAt: minutesAgo(12),
        in: true,
        out: false,
      },
      {
        ordinal: 2,
        label: null,
        teamId: TEAM_A,
        stationName: null,
        depthM: 84,
        lastRecordedAt: minutesAgo(35),
        in: true,
        out: false,
      },
      {
        ordinal: 3,
        label: 'Carmen',
        teamId: TEAM_B,
        stationName: null,
        depthM: null,
        lastRecordedAt: minutesAgo(5),
        in: false,
        out: true,
      },
      {
        ordinal: 4,
        label: null,
        teamId: null,
        stationName: null,
        depthM: null,
        lastRecordedAt: null,
        in: false,
        out: false,
      },
    ],
    ...overrides,
  };
}

/**
 * Answers the follow read with a given envelope, and counts how many times it was asked.
 *
 * The count is the whole point of the closed-trip test: a link handed round a club sits open in
 * tabs nobody closes, and a page that kept asking after the party came out would be an unknown
 * number of strangers' browsers polling a route with no session behind it, forever.
 */
async function servePublishedTrip(page: Page, body: Record<string, unknown>) {
  const asked = { count: 0 };
  await page.route('**/api/v1/public/trips/**', async (route) => {
    asked.count++;
    await route.fulfill({ json: body });
  });
  return asked;
}

/** Serves the survey file the envelope's delivery URL points at. */
async function serveSurveyFile(page: Page) {
  await page.route('**/api/v1/files/**', (route) =>
    route.fulfill({
      path: join(import.meta.dirname, 'fixtures', 'P8_Master.3d'),
      contentType: 'application/octet-stream',
    }),
  );
}

test.describe('following a published trip on a phone', () => {
  test('a link nobody issued lands on a page, not on a payload', async ({ page, consoleErrors }) => {
    // Declared rather than left looking like a defect: the 404 IS the subject here. A malformed
    // token, an unknown one, a revoked one and a cave somebody has since protected all answer
    // identically on purpose, because telling them apart tells a stranger which tokens exist.
    consoleErrors.allow(
      /Failed to load resource.*404/,
      'the not-found answer this test navigates to on purpose',
    );

    await page.setViewportSize(NARROWEST);
    await page.goto('/shared/trips/definitely-not-a-real-token-000');

    await expect(page.getByTestId('public-trip-not-found')).toBeVisible({ timeout: 20_000 });
    await expect(page.getByText('Nothing to show for this link')).toBeVisible();
    // Nothing on it invites an account, because the reader has none and every other address here
    // would refuse them.
    await expect(page.locator('a')).toHaveCount(0);
  });

  test('the party reads at 360px: grouped by team, named as the club named them', async ({ page }) => {
    await servePublishedTrip(page, envelope());
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);

    await expect(page.getByTestId('public-trip-title')).toHaveText('Peștera Demo Mare — explorare');

    // The club's own arrangement of its party, in the club's own order.
    const headings = page.getByTestId('public-trip-party').getByRole('heading', { level: 2 });
    await expect(headings).toHaveText(['Advance', 'Survey', 'Not in a team']);

    // A name where an administrator typed one, a place in the party where nobody did.
    await expect(page.getByTestId('public-trip-caver-1')).toContainText('Ana');
    await expect(page.getByTestId('public-trip-caver-2')).toContainText('Caver 2');
    await expect(page.getByTestId('public-trip-caver-1')).toContainText('p.g.7');

    // The three states, told apart. Somebody nobody has reported is not somebody who is back.
    await expect(page.getByTestId('public-trip-caver-3')).toContainText('Out');
    await expect(page.getByTestId('public-trip-caver-4')).toContainText('Not reported yet');
    await expect(page.getByTestId('public-trip-count-underground')).toHaveText('2');
    await expect(page.getByTestId('public-trip-count-out')).toHaveText('1');
    await expect(page.getByTestId('public-trip-count-unheard')).toHaveText('1');

    // Nothing is reachable only by dragging the page sideways — the failure this layout was
    // written against, and one no assertion about a component's markup can see.
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(1);
  });

  test('offers a reader with no account nothing they cannot reach', async ({ page }) => {
    await servePublishedTrip(page, envelope());
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);
    await expect(page.getByTestId('public-trip')).toBeVisible();

    // No workspace chrome, and above all no links: every other address in this installation
    // refuses this reader, so a link would be a door that is locked.
    await expect(page.locator('a')).toHaveCount(0);
    await expect(page.locator('.ant-layout-header')).toHaveCount(0);
    await expect(page.locator('.ant-layout-sider')).toHaveCount(0);
  });

  test('keeps asking while the party is underground', async ({ page }) => {
    // The control for the test below, and the reason both are written with a fake clock.
    //
    // A published trip is re-read once a minute. An assertion that waits a few real seconds and
    // finds the count unchanged is true whether the page stopped polling or never polled at all —
    // it measures the wait, not the behaviour. So time is faked before anything on the page runs,
    // and this test proves the measurement can see a poll before the next one claims there is not
    // one: delete the condition that stops a closed trip polling and this still passes, delete the
    // polling altogether and it fails.
    await page.clock.install();
    const asked = await servePublishedTrip(page, envelope());
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);

    await expect(page.getByTestId('public-trip')).toBeVisible();
    const afterLoad = asked.count;
    await page.clock.runFor(3 * 60_000);

    await expect.poll(() => asked.count).toBeGreaterThan(afterLoad);
  });

  test('stops asking once the trip is over', async ({ page }) => {
    // A link handed round a club sits open in tabs nobody closes. A page that kept asking after
    // the party came out would be an unknown number of strangers' browsers polling a route with
    // no session behind it, for as long as those tabs stay open.
    await page.clock.install();
    const asked = await servePublishedTrip(
      page,
      envelope({ state: 'closed', closedAt: minutesAgo(20), armedAt: minutesAgo(300) }),
    );
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);

    await expect(page.getByTestId('public-trip-closed')).toBeVisible();
    const afterLoad = asked.count;
    // Three of the minutes an armed trip would have been re-read on, spent in no time at all.
    await page.clock.runFor(3 * 60_000);

    expect(asked.count).toBe(afterLoad);
  });

  test('draws the party on the survey, with the list a finger can reach', async ({ page }) => {
    await serveSurveyFile(page);
    await servePublishedTrip(
      page,
      envelope({
        model: {
          format: 'survex3d',
          modelUrl: '/api/v1/files/00000000-0000-0000-0000-000000000001/content?token=e2e',
          meshUrl: null,
          anchorLongitude: null,
          anchorLatitude: null,
          anchorHeightM: null,
          sourceEpsg: null,
          proj4: null,
        },
      }),
    );
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);

    // The overlay is drawn only once the model has loaded, so its presence is the load.
    await expect(page.getByTestId('caveview-tracking')).toBeVisible({ timeout: 90_000 });
    await expect(page.getByTestId('caveview-tracking')).toContainText('Cavers (4)');

    // Folded behind its own count at this width, because a list of names takes a third of a phone
    // screen — and then opened by the tap that is the whole point of it. On a screen with no
    // pointer that can hover, this list is the only way to a caver's card: the marker hover a
    // mouse uses never happens under a finger.
    await expect(page.getByTestId('caveview-caver-1')).toHaveCount(0);
    await page.getByTestId('caveview-tracking-toggle').tap();
    await page.getByTestId('caveview-caver-1').tap();
    await expect(page.getByTestId('caveview-caver-card')).toContainText('Ana');
  });
});

/**
 * The language a published trip is read in.
 *
 * The readers of this page are not members and did not choose anything: a family in another
 * country is sent the same link as the club's own. The installation opens in Romanian, and that
 * is what a link that says nothing opens in; a link may say otherwise, and so may the reader, by
 * a button that is on the page in the language it offers.
 */
test.describe('the language of a published trip', () => {
  test('changes at the button, which says so to the browser and in the address', async ({ page }) => {
    await servePublishedTrip(page, envelope());
    await page.setViewportSize(NARROWEST);
    await page.goto(`/shared/trips/${TOKEN}`);

    // This suite's browsers have chosen English, and an address that names no language leaves a
    // choice already made alone.
    const state = page.getByTestId('public-trip-state-armed');
    await expect(state).toHaveText('Underground now');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');

    // The invitation is written in the language it leads to, and marked as that language — a
    // screen reader would otherwise say a Romanian word in an English voice.
    const button = page.getByTestId('public-trip-language');
    await expect(button).toHaveText('Română');
    await expect(button).toHaveAttribute('lang', 'ro');
    await button.tap();

    await expect(state).toHaveText('În peșteră acum');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ro');
    await expect(button).toHaveText('English');
    // In the address, so that the page sent on from here opens the way it was being read — and
    // put there in place of the address before it: a language is not somewhere to go back to.
    expect(new URL(page.url()).searchParams.get('lang')).toBe('ro');
    expect(new URL(page.url()).pathname).toBe(`/shared/trips/${TOKEN}`);
    // Pressing is the reader choosing, and it is remembered in this browser — for published
    // trips. The record the signed-in application keeps of its own language is not this page's
    // to write: it still says what this suite's browsers chose there.
    const stored = (key: string) => page.evaluate((name) => window.localStorage.getItem(name), key);
    expect(await stored(PUBLIC_CHOICE_KEY)).toBe('ro');
    expect(await stored(CHOICE_KEY)).toBe('en');

    // So the link opened again, naming no language, opens the way this reader left it — although
    // the application itself reads English in this browser.
    await page.goto(`/shared/trips/${TOKEN}`);
    await expect(state).toHaveText('În peșteră acum');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ro');

    // And an address that names a language wins over what was pressed here before: whoever wrote
    // the link wrote it for readers of that language. Nothing pressed, nothing rewritten.
    await page.goto(`/shared/trips/${TOKEN}?lang=en`);
    await expect(state).toHaveText('Underground now');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    expect(await stored(PUBLIC_CHOICE_KEY)).toBe('ro');
  });

  test('is Romanian for a first visit by a link that names none, on the page and in the frame', async ({
    browser,
  }) => {
    // A browser that has never been here: no language recorded, which no other context in this
    // suite can say of itself.
    const visitor = await firstVisitContext(browser, { viewport: NARROWEST });
    try {
      const page = await visitor.newPage();
      await servePublishedTrip(page, envelope());

      await page.goto(`/shared/trips/${TOKEN}`);
      const state = page.getByTestId('public-trip-state-armed');
      await expect(state).toHaveText('În peșteră acum');
      await expect(page.locator('html')).toHaveAttribute('lang', 'ro');
      await expect(page.getByTestId('public-trip-language')).toHaveText('English');

      // The same link with a language on it, in the same browser. Nothing was pressed, so
      // nothing is remembered: the language was the link's author's choice and not this
      // reader's, and it lasts for the visit.
      await page.goto(`/shared/trips/${TOKEN}?lang=en`);
      await expect(state).toHaveText('Underground now');
      await expect(page.locator('html')).toHaveAttribute('lang', 'en');
      expect(await page.evaluate((key) => window.localStorage.getItem(key), CHOICE_KEY)).toBeNull();
      // An address that names a language this application does not speak names none.
      await page.goto(`/shared/trips/${TOKEN}?lang=de`);
      await expect(state).toHaveText('În peșteră acum');

      // The frame a club pastes into an article answers the same way, and carries the button in
      // the two letters a narrow box has room for.
      await page.goto(`/shared/trips/${TOKEN}/embed`);
      await expect(page.getByText('Nu există nicio ridicare topografică de arătat')).toBeVisible();
      await expect(page.locator('html')).toHaveAttribute('lang', 'ro');
      await expect(page.getByTestId('public-trip-language')).toHaveText('EN');
      await page.goto(`/shared/trips/${TOKEN}/embed?lang=en`);
      await expect(page.getByText('There is no survey drawing to show for this trip.')).toBeVisible();
      await expect(page.getByTestId('public-trip-language')).toHaveText('RO');

      // Last, this reader presses the button — in the one browser of the suite where nothing
      // puts a language back between pages, so what is read below is what the press left.
      const stored = (key: string) => page.evaluate((name) => window.localStorage.getItem(name), key);
      await page.goto(`/shared/trips/${TOKEN}`);
      await expect(state).toHaveText('În peșteră acum');
      await page.getByTestId('public-trip-language').tap();
      await expect(state).toHaveText('Underground now');
      expect(await stored(PUBLIC_CHOICE_KEY)).toBe('en');
      // The signed-in application was told nothing: it would still open in Romanian here, and an
      // account signing in on this machine is still given its own language.
      expect(await stored(CHOICE_KEY)).toBeNull();

      // An address naming the other language wins over the press, and leaves it standing…
      await page.goto(`/shared/trips/${TOKEN}?lang=ro`);
      await expect(state).toHaveText('În peșteră acum');
      await expect(page.locator('html')).toHaveAttribute('lang', 'ro');
      expect(await stored(PUBLIC_CHOICE_KEY)).toBe('en');
      // …so the link that names none opens the way the reader chose, page and frame alike.
      await page.goto(`/shared/trips/${TOKEN}`);
      await expect(state).toHaveText('Underground now');
      await expect(page.locator('html')).toHaveAttribute('lang', 'en');
      await page.goto(`/shared/trips/${TOKEN}/embed`);
      await expect(page.getByText('There is no survey drawing to show for this trip.')).toBeVisible();
    } finally {
      await visitor.close();
    }
  });
});

test.describe('who may frame this application', () => {
  test('denies framing everywhere, and allows it on a published trip', async ({ page }) => {
    // Taken from the server that is actually running rather than from a configuration file, because
    // the defect this guards against is a header nobody sends: every page still loads, nothing
    // appears in a log, and the only difference is that another site can load this one invisibly
    // over its own controls.
    const workspace = await page.request.get('/map');
    expect(workspace.headers()['content-security-policy']).toContain("frame-ancestors 'none'");
    expect(workspace.headers()['x-frame-options']).toBe('DENY');
    expect(workspace.headers()['x-robots-tag']).toBeUndefined();

    for (const path of [`/shared/trips/${TOKEN}`, `/shared/trips/${TOKEN}/embed`]) {
      const published = await page.request.get(path);
      expect(published.headers()['content-security-policy']).toContain("frame-ancestors 'self'");
      expect(published.headers()['content-security-policy']).not.toContain("'none'");
      // The header that cannot name an origin must not be sent beside the one that can: it would
      // refuse exactly what the other allows, and the embed would fail as a blank frame.
      expect(published.headers()['x-frame-options']).toBeUndefined();
      // Framing is how this page legitimately reaches a club's public article, and the token in the
      // paste-in block goes into that article's HTML — so the same permission that makes the embed
      // work is what walks a crawler up to a page naming the party. An index outlives revocation,
      // which is the only remedy a follow link has.
      expect(published.headers()['x-robots-tag']).toBe('noindex, nofollow');
    }
  });
});
