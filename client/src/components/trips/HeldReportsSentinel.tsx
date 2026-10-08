// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { CloudUploadOutlined } from '@ant-design/icons';
import { Alert, App, Badge, Button, Flex, Modal, Typography, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'react-router-dom';
import { signInAgain } from '../../auth/renewSignIn.ts';
import HeldReportRow from './HeldReportRow.tsx';
import { useHeldReports, type ListedHeldReport } from './trackingOutbox.ts';
import { useHeldReportsSignInLapsed } from './trackingOutboxDrain.ts';
import { useHeldReportActions } from './useHeldReportActions.ts';
import { useTrackingOutboxDrain } from './useTrackingOutboxDrain.ts';

/**
 * What the signed-in shell does about tracking reports this browser is holding: sends them, says
 * that they are there, and lists them.
 *
 * <b>Mounted for the whole of a signed-in session, on every page.</b> A report typed with no signal
 * has to leave when the signal returns — not when its author next happens to open the tab it was
 * typed on. So the sending lives here and not on that tab.
 *
 * <b>It draws nothing while nothing is held</b>, which is nearly always: no icon, no space taken in
 * a header that has none to spare on a phone. While something is held it is one button with the
 * count on it, and pressing it lists every report this account is holding, whatever trip each is
 * for.
 *
 * <b>The list is here, and not only on each trip, because a trip can stop being openable.</b> A
 * trip deleted while the phone had no signal, or one its author may no longer read, answers as not
 * found: its page draws nothing of the trip, so a report held for it could be neither seen nor
 * thrown away there — and what is held is somebody's free text about people, which its author must
 * always be able to remove. Each row leads to its trip, where the people are named; here they are
 * counted, because naming them takes the trip's roster.
 *
 * <b>It says when the reports cannot leave at all.</b> A sign-in that is over is not the server
 * being away: nothing mends it but signing in again, and that is offered here. The reports are in
 * the browser's own storage, which signing in again does not touch.
 *
 * <b>It never asks the server anything for its own sake.</b> Sending is started by the page being
 * opened, the connection returning, the page coming back into view, the sign-in being renewed, the
 * server answering something else, a wait the server named running out and — after a send that got
 * no answer — one timer that fires less and less often. With nothing held none of those makes a
 * request, and there is no timer.
 */
export default function HeldReportsSentinel({ accountId }: { accountId: string | null }) {
  const held = useHeldReports(accountId);
  const { sendNow } = useTrackingOutboxDrain(accountId, { automatic: true });

  if (held.length === 0) {
    return null;
  }
  return <HeldReportsButton accountId={accountId} held={held} sendNow={sendNow} />;
}

/**
 * The button and the list behind it. A component of its own so that it exists only while something
 * is held: a list left open when its last report went is then not found open again by the next
 * report to be held, days later.
 */
function HeldReportsButton({
  accountId,
  held,
  sendNow,
}: {
  accountId: string | null;
  held: readonly ListedHeldReport[];
  sendNow: () => Promise<unknown>;
}) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const location = useLocation();
  const { token } = theme.useToken();
  const lapsed = useHeldReportsSignInLapsed();
  const { asking, send, sendAgain, discard } = useHeldReportActions(accountId, sendNow);
  const [listed, setListed] = useState(false);

  const label = t('trips.tracking.outbox.everywhere', { count: held.length });
  const waiting = held.some((report) => report.state === 'held');
  const signIn = () => {
    // Back to this very page. A server that cannot be reached rejects the redirect before leaving,
    // and then the page is still here to say so.
    signInAgain(location.pathname + location.search + location.hash).catch(() => {
      void message.error(t('auth.serverUnreachable'));
    });
  };

  return (
    <>
      <Badge count={held.length} size="small" overflowCount={99} color={token.colorWarning}>
        <Button
          type="text"
          shape="circle"
          aria-label={label}
          title={label}
          data-testid="held-reports"
          // The header paints itself dark, and an icon button inherits the body's text colour
          // rather than the header's, so it has to be told which one it is standing on.
          icon={<CloudUploadOutlined style={{ color: token.colorTextLightSolid, fontSize: 18 }} />}
          onClick={() => setListed(true)}
        />
      </Badge>
      <Modal
        open={listed}
        onCancel={() => setListed(false)}
        footer={null}
        destroyOnHidden
        title={t('trips.tracking.outbox.title', { count: held.length })}
      >
        <Flex vertical gap={12} data-testid="held-reports-list">
          <Typography.Paragraph style={{ marginBottom: 0 }}>
            {t('trips.tracking.outbox.storedHere')}
          </Typography.Paragraph>
          {lapsed && (
            <Alert
              type="error"
              showIcon
              data-testid="held-reports-lapsed"
              title={t('trips.tracking.outbox.lapsedTitle')}
              description={t('trips.tracking.outbox.lapsedBody')}
              action={
                <Button type="primary" onClick={signIn} data-testid="held-reports-sign-in">
                  {t('trips.tracking.outbox.signInAgain')}
                </Button>
              }
            />
          )}
          {
            // Nothing to press where every report here has been refused: each of those is sent
            // again on its own row, by somebody who has read why it was refused.
            waiting && (
              <Flex justify="end">
                <Button
                  type="primary"
                  icon={<CloudUploadOutlined />}
                  loading={asking}
                  onClick={() => void send()}
                  data-testid="held-reports-send"
                >
                  {t('trips.tracking.outbox.sendNow')}
                </Button>
              </Flex>
            )
          }
          {held.map((report) => (
            <HeldReportRow
              key={report.clientKey}
              report={report}
              who={t('trips.tracking.outbox.people', { count: report.body.caverIds.length })}
              when={new Date(report.body.recordedAt).toLocaleString(i18n.language)}
              controlSize="middle"
              testId="held-reports"
              onSendAgain={() => sendAgain(report)}
              onDiscard={() => discard(report)}
              extra={
                <Link
                  to={`/trip-logs/${report.tripLogId}?tab=tracking`}
                  onClick={() => setListed(false)}
                  data-testid={`held-reports-trip-${report.clientKey}`}
                >
                  {t('trips.tracking.outbox.openTrip')}
                </Link>
              }
            />
          ))}
        </Flex>
      </Modal>
    </>
  );
}
