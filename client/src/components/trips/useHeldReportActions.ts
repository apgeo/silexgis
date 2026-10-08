// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { dropHeldReport, heldReportQueuedAgain, type ListedHeldReport } from './trackingOutbox.ts';

/**
 * What can be done to a held report by its author, wherever it is listed.
 *
 * Listed in two places — above the card it was composed on, and in the page header for every trip
 * at once — and the rule for each act is about the report and not about the list: one home, so
 * that discarding from the header is the same act, said in the same words, as discarding from the
 * trip.
 *
 * `sendNow` is the caller's own way of sending what is held: the header's is the sender that lives
 * for the whole signed-in session, a trip's is one that sends only on a press.
 */
export function useHeldReportActions(accountId: string | null, sendNow: () => Promise<unknown>) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [asking, setAsking] = useState(false);

  const send = async () => {
    setAsking(true);
    try {
      await sendNow();
    } finally {
      setAsking(false);
    }
  };

  const sendAgain = (report: ListedHeldReport) => {
    if (accountId === null) return;
    // Back among the reports that are waiting, and then sent like any of them: the one way a
    // report leaves is the same whether it was held or had been refused.
    heldReportQueuedAgain(accountId, report.clientKey);
    void send();
  };

  const discard = (report: ListedHeldReport) => {
    if (accountId === null) return;
    dropHeldReport(accountId, report.clientKey);
    // Says what is known and no more: the copy here is gone. Whether the report reached the
    // server on a send that was never answered is exactly what this browser cannot know.
    void message.success({ content: t('trips.tracking.outbox.discarded'), duration: 8 });
  };

  return { asking, send, sendAgain, discard };
}
