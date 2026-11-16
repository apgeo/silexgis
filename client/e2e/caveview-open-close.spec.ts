// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { tryAsPerson } from './arrange.ts';
import { caveWithSurvey, openModel } from './caveviewLabelSizeFlow.ts';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

// A survey opened and closed again and again in its dialog, in the real viewer. The unit tests show
// that closing the viewer hands its drawing context back and calls off its download, but they show
// it against stand-ins for the context and the download. What only a browser can show is the
// consequence: it keeps a small number of drawing contexts alive at once — sixteen in the one this
// suite runs — and past that it takes the OLDEST away, with a warning on the console. A dialog
// that leaked its context on every close would reach the limit within a working session, and from
// then on every model opened would take the drawing away from something else on the page.

/** Comfortably past the number of live drawing contexts the browser allows at once. */
const OPENINGS = 18;

/** The caves this file made, removed again when the test that made them ends — passed or failed. */
const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

test('a survey opened and closed many times leaves one live drawing and never runs the browser out of them', async ({
  page,
}) => {
  test.setTimeout(600_000);
  const warnings: string[] = [];
  page.on('console', (message) => {
    if (/too many active webgl contexts|context lost/i.test(message.text())) {
      warnings.push(message.text());
    }
  });
  await login(page);
  const caveId = await caveWithSurvey(page, madeCaves);

  // Opened from the cave's page once, and from then on again and again from the same page: a
  // page load would hand every context back by itself and the test would prove nothing.
  let viewer = await openModel(page, caveId);
  const reopen = async () => {
    await page
      .locator('.ant-card', { hasText: '3D survey models' })
      .getByRole('row')
      .filter({ hasText: 'Label sizes' })
      .getByRole('button', { name: 'View in 3D' })
      .click();
    viewer = page.getByRole('dialog').filter({ has: page.getByTestId('caveview-container') });
    await expect(viewer.getByTestId('caveview-container').locator('canvas').first()).toBeAttached({
      timeout: 30_000,
    });
    await expect(viewer.getByTestId('caveview-loading')).toHaveCount(0, { timeout: 30_000 });
  };
  // Marked so a reload in between — which would hand every context back by itself — is caught.
  await page.evaluate(() => {
    (window as unknown as { samePage?: boolean }).samePage = true;
  });

  for (let opening = 1; opening <= OPENINGS; opening++) {
    if (opening > 1) {
      await reopen();
    }
    if (opening === OPENINGS) {
      // The last one stays open: it is the only drawing on the page, and it is alive.
      await expect(page.locator('[data-testid="caveview-container"] canvas')).toHaveCount(1);
      const lost = await viewer
        .getByTestId('caveview-container')
        .locator('canvas')
        .first()
        .evaluate((canvas: HTMLCanvasElement) => {
          const context = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
          return context === null ? 'no context' : context.isContextLost();
        });
      expect(lost).toBe(false);
      break;
    }
    await viewer.getByRole('button', { name: 'Close' }).first().click();
    await expect(page.getByTestId('caveview-container')).toHaveCount(0, { timeout: 30_000 });
  }

  expect(warnings, 'the browser took a drawing context away').toEqual([]);
  // Still the one page that was loaded at the start.
  expect(await page.evaluate(() => (window as unknown as { samePage?: boolean }).samePage)).toBe(true);
});
