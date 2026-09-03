// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Modal, Statistic, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import type { TripImportCommitResult } from '../../api/hooks.ts';

interface Props {
  result: TripImportCommitResult | null;
  onClose: () => void;
}

/**
 * What a confirmation did. The failures are shown rather than summarised away: one odd row must
 * not cost the other three hundred, and it must not be swallowed either.
 *
 * The note about taking it back says only what taking it back actually does. Reverting removes
 * the trips and the features this batch made; the cavers and the vocabulary words it added stay,
 * because a word is installation-wide and a person may already have been written into something
 * else. Saying "undo removes everything" would be a promise this import cannot keep.
 */
export default function TripImportResultModal({ result, onClose }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <Modal
      open={result !== null}
      destroyOnHidden
      width={720}
      title={t('tripImport.resultTitle')}
      onCancel={onClose}
      onOk={() => {
        onClose();
        navigate('/geodata?tab=batches');
      }}
      okText={t('tripImport.viewBatches')}
      cancelText={t('common.close')}
      data-testid="trip-import-result"
    >
      {result && (
        <>
          <Flex gap={32} wrap style={{ marginBottom: 16 }}>
            <Statistic title={t('tripImport.createdTrips')} value={result.createdTripCount} />
            <Statistic title={t('tripImport.createdFeatures')} value={result.createdFeatureCount} />
            <Statistic title={t('tripImport.skipped')} value={result.skippedCount} />
          </Flex>
          <Typography.Paragraph type="secondary">{t('tripImport.undoHint')}</Typography.Paragraph>
          {result.failures.length > 0 && (
            <>
              <Alert
                type="warning"
                showIcon
                style={{ marginBottom: 12 }}
                data-testid="trip-import-result-failures"
                title={t('tripImport.someFailedTitle', { count: result.failures.length })}
              />
              <Table
                size="small"
                rowKey="line"
                pagination={false}
                dataSource={result.failures}
                columns={[
                  { title: t('tripImport.columns.line'), dataIndex: 'line', key: 'line', width: 80 },
                  { title: t('tripImport.columns.title'), dataIndex: 'title', key: 'title' },
                  { title: t('tripImport.columns.reason'), dataIndex: 'reason', key: 'reason' },
                ]}
              />
            </>
          )}
        </>
      )}
    </Modal>
  );
}
