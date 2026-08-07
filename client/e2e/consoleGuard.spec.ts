// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { test } from './consoleGuard.ts';

// The console sweep, proving itself.
//
// Everything else in this suite exercises the sweep only by accident: these flows happen not to
// throw, so the path that matters most — an uncaught error being recorded, once, with the stack that
// says where it came from — was never run by anything. A mechanism whose whole purpose is to be
// trusted when it says "no errors" cannot have that path unproven, because a guard that silently
// records nothing looks exactly like an application that is working.
//
// No application involved: a blank page and a deliberate throw. That keeps this fast, keeps it
// independent of every other thing that could be wrong, and means the errors it makes are certainly
// its own. Desktop only — the mechanism is not per-device, and what these assert about how a browser
// reports a throw is chromium's behaviour.
//
// Each test declares the error it makes before it ends, so the suite stays green and nothing this
// file does is ever reported as a defect of the application.

/** Throws inside a task nothing is waiting on, which is what makes it uncaught. */
async function throwUncaught(page: Page, message: string) {
  // Not `evaluate(() => { throw … })`: that rejects the evaluate call instead, which is a handled
  // error in the test and reaches the page's error reporting not at all.
  await page.evaluate((text) => {
    setTimeout(() => {
      throw new Error(text);
    }, 0);
  }, message);
}

test('an uncaught error is recorded once, as a throw, with the frame it came from', async ({
  page,
  consoleErrors,
}) => {
  const message = 'deliberate uncaught error, proving the sweep records one';
  await page.goto('about:blank');
  await throwUncaught(page, message);

  // Exactly one, and this is the assertion that matters: the browser reports an uncaught error
  // twice, as the throw and as a console line about it, and two records would mean one defect
  // filed as two — the second of them with no stack and no way to locate it.
  await expect
    .poll(() => consoleErrors.captured().filter((error) => error.message.includes(message)).length, {
      message: 'the sweep did not record the uncaught error exactly once',
    })
    .toBe(1);

  const [recorded] = consoleErrors.captured().filter((error) => error.message.includes(message));
  expect(recorded.kind).toBe('uncaught');
  expect(recorded.name).toBe('Error');
  expect(recorded.fingerprint).toMatch(/^[0-9a-f]{8}$/);

  consoleErrors.allow(new RegExp(message), 'thrown by this test to prove the sweep records it');
  expect(consoleErrors.captured()).toHaveLength(0);
});

test('a console error is recorded, and declaring it takes it back out', async ({
  page,
  consoleErrors,
}) => {
  const message = 'deliberate console error, proving the sweep records one';
  await page.goto('about:blank');
  await page.evaluate((text) => console.error(text), message);

  await expect
    .poll(() => consoleErrors.captured().length, { message: 'the sweep recorded no console error' })
    .toBe(1);
  expect(consoleErrors.captured()[0].kind).toBe('console');

  // The other half of the mechanism: what a test says it expects is gone from the count, which is
  // what stands between a declared error and a failing run in enforcing mode.
  consoleErrors.allow(new RegExp(message), 'logged by this test to prove declaring one works');
  expect(consoleErrors.captured()).toHaveLength(0);
});
