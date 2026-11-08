// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Descriptions, Flex } from 'antd';
import { useTranslation } from 'react-i18next';
import { useStorageRoom } from '../../api/hooks.ts';
import { formatBytes, levelAlertType, limitKey, percentOfCap } from './storageFigures.ts';

/**
 * The room left, said before somebody starts: the installation's total, the scans' total when it
 * is a point cloud, what this upload or run adds, how full the store already is, and — when it
 * will not fit — which ceiling it is under and how much is missing.
 *
 * Advice only. The server checks the same figures again when the bytes or the run arrive, so this
 * shows nothing at all while the answer is loading or failed rather than holding the person up.
 */
export default function StorageRoomSummary({
  addingBytes,
  scan = false,
  processing = false,
}: {
  addingBytes: number | null | undefined;
  scan?: boolean;
  processing?: boolean;
}) {
  const { t } = useTranslation();
  const { data: room } = useStorageRoom({ addingBytes, scan, processing });
  if (!room) {
    return null;
  }

  const total = (used: number, cap: number | null | undefined) => {
    const percent = percentOfCap(used, cap);
    return percent === null || cap === null || cap === undefined
      ? t('fileStorage.room.usedNoCap', { used: formatBytes(used) })
      : t('fileStorage.room.usedOfCap', { used: formatBytes(used), cap: formatBytes(cap), percent });
  };
  const alert = levelAlertType(room.level);

  return (
    <Flex vertical gap={8} data-testid="storage-room" style={{ marginTop: 12 }}>
      <Descriptions size="small" column={1} bordered>
        <Descriptions.Item label={t('fileStorage.room.installation')}>
          <span data-testid="storage-room-installation">{total(room.storeUsedBytes, room.storeCapBytes)}</span>
        </Descriptions.Item>
        {scan && (
          <Descriptions.Item label={t('fileStorage.room.scans')}>
            <span data-testid="storage-room-scans">{total(room.scanUsedBytes, room.scanCapBytes)}</span>
          </Descriptions.Item>
        )}
        {room.accountCapBytes !== null && room.accountCapBytes !== undefined && (
          <Descriptions.Item label={t('fileStorage.room.account')}>
            {total(room.accountUsedBytes, room.accountCapBytes)}
          </Descriptions.Item>
        )}
        <Descriptions.Item label={t(processing ? 'fileStorage.room.runAdds' : 'fileStorage.room.uploadAdds')}>
          <span data-testid="storage-room-adding">{formatBytes(room.addingBytes)}</span>
        </Descriptions.Item>
      </Descriptions>
      {room.shortfall ? (
        <Alert
          type="error"
          showIcon
          data-testid="storage-room-shortfall"
          title={t('fileStorage.room.doesNotFit', {
            missing: formatBytes(room.shortfall.missingBytes),
            limit: t(limitKey(room.shortfall.limit)),
          })}
          description={t('fileStorage.room.doesNotFitHint')}
        />
      ) : (
        alert && (
          <Alert type={alert} showIcon data-testid="storage-room-level" title={t(`fileStorage.levels.${room.level}`)} />
        )
      )}
    </Flex>
  );
}
