// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { login } from './helpers.ts';

/**
 * Handing caves to somebody else in the shared interchange vocabulary, and being asked first
 * what the file should say about a location this installation protects.
 *
 * Three things are driven here that no server test can reach, because each of them is about the
 * moment a person decides rather than about what the server does with the decision:
 *
 *  - that the exporter is actually **asked**. The route refuses an export that covers a
 *    protected cave with nothing, so a client that never asked would produce a refusal rather
 *    than a file — but it would also produce a screen that looks broken for a reason nothing on
 *    it explains. Only a browser can show that the question is put before the download starts.
 *  - that the **size of the decision is shown before it is made**. Three caves and three
 *    thousand are different decisions and the same click; a chooser that did not say which one
 *    this is would be collecting a press rather than an answer.
 *  - that choosing to leave caves out produces a file **which says so**. That is the property
 *    the whole option stands on: counts taken from the file will disagree with the registry,
 *    and a recipient who cannot tell has been misled by a document containing only true
 *    statements. The assertion is on the downloaded bytes, not on a response body, because what
 *    travels is the file.
 *
 *  - that somebody who ticked **"stop asking me" is not asked again**. A remembered answer that
 *    only pre-selects a radio button in a dialog that reappears is a promise the product does
 *    not keep, and nothing but driving the second export can tell the two apart.
 *
 * It runs as an account minted for this run rather than as the demonstration administrator. That
 * is not tidiness: an administrator reads past visibility at the widest scope, so the set of
 * caves in the file would follow the installation rather than the filter. (What has to be
 * decided about a protected position does not depend on who is asking — that is the server's
 * rule and it is asserted there.)
 */
test('an interchange export asks what to do about protected caves, and says what it did', async ({
  page,
  request,
}) => {
  test.slow();

  const stamp = Date.now();
  const email = `e2e-karstlink-${stamp}@dev.local`;
  const password = 'e2e-karstlink-pass-1';

  const config = await (await request.get('/api/v1/auth/config')).json();
  expect(
    config.openRegistration,
    'this flow needs an account that is not the administrator: start the API with SILEXGIS__Auth__OpenRegistration=true',
  ).toBe(true);
  const registered = await request.post('/api/v1/auth/register', {
    data: { email, password, displayName: `E2E KarstLink ${stamp}` },
  });
  expect(registered.ok()).toBeTruthy();

  await login(page, email, password);
  await page.goto('/caves');

  // The demonstration data's protected pit. Narrowing to it is what makes the numbers below
  // fixed: the whole export is one cave, and that cave's position is one this account may not
  // see. Without the filter the counts would follow whatever else the installation holds.
  await page.getByPlaceholder(/name or toponym/i).fill('Protejat');
  await expect(page.getByRole('cell', { name: 'Avenul Demo Protejat' })).toBeVisible();

  // The export menu opens on hover, the way every other export on this page is reached.
  await page.getByRole('button', { name: 'Export' }).hover();
  await page.getByRole('menuitem', { name: 'KarstLink (JSON-LD)' }).click();

  // Asked, and asked before anything is downloaded.
  const chooser = page.getByTestId('karstlink-export-treatment');
  await expect(chooser).toBeVisible();

  // How big the decision is, in the dialog that asks it.
  const count = page.getByTestId('karstlink-export-count');
  await expect(count).toContainText('1 cave(s) in this export.');
  await expect(count).toContainText('1 of them have a location this installation protects');

  // Exactly three answers and no fourth. The surveyed position is not offered — and is not
  // offered because the server's vocabulary has no member naming it, which is what makes this
  // an assertion about the product rather than about this screen.
  await expect(chooser.getByRole('radio')).toHaveCount(3);
  await expect(page.getByTestId('karstlink-export-treatment-grid_position')).toBeVisible();
  await expect(page.getByTestId('karstlink-export-treatment-no_position')).toBeVisible();
  await expect(page.getByTestId('karstlink-export-treatment-omit')).toBeVisible();

  await page.getByTestId('karstlink-export-treatment-omit').click();

  // Ticked here so the second half of this test measures what the label promises.
  await page.getByTestId('karstlink-export-remember').check();

  const download = page.waitForEvent('download');
  await page.getByTestId('karstlink-export-download').click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/^caves-karstlink-\d{8}\.jsonld$/);

  const written = await file.path();
  const document = JSON.parse(readFileSync(written, 'utf8'));

  // The file holds no cave, and — the part that matters — says that it does not, in a count a
  // machine reads and a sentence a person reads. Either one alone would be a file whose
  // shortfall somebody could miss.
  expect(document.caveCount).toBe(0);
  expect(document.omittedCaveCount).toBe(1);
  expect(document.provenance).toContain('deliberately left out');
  expect(JSON.stringify(document)).not.toContain('Avenul Demo Protejat');

  // Asked once. The second export starts from the same menu item and produces a file with no
  // dialog in between — the answer settled on above travels with the request instead.
  await expect(chooser).toBeHidden();
  await page.getByRole('button', { name: 'Export' }).hover();
  const repeat = page.waitForEvent('download');
  await page.getByRole('menuitem', { name: 'KarstLink (JSON-LD)', exact: true }).click();

  const second = await repeat;
  await expect(chooser).toBeHidden();

  const repeated = JSON.parse(readFileSync(await second.path(), 'utf8'));
  expect(repeated.caveCount).toBe(0);
  expect(repeated.omittedCaveCount).toBe(1);

  // And the answer is still reachable, so "not asked again" is not the same as "stuck".
  await page.getByRole('button', { name: 'Export' }).hover();
  await expect(page.getByRole('menuitem', { name: /change my answer/i })).toBeVisible();
});
