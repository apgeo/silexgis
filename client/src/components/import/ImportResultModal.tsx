// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Modal, Spin, Statistic, Flex, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import type { ImportBatch, ImportFailure, ProcessingJob } from '../../api/hooks.ts';

interface Props {
  /** The batch, once the work has finished and it exists. */
  batch: ImportBatch | null;
  /**
   * The rows that could not be created.
   *
   * Passed rather than read off the batch, because the two confirmations that end here do not
   * carry them the same way: photographs are still created inside the request that asks, and
   * answer with their failures directly, while a vector file is created on the queue and has to
   * record them on the batch for somebody to find afterwards.
   */
  failures: readonly ImportFailure[];
  /** How many rows were confirmed, so the wait can say what it is waiting for. */
  queued: number;
  status: ProcessingJob['status'] | undefined;
  error: string | null;
  open: boolean;
  onClose: () => void;
}

/**
 * What the confirmation did — and, when some rows could not be created, exactly which and why.
 *
 * The failures are shown rather than summarised away: one odd row must not cost the other
 * three hundred, and it must not be swallowed either. The batch stays revertible as a unit,
 * which is what the link at the bottom leads to.
 */
export default function ImportResultModal(
  { batch, failures, queued, status, error, open, onClose }: Props,
) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // Still going until the job says otherwise. `undefined` is the moment between confirming and
  // the first answer about the job, which is a wait like any other rather than a result.
  const working = status === undefined || status === 'queued' || status === 'running';

  return (
    <Modal
      open={open}
      title={t('vectorImport.resultTitle')}
      onCancel={onClose}
      onOk={() => {
        onClose();
        navigate('/geodata?tab=batches');
      }}
      okText={t('vectorImport.viewBatches')}
      cancelText={t('common.close')}
      okButtonProps={{ disabled: working }}
      width={720}
      destroyOnHidden
    >
      {working && (
        <Flex vertical gap={12}>
          {/* A spinner, not a bar. The work reports no per-row progress, and antd's bar has no
              indeterminate form — drawn at 100 it reads as finished, which is exactly the wrong
              thing to show somebody wondering whether their import is stuck. */}
          <Spin />
          <Typography.Text>{t('vectorImport.creating', { count: queued })}</Typography.Text>
          <Typography.Text type="secondary">{t('vectorImport.creatingHint')}</Typography.Text>
        </Flex>
      )}

      {status === 'failed' && (
        <Alert
          type="error"
          showIcon
          title={t('vectorImport.commitFailed')}
          description={error ?? undefined}
        />
      )}

      {batch && (
        <Flex vertical gap={16}>
          <Flex gap={32} wrap>
            <Statistic title={t('vectorImport.created')} value={batch.createdCount} />
            <Statistic title={t('vectorImport.attached')} value={batch.attachedCount} />
            <Statistic title={t('vectorImport.skipped')} value={batch.skippedCount} />
          </Flex>

          {failures.length > 0 && (
            <>
              <Alert
                type="warning"
                showIcon
                title={t('vectorImport.someFailedTitle', { count: failures.length })}
                description={t('vectorImport.someFailed')}
              />
              <Table
                size="small"
                rowKey="sourceId"
                pagination={false}
                dataSource={[...failures]}
                columns={[
                  { title: t('vectorImport.columns.name'), dataIndex: 'name', width: 200 },
                  {
                    title: t('vectorImport.failureReason'),
                    dataIndex: 'reason',
                    render: (reason: string) => <Typography.Text>{reason}</Typography.Text>,
                  },
                ]}
              />
            </>
          )}
        </Flex>
      )}
    </Modal>
  );
}
