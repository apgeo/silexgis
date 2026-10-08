// SPDX-License-Identifier: AGPL-3.0-or-later
import { test } from './consoleGuard.ts';
import {
  aReportWhoseAnswerWasLostIsNotWrittenTwice,
  aReportWithNoSignalIsHeldAndSentOnReturn,
  removeWhatWasMade,
} from './trackingHeldReportsFlow.ts';

/**
 * The same two walks on a phone — which is where they happen. Somebody relaying reports from a
 * cave entrance is holding a phone at the edge of its signal; the notice of held reports, its
 * count in the page header and the button that sends them are all drawn for a finger there, in a
 * header with no room to spare.
 */
test.afterEach(async ({ page }) => {
  await removeWhatWasMade(page);
});

test('on a phone, a report recorded with no signal is held, survives a reload and is sent once when the connection returns', async ({
  page,
  consoleErrors,
}) => {
  test.setTimeout(240_000);
  await aReportWithNoSignalIsHeldAndSentOnReturn(page, consoleErrors);
});

test('on a phone, a report the server took but never answered for is held, and sending it again writes nothing', async ({
  page,
  consoleErrors,
}) => {
  test.setTimeout(180_000);
  await aReportWhoseAnswerWasLostIsNotWrittenTwice(page, consoleErrors);
});
