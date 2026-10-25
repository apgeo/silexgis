// SPDX-License-Identifier: AGPL-3.0-or-later
import { EnvironmentOutlined } from '@ant-design/icons';
import { Tag, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import type { Delivery } from './downloadTitle.ts';

/**
 * Says, beside a picture, that the copy this reader is given has had its location data taken
 * out. The server decides that per reader, so two people looking at one photograph can see
 * different things here. Draws nothing for the stored file.
 */
export default function LocationRemovedTag({ file }: { file: Delivery }) {
  const { t } = useTranslation();
  if (!file.locationRemoved) {
    return null;
  }
  return (
    <Tooltip title={t('attachments.locationRemovedHint')}>
      <Tag icon={<EnvironmentOutlined />} style={{ marginInlineEnd: 0 }} data-testid="location-removed">
        {t('attachments.locationRemoved')}
      </Tag>
    </Tooltip>
  );
}
