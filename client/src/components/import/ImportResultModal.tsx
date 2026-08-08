// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Modal, Statistic, Flex, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import type { ImportCommitResult } from '../../api/hooks.ts';

interface Props {
  result: ImportCommitResult | null;
  onClose: () => void;
}

/**
 * What the confirmation did — and, when some rows could not be created, exactly which and why.
 *
 * The failures are shown rather than summarised away: one odd row must not cost the other
 * three hundred, and it must not be swallowed either. The batch stays revertible as a unit,
 * which is what the link at the bottom leads to.
 */
export default function ImportResultModal({ result, onClose }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <Modal
      open={result !== null}
      title={t('vectorImport.resultTitle')}
      onCancel={onClose}
      onOk={() => {
        onClose();
        navigate('/geodata?tab=batches');
      }}
      okText={t('vectorImport.viewBatches')}
      cancelText={t('common.close')}
      width={720}
      destroyOnHidden
    >
      {result && (
        <Flex vertical gap={16}>
          <Flex gap={32} wrap>
            <Statistic title={t('vectorImport.created')} value={result.batch.createdCount} />
            <Statistic title={t('vectorImport.attached')} value={result.batch.attachedCount} />
            <Statistic title={t('vectorImport.skipped')} value={result.batch.skippedCount} />
          </Flex>

          {result.failures.length > 0 && (
            <>
              <Alert
                type="warning"
                showIcon
                title={t('vectorImport.someFailedTitle', { count: result.failures.length })}
                description={t('vectorImport.someFailed')}
              />
              <Table
                size="small"
                rowKey="sourceId"
                pagination={false}
                dataSource={result.failures}
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
