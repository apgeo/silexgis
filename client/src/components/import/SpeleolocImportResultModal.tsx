// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Modal, Statistic, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import type { SpeleolocImportCommitResult, SpeleolocImportFailure } from '../../api/hooks.ts';

interface Props {
  result: SpeleolocImportCommitResult | null;
  onClose: () => void;
}

/**
 * Why one scan was not recorded, keyed by the code the server answers with rather than by the
 * sentence beside it. The sentence is prose written for a person and may be reworded or
 * translated; the code is the contract, and it is the only part of a refusal worth matching on.
 */
const FAILURE_KEYS: Record<string, string> = {
  'speleoloc_import.point_missing': 'pointMissing',
  'speleoloc_import.station_unresolved': 'stationUnresolved',
  'speleoloc_import.station_unknown': 'stationUnknown',
  'speleoloc_import.caver_unmapped': 'caverUnmapped',
  'speleoloc_import.caver_not_participant': 'caverNotParticipant',
  'speleoloc_import.recorded_in_future': 'recordedInFuture',
  'speleoloc_import.note_too_long': 'noteTooLong',
};

/**
 * What a confirmation did.
 *
 * The scans that failed are shown rather than summarised away: one odd scan must not cost the
 * other two hundred, and it must not be swallowed either. A confirmation where *every* scan failed
 * never reaches here at all — the server writes nothing and refuses, which leaves the review
 * standing to be fixed and tried again.
 */
export default function SpeleolocImportResultModal({ result, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();

  const reason = (failure: SpeleolocImportFailure) => {
    const key = FAILURE_KEYS[failure.code];
    return key ? t(`speleolocImport.failures.${key}`) : t('speleolocImport.failures.unknown');
  };

  return (
    <Modal
      open={result !== null}
      destroyOnHidden
      width={720}
      title={t('speleolocImport.resultTitle')}
      onCancel={onClose}
      onOk={() => {
        onClose();
        navigate('/geodata?tab=batches');
      }}
      okText={t('speleolocImport.viewBatches')}
      cancelText={t('common.close')}
      data-testid="speleoloc-import-result"
    >
      {result && (
        <>
          <Flex gap={32} wrap style={{ marginBottom: 16 }}>
            <Statistic
              title={t('speleolocImport.createdPositions')}
              value={result.createdEventCount}
            />
            <Statistic title={t('speleolocImport.skipped')} value={result.skippedCount} />
          </Flex>
          <Typography.Paragraph data-testid="speleoloc-import-result-destination">
            {result.createdTrip
              ? t('speleolocImport.tripWasCreated')
              : t('speleolocImport.tripWasExisting')}
          </Typography.Paragraph>
          <Typography.Paragraph type="secondary">
            {t('speleolocImport.undoHint')}
          </Typography.Paragraph>
          {result.failures.length > 0 && (
            <>
              <Alert
                type="warning"
                showIcon
                style={{ marginBottom: 12 }}
                data-testid="speleoloc-import-result-failures"
                title={t('speleolocImport.someFailedTitle', { count: result.failures.length })}
              />
              <Table<SpeleolocImportFailure>
                size="small"
                rowKey="pointId"
                pagination={false}
                scroll={{ x: 'max-content' }}
                dataSource={result.failures}
                columns={[
                  {
                    title: t('speleolocImport.columns.when'),
                    key: 'when',
                    render: (_, failure) =>
                      new Date(failure.scannedAt).toLocaleString(i18n.language),
                  },
                  {
                    title: t('speleolocImport.columns.reason'),
                    key: 'reason',
                    render: (_, failure) => reason(failure),
                  },
                ]}
              />
            </>
          )}
        </>
      )}
    </Modal>
  );
}
