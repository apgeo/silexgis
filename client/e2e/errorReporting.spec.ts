// SPDX-License-Identifier: AGPL-3.0-or-later
import { existsSync, readFileSync, truncateSync } from 'node:fs';
import path from 'node:path';
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';

// The application reporting its own errors, proved along its whole length.
//
// Everything between the listener and the file has a way of failing quietly: the route can be
// unregistered, the batch can be dropped by the navigation that follows the error, the body can be
// the wrong shape, the append can go to a path nobody reads. Each of those looks identical from the
// browser — nothing happens — and identical to an application with no errors in it. So this drives a
// real error in a real browser and then reads the file on disk.
//
// This is the one automated run that is allowed to report: the reporter stands down under a driver,
// because the console sweep already records the same errors from outside and a suite reporting through
// the application as well would fill the browsing log with test traffic. The flag below is that
// exception, and it has to be set in an init script because the reporter installs before anything the
// application renders.
//
// The file is restored to its previous length afterwards. What this test writes is a deliberate error,
// and leaving it behind would put a defect in the record that nothing in the application has.

const SINK = path.join(import.meta.dirname, '..', '.diagnostics', 'browsing-errors.jsonl');

// Mirrors the flag the reporter reads. Declared again here rather than imported because the two live
// in separate type projects — and if the name ever drifts, the poll below times out and says so.
declare global {
  interface Window {
    __silexgisReportUnderAutomation?: boolean;
  }
}

/** The sink's current length in bytes, or zero when nothing has written to it yet. */
function sinkLength(): number {
  return existsSync(SINK) ? readFileSync(SINK).length : 0;
}

/** Everything appended past `from`, parsed. */
function recordsAfter(from: number): Record<string, unknown>[] {
  if (!existsSync(SINK)) {
    return [];
  }
  return readFileSync(SINK)
    .subarray(from)
    .toString('utf8')
    .split('\n')
    .filter((line) => line.trim())
    .flatMap((line) => {
      try {
        return [JSON.parse(line) as Record<string, unknown>];
      } catch {
        return [];
      }
    });
}

test('an error in the browser reaches the file on disk, with what was done before it', async ({
  page,
  consoleErrors,
}) => {
  const marker = `deliberate reporting probe ${Date.now()}`;
  const before = sinkLength();

  try {
    await page.addInitScript(() => {
      window.__silexgisReportUnderAutomation = true;
    });

    // The sign-in page: it runs the whole startup path, including the reporter, and needs no account.
    await page.goto('/');
    await expect(page.getByLabel('Email')).toBeVisible({ timeout: 30_000 });

    // A click first, so the trail has something in it that this test can recognise — the trail is the
    // reason this observer exists at all, and a report without one is only half of what was built.
    await page.getByLabel('Email').click();

    await page.evaluate((text) => {
      setTimeout(() => {
        throw new Error(text);
      }, 0);
    }, marker);

    await expect
      .poll(() => recordsAfter(before).filter((record) => String(record.message).includes(marker)).length, {
        message: 'the application never reported the thrown error to the development server',
        timeout: 15_000,
      })
      .toBe(1);

    const [reported] = recordsAfter(before).filter((record) => String(record.message).includes(marker));
    expect(reported.source).toBe('browsing');
    expect(reported.kind).toBe('uncaught');
    expect(reported.name).toBe('Error');
    expect(reported.fingerprint).toMatch(/^[0-9a-f]{8}$/);
    // The trail, which is what a stack cannot give: the route that was opened and the field clicked.
    const trail = (reported.breadcrumbs ?? []) as string[];
    expect(trail.join('\n')).toContain('route');
    expect(trail.some((entry) => entry.includes('click'))).toBe(true);

    // Exactly one record, not two: the browser also prints a console line about an uncaught error,
    // and the batch drops that echo rather than filing one failure twice.
    expect(recordsAfter(before).filter((record) => String(record.message).includes(marker))).toHaveLength(1);

    consoleErrors.allow(new RegExp(marker), 'thrown by this test to prove the application reports it');
  } finally {
    // Back to where it was, whatever happened above.
    if (existsSync(SINK)) {
      truncateSync(SINK, before);
    }
  }
});

test('both observers give one error one identity, so it is never recorded twice', async ({
  page,
  consoleErrors,
}) => {
  const marker = `deliberate agreement probe ${Date.now()}`;
  const before = sinkLength();

  try {
    await page.addInitScript(() => {
      window.__silexgisReportUnderAutomation = true;
    });
    await page.goto('/');
    await expect(page.getByLabel('Email')).toBeVisible({ timeout: 30_000 });

    // One console error, seen twice: by the application from the inside, and by this suite watching
    // the console from the outside. If the two computed identity differently, every defect seen both
    // ways would be filed twice — which is the whole reason the rule lives in one module that both
    // import. The awkward case, where the two observers genuinely disagree about the frame, is pinned
    // in that module's unit tests; what this adds is that the real pipeline agrees on a real event.
    await page.evaluate((text) => console.error(text), marker);

    await expect
      .poll(() => recordsAfter(before).filter((record) => String(record.message).includes(marker)).length, {
        message: 'the application never reported the console error',
        timeout: 15_000,
      })
      .toBe(1);

    const [reported] = recordsAfter(before).filter((record) => String(record.message).includes(marker));
    const [captured] = consoleErrors.captured().filter((error) => error.message.includes(marker));
    expect(captured, 'the suite did not see the console error at all').toBeDefined();
    expect(reported.source).toBe('browsing');
    expect(captured.source).toBe('sweep');
    expect(reported.fingerprint).toBe(captured.fingerprint);

    consoleErrors.allow(new RegExp(marker), 'logged by this test to compare the two observers');
  } finally {
    if (existsSync(SINK)) {
      truncateSync(SINK, before);
    }
  }
});
