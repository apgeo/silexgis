// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined, EnvironmentOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { Button, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { formatLonLat } from '../../geo/coords.ts';
import PointPickerModal from './PointPickerModal.tsx';

interface Props {
  value?: [number, number] | null;
  onChange?: (value: [number, number] | null) => void;
}

/**
 * The form-facing half of the point picker: a line of text plus the buttons that open the map
 * and clear the point. Keeping the map in a dialog rather than inline means a list of addresses
 * does not try to render one map per row.
 */
export default function PointField({ value = null, onChange }: Props) {
  const { t } = useTranslation();
  const [picking, setPicking] = useState(false);

  return (
    <Flex gap={8} align="center" wrap>
      <Typography.Text type={value ? undefined : 'secondary'}>
        {value ? formatLonLat(value[0], value[1]) : t('settings.profile.noLocation')}
      </Typography.Text>
      <Button size="small" icon={<EnvironmentOutlined />} onClick={() => setPicking(true)}>
        {value ? t('settings.profile.changeLocation') : t('settings.profile.pickLocation')}
      </Button>
      {value && (
        <Button
          size="small"
          type="text"
          icon={<DeleteOutlined />}
          aria-label={t('settings.profile.clearLocation')}
          onClick={() => onChange?.(null)}
        />
      )}
      <PointPickerModal
        open={picking}
        value={value}
        onCancel={() => setPicking(false)}
        onPick={(lonLat) => {
          onChange?.(lonLat);
          setPicking(false);
        }}
      />
    </Flex>
  );
}
