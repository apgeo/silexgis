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
  await correctImportAndReportByPlace(page);
});
