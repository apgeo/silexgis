// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';

// The pages a mail link opens — confirming an address, choosing a password, opting out of a kind of
// message — are read on a phone more often than anywhere else, by somebody with no account open.
// Each is one fixed-width card in the middle of the screen, and a fixed width wider than a phone
// makes the whole page scroll sideways. The sign-in page was mended for that once on its own and
// these three, built the same way, kept the defect; so all of them are opened here at the width of
// a small phone. No link is followed through: the card is the same whatever the link holds, and an
// incomplete link answers without asking the server anything.
const pages = [
  { path: '/reset-password?email=someone%40example.test&token=not-a-real-token', heading: 'Choose a new password' },
  { path: '/reset-password?email=someone%40example.test&token=not-a-real-token&invite=1', heading: 'Choose your password' },
  { path: '/confirm-email', heading: 'Confirm your address' },
  { path: '/unsubscribe', heading: 'Notification settings' },
];

test('the pages a mail link opens fit a phone screen without a sideways scroll', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 740 });
  for (const { path, heading } of pages) {
    await page.goto(path);
    await expect(page.getByRole('heading', { name: heading }), path).toBeVisible({ timeout: 30_000 });
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow, `${path} fits a phone screen`).toBeLessThanOrEqual(0);
    // And the card itself is inside the screen on both sides: a card too wide for a centred row
    // spills past the left edge as well, where no scrollbar can reach it.
    const card = await page.locator('.ant-card').first().boundingBox();
    expect(card, `${path} shows its card`).not.toBeNull();
    expect(card!.x, `${path} card starts on screen`).toBeGreaterThanOrEqual(0);
    expect(card!.x + card!.width, `${path} card ends on screen`).toBeLessThanOrEqual(360);
  }
});
