// SPDX-License-Identifier: AGPL-3.0-or-later
import { test } from './consoleGuard.ts';
import { correctImportAndReportByPlace } from './trackingWritesFlow.ts';

/**
 * The same three writes on a phone: the log's rows stacked into cards, the sheet's preview a list
 * of cards with a tick on each, and every dialog under a finger. A coordinator fixing an hour typed
 * off a call is as likely to be holding a phone as sitting at a desk, and the phone layouts of
 * these surfaces are separate code from the desk ones.
 */
test('on a phone, a coordinator corrects a report, imports a sheet and reports by a place', async ({
  page,
}) => {
  await correctImportAndReportByPlace(page);
});
