// SPDX-License-Identifier: AGPL-3.0-or-later
import { test } from './consoleGuard.ts';
import {
  aReportWhoseAnswerWasLostIsNotWrittenTwice,
  aReportWithNoSignalIsHeldAndSentOnReturn,
  removeWhatWasMade,
} from './trackingHeldReportsFlow.ts';

/**
 * A report recorded where the server cannot be reached, at a desk. The two walks, and why they
 * need a real browser and a real server, are described beside the flow.
 *
 * Two tests rather than one walk: each ends with a browser that has been offline, and a page of its
 * own is what guarantees the second outage is the only one its report ever met.
 */
test.afterEach(async ({ page }) => {
  await removeWhatWasMade(page);
});

test('a report recorded with no signal is held, survives a reload and is sent once when the connection returns', async ({
  page,
  consoleErrors,
}) => {
  test.setTimeout(240_000);
  await aReportWithNoSignalIsHeldAndSentOnReturn(page, consoleErrors);
});

test('a report the server took but never answered for is held, and sending it again writes nothing', async ({
  page,
  consoleErrors,
}) => {
  test.setTimeout(180_000);
  await aReportWhoseAnswerWasLostIsNotWrittenTwice(page, consoleErrors);
});
