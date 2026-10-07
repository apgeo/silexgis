// SPDX-License-Identifier: AGPL-3.0-or-later
import { test } from './consoleGuard.ts';
import { correctImportAndReportByPlace } from './trackingWritesFlow.ts';

/**
 * Correcting a report, importing a sheet of them, and reporting by a declared place — at a desk.
 * The walk itself, and why it is a browser flow at all, is described beside the flow.
 */
test('a coordinator corrects a report, imports a sheet and reports by a declared place', async ({
  page,
}) => {
  // One walk against a real server — a cave and its survey stood up, five sheets read and
  // imported, reports corrected and recorded — which is well over a minute of work on a quiet
  // machine and has to stay one test: each step is checked against the log the steps before it
  // left.
  test.setTimeout(240_000);
  await correctImportAndReportByPlace(page);
});
