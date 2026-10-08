// SPDX-License-Identifier: AGPL-3.0-or-later
import { CloudUploadOutlined } from '@ant-design/icons';
import { Alert, Button, Collapse, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSignedInAccountId } from '../../auth/accountId.ts';
import HeldReportRow from './HeldReportRow.tsx';
import { useHeldReports } from './trackingOutbox.ts';
import { useHeldReportActions } from './useHeldReportActions.ts';
import { useTrackingOutboxDrain } from './useTrackingOutboxDrain.ts';

/**
 * The reports this account composed for this trip that the server has not answered for yet, above
 * the card they were composed on.
 *
 * <b>Absent while there is nothing held</b> — which is nearly always — so the tab is exactly what
 * it was for everybody who has never lost a connection mid-report.
 *
 * <b>Only this account's, and only this trip's.</b> What is kept is free text about people, on a
 * phone that may be handed to somebody else. The queue answers nothing without being told whose,
 * and a tab signed in as nobody it can name is shown nothing. Reports held for other trips are
 * counted and listed in the page header, which is on every page; they are not listed here because
 * this tab has no roster to name their people from. The header's list is also where a report is
 * met whose trip can no longer be opened at all, and this notice with it.
 *
 * <b>It says in words where the text is.</b> Somebody who typed a note about a tired or injured
 * person is entitled to know it is sitting in this browser, and until when.
 *
 * <b>Discarding is behind a confirmation, and is the only way a report leaves without being sent.</b>
 * A refused report is never dropped by anything but its author: it stays here with the server's
 * reason, to be sent again once the reason is gone or thrown away on purpose.
 */
export default function TrackingHeldReports({
  tripLogId,
  nameOf,
  when,
  controlSize,
}: {
  tripLogId: string;
  nameOf: (caverId: string) => string;
  when: (value: string | null) => string;
  /** How big everything pressed here is drawn: the tab's own size for the pointer in use. */
  controlSize: 'large' | 'small';
}) {
  const { t } = useTranslation();
  const accountId = useSignedInAccountId();
  const held = useHeldReports(accountId, tripLogId);
  const { sendNow } = useTrackingOutboxDrain(accountId);
  const { asking, send, sendAgain, discard } = useHeldReportActions(accountId, sendNow);

  if (accountId === null || held.length === 0) {
    return null;
  }

  const waiting = held.some((report) => report.state === 'held');

  return (
    <Alert
      type="warning"
      showIcon
      data-testid="trip-tracking-outbox"
      title={
        <span data-testid="trip-tracking-outbox-count">
          {t('trips.tracking.outbox.title', { count: held.length })}
        </span>
      }
      action={
        // Nothing to press where every report here has been refused: each of those is sent again
        // on its own row, by somebody who has read why it was refused.
        waiting && (
          <Button
            size={controlSize}
            type="primary"
            icon={<CloudUploadOutlined />}
            loading={asking}
            onClick={() => void send()}
            data-testid="trip-tracking-outbox-send"
          >
            {t('trips.tracking.outbox.sendNow')}
          </Button>
        )
      }
      description={
        <>
          <Typography.Paragraph style={{ marginBottom: 8 }} data-testid="trip-tracking-outbox-kept">
            {t('trips.tracking.outbox.storedHere')}
          </Typography.Paragraph>
          <Collapse
            size="small"
            ghost
            items={[
              {
                key: 'held',
                label: (
                  <span data-testid="trip-tracking-outbox-open">
                    {t('trips.tracking.outbox.show')}
                  </span>
                ),
                children: (
                  <Flex vertical gap={12}>
                    {held.map((report) => (
                      <HeldReportRow
                        key={report.clientKey}
                        report={report}
                        who={report.body.caverIds.map(nameOf).join(', ')}
                        when={when(report.body.recordedAt)}
                        controlSize={controlSize}
                        testId="trip-tracking-outbox"
                        onSendAgain={() => sendAgain(report)}
                        onDiscard={() => discard(report)}
                      />
                    ))}
                  </Flex>
                ),
              },
            ]}
          />
        </>
      }
    />
  );
}
