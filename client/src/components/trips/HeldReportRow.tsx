// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { CloudUploadOutlined, DeleteOutlined } from '@ant-design/icons';
import { Button, Flex, Popconfirm, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ListedHeldReport } from './trackingOutbox.ts';
import { trackingProblemCodeMessage } from './trackingProblems.ts';

/**
 * One held report as its author is shown it: when, who, what, the note, why it was refused if it
 * was, and the two things that can be done about it.
 *
 * <b>Discarding is behind a confirmation, and the confirmation does not say the report was never
 * sent.</b> A report is held because no answer came, and that covers the request that reached the
 * server and whose answer was lost: the report is then on the log already. So the question says
 * what discarding does — the copy here is deleted and not sent again — and where to look for a
 * report that got through after all.
 *
 * `who` is given rather than worked out, because only a surface with the trip's roster can name
 * people; one without says how many there are.
 */
export default function HeldReportRow({
  report,
  who,
  when,
  controlSize,
  testId,
  onSendAgain,
  onDiscard,
  extra,
}: {
  report: ListedHeldReport;
  who: string;
  when: string;
  /** How big everything pressed here is drawn: the surface's own size for the pointer in use. */
  controlSize: 'large' | 'middle' | 'small';
  /** What every test id here starts with, so two lists of the same reports can be told apart. */
  testId: string;
  onSendAgain: () => void;
  onDiscard: () => void;
  /** Anything else to press, drawn before the two that are always there. */
  extra?: ReactNode;
}) {
  const { t } = useTranslation();

  const place =
    report.body.stationName !== null
      ? report.body.toStationName !== null
        ? t('trips.tracking.stretch', {
            from: report.body.stationName,
            to: report.body.toStationName,
          })
        : report.body.stationName
      : report.body.depthM !== null
        ? t('trips.metres', { value: report.body.depthM })
        : null;

  return (
    <Flex vertical gap={4} data-testid={`${testId}-${report.clientKey}`}>
      <Flex gap={8} align="center" wrap>
        <Typography.Text strong>{when}</Typography.Text>
        {/* A kept note about the cave names nobody, whatever its caller counted or listed. */}
        <span>{report.body.kind === 'caveNote' ? t('trips.tracking.caveNote.who') : who}</span>
        <Tag color={report.body.kind === 'caveNote' ? 'warning' : undefined}>
          {t(`trips.tracking.kinds.${report.body.kind}`)}
        </Tag>
        <span>{place}</span>
        {report.body.activity ? <Tag>{report.body.activity}</Tag> : null}
      </Flex>
      {report.body.note !== null && <div>{report.body.note}</div>}
      {report.state === 'refused' && (
        <Typography.Text type="danger" data-testid={`${testId}-refused-${report.clientKey}`}>
          {t('trips.tracking.outbox.refused', {
            reason:
              trackingProblemCodeMessage(report.problemCode, t) ??
              t('trips.tracking.outbox.refusedNoReason'),
          })}
        </Typography.Text>
      )}
      <Flex gap={8} wrap justify="end" align="center">
        {extra}
        {report.state === 'refused' && (
          <Button
            size={controlSize}
            icon={<CloudUploadOutlined />}
            loading={report.sending}
            onClick={onSendAgain}
            data-testid={`${testId}-again-${report.clientKey}`}
          >
            {t('trips.tracking.outbox.sendAgain')}
          </Button>
        )}
        <Popconfirm
          title={t('trips.tracking.outbox.discardConfirm')}
          description={
            <div style={{ maxWidth: 320 }}>{t('trips.tracking.outbox.discardConfirmDetail')}</div>
          }
          okText={t('trips.tracking.outbox.discard')}
          okButtonProps={{ size: controlSize, danger: true }}
          cancelButtonProps={{ size: controlSize }}
          onConfirm={onDiscard}
        >
          <Button
            danger
            size={controlSize}
            icon={<DeleteOutlined />}
            // A report on its way cannot be thrown away from under its own request: what the
            // server answers decides what becomes of it.
            disabled={report.sending}
            data-testid={`${testId}-discard-${report.clientKey}`}
          >
            {t('trips.tracking.outbox.discard')}
          </Button>
        </Popconfirm>
      </Flex>
    </Flex>
  );
}
